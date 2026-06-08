using FluentAssertions;
using NetForward.Rewriter.Rewriters;
using Xunit;

namespace NetForward.Rewriter.Tests;

public class R004AttributeConversionRewriterTests
{
    private readonly R004AttributeConversionRewriter _rule = new();

    [Fact]
    public async Task Renames_FromUri_to_FromQuery()
    {
        var source = @"
using Microsoft.AspNetCore.Mvc;
public class FooController : ControllerBase
{
    public IActionResult Get([FromUri] string filter) => Ok();
}";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        output.Should().Contain("[FromQuery]");
        output.Should().NotContain("[FromUri]");
        result.Transformations.Should().Contain(t => t.Description.Contains("FromQuery"));
    }

    [Fact]
    public async Task Removes_ChildActionOnly_and_raises_issue()
    {
        var source = @"
public class FooController
{
    [ChildActionOnly]
    public IActionResult Partial() => View();
}";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        output.Should().NotContain("[ChildActionOnly]");
        result.Issues.Should().Contain(i => i.Id == "NF404");
        result.Transformations.Should().Contain(t => t.Description.Contains("ChildActionOnly"));
    }

    [Fact]
    public async Task Removes_RequireHttps_and_raises_issue()
    {
        var source = @"
public class FooController
{
    [RequireHttps]
    public IActionResult Secure() => Ok();
}";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        output.Should().NotContain("[RequireHttps]");
        result.Issues.Should().Contain(i => i.Id == "NF404"
            && i.Title.Contains("RequireHttps"));
    }

    [Fact]
    public async Task Removes_HandleError_and_raises_issue()
    {
        var source = @"
[HandleError]
public class FooController
{
    public IActionResult Index() => Ok();
}";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        output.Should().NotContain("[HandleError]");
        result.Issues.Should().Contain(i => i.Id == "NF406");
    }

    [Fact]
    public async Task Removes_OutputCache_and_raises_issue()
    {
        var source = @"
public class FooController
{
    [OutputCache(Duration = 60)]
    public IActionResult Cached() => Ok();
}";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        output.Should().NotContain("[OutputCache]");
        result.Issues.Should().Contain(i => i.Id == "NF405");
    }

    [Fact]
    public async Task Does_not_touch_modern_attributes()
    {
        var source = @"
using Microsoft.AspNetCore.Mvc;
public class FooController : ControllerBase
{
    [HttpGet]
    [Authorize]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public IActionResult Get() => Ok();
}";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        output.Should().Be(source);
        result.Transformations.Should().BeEmpty();
        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public void IsApplicable_returns_true_for_FromUri()
    {
        var tree = RuleTestHelpers.ParseSource("class C { void M([FromUri] string x) {} }");
        _rule.IsApplicable(tree).Should().BeTrue();
    }

    [Fact]
    public void IsApplicable_returns_true_for_OutputCache()
    {
        var tree = RuleTestHelpers.ParseSource("[OutputCache(Duration=60)] class C {}");
        _rule.IsApplicable(tree).Should().BeTrue();
    }

    [Fact]
    public void IsApplicable_returns_false_for_modern_only_source()
    {
        var tree = RuleTestHelpers.ParseSource(@"
using Microsoft.AspNetCore.Mvc;
[ApiController] public class C : ControllerBase {}");
        _rule.IsApplicable(tree).Should().BeFalse();
    }
}
