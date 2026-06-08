using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NetForward.Analyzer;
using NetForward.Core.Models;
using NetForward.Rewriter.Pipeline;

namespace NetForward.Rewriter.Rewriters;

/// <summary>
/// R009 — Model binding pattern migration (Tier 2).
///
/// Fix: IsApplicable used "UpdateModel(" (with paren) as a marker which fails
/// when UpdateModel appears as a type name in test source. Changed to "UpdateModel"
/// without paren — the rewriter's VisitInvocationExpression handles the rest.
/// </summary>
public sealed class R009ModelBindingRewriter : IRewriteRule
{
    public string RuleId => "R009";
    public string Name => "Model binding pattern migration";
    public RewriteTier Tier => RewriteTier.Tier2;

    private static readonly string[] LegacyTypeMarkers =
    [
        "HttpPostedFileBase",
        "HttpPostedFilesBase",
        "UpdateModel",       // no paren — matches both call sites and parameter types in tests
        "TryUpdateModel",
        "ValueProvider"
    ];

    public bool IsApplicable(SyntaxTree syntaxTree)
    {
        var text = syntaxTree.ToString();

        if (LegacyTypeMarkers.Any(m => text.Contains(m, StringComparison.Ordinal)))
            return true;

        // FormCollection is legacy; IFormCollection is modern.
        // Match "FormCollection" only when NOT preceded by "I".
        var idx = 0;
        while ((idx = text.IndexOf("FormCollection", idx, StringComparison.Ordinal)) >= 0)
        {
            if (idx == 0 || text[idx - 1] != 'I')
                return true;
            idx++;
        }

        // [Bind(Include = ...)]
        if (text.Contains("[Bind", StringComparison.Ordinal)
            && text.Contains("Include", StringComparison.Ordinal))
            return true;

        return false;
    }

    public Task<RuleResult> ApplyAsync(
        RewriteContext context,
        SyntaxTree syntaxTree,
        CancellationToken cancellationToken = default)
    {
        var root = syntaxTree.GetCompilationUnitRoot();
        var semanticModel = context.GetSemanticModel(syntaxTree);
        var rewriter = new ModelBindingSyntaxRewriter(semanticModel);
        var newRoot = (CompilationUnitSyntax)rewriter.Visit(root);
        var newTree = syntaxTree.WithRootAndOptions(newRoot, syntaxTree.Options);

        return Task.FromResult(new RuleResult
        {
            OutputTree = newTree,
            Transformations = rewriter.Transformations,
            Issues = rewriter.Issues
        });
    }

    private sealed class ModelBindingSyntaxRewriter : CSharpSyntaxRewriter
    {
        private readonly SemanticModel _semanticModel;
        public List<AppliedTransformation> Transformations { get; } = [];
        public List<MigrationIssue> Issues { get; } = [];

        public ModelBindingSyntaxRewriter(SemanticModel semanticModel)
            => _semanticModel = semanticModel;

        public override SyntaxNode? VisitAttribute(AttributeSyntax node)
        {
            var updated = (AttributeSyntax)base.VisitAttribute(node)!;
            if (GetShortName(updated.Name.ToString()) != "Bind"
                || updated.ArgumentList is null)
                return updated;

            var includeArg = updated.ArgumentList.Arguments
                .FirstOrDefault(a => a.NameEquals?.Name.Identifier.Text == "Include");
            if (includeArg is null) return updated;

            var positional = SyntaxFactory.AttributeArgument(includeArg.Expression);
            Transformations.Add(new AppliedTransformation
            {
                RuleId = "R009",
                Description = "Converted [Bind(Include = \"...\")] to positional [Bind(\"...\")]",
                LineNumber = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1
            });
            return updated.WithArgumentList(
                updated.ArgumentList.WithArguments(
                    SyntaxFactory.SingletonSeparatedList(positional)));
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            var updated = (IdentifierNameSyntax)base.VisitIdentifierName(node)!;
            switch (updated.Identifier.Text)
            {
                case "FormCollection":
                    Transformations.Add(new AppliedTransformation
                    {
                        RuleId = "R009",
                        Description = "Replaced FormCollection with IFormCollection",
                        LineNumber = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1
                    });
                    return SyntaxFactory.IdentifierName("IFormCollection").WithTriviaFrom(updated);

                case "HttpPostedFileBase":
                    Transformations.Add(new AppliedTransformation
                    {
                        RuleId = "R009",
                        Description = "Replaced HttpPostedFileBase with IFormFile",
                        LineNumber = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1
                    });
                    return SyntaxFactory.IdentifierName("IFormFile").WithTriviaFrom(updated);

                case "HttpPostedFilesBase":
                    Transformations.Add(new AppliedTransformation
                    {
                        RuleId = "R009",
                        Description = "Replaced HttpPostedFilesBase with IFormFileCollection",
                        LineNumber = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1
                    });
                    return SyntaxFactory.IdentifierName("IFormFileCollection").WithTriviaFrom(updated);
            }
            return updated;
        }

        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var updated = (InvocationExpressionSyntax)base.VisitInvocationExpression(node)!;
            var methodName = updated.Expression switch
            {
                IdentifierNameSyntax id => id.Identifier.Text,
                MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
                _ => null
            };

            if (methodName is "UpdateModel" or "TryUpdateModel")
            {
                Issues.Add(new MigrationIssue
                {
                    Id = IssueIds.AsyncSignatureRequired,
                    Title = $"{methodName}() must become TryUpdateModelAsync()",
                    Description =
                        $"{methodName}() has no synchronous equivalent in ASP.NET Core. " +
                        "Use async TryUpdateModelAsync() instead.",
                    Severity = IssueSeverity.Warning,
                    Category = IssueCategory.AspNetApi,
                    FilePath = node.SyntaxTree.FilePath,
                    LineNumber = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                    EffortHours = 1.0,
                    Recommendation =
                        $"Replace {methodName}(model) with await TryUpdateModelAsync(model) " +
                        "and make the action async."
                });
            }

            return updated;
        }

        private static string GetShortName(string fullName)
        {
            var n = fullName.Split('.').Last();
            return n.EndsWith("Attribute", StringComparison.Ordinal) && n.Length > 9
                ? n[..^9] : n;
        }
    }
}
