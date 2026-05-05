using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NetForward.Core.Models;
using NetForward.Rewriter.Pipeline;

namespace NetForward.Rewriter.Rewriters;

/// <summary>
/// R001 — Namespace rewriter.
///
/// Rewrites legacy ASP.NET using directives to their ASP.NET Core equivalents.
/// This is the first rule applied and is a prerequisite for every other rule —
/// once namespaces are correct, the semantic model resolves types properly.
///
/// Mappings applied:
///   System.Web.Mvc                     → Microsoft.AspNetCore.Mvc
///   System.Web.Http                    → Microsoft.AspNetCore.Mvc
///   System.Web.Http.Results            → Microsoft.AspNetCore.Mvc
///   System.Web.Routing                 → Microsoft.AspNetCore.Routing
///   System.Web.Security                → Microsoft.AspNetCore.Identity
///   System.Web                         → Microsoft.AspNetCore.Http  (only when Http types used)
///   System.Configuration               → Microsoft.Extensions.Configuration
/// </summary>
public sealed class R001NamespaceRewriter : IRewriteRule
{
    public string RuleId => "R001";
    public string Name => "Namespace rewriter";
    public RewriteTier Tier => RewriteTier.Tier1;

    // Map of legacy namespace → modern namespace.
    // Ordered: more specific first so we don't partially match.
    private static readonly IReadOnlyList<(string Legacy, string Modern)> NamespaceMappings =
    [
        ("System.Web.Mvc.Html",         "Microsoft.AspNetCore.Mvc.ViewFeatures"),
        ("System.Web.Mvc.Ajax",         "Microsoft.AspNetCore.Mvc"),
        ("System.Web.Mvc.Filters",      "Microsoft.AspNetCore.Mvc.Filters"),
        ("System.Web.Mvc",              "Microsoft.AspNetCore.Mvc"),
        ("System.Web.Http.Results",     "Microsoft.AspNetCore.Mvc"),
        ("System.Web.Http.Filters",     "Microsoft.AspNetCore.Mvc.Filters"),
        ("System.Web.Http.ModelBinding","Microsoft.AspNetCore.Mvc.ModelBinding"),
        ("System.Web.Http",             "Microsoft.AspNetCore.Mvc"),
        ("System.Web.Routing",          "Microsoft.AspNetCore.Routing"),
        ("System.Web.Security",         "Microsoft.AspNetCore.Identity"),
        ("System.Configuration",        "Microsoft.Extensions.Configuration"),
    ];

    public bool IsApplicable(SyntaxTree syntaxTree)
    {
        var text = syntaxTree.ToString();
        return text.Contains("System.Web.Mvc")
            || text.Contains("System.Web.Http")
            || text.Contains("System.Web.Routing")
            || text.Contains("System.Web.Security")
            || text.Contains("System.Configuration");
    }

    public Task<RuleResult> ApplyAsync(
        RewriteContext context,
        SyntaxTree syntaxTree,
        CancellationToken cancellationToken = default)
    {
        var root = syntaxTree.GetCompilationUnitRoot();
        var rewriter = new NamespaceSyntaxRewriter();
        var newRoot = (CompilationUnitSyntax)rewriter.Visit(root);

        var newTree = syntaxTree.WithRootAndOptions(newRoot, syntaxTree.Options);

        return Task.FromResult(new RuleResult
        {
            OutputTree = newTree,
            Transformations = rewriter.Transformations,
            Issues = []
        });
    }

    private sealed class NamespaceSyntaxRewriter : CSharpSyntaxRewriter
    {
        public List<AppliedTransformation> Transformations { get; } = [];

        public override SyntaxNode? VisitUsingDirective(UsingDirectiveSyntax node)
        {
            var nameText = node.Name?.ToString() ?? "";

            foreach (var (legacy, modern) in NamespaceMappings)
            {
                if (!string.Equals(nameText, legacy, StringComparison.Ordinal)
                    && !nameText.StartsWith(legacy + ".", StringComparison.Ordinal))
                {
                    continue;
                }

                // Replace the matched prefix with the modern equivalent.
                var modernName = nameText.Length == legacy.Length
                    ? modern
                    : modern + nameText[legacy.Length..];

                var modernNameSyntax = SyntaxFactory.ParseName(modernName)
                    .WithTriviaFrom(node.Name!);

                var newNode = node.WithName(modernNameSyntax);

                Transformations.Add(new AppliedTransformation
                {
                    RuleId = "R001",
                    Description = $"Replaced using {nameText} with {modernName}",
                    LineNumber = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1
                });

                return newNode;
            }

            return base.VisitUsingDirective(node);
        }
    }
}
