using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NetForward.Analyzer;
using NetForward.Core.Models;
using NetForward.Rewriter.Pipeline;

namespace NetForward.Rewriter.Rewriters;

/// <summary>
/// R007 — Dependency injection retrofit (Tier 2).
/// Detects service-locator patterns and rewrites them to constructor injection.
/// Fix: SyntaxFactory tokens for modifiers need explicit trailing-space trivia
/// so the generated code doesn't squash keywords together.
/// </summary>
public sealed class R007DependencyInjectionRewriter : IRewriteRule
{
    public string RuleId => "R007";
    public string Name => "DI retrofit: service locator to constructor injection";
    public RewriteTier Tier => RewriteTier.Tier2;

    private static readonly HashSet<string> ServiceLocatorMethods =
        new(StringComparer.Ordinal) { "GetService", "GetInstance", "GetRequiredService" };

    private static readonly HashSet<string> ServiceLocatorRoots =
        new(StringComparer.Ordinal) { "DependencyResolver", "ServiceLocator", "IoCContainer", "Container" };

    private static readonly SyntaxTrivia Space = SyntaxFactory.Space;
    private static readonly SyntaxTrivia Newline = SyntaxFactory.CarriageReturnLineFeed;

    public bool IsApplicable(SyntaxTree syntaxTree)
    {
        var text = syntaxTree.ToString();
        return text.Contains("DependencyResolver")
            || text.Contains("ServiceLocator")
            || text.Contains("GetService")
            || text.Contains("GetInstance");
    }

    public Task<RuleResult> ApplyAsync(
        RewriteContext context,
        SyntaxTree syntaxTree,
        CancellationToken cancellationToken = default)
    {
        var root = syntaxTree.GetCompilationUnitRoot();
        var semanticModel = context.GetSemanticModel(syntaxTree);

        var collector = new ServiceLocatorCollector(semanticModel);
        collector.Visit(root);

        if (collector.Resolutions.Count == 0 && collector.Issues.Count == 0)
            return Task.FromResult(new RuleResult { OutputTree = syntaxTree });

        var rewriter = new DependencyInjectionSyntaxRewriter(
            collector.Resolutions, collector.Issues, semanticModel);
        var newRoot = (CompilationUnitSyntax)rewriter.Visit(root);
        var newTree = syntaxTree.WithRootAndOptions(newRoot, syntaxTree.Options);

        return Task.FromResult(new RuleResult
        {
            OutputTree = newTree,
            Transformations = rewriter.Transformations,
            Issues = rewriter.Issues
        });
    }

    internal sealed record ServiceResolution(
        string TypeName,
        string FieldName,
        string ParameterName,
        InvocationExpressionSyntax OriginalCall,
        string ContainingClassName);

    // -------------------------------------------------------------------------
    // Pass 1: collect
    // -------------------------------------------------------------------------

    private sealed class ServiceLocatorCollector : CSharpSyntaxWalker
    {
        private readonly SemanticModel _semanticModel;
        public List<ServiceResolution> Resolutions { get; } = [];
        public List<MigrationIssue> Issues { get; } = [];
        private string? _currentClassName;
        private bool _inStaticMethod;

        public ServiceLocatorCollector(SemanticModel semanticModel)
            => _semanticModel = semanticModel;

        public override void VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            _currentClassName = node.Identifier.Text;
            base.VisitClassDeclaration(node);
            _currentClassName = null;
        }

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            _inStaticMethod = node.Modifiers.Any(SyntaxKind.StaticKeyword);
            base.VisitMethodDeclaration(node);
            _inStaticMethod = false;
        }

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            base.VisitInvocationExpression(node);
            if (!IsServiceLocatorCall(node, out var typeName) || typeName is null) return;

            if (_inStaticMethod || _currentClassName is null)
            {
                Issues.Add(MakeManualIssue(node,
                    "Service locator call in static method or outside a class — cannot auto-inject."));
                return;
            }

            if (Resolutions.Any(r => r.TypeName == typeName && r.ContainingClassName == _currentClassName))
                return;

            Resolutions.Add(new ServiceResolution(
                TypeName: typeName,
                FieldName: ToFieldName(typeName),
                ParameterName: ToParameterName(typeName),
                OriginalCall: node,
                ContainingClassName: _currentClassName));
        }

        private static bool IsServiceLocatorCall(InvocationExpressionSyntax node, out string? typeName)
        {
            typeName = null;
            if (node.Expression is not MemberAccessExpressionSyntax ma) return false;
            if (!ServiceLocatorMethods.Contains(ma.Name.Identifier.Text)) return false;

            var exprText = ma.Expression.ToString();
            if (!ServiceLocatorRoots.Any(r => exprText.Contains(r, StringComparison.Ordinal)))
                return false;

            if (ma.Name is GenericNameSyntax generic && generic.TypeArgumentList.Arguments.Count == 1)
            {
                typeName = generic.TypeArgumentList.Arguments[0].ToString().Trim();
                return true;
            }

            if (node.ArgumentList.Arguments.Count == 1 &&
                node.ArgumentList.Arguments[0].Expression is TypeOfExpressionSyntax typeofExpr)
            {
                typeName = typeofExpr.Type.ToString().Trim();
                return true;
            }

            return false;
        }

        private static MigrationIssue MakeManualIssue(SyntaxNode node, string detail) => new()
        {
            Id = IssueIds.DependencyInjectionServiceLocator,
            Title = "Service locator call requires manual DI migration",
            Description = $"{detail} Review and inject manually via constructor.",
            Severity = IssueSeverity.Warning,
            Category = IssueCategory.DependencyInjection,
            FilePath = node.SyntaxTree.FilePath,
            LineNumber = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
            EffortHours = 1.0,
            Recommendation = "Register with builder.Services.Add*() and inject via constructor."
        };

        private static string ToFieldName(string typeName)
        {
            var n = StripI(typeName);
            return "_" + char.ToLowerInvariant(n[0]) + n[1..];
        }

        private static string ToParameterName(string typeName)
        {
            var n = StripI(typeName);
            return char.ToLowerInvariant(n[0]) + n[1..];
        }

        private static string StripI(string t)
        {
            var s = t.Split('.').Last();
            return s.Length > 1 && s[0] == 'I' && char.IsUpper(s[1]) ? s[1..] : s;
        }
    }

    // -------------------------------------------------------------------------
    // Pass 2: rewrite
    // -------------------------------------------------------------------------

    private sealed class DependencyInjectionSyntaxRewriter : CSharpSyntaxRewriter
    {
        private readonly IReadOnlyList<ServiceResolution> _resolutions;
        private readonly SemanticModel _semanticModel;
        public List<AppliedTransformation> Transformations { get; } = [];
        public List<MigrationIssue> Issues { get; }

        public DependencyInjectionSyntaxRewriter(
            IReadOnlyList<ServiceResolution> resolutions,
            List<MigrationIssue> existingIssues,
            SemanticModel semanticModel)
        {
            _resolutions = resolutions;
            Issues = existingIssues;
            _semanticModel = semanticModel;
        }

        public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            var classRes = _resolutions
                .Where(r => r.ContainingClassName == node.Identifier.Text).ToList();
            if (classRes.Count == 0) return base.VisitClassDeclaration(node);

            var updatedClass = (ClassDeclarationSyntax)base.VisitClassDeclaration(node)!;

            // Build properly-spaced field declarations.
            var fields = classRes.Select(r => BuildField(r.TypeName, r.FieldName))
                                 .Cast<MemberDeclarationSyntax>()
                                 .ToList();

            var existingCtor = updatedClass.Members
                .OfType<ConstructorDeclarationSyntax>().FirstOrDefault();

            ClassDeclarationSyntax result;

            if (existingCtor is not null)
            {
                var newParams = classRes.Select(r => BuildParameter(r.TypeName, r.ParameterName));
                var updatedParams = existingCtor.ParameterList.Parameters.Count == 0
                    ? SyntaxFactory.SeparatedList(newParams)
                    : existingCtor.ParameterList.Parameters.AddRange(newParams);

                var assignments = classRes
                    .Select(r => BuildAssignment(r.FieldName, r.ParameterName))
                    .ToArray();

                var updatedCtor = existingCtor
                    .WithParameterList(existingCtor.ParameterList.WithParameters(updatedParams))
                    .WithBody(existingCtor.Body!.AddStatements(assignments));

                result = updatedClass.ReplaceNode(existingCtor, updatedCtor);
            }
            else
            {
                var parameters = SyntaxFactory.SeparatedList(
                    classRes.Select(r => BuildParameter(r.TypeName, r.ParameterName)));

                var stmts = classRes
                    .Select(r => BuildAssignment(r.FieldName, r.ParameterName))
                    .ToArray();

                var newCtor = SyntaxFactory
                    .ConstructorDeclaration(
                        SyntaxFactory.Identifier(node.Identifier.Text))
                    .WithModifiers(SyntaxFactory.TokenList(
                        SyntaxFactory.Token(SyntaxKind.PublicKeyword)
                            .WithTrailingTrivia(Space)))
                    .WithParameterList(SyntaxFactory.ParameterList(parameters))
                    .WithBody(SyntaxFactory.Block(stmts))
                    .WithLeadingTrivia(Newline)
                    .WithTrailingTrivia(Newline);

                result = updatedClass.AddMembers(newCtor);
            }

            var allMembers = fields.Concat(result.Members).ToArray();
            result = result.WithMembers(SyntaxFactory.List(allMembers));

            foreach (var r in classRes)
            {
                Transformations.Add(new AppliedTransformation
                {
                    RuleId = "R007",
                    Description = $"Injected {r.TypeName} as {r.FieldName} into {r.ContainingClassName}",
                    LineNumber = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1
                });
            }

            return result;
        }

        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var resolution = _resolutions.FirstOrDefault(r =>
                r.OriginalCall.IsEquivalentTo(node, topLevel: false));

            if (resolution is not null)
                return SyntaxFactory.IdentifierName(resolution.FieldName).WithTriviaFrom(node);

            return base.VisitInvocationExpression(node);
        }

        // ---- Properly-spaced syntax helpers ---------------------------------

        /// <summary>
        /// Builds: private readonly TypeName _fieldName;
        /// With correct spaces between each token.
        /// </summary>
        private static FieldDeclarationSyntax BuildField(string typeName, string fieldName)
        {
            return SyntaxFactory.FieldDeclaration(
                    SyntaxFactory.VariableDeclaration(
                        SyntaxFactory.ParseTypeName(typeName)
                            .WithTrailingTrivia(Space))
                    .WithVariables(SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.VariableDeclarator(fieldName))))
                .WithModifiers(SyntaxFactory.TokenList(
                    SyntaxFactory.Token(SyntaxKind.PrivateKeyword)
                        .WithTrailingTrivia(Space),
                    SyntaxFactory.Token(SyntaxKind.ReadOnlyKeyword)
                        .WithTrailingTrivia(Space)))
                .WithLeadingTrivia(Newline)
                .WithTrailingTrivia(Newline);
        }

        /// <summary>Builds: TypeName paramName</summary>
        private static ParameterSyntax BuildParameter(string typeName, string paramName)
        {
            return SyntaxFactory.Parameter(
                    SyntaxFactory.Identifier(paramName))
                .WithType(SyntaxFactory.ParseTypeName(typeName)
                    .WithTrailingTrivia(Space));
        }

        /// <summary>Builds: _fieldName = paramName;</summary>
        private static ExpressionStatementSyntax BuildAssignment(string fieldName, string paramName)
        {
            return SyntaxFactory.ExpressionStatement(
                SyntaxFactory.AssignmentExpression(
                    SyntaxKind.SimpleAssignmentExpression,
                    SyntaxFactory.IdentifierName(fieldName)
                        .WithTrailingTrivia(Space),
                    SyntaxFactory.IdentifierName(paramName)
                        .WithLeadingTrivia(Space)))
                .WithLeadingTrivia(SyntaxFactory.Whitespace("            "))
                .WithTrailingTrivia(Newline);
        }
    }
}
