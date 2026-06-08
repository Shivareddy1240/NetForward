using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NetForward.Core.Models;
using NetForward.Rewriter.Pipeline;

namespace NetForward.Rewriter.Rewriters;

/// <summary>
/// R006 — Deep IHttpActionResult pass.
///
/// Handles cases R003 misses:
///   Task&lt;IHttpActionResult&gt;  → Task&lt;IActionResult&gt;
///   Remaining bare IHttpActionResult identifiers → IActionResult
///   ResponseMessage(...)       → flagged NF408
///   InternalServerError(ex)    → flagged NF409
///
/// Key design: VisitGenericName inspects the ORIGINAL node's type arguments
/// BEFORE calling base (which visits children). If the argument is
/// IHttpActionResult, the span start is added to a suppression set so that
/// VisitIdentifierName does not also fire on the same node.
/// </summary>
public sealed class R006IHttpActionResultRewriter : IRewriteRule
{
    public string RuleId => "R006";
    public string Name => "Deep IHttpActionResult pass";
    public RewriteTier Tier => RewriteTier.Tier1;

    public bool IsApplicable(SyntaxTree syntaxTree)
    {
        var text = syntaxTree.ToString();
        return text.Contains("IHttpActionResult")
            || text.Contains("ResponseMessage");
    }

    public Task<RuleResult> ApplyAsync(
        RewriteContext context,
        SyntaxTree syntaxTree,
        CancellationToken cancellationToken = default)
    {
        var root = syntaxTree.GetCompilationUnitRoot();
        var semanticModel = context.GetSemanticModel(syntaxTree);
        var rewriter = new DeepActionResultRewriter(semanticModel);
        var newRoot = (CompilationUnitSyntax)rewriter.Visit(root);
        var newTree = syntaxTree.WithRootAndOptions(newRoot, syntaxTree.Options);

        return Task.FromResult(new RuleResult
        {
            OutputTree = newTree,
            Transformations = rewriter.Transformations,
            Issues = rewriter.Issues
        });
    }

    private sealed class DeepActionResultRewriter : CSharpSyntaxRewriter
    {
        private readonly SemanticModel _semanticModel;

        /// <summary>
        /// Span starts of IHttpActionResult nodes that are type arguments inside
        /// Task&lt;&gt; already handled by VisitGenericName.
        /// VisitIdentifierName must skip these.
        /// </summary>
        private readonly HashSet<int> _suppressedSpanStarts = [];

        public List<AppliedTransformation> Transformations { get; } = [];
        public List<MigrationIssue> Issues { get; } = [];

        public DeepActionResultRewriter(SemanticModel semanticModel)
        {
            _semanticModel = semanticModel;
        }

        // ---- Task<IHttpActionResult> → Task<IActionResult> -----------------

        public override SyntaxNode? VisitGenericName(GenericNameSyntax node)
        {
            // Check the ORIGINAL node BEFORE visiting children.
            // base.VisitGenericName would fire VisitIdentifierName on the
            // type argument first, beating us to the node.
            if (node.Identifier.Text.Equals("Task", StringComparison.Ordinal)
                && node.TypeArgumentList.Arguments.Count == 1)
            {
                var originalArg = node.TypeArgumentList.Arguments[0];
                var argText = originalArg.ToString().Trim();

                if (argText is "IHttpActionResult" or "System.Web.Http.IHttpActionResult")
                {
                    // Suppress BEFORE base.Visit fires VisitIdentifierName.
                    _suppressedSpanStarts.Add(originalArg.SpanStart);

                    // Visit children with suppression already registered.
                    var visited = (GenericNameSyntax)base.VisitGenericName(node)!;

                    // Build the replacement type argument.
                    var newArg = SyntaxFactory.ParseTypeName("IActionResult")
                        .WithTriviaFrom(visited.TypeArgumentList.Arguments[0]);

                    var newTypeArgList = visited.TypeArgumentList.WithArguments(
                        SyntaxFactory.SingletonSeparatedList(newArg));

                    Transformations.Add(new AppliedTransformation
                    {
                        RuleId = "R006",
                        Description = "Changed Task<IHttpActionResult> to Task<IActionResult>",
                        LineNumber = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1
                    });

                    return visited.WithTypeArgumentList(newTypeArgList);
                }
            }

            return base.VisitGenericName(node);
        }

        // ---- Remaining bare IHttpActionResult identifiers → IActionResult --

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            if (!node.Identifier.Text.Equals("IHttpActionResult", StringComparison.Ordinal))
                return base.VisitIdentifierName(node);

            // Skip nodes already handled by VisitGenericName above.
            if (_suppressedSpanStarts.Contains(node.SpanStart))
                return base.VisitIdentifierName(node);

            Transformations.Add(new AppliedTransformation
            {
                RuleId = "R006",
                Description = "Changed IHttpActionResult to IActionResult",
                LineNumber = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1
            });

            return SyntaxFactory.IdentifierName("IActionResult")
                .WithTriviaFrom(node);
        }

        // ---- ResponseMessage(...) → flag NF408 -----------------------------

        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var updated = (InvocationExpressionSyntax)base.VisitInvocationExpression(node)!;

            var methodName = updated.Expression switch
            {
                IdentifierNameSyntax id => id.Identifier.Text,
                MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
                _ => null
            };

            if (methodName is "ResponseMessage")
            {
                Issues.Add(new MigrationIssue
                {
                    Id = "NF408",
                    Title = "ResponseMessage() cannot be auto-migrated",
                    Description =
                        "ResponseMessage(HttpResponseMessage) has no direct ASP.NET Core equivalent. " +
                        "Use StatusCode(), File(), Content(), or a custom IActionResult instead.",
                    Severity = IssueSeverity.Error,
                    Category = IssueCategory.AspNetApi,
                    FilePath = node.SyntaxTree.FilePath,
                    LineNumber = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                    EffortHours = 2.0,
                    Recommendation =
                        "Replace ResponseMessage(response) with Ok(), File(), StatusCode(), " +
                        "or a custom ActionResult<T>."
                });
            }

            if (methodName is "InternalServerError"
                && updated.ArgumentList.Arguments.Count > 0)
            {
                Issues.Add(new MigrationIssue
                {
                    Id = "NF409",
                    Title = "InternalServerError(exception) requires manual migration",
                    Description =
                        "InternalServerError(Exception) was used. In ASP.NET Core, exceptions " +
                        "are handled by middleware. Re-throw or use a problem details response.",
                    Severity = IssueSeverity.Warning,
                    Category = IssueCategory.AspNetApi,
                    FilePath = node.SyntaxTree.FilePath,
                    LineNumber = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                    EffortHours = 1.0,
                    Recommendation =
                        "Replace with throw or StatusCode(500) and log via ILogger."
                });
            }

            return updated;
        }
    }
}
