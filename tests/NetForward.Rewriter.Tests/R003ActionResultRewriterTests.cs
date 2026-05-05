using FluentAssertions;
using NetForward.Rewriter.Rewriters;
using Xunit;

namespace NetForward.Rewriter.Tests;

public class R003ActionResultRewriterTests
{
    private readonly R003ActionResultRewriter _rule = new();

    [Fact]
    public async Task Rewrites_IHttpActionResult_return_type_to_IActionResult()
    {
        var source = @"
public class FooController
{
    public IHttpActionResult Get() => null;
}";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        output.Should().Contain("IActionResult");
        output.Should().NotContain("IHttpActionResult");
        result.Transformations.Should().Contain(t => t.Description.Contains("IActionResult"));
    }

    [Fact]
    public async Task Rewrites_HttpNotFound_to_NotFound()
    {
        var source = @"
public class FooController
{
    public IActionResult Get(int id)
    {
        return HttpNotFound();
    }
}";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        output.Should().Contain("NotFound()");
        output.Should().NotContain("HttpNotFound()");
        result.Transformations.Should().Contain(t => t.Description.Contains("NotFound"));
    }

    [Fact]
    public async Task Rewrites_HttpBadRequest_to_BadRequest()
    {
        var source = @"
public class FooController
{
    public IActionResult Post()
    {
        return HttpBadRequest();
    }
}";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        output.Should().Contain("BadRequest()");
        output.Should().NotContain("HttpBadRequest()");
    }

    [Fact]
    public async Task Rewrites_HttpOk_to_Ok()
    {
        var source = @"
public class FooController
{
    public IActionResult Get() => HttpOk(new { id = 1 });
}";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        output.Should().Contain("Ok(");
        output.Should().NotContain("HttpOk(");
    }

    [Fact]
    public async Task Rewrites_InternalServerError_to_StatusCode_500()
    {
        var source = @"
public class FooController
{
    public IActionResult Error() => InternalServerError();
}";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        output.Should().Contain("StatusCode(500)");
        output.Should().NotContain("InternalServerError()");
    }

    [Fact]
    public async Task Rewrites_new_HttpStatusCodeResult_to_StatusCode_call()
    {
        var source = @"
public class FooController
{
    public IActionResult Error() => new HttpStatusCodeResult(503);
}";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        output.Should().Contain("StatusCode(503)");
        output.Should().NotContain("HttpStatusCodeResult");
    }

    [Fact]
    public async Task Raises_issue_when_HttpStatusCodeResult_has_description_arg()
    {
        var source = @"
public class FooController
{
    public IActionResult Error() => new HttpStatusCodeResult(500, ""Internal error"");
}";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        output.Should().Contain("StatusCode(500)");
        result.Issues.Should().Contain(i => i.Title.Contains("description argument dropped"));
    }

    [Fact]
    public async Task Does_not_modify_already_modern_action_results()
    {
        var source = @"
public class FooController
{
    public IActionResult Get() => Ok();
    public IActionResult NotFoundResult() => NotFound();
}";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        output.Should().Be(source);
        result.Transformations.Should().BeEmpty();
    }

    [Theory]
    [InlineData("HttpNotFound")]
    [InlineData("HttpBadRequest")]
    [InlineData("InternalServerError")]
    [InlineData("IHttpActionResult")]
    public void IsApplicable_returns_true_for_legacy_patterns(string pattern)
    {
        var tree = RuleTestHelpers.ParseSource($"class C {{ void M() {{ var x = {pattern}(); }} }}");
        _rule.IsApplicable(tree).Should().BeTrue();
    }

    [Fact]
    public void IsApplicable_returns_false_for_modern_only_source()
    {
        var tree = RuleTestHelpers.ParseSource(@"
public class C {
    public IActionResult Get() => Ok();
}");
        _rule.IsApplicable(tree).Should().BeFalse();
    }
}
