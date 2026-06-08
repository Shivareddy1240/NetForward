using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NetForward.Core.Models;
using NetForward.Rewriter.Pipeline;

namespace NetForward.Rewriter.Rewriters;

/// <summary>
/// R004 — Attribute conversions.
///
/// Handles attributes that exist in both frameworks but need namespace
/// corrections or minor signature changes after R001 runs.
///
/// Transformations:
///   [ValidateAntiForgeryToken]     → unchanged (exists in Core, R001 fixes namespace)
///   [AllowAnonymous]               → unchanged (exists in Core, R001 fixes namespace)
///   [Authorize]                    → unchanged (exists in Core, R001 fixes namespace)
///   [FromBody]                     → unchanged (built-in to Core)
///   [FromUri]                      → [FromQuery]  (Web API 2 specific, no Core equivalent)
///   [FromHeader(Name="x")]         → unchanged
///   [ActionName("x")]              → unchanged
///   [NonAction]                    → unchanged
///   [ChildActionOnly]              → REMOVED (no Core equivalent, flagged NF404)
///   [RequireHttps]                 → REMOVED (use app.UseHttpsRedirection(), flagged NF404)
///   [OutputCache(...)]             → REMOVED (use IMemoryCache/Response caching, flagged NF405)
///   [HandleError]                  → REMOVED (use exception handling middleware, flagged NF406)
/// </summary>
public sealed class R004AttributeConversionRewriter : IRewriteRule
{
    public string RuleId => "R004";
    public string Name => "Attribute conversions";
    public RewriteTier Tier => RewriteTier.Tier1;

    // Attributes that are simply renamed.
    private static readonly Dictionary<string, string> Renames =
        new(StringComparer.Ordinal)
        {
            ["FromUri"] = "FromQuery",
        };

    // Attributes that are removed with a warning issue raised.
    private static readonly Dictionary<string, (string IssueId, string Title, string Guidance)> Removals =
        new(StringComparer.Ordinal)
        {
            ["ChildActionOnly"] = (
                "NF404",
                "[ChildActionOnly] removed",
                "ChildActionOnly has no equivalent in ASP.NET Core. Refactor to a partial view component or ViewComponent."),

            ["RequireHttps"] = (
                "NF404",
                "[RequireHttps] removed",
                "Use app.UseHttpsRedirection() in Program.cs instead of per-action [RequireHttps]."),

            ["HandleError"] = (
                "NF406",
                "[HandleError] removed",
                "Replace with exception handling middleware: app.UseExceptionHandler() in Program.cs."),

            ["OutputCache"] = (
                "NF405",
                "[OutputCache] removed",
                "Use ASP.NET Core Response Caching middleware or IMemoryCache. " +
                "Add builder.Services.AddResponseCaching() and app.UseResponseCaching() in Program.cs."),
        };

    public bool IsApplicable(SyntaxTree syntaxTree)
    {
        var text = syntaxTree.ToString();
        return text.Contains("FromUri")
            || text.Contains("ChildActionOnly")
            || text.Contains("RequireHttps")
            || text.Contains("HandleError")
            || text.Contains("OutputCache");
    }

    public Task<RuleResult> ApplyAsync(
        RewriteContext context,
        SyntaxTree syntaxTree,
        CancellationToken cancellationToken = default)
    {
        var root = syntaxTree.GetCompilationUnitRoot();
        var rewriter = new AttributeSyntaxRewriter();
        var newRoot = (CompilationUnitSyntax)rewriter.Visit(root);
        var newTree = syntaxTree.WithRootAndOptions(newRoot, syntaxTree.Options);

        return Task.FromResult(new RuleResult
        {
            OutputTree = newTree,
            Transformations = rewriter.Transformations,
            Issues = rewriter.Issues
        });
    }

    private sealed class AttributeSyntaxRewriter : CSharpSyntaxRewriter
    {
        public List<AppliedTransformation> Transformations { get; } = [];
        public List<MigrationIssue> Issues { get; } = [];

        public override SyntaxNode? VisitAttribute(AttributeSyntax node)
        {
            var name = GetAttributeShortName(node.Name.ToString());

            // ---- Renames ----
            if (Renames.TryGetValue(name, out var modernName))
            {
                var newName = SyntaxFactory.ParseName(modernName)
                    .WithTriviaFrom(node.Name);

                Transformations.Add(new AppliedTransformation
                {
                    RuleId = "R004",
                    Description = $"Renamed [{name}] to [{modernName}]",
                    LineNumber = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1
                });

                return node.WithName(newName);
            }

            // ---- Removals ----
            if (Removals.TryGetValue(name, out var removal))
            {
                Issues.Add(new MigrationIssue
                {
                    Id = removal.IssueId,
                    Title = removal.Title,
                    Description = removal.Guidance,
                    Severity = IssueSeverity.Warning,
                    Category = IssueCategory.AspNetApi,
                    FilePath = node.SyntaxTree.FilePath,
                    LineNumber = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                    EffortHours = 1.0,
                    Recommendation = removal.Guidance
                });

                Transformations.Add(new AppliedTransformation
                {
                    RuleId = "R004",
                    Description = $"Removed [{name}] — {removal.Title}",
                    LineNumber = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1
                });

                // Return null removes the attribute node from the tree.
                return null;
            }

            return base.VisitAttribute(node);
        }

        /// <summary>
        /// Strip generic args and namespace prefix: "System.Web.Mvc.OutputCache" → "OutputCache"
        /// </summary>
        private static string GetAttributeShortName(string fullName)
        {
            var name = fullName;
            var dotIndex = name.LastIndexOf('.');
            if (dotIndex >= 0) name = name[(dotIndex + 1)..];

            // Strip trailing "Attribute" suffix if present.
            if (name.EndsWith("Attribute", StringComparison.Ordinal) && name.Length > "Attribute".Length)
                name = name[..^"Attribute".Length];

            return name;
        }
    }
}
