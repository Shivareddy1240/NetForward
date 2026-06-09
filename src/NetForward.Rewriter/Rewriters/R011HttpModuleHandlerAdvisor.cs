using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NetForward.Analyzer;
using NetForward.Core.Models;
using NetForward.Rewriter.Pipeline;

namespace NetForward.Rewriter.Rewriters;

/// <summary>
/// R011 — HttpModule and HttpHandler advisor (Tier 3 — flag only, no source modification).
///
/// IHttpModule → ASP.NET Core middleware
/// IHttpHandler / IHttpAsyncHandler → ASP.NET Core endpoint or terminal middleware
///
/// These cannot be auto-converted because the entire execution model is different:
///   - HttpModules hook into the ASP.NET pipeline via events (BeginRequest, etc.)
///   - Middleware uses a linear pipeline with next() delegates
///   - HttpHandlers process requests directly; Core uses endpoint routing
///
/// This rule raises NF452/NF453 per detected class and includes a concrete
/// middleware/endpoint stub in the recommendation so developers have a
/// starting point rather than a blank screen.
/// </summary>
public sealed class R011HttpModuleHandlerAdvisor : IRewriteRule
{
    public string RuleId => "R011";
    public string Name => "HttpModule and HttpHandler advisor";
    public RewriteTier Tier => RewriteTier.Tier3;

    public bool IsApplicable(SyntaxTree syntaxTree)
    {
        var text = syntaxTree.ToString();
        return text.Contains("IHttpModule", StringComparison.Ordinal)
            || text.Contains("IHttpHandler", StringComparison.Ordinal)
            || text.Contains("IHttpAsyncHandler", StringComparison.Ordinal);
    }

    public Task<RuleResult> ApplyAsync(
        RewriteContext context,
        SyntaxTree syntaxTree,
        CancellationToken cancellationToken = default)
    {
        var root = syntaxTree.GetCompilationUnitRoot();
        var issues = new List<MigrationIssue>();

        foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            if (classDecl.BaseList is null) continue;

            foreach (var baseType in classDecl.BaseList.Types)
            {
                var typeName = baseType.Type.ToString().Trim();
                var shortName = typeName.Split('.').Last();

                switch (shortName)
                {
                    case "IHttpModule":
                        issues.Add(BuildModuleIssue(classDecl, syntaxTree.FilePath));
                        break;

                    case "IHttpHandler":
                    case "IHttpAsyncHandler":
                        issues.Add(BuildHandlerIssue(classDecl, shortName, syntaxTree.FilePath));
                        break;
                }
            }
        }

        return Task.FromResult(new RuleResult
        {
            OutputTree = syntaxTree,
            Transformations = [],
            Issues = issues
        });
    }

    private static MigrationIssue BuildModuleIssue(
        ClassDeclarationSyntax classDecl, string? filePath)
    {
        var className = classDecl.Identifier.Text;

        // Extract Init() event subscriptions to include in the stub.
        var initMethod = classDecl.Members
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.Text == "Init");

        var eventHints = ExtractEventSubscriptions(initMethod);
        var stub = BuildMiddlewareStub(className, eventHints);

        return new MigrationIssue
        {
            Id = IssueIds.HttpModuleToMiddleware,
            Title = $"IHttpModule '{className}' must be rewritten as middleware",
            Description =
                $"'{className}' implements IHttpModule which is not supported on modern .NET. " +
                "HTTP modules are replaced by ASP.NET Core middleware. The module's Init() " +
                "event subscriptions must become middleware pipeline logic.",
            Severity = IssueSeverity.Warning,
            Category = IssueCategory.AspNetApi,
            FilePath = filePath,
            LineNumber = classDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
            EffortHours = 3.0,
            Recommendation =
                "Create a new middleware class (see stub below) and register in Program.cs " +
                "with app.UseMiddleware<" + className + "Middleware>(). " +
                "Then remove the module registration from web.config <system.webServer><modules>.\n\n" +
                "=== Middleware stub ===\n" + stub,
            HelpUrl = "https://docs.microsoft.com/aspnet/core/migration/http-modules"
        };
    }

    private static MigrationIssue BuildHandlerIssue(
        ClassDeclarationSyntax classDecl, string interfaceName, string? filePath)
    {
        var className = classDecl.Identifier.Text;
        var stub = BuildEndpointStub(className, interfaceName);

        return new MigrationIssue
        {
            Id = IssueIds.HttpHandlerToEndpoint,
            Title = $"{interfaceName} '{className}' must be rewritten as an endpoint",
            Description =
                $"'{className}' implements {interfaceName} which is not supported on modern .NET. " +
                "HTTP handlers are replaced by minimal API endpoints or terminal middleware. " +
                "The handler's ProcessRequest logic becomes an endpoint delegate.",
            Severity = IssueSeverity.Warning,
            Category = IssueCategory.AspNetApi,
            FilePath = filePath,
            LineNumber = classDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
            EffortHours = 2.0,
            Recommendation =
                "Option A (minimal API endpoint) — preferred for simple handlers:\n" + stub +
                "\n\nOption B (terminal middleware): implement as app.Use() or app.Run() " +
                "delegate in Program.cs and route via app.Map(\"/your-path\", ...).",
            HelpUrl = "https://docs.microsoft.com/aspnet/core/migration/http-modules"
        };
    }

    private static List<string> ExtractEventSubscriptions(MethodDeclarationSyntax? initMethod)
    {
        if (initMethod?.Body is null) return [];

        // Look for += assignments in the Init body to identify which events are used.
        return initMethod.Body
            .DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Where(a => a.IsKind(SyntaxKind.AddAssignmentExpression))
            .Select(a => a.Left.ToString().Split('.').Last())
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Distinct()
            .ToList();
    }

    private static string BuildMiddlewareStub(string className, List<string> eventHints)
    {
        var middlewareName = className.EndsWith("Module", StringComparison.Ordinal)
            ? className[..^6] + "Middleware"
            : className + "Middleware";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"public class {middlewareName}");
        sb.AppendLine("{");
        sb.AppendLine("    private readonly RequestDelegate _next;");
        sb.AppendLine();
        sb.AppendLine($"    public {middlewareName}(RequestDelegate next)");
        sb.AppendLine("    {");
        sb.AppendLine("        _next = next;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public async Task InvokeAsync(HttpContext context)");
        sb.AppendLine("    {");

        if (eventHints.Any(e => e.Contains("BeginRequest")))
            sb.AppendLine("        // TODO: BeginRequest logic here (runs before handler)");

        sb.AppendLine("        await _next(context);");

        if (eventHints.Any(e => e.Contains("EndRequest")))
            sb.AppendLine("        // TODO: EndRequest logic here (runs after handler)");

        if (eventHints.Any(e => e.Contains("Error")))
            sb.AppendLine("        // TODO: Error handling — consider app.UseExceptionHandler() instead");

        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("// Register in Program.cs:");
        sb.AppendLine($"// app.UseMiddleware<{middlewareName}>();");

        if (eventHints.Count > 0)
        {
            sb.AppendLine($"// Detected events in Init(): {string.Join(", ", eventHints)}");
            sb.AppendLine("// Map these events to the before/after next() positions above.");
        }

        return sb.ToString();
    }

    private static string BuildEndpointStub(string className, string interfaceName)
    {
        var routeName = className
            .Replace("Handler", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Async", "", StringComparison.OrdinalIgnoreCase)
            .ToLowerInvariant();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("// In Program.cs:");
        sb.AppendLine($"// Migrated from {className} ({interfaceName})");
        sb.AppendLine($"app.MapGet(\"/{routeName}\", async (HttpContext context) =>");
        sb.AppendLine("{");
        sb.AppendLine("    // TODO: migrate ProcessRequest logic here");
        sb.AppendLine("    // Access: context.Request, context.Response");
        sb.AppendLine("    // Return: Results.Ok(), Results.File(), Results.Content() etc.");
        sb.AppendLine("    await context.Response.WriteAsync(\"Migrated from " + className + "\");");
        sb.AppendLine("});");
        sb.AppendLine();
        sb.AppendLine("// Or as a typed endpoint handler:");
        sb.AppendLine($"// app.MapGet(\"/{routeName}\", " + className + "Handler.Handle);");
        sb.AppendLine($"// static class {className}Handler {{ static IResult Handle(...) {{ ... }} }}");

        return sb.ToString();
    }
}
