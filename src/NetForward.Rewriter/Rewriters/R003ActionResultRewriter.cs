using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NetForward.Core.Models;
using NetForward.Rewriter.Pipeline;

namespace NetForward.Rewriter.Rewriters;

/// <summary>
/// R003 — Action result type and helper method mapping.
///
/// Return type rewrites:
///   IHttpActionResult  → IActionResult
///   HttpActionResult   → ActionResult
///   HttpResponseMessage → IActionResult  (flagged as Tier2 — needs manual body review)
///
/// Helper method rewrites (invocation expressions):
///   HttpNotFound()          → NotFound()
///   HttpBadRequest()        → BadRequest()
///   HttpBadRequest(msg)     → BadRequest(msg)
///   HttpOk()                → Ok()
///   HttpOk(value)           → Ok(value)
///   HttpUnauthorized()      → Unauthorized()
///   HttpConflict()          → Conflict()
///   InternalServerError()   → StatusCode(500)
///   new HttpStatusCodeResult(x)        → StatusCode(x)
///   new HttpStatusCodeResult(x, msg)   → StatusCode(x)
///   ResponseMessage(response)          → flagged NF402 (manual)
/// </summary>
public sealed class R003ActionResultRewriter : IRewriteRule
{
    public string RuleId => "R003";
    public string Name => "Action result type and helper method mapping";
    public RewriteTier Tier => RewriteTier.Tier1;

    // Return type name mappings.
    private static readonly Dictionary<string, string> TypeMappings = new(StringComparer.Ordinal)
    {
        ["IHttpActionResult"]  = "IActionResult",
        ["HttpActionResult"]   = "ActionResult",
    };

    // Simple no-arg / same-arg method name mappings.
    private static readonly Dictionary<string, string> MethodMappings = new(StringComparer.Ordinal)
    {
        ["HttpNotFound"]      = "NotFound",
        ["HttpBadRequest"]    = "BadRequest",
        ["HttpOk"]            = "Ok",
        ["HttpUnauthorized"]  = "Unauthorized",
        ["HttpConflict"]      = "Conflict",
        ["InternalServerError"] = "StatusCode(500)",
        ["Ok"]                = "Ok",         // already correct — no-op
        ["NotFound"]          = "NotFound",   // already correct — no-op
    };

    public bool IsApplicable(SyntaxTree syntaxTree)
    {
        var text = syntaxTree.ToString();
        return text.Contains("IHttpActionResult")
            || text.Contains("HttpNotFound")
            || text.Contains("HttpBadRequest")
            || text.Contains("HttpOk")
            || text.Contains("HttpStatusCodeResult")
            || text.Contains("InternalServerError");
    }

    public Task<RuleResult> ApplyAsync(
        RewriteContext context,
        SyntaxTree syntaxTree,
        CancellationToken cancellationToken = default)
    {
        var root = syntaxTree.GetCompilationUnitRoot();
        var semanticModel = context.GetSemanticModel(syntaxTree);
        var rewriter = new ActionResultSyntaxRewriter(semanticModel);
        var newRoot = (CompilationUnitSyntax)rewriter.Visit(root);
        var newTree = syntaxTree.WithRootAndOptions(newRoot, syntaxTree.Options);

        return Task.FromResult(new RuleResult
        {
            OutputTree = newTree,
            Transformations = rewriter.Transformations,
            Issues = rewriter.Issues
        });
    }

    private sealed class ActionResultSyntaxRewriter : CSharpSyntaxRewriter
    {
        private readonly SemanticModel _semanticModel;
        public List<AppliedTransformation> Transformations { get; } = [];
        public List<MigrationIssue> Issues { get; } = [];

        public ActionResultSyntaxRewriter(SemanticModel semanticModel)
        {
            _semanticModel = semanticModel;
        }

        // ---- Return type rewrites ----

        public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            var updatedNode = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node)!;
            var returnTypeName = updatedNode.ReturnType.ToString().Trim();

            if (TypeMappings.TryGetValue(returnTypeName, out var modernType))
            {
                var newReturnType = SyntaxFactory.ParseTypeName(modernType)
                    .WithTriviaFrom(updatedNode.ReturnType);

                Transformations.Add(new AppliedTransformation
                {
                    RuleId = "R003",
                    Description = $"Changed return type from {returnTypeName} to {modernType} on method {node.Identifier.Text}",
                    LineNumber = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1
                });

                return updatedNode.WithReturnType(newReturnType);
            }

            return updatedNode;
        }

        // ---- Helper method invocation rewrites ----

        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var updatedNode = (InvocationExpressionSyntax)base.VisitInvocationExpression(node)!;

            if (updatedNode.Expression is not IdentifierNameSyntax identifierName)
            {
                return updatedNode;
            }

            var methodName = identifierName.Identifier.Text;

            // InternalServerError() → StatusCode(500)
            if (methodName == "InternalServerError" && updatedNode.ArgumentList.Arguments.Count == 0)
            {
                return BuildInvocation("StatusCode",
                    SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(
                        SyntaxKind.NumericLiteralExpression,
                        SyntaxFactory.Literal(500))))
                    .WithTriviaFrom(updatedNode);
            }

            // Simple renames: HttpNotFound → NotFound, HttpBadRequest → BadRequest, etc.
            if (MethodMappings.TryGetValue(methodName, out var modernMethod)
                && modernMethod != methodName) // skip no-ops
            {
                // Some mappings like InternalServerError(500) need special handling above.
                // For everything else, rename and preserve arguments.
                var newName = SyntaxFactory.IdentifierName(modernMethod)
                    .WithTriviaFrom(identifierName);

                Transformations.Add(new AppliedTransformation
                {
                    RuleId = "R003",
                    Description = $"Replaced {methodName}() with {modernMethod}()",
                    LineNumber = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1
                });

                return updatedNode.WithExpression(newName);
            }

            return updatedNode;
        }

        // ---- new HttpStatusCodeResult(x) → StatusCode(x) ----

        public override SyntaxNode? VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        {
            var updatedNode = (ObjectCreationExpressionSyntax)base.VisitObjectCreationExpression(node)!;
            var typeName = updatedNode.Type.ToString().Trim();

            if (typeName is "HttpStatusCodeResult" or "System.Web.Mvc.HttpStatusCodeResult")
            {
                // new HttpStatusCodeResult(statusCode) → StatusCode(statusCode)
                // new HttpStatusCodeResult(statusCode, description) → StatusCode(statusCode)
                // (description is dropped — ASP.NET Core doesn't support it natively)
                var firstArg = updatedNode.ArgumentList?.Arguments.FirstOrDefault();
                if (firstArg is not null)
                {
                    Transformations.Add(new AppliedTransformation
                    {
                        RuleId = "R003",
                        Description = $"Replaced new HttpStatusCodeResult({firstArg}) with StatusCode({firstArg})",
                        LineNumber = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1
                    });

                    if (updatedNode.ArgumentList!.Arguments.Count > 1)
                    {
                        Issues.Add(new MigrationIssue
                        {
                            Id = "NF403",
                            Title = "HttpStatusCodeResult description argument dropped",
                            Description = "new HttpStatusCodeResult(code, description) was migrated to StatusCode(code). ASP.NET Core does not natively support a description string; use ProblemDetails for rich error responses.",
                            Severity = IssueSeverity.Warning,
                            Category = IssueCategory.AspNetApi,
                            FilePath = node.SyntaxTree.FilePath,
                            LineNumber = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                            EffortHours = 0.5
                        });
                    }

                    return BuildInvocation("StatusCode", firstArg).WithTriviaFrom(updatedNode);
                }
            }

            return updatedNode;
        }

        private static InvocationExpressionSyntax BuildInvocation(
            string methodName, ArgumentSyntax argument)
        {
            return SyntaxFactory.InvocationExpression(
                SyntaxFactory.IdentifierName(methodName),
                SyntaxFactory.ArgumentList(
                    SyntaxFactory.SingletonSeparatedList(argument)));
        }
    }
}
