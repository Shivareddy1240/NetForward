using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NetForward.Analyzer;
using NetForward.Core.Models;
using NetForward.Rewriter.Pipeline;

namespace NetForward.Rewriter.Rewriters;

/// <summary>
/// R008 — HttpContext.Current → IHttpContextAccessor (Tier 2).
///
/// Fixes vs previous version:
/// 1. Field and constructor token trivia: explicit Space trivia between keywords.
/// 2. StaticMethodIssues are now merged into the Issues returned from ApplyAsync
///    so Raises_NF401_for_static_method test passes.
/// </summary>
public sealed class R008HttpContextRewriter : IRewriteRule
{
    public string RuleId => "R008";
    public string Name => "HttpContext.Current → IHttpContextAccessor";
    public RewriteTier Tier => RewriteTier.Tier2;

    private const string AccessorTypeName = "IHttpContextAccessor";
    private const string AccessorFieldName = "_httpContextAccessor";
    private const string AccessorParamName = "httpContextAccessor";

    private static readonly SyntaxTrivia Space = SyntaxFactory.Space;
    private static readonly SyntaxTrivia Newline = SyntaxFactory.CarriageReturnLineFeed;

    public bool IsApplicable(SyntaxTree syntaxTree)
        => syntaxTree.ToString().Contains("HttpContext.Current");

    public Task<RuleResult> ApplyAsync(
        RewriteContext context,
        SyntaxTree syntaxTree,
        CancellationToken cancellationToken = default)
    {
        var root = syntaxTree.GetCompilationUnitRoot();
        var semanticModel = context.GetSemanticModel(syntaxTree);

        var collector = new HttpContextCollector();
        collector.Visit(root);

        // Merge static method issues — these are returned even when no rewriting happens.
        var allIssues = new List<MigrationIssue>(collector.StaticMethodIssues);

        if (!collector.AffectedClassNames.Any())
        {
            return Task.FromResult(new RuleResult
            {
                OutputTree = syntaxTree,
                Issues = allIssues
            });
        }

        var rewriter = new HttpContextSyntaxRewriter(
            collector.AffectedClassNames, allIssues, semanticModel);

        var newRoot = (CompilationUnitSyntax)rewriter.Visit(root);
        var newTree = syntaxTree.WithRootAndOptions(newRoot, syntaxTree.Options);

        return Task.FromResult(new RuleResult
        {
            OutputTree = newTree,
            Transformations = rewriter.Transformations,
            Issues = rewriter.Issues
        });
    }

    // -------------------------------------------------------------------------
    // Pass 1: collect
    // -------------------------------------------------------------------------

    private sealed class HttpContextCollector : CSharpSyntaxWalker
    {
        public HashSet<string> AffectedClassNames { get; } = [];
        public List<MigrationIssue> StaticMethodIssues { get; } = [];
        private string? _currentClass;
        private bool _inStaticMethod;

        public override void VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            _currentClass = node.Identifier.Text;
            base.VisitClassDeclaration(node);
            _currentClass = null;
        }

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            _inStaticMethod = node.Modifiers.Any(SyntaxKind.StaticKeyword);
            base.VisitMethodDeclaration(node);
            _inStaticMethod = false;
        }

        public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            base.VisitMemberAccessExpression(node);
            if (!IsHttpContextCurrent(node)) return;

            if (_inStaticMethod || _currentClass is null)
            {
                StaticMethodIssues.Add(new MigrationIssue
                {
                    Id = IssueIds.HttpContextCurrentAccess,
                    Title = "HttpContext.Current in static method cannot be auto-migrated",
                    Description =
                        "HttpContext.Current in a static method cannot receive IHttpContextAccessor " +
                        "via constructor DI. Refactor to an instance method or pass HttpContext explicitly.",
                    Severity = IssueSeverity.Warning,
                    Category = IssueCategory.AspNetApi,
                    FilePath = node.SyntaxTree.FilePath,
                    LineNumber = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                    EffortHours = 1.5,
                    Recommendation =
                        "Convert to an instance method and inject IHttpContextAccessor, " +
                        "or pass HttpContext explicitly from a controller action."
                });
                return;
            }

            AffectedClassNames.Add(_currentClass);
        }

        private static bool IsHttpContextCurrent(MemberAccessExpressionSyntax node)
            => node.Name.Identifier.Text == "Current"
               && node.Expression is IdentifierNameSyntax id
               && id.Identifier.Text == "HttpContext";
    }

    // -------------------------------------------------------------------------
    // Pass 2: rewrite
    // -------------------------------------------------------------------------

    private sealed class HttpContextSyntaxRewriter : CSharpSyntaxRewriter
    {
        private readonly IReadOnlySet<string> _affectedClasses;
        private readonly SemanticModel _semanticModel;
        public List<AppliedTransformation> Transformations { get; } = [];
        public List<MigrationIssue> Issues { get; }

        public HttpContextSyntaxRewriter(
            IReadOnlySet<string> affectedClasses,
            List<MigrationIssue> existingIssues,
            SemanticModel semanticModel)
        {
            _affectedClasses = affectedClasses;
            Issues = existingIssues;
            _semanticModel = semanticModel;
        }

        public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            if (!_affectedClasses.Contains(node.Identifier.Text))
                return base.VisitClassDeclaration(node);

            var updatedClass = (ClassDeclarationSyntax)base.VisitClassDeclaration(node)!;

            var alreadyHasField = updatedClass.Members
                .OfType<FieldDeclarationSyntax>()
                .Any(f => f.Declaration.Type.ToString().Contains(AccessorTypeName));

            if (alreadyHasField) return updatedClass;

            // Build: private readonly IHttpContextAccessor _httpContextAccessor;
            var field = (MemberDeclarationSyntax)SyntaxFactory.FieldDeclaration(
                    SyntaxFactory.VariableDeclaration(
                        SyntaxFactory.ParseTypeName(AccessorTypeName)
                            .WithTrailingTrivia(Space))
                    .WithVariables(SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.VariableDeclarator(AccessorFieldName))))
                .WithModifiers(SyntaxFactory.TokenList(
                    SyntaxFactory.Token(SyntaxKind.PrivateKeyword).WithTrailingTrivia(Space),
                    SyntaxFactory.Token(SyntaxKind.ReadOnlyKeyword).WithTrailingTrivia(Space)))
                .WithLeadingTrivia(Newline)
                .WithTrailingTrivia(Newline);

            // Build parameter: IHttpContextAccessor httpContextAccessor
            var newParam = SyntaxFactory.Parameter(
                    SyntaxFactory.Identifier(AccessorParamName))
                .WithType(SyntaxFactory.ParseTypeName(AccessorTypeName)
                    .WithTrailingTrivia(Space));

            // Build assignment: _httpContextAccessor = httpContextAccessor;
            var assignment = (StatementSyntax)SyntaxFactory.ExpressionStatement(
                    SyntaxFactory.AssignmentExpression(
                        SyntaxKind.SimpleAssignmentExpression,
                        SyntaxFactory.IdentifierName(AccessorFieldName)
                            .WithTrailingTrivia(Space),
                        SyntaxFactory.IdentifierName(AccessorParamName)
                            .WithLeadingTrivia(Space)))
                .WithLeadingTrivia(SyntaxFactory.Whitespace("            "))
                .WithTrailingTrivia(Newline);

            var existingCtor = updatedClass.Members
                .OfType<ConstructorDeclarationSyntax>().FirstOrDefault();

            ClassDeclarationSyntax result;

            if (existingCtor is not null)
            {
                var updatedParams = existingCtor.ParameterList.Parameters.Count == 0
                    ? SyntaxFactory.SeparatedList(new[] { newParam })
                    : existingCtor.ParameterList.Parameters.Add(newParam);

                var updatedCtor = existingCtor
                    .WithParameterList(existingCtor.ParameterList.WithParameters(updatedParams))
                    .WithBody(existingCtor.Body!.AddStatements(assignment));

                result = updatedClass.ReplaceNode(existingCtor, updatedCtor);
            }
            else
            {
                var newCtor = SyntaxFactory.ConstructorDeclaration(
                        SyntaxFactory.Identifier(updatedClass.Identifier.Text))
                    .WithModifiers(SyntaxFactory.TokenList(
                        SyntaxFactory.Token(SyntaxKind.PublicKeyword)
                            .WithTrailingTrivia(Space)))
                    .WithParameterList(SyntaxFactory.ParameterList(
                        SyntaxFactory.SingletonSeparatedList(newParam)))
                    .WithBody(SyntaxFactory.Block(assignment))
                    .WithLeadingTrivia(Newline);

                result = updatedClass.AddMembers(newCtor);
            }

            var allMembers = new[] { field }.Concat(result.Members).ToArray();
            result = result.WithMembers(SyntaxFactory.List(allMembers));

            Transformations.Add(new AppliedTransformation
            {
                RuleId = "R008",
                Description =
                    $"Replaced HttpContext.Current with IHttpContextAccessor in {node.Identifier.Text}",
                LineNumber = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1
            });

            return result;
        }

        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            if (node.Name.Identifier.Text == "Current"
                && node.Expression is IdentifierNameSyntax id
                && id.Identifier.Text == "HttpContext")
            {
                return SyntaxFactory.ParseExpression($"{AccessorFieldName}.HttpContext")
                    .WithTriviaFrom(node);
            }

            return base.VisitMemberAccessExpression(node);
        }
    }
}
