using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NetForward.Core.Models;
using NetForward.Rewriter.Pipeline;

namespace NetForward.Rewriter.Rewriters;

/// <summary>
/// R002 — Controller base class swap.
///
/// Handles two cases:
///
/// 1. MVC 5 controllers: class FooController : Controller
///    → Already correct in ASP.NET Core MVC (after R001 fixes the namespace).
///      But we verify the inheritance and add [ApiController] where appropriate.
///
/// 2. Web API 2 controllers: class FooController : ApiController
///    → class FooController : ControllerBase
///      + adds [ApiController] attribute if not present
///      + adds [Route("[controller]")] if no route attribute present
///
/// Must run AFTER R001 so namespace resolution is already correct.
/// </summary>
public sealed class R002ControllerBaseRewriter : IRewriteRule
{
    public string RuleId => "R002";
    public string Name => "Controller base class swap";
    public RewriteTier Tier => RewriteTier.Tier1;

    public bool IsApplicable(SyntaxTree syntaxTree)
    {
        var text = syntaxTree.ToString();
        return text.Contains("ApiController") || text.Contains(": Controller");
    }

    public Task<RuleResult> ApplyAsync(
        RewriteContext context,
        SyntaxTree syntaxTree,
        CancellationToken cancellationToken = default)
    {
        var root = syntaxTree.GetCompilationUnitRoot();
        var semanticModel = context.GetSemanticModel(syntaxTree);
        var rewriter = new ControllerSyntaxRewriter(semanticModel);
        var newRoot = (CompilationUnitSyntax)rewriter.Visit(root);
        var newTree = syntaxTree.WithRootAndOptions(newRoot, syntaxTree.Options);

        return Task.FromResult(new RuleResult
        {
            OutputTree = newTree,
            Transformations = rewriter.Transformations,
            Issues = []
        });
    }

    private sealed class ControllerSyntaxRewriter : CSharpSyntaxRewriter
    {
        private readonly SemanticModel _semanticModel;
        public List<AppliedTransformation> Transformations { get; } = [];

        public ControllerSyntaxRewriter(SemanticModel semanticModel)
        {
            _semanticModel = semanticModel;
        }

        public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            var updatedNode = (ClassDeclarationSyntax)base.VisitClassDeclaration(node)!;

            if (updatedNode.BaseList is null) return updatedNode;

            var newBases = new List<BaseTypeSyntax>();
            bool changedApiController = false;
            bool hasApiControllerAttribute = HasAttribute(updatedNode, "ApiController");
            bool hasRouteAttribute = HasAttribute(updatedNode, "Route");

            foreach (var baseType in updatedNode.BaseList.Types)
            {
                var typeName = baseType.Type.ToString().Trim();

                if (typeName is "ApiController" or "System.Web.Http.ApiController")
                {
                    // Web API 2 → ASP.NET Core: ApiController → ControllerBase
                    var controllerBase = SyntaxFactory.SimpleBaseType(
                        SyntaxFactory.IdentifierName("ControllerBase")
                            .WithTriviaFrom(baseType.Type));

                    newBases.Add(baseType.WithType(controllerBase.Type));
                    changedApiController = true;

                    Transformations.Add(new AppliedTransformation
                    {
                        RuleId = "R002",
                        Description = $"Changed base class from ApiController to ControllerBase on {node.Identifier.Text}",
                        LineNumber = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1
                    });
                }
                else
                {
                    newBases.Add(baseType);
                }
            }

            if (!changedApiController) return updatedNode;

            var result = updatedNode.WithBaseList(
                updatedNode.BaseList.WithTypes(
                    SyntaxFactory.SeparatedList(newBases)));

            // Add [ApiController] attribute if not present.
            if (!hasApiControllerAttribute)
            {
                result = AddAttribute(result, "ApiController");
                Transformations.Add(new AppliedTransformation
                {
                    RuleId = "R002",
                    Description = $"Added [ApiController] attribute to {node.Identifier.Text}",
                    LineNumber = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1
                });
            }

            // Add [Route("[controller]")] if no route attribute present.
            if (!hasRouteAttribute)
            {
                result = AddAttribute(result, @"Route(""[controller]"")");
                Transformations.Add(new AppliedTransformation
                {
                    RuleId = "R002",
                    Description = $"Added [Route(\"[controller]\")] attribute to {node.Identifier.Text}",
                    LineNumber = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1
                });
            }

            return result;
        }

        private static bool HasAttribute(ClassDeclarationSyntax node, string attributeName)
        {
            return node.AttributeLists
                .SelectMany(al => al.Attributes)
                .Any(a => a.Name.ToString().Contains(attributeName));
        }

        private static ClassDeclarationSyntax AddAttribute(
            ClassDeclarationSyntax node, string attributeText)
        {
            var attribute = SyntaxFactory.Attribute(SyntaxFactory.ParseName(attributeText));
            var attributeList = SyntaxFactory.AttributeList(
                SyntaxFactory.SingletonSeparatedList(attribute))
                .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);

            return node.AddAttributeLists(attributeList);
        }
    }
}
