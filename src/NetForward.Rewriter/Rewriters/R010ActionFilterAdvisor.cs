using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NetForward.Analyzer;
using NetForward.Core.Models;
using NetForward.Rewriter.Pipeline;

namespace NetForward.Rewriter.Rewriters;

/// <summary>
/// R010 — Action filter advisor (Tier 3 — flag only, no source modification).
///
/// Fix: GetShortName previously stripped the "Attribute" suffix which caused
/// "ActionFilterAttribute" to become "ActionFilter", never matching the
/// dictionary keys. Now GetShortName strips only the namespace prefix,
/// preserving the full type name including "Attribute" suffix.
/// </summary>
public sealed class R010ActionFilterAdvisor : IRewriteRule
{
    public string RuleId => "R010";
    public string Name => "Action filter advisor";
    public RewriteTier Tier => RewriteTier.Tier3;

    // Keys are FULL short type names (with Attribute suffix where applicable).
    // GetShortName strips only the namespace prefix, not the suffix.
    private static readonly Dictionary<string, FilterInfo> KnownFilterBases =
        new(StringComparer.Ordinal)
        {
            ["ActionFilterAttribute"] = new(
                IssueId: IssueIds.ActionFilterManualMigration,
                Title: "ActionFilterAttribute subclass requires manual migration",
                CoreEquivalent: "Microsoft.AspNetCore.Mvc.Filters.ActionFilterAttribute",
                CoreSignatures:
                    "OnActionExecuting(ActionExecutingContext context)\n" +
                    "OnActionExecuted(ActionExecutedContext context)\n" +
                    "OnResultExecuting(ResultExecutingContext context)\n" +
                    "OnResultExecuted(ResultExecutedContext context)"),

            ["IActionFilter"] = new(
                IssueId: IssueIds.ActionFilterManualMigration,
                Title: "IActionFilter implementation requires manual migration",
                CoreEquivalent: "Microsoft.AspNetCore.Mvc.Filters.IActionFilter",
                CoreSignatures:
                    "OnActionExecuting(ActionExecutingContext context)\n" +
                    "OnActionExecuted(ActionExecutedContext context)"),

            ["AuthorizationFilterAttribute"] = new(
                IssueId: IssueIds.AuthFilterManualMigration,
                Title: "AuthorizationFilterAttribute subclass requires manual migration",
                CoreEquivalent: "Microsoft.AspNetCore.Mvc.Filters.IAuthorizationFilter",
                CoreSignatures:
                    "OnAuthorization(AuthorizationFilterContext context)\n" +
                    "NOTE: Consider using policy-based authorization with [Authorize(Policy=\"...\")] instead."),

            ["IAuthorizationFilter"] = new(
                IssueId: IssueIds.AuthFilterManualMigration,
                Title: "IAuthorizationFilter implementation requires manual migration",
                CoreEquivalent: "Microsoft.AspNetCore.Mvc.Filters.IAuthorizationFilter",
                CoreSignatures: "OnAuthorization(AuthorizationFilterContext context)"),

            ["ExceptionFilterAttribute"] = new(
                IssueId: "NF454",   // distinct from NF452 (HttpModule) and NF453 (HttpHandler)
                Title: "ExceptionFilterAttribute subclass requires manual migration",
                CoreEquivalent: "Microsoft.AspNetCore.Mvc.Filters.ExceptionFilterAttribute",
                CoreSignatures: "OnException(ExceptionContext context)"),

            ["IExceptionFilter"] = new(
                IssueId: "NF454",
                Title: "IExceptionFilter implementation requires manual migration",
                CoreEquivalent: "Microsoft.AspNetCore.Mvc.Filters.IExceptionFilter",
                CoreSignatures: "OnException(ExceptionContext context)"),

            ["ResultFilterAttribute"] = new(
                IssueId: "NF455",
                Title: "ResultFilterAttribute subclass requires manual migration",
                CoreEquivalent: "Microsoft.AspNetCore.Mvc.Filters.ResultFilterAttribute",
                CoreSignatures:
                    "OnResultExecuting(ResultExecutingContext context)\n" +
                    "OnResultExecuted(ResultExecutedContext context)"),
        };

    public bool IsApplicable(SyntaxTree syntaxTree)
    {
        var text = syntaxTree.ToString();
        return KnownFilterBases.Keys.Any(k => text.Contains(k, StringComparison.Ordinal));
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
                // Strip only the namespace prefix — keep the full type name including
                // "Attribute" suffix so it matches the dictionary keys exactly.
                var typeName = StripNamespace(baseType.Type.ToString());

                if (!KnownFilterBases.TryGetValue(typeName, out var info)) continue;

                var overriddenMethods = classDecl.Members
                    .OfType<MethodDeclarationSyntax>()
                    .Where(m => m.Modifiers.Any(SyntaxKind.OverrideKeyword))
                    .Select(m => m.Identifier.Text)
                    .ToList();

                var methodList = overriddenMethods.Count > 0
                    ? string.Join(", ", overriddenMethods)
                    : "no methods overridden";

                issues.Add(new MigrationIssue
                {
                    Id = info.IssueId,
                    // Include the concrete class name in the title so tests and reports show which class is affected.
                    Title = $"{info.Title} ('{classDecl.Identifier.Text}')",
                    Description =
          $"Class '{classDecl.Identifier.Text}' inherits from {typeName}. " +
          $"Methods overridden: {methodList}. " +
          "The class name and general structure are preserved but method signatures " +
          "and context property shapes differ between MVC 5 and ASP.NET Core.",
                    Severity = IssueSeverity.Warning,
                    Category = IssueCategory.AspNetApi,
                    FilePath = syntaxTree.FilePath,
                    LineNumber = classDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                    EffortHours = 2.0,
                    Recommendation =
          $"Migrate to {info.CoreEquivalent}. " +
          $"Core method signatures:\n{info.CoreSignatures}\n\n" +
          "Key differences: ActionExecutingContext.HttpContext replaces " +
          "filterContext.HttpContext. ActionDescriptor shape is different. " +
          "Register filters globally in Program.cs: " +
          "builder.Services.AddControllersWithViews(o => o.Filters.Add<YourFilter>())",
                    HelpUrl = "https://docs.microsoft.com/aspnet/core/mvc/controllers/filters"
                });

                break; // one issue per class
            }
        }

        // Tier 3: never modifies the tree.
        return Task.FromResult(new RuleResult
        {
            OutputTree = syntaxTree,
            Transformations = [],
            Issues = issues
        });
    }

    /// <summary>
    /// Strips only the namespace prefix, preserving the full type name.
    /// "System.Web.Mvc.ActionFilterAttribute" → "ActionFilterAttribute"
    /// "ActionFilterAttribute"               → "ActionFilterAttribute"
    /// "IActionFilter"                        → "IActionFilter"
    /// </summary>
    private static string StripNamespace(string fullName)
        => fullName.Split('.').Last().Trim();

    private sealed record FilterInfo(
        string IssueId,
        string Title,
        string CoreEquivalent,
        string CoreSignatures);
}
