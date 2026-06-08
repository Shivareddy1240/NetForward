using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NetForward.Core.Models;
using NetForward.Rewriter.Pipeline;

namespace NetForward.Rewriter.Rewriters;

/// <summary>
/// R005 — RoutePrefix consolidation.
///
/// ASP.NET MVC 5 / Web API 2 pattern:
///   [RoutePrefix("api/orders")]
///   public class OrdersController : ApiController
///   {
///       [Route("")]        → GET api/orders
///       [Route("{id}")]    → GET api/orders/{id}
///       [Route("active")]  → GET api/orders/active
///   }
///
/// ASP.NET Core has no [RoutePrefix]. The prefix must be merged into each
/// action's [Route] attribute:
///   [Route("api/orders")]
///   public class OrdersController : ControllerBase   (R002 handles base class)
///   {
///       [Route("")]        → stays as-is (inherits controller route)
///       [Route("{id}")]    → stays as-is
///       [Route("active")]  → stays as-is
///   }
///
/// Special cases:
///   Action [Route("")] stays as "" — means "same as controller route".
///   Action with NO [Route] gets no change.
///   If controller already has [Route] (not RoutePrefix), leave it alone.
///   Tilde prefix [Route("~/absolute")] overrides the controller route — leave as-is.
/// </summary>
public sealed class R005RoutePrefixRewriter : IRewriteRule
{
    public string RuleId => "R005";
    public string Name => "RoutePrefix consolidation";
    public RewriteTier Tier => RewriteTier.Tier1;

    public bool IsApplicable(SyntaxTree syntaxTree)
        => syntaxTree.ToString().Contains("RoutePrefix");

    public Task<RuleResult> ApplyAsync(
        RewriteContext context,
        SyntaxTree syntaxTree,
        CancellationToken cancellationToken = default)
    {
        var root = syntaxTree.GetCompilationUnitRoot();
        var rewriter = new RoutePrefixSyntaxRewriter();
        var newRoot = (CompilationUnitSyntax)rewriter.Visit(root);
        var newTree = syntaxTree.WithRootAndOptions(newRoot, syntaxTree.Options);

        return Task.FromResult(new RuleResult
        {
            OutputTree = newTree,
            Transformations = rewriter.Transformations,
            Issues = rewriter.Issues
        });
    }

    private sealed class RoutePrefixSyntaxRewriter : CSharpSyntaxRewriter
    {
        public List<AppliedTransformation> Transformations { get; } = [];
        public List<MigrationIssue> Issues { get; } = [];

        public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            // Find [RoutePrefix("...")] on this class.
            var routePrefixAttr = FindAttribute(node.AttributeLists, "RoutePrefix");
            if (routePrefixAttr is null)
                return base.VisitClassDeclaration(node);

            var prefixValue = GetFirstStringArgument(routePrefixAttr);
            if (prefixValue is null)
            {
                // Can't extract prefix value — flag it and leave.
                Issues.Add(new MigrationIssue
                {
                    Id = "NF407",
                    Title = "[RoutePrefix] could not be auto-consolidated",
                    Description = "RoutePrefix attribute was found but its argument could not be parsed. Manually convert to [Route] on the controller.",
                    Severity = IssueSeverity.Warning,
                    Category = IssueCategory.AspNetApi,
                    FilePath = node.SyntaxTree.FilePath,
                    LineNumber = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                    EffortHours = 0.5
                });
                return base.VisitClassDeclaration(node);
            }

            // Remove [RoutePrefix] from the class.
            var newAttributeLists = RemoveAttribute(node.AttributeLists, "RoutePrefix");

            // Add [Route("prefix")] to the class instead.
            var routeAttr = BuildRouteAttribute(prefixValue);
            var routeAttrList = SyntaxFactory.AttributeList(
                    SyntaxFactory.SingletonSeparatedList(routeAttr))
                .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);

            newAttributeLists = newAttributeLists.Insert(0, routeAttrList);

            var updatedClass = node
                .WithAttributeLists(newAttributeLists);

            Transformations.Add(new AppliedTransformation
            {
                RuleId = "R005",
                Description = $"Replaced [RoutePrefix(\"{prefixValue}\")] with [Route(\"{prefixValue}\")] on {node.Identifier.Text}",
                LineNumber = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1
            });

            // Now visit children (methods) with the base visitor — their [Route] attrs stay as-is.
            return base.VisitClassDeclaration(updatedClass);
        }

        private static AttributeSyntax? FindAttribute(
            SyntaxList<AttributeListSyntax> lists, string shortName)
        {
            return lists
                .SelectMany(l => l.Attributes)
                .FirstOrDefault(a => GetShortName(a.Name.ToString()) == shortName);
        }

        private static SyntaxList<AttributeListSyntax> RemoveAttribute(
            SyntaxList<AttributeListSyntax> lists, string shortName)
        {
            var result = new List<AttributeListSyntax>();
            foreach (var list in lists)
            {
                var remaining = list.Attributes
                    .Where(a => GetShortName(a.Name.ToString()) != shortName)
                    .ToArray();

                if (remaining.Length == 0) continue; // drop the entire list

                var newList = list.WithAttributes(
                    SyntaxFactory.SeparatedList(remaining));
                result.Add(newList);
            }
            return SyntaxFactory.List(result);
        }

        private static string? GetFirstStringArgument(AttributeSyntax attr)
        {
            var arg = attr.ArgumentList?.Arguments.FirstOrDefault();
            if (arg is null) return null;

            // Handle string literals: "api/orders"
            if (arg.Expression is LiteralExpressionSyntax lit
                && lit.IsKind(SyntaxKind.StringLiteralExpression))
            {
                return lit.Token.ValueText;
            }

            return null;
        }

        private static AttributeSyntax BuildRouteAttribute(string prefix)
        {
            var argument = SyntaxFactory.AttributeArgument(
                SyntaxFactory.LiteralExpression(
                    SyntaxKind.StringLiteralExpression,
                    SyntaxFactory.Literal(prefix)));

            return SyntaxFactory.Attribute(
                SyntaxFactory.IdentifierName("Route"),
                SyntaxFactory.AttributeArgumentList(
                    SyntaxFactory.SingletonSeparatedList(argument)));
        }

        private static string GetShortName(string fullName)
        {
            var name = fullName;
            var dot = name.LastIndexOf('.');
            if (dot >= 0) name = name[(dot + 1)..];
            if (name.EndsWith("Attribute", StringComparison.Ordinal)
                && name.Length > "Attribute".Length)
                name = name[..^"Attribute".Length];
            return name;
        }
    }
}
