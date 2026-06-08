using FluentAssertions;
using NetForward.Analyzer;
using NetForward.Rewriter.Rewriters;
using Xunit;

namespace NetForward.Rewriter.Tests;

public class R008HttpContextRewriterTests
{
    private readonly R008HttpContextRewriter _rule = new();

    [Fact]
    public async Task Replaces_HttpContext_Current_with_accessor_field()
    {
        var source = @"
public class OrdersController
{
    public void Index()
    {
        var user = HttpContext.Current.User;
    }
}";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        output.Should().Contain("private readonly IHttpContextAccessor _httpContextAccessor");
        output.Should().Contain("_httpContextAccessor.HttpContext");
        output.Should().NotContain("HttpContext.Current");
        result.Transformations.Should().Contain(t =>
            t.Description.Contains("IHttpContextAccessor") && t.RuleId == "R008");
    }

    [Fact]
    public async Task Generates_constructor_when_none_exists()
    {
        var source = @"
public class ReportService
{
    public string GetUser()
    {
        return HttpContext.Current.User.Identity.Name;
    }
}";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        output.Should().Contain("public ReportService(IHttpContextAccessor httpContextAccessor)");
        output.Should().Contain("_httpContextAccessor = httpContextAccessor");
        output.Should().NotContain("HttpContext.Current");
    }

    [Fact]
    public async Task Adds_to_existing_constructor()
    {
        var source = @"
public class ReportService
{
    private readonly ILogger _logger;

    public ReportService(ILogger logger)
    {
        _logger = logger;
    }

    public void Log()
    {
        var path = HttpContext.Current.Request.Path;
    }
}";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        output.Should().Contain("ILogger logger");
        output.Should().Contain("IHttpContextAccessor httpContextAccessor");
        output.Should().Contain("_httpContextAccessor = httpContextAccessor");
        output.Should().NotContain("HttpContext.Current");
    }

    [Fact]
    public async Task Raises_NF401_for_static_method()
    {
        var source = @"
public class Helpers
{
    public static string GetCurrentUser()
    {
        return HttpContext.Current.User.Identity.Name;
    }
}";
        var (_, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        result.Issues.Should().Contain(i => i.Id == IssueIds.HttpContextCurrentAccess);
    }

    [Fact]
    public async Task Does_not_add_duplicate_field_if_already_present()
    {
        var source = @"
public class FooController
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public FooController(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void Action()
    {
        var ctx = HttpContext.Current.User;
    }
}";
        var (output, _) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        (output.Split("IHttpContextAccessor _httpContextAccessor").Length - 1).Should().Be(1);
    }

    [Fact]
    public async Task Does_not_modify_source_without_HttpContext_Current()
    {
        var source = @"
public class FooController
{
    public void Action()
    {
        var user = this.HttpContext.User;
    }
}";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        output.Should().Be(source);
        result.Transformations.Should().BeEmpty();
    }

    [Fact]
    public void IsApplicable_returns_true_for_HttpContext_Current()
    {
        var tree = RuleTestHelpers.ParseSource(
            "class C { void M() { var x = HttpContext.Current.User; } }");
        _rule.IsApplicable(tree).Should().BeTrue();
    }

    [Fact]
    public void IsApplicable_returns_false_for_this_HttpContext()
    {
        var tree = RuleTestHelpers.ParseSource(
            "class C { void M() { var x = this.HttpContext.User; } }");
        _rule.IsApplicable(tree).Should().BeFalse();
    }
}
