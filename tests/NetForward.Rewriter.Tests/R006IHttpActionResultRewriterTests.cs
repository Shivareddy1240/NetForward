using FluentAssertions;
using NetForward.Rewriter.Rewriters;
using Xunit;

namespace NetForward.Rewriter.Tests;

public class R006IHttpActionResultRewriterTests
{
    private readonly R006IHttpActionResultRewriter _rule = new();

    [Fact]
    public async Task Rewrites_Task_IHttpActionResult_return_type()
    {
        var source = @"
using System.Threading.Tasks;
public class FooController
{
    public async Task<IHttpActionResult> GetAsync()
    {
        return null;
    }
}";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        output.Should().Contain("Task<IActionResult>");
        output.Should().NotContain("Task<IHttpActionResult>");

        // The generic rewriter records the Task<> transformation;
        // the identifier rewriter is suppressed for the type argument.
        result.Transformations.Should().ContainSingle()
            .Which.Description.Should().Contain("Task<IActionResult>");
    }

    [Fact]
    public async Task Rewrites_bare_IHttpActionResult_return_type()
    {
        var source = @"
public class FooController
{
    public IHttpActionResult Get() => null;
}";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        output.Should().Contain("IActionResult");
        output.Should().NotContain("IHttpActionResult");
        result.Transformations.Should().ContainSingle()
            .Which.Description.Should().Contain("IActionResult");
    }

    [Fact]
    public async Task Rewrites_IHttpActionResult_variable_declarations()
    {
        var source = @"
public class FooController
{
    public IHttpActionResult Get()
    {
        IHttpActionResult result = null;
        return result;
    }
}";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        output.Should().NotContain("IHttpActionResult");
        output.Should().Contain("IActionResult");
        // Two occurrences: return type + variable declaration
        result.Transformations.Should().HaveCount(2);
    }

    [Fact]
    public async Task Raises_NF408_for_ResponseMessage_call()
    {
        var source = @"
using System.Net.Http;
public class FooController
{
    public IActionResult Get()
    {
        var response = new HttpResponseMessage();
        return ResponseMessage(response);
    }
}";
        var (_, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        result.Issues.Should().Contain(i => i.Id == "NF408");
        result.Issues.Single(i => i.Id == "NF408")
            .Title.Should().Contain("ResponseMessage");
    }

    [Fact]
    public async Task Raises_NF409_for_InternalServerError_with_exception_arg()
    {
        var source = @"
public class FooController
{
    public IActionResult Error()
    {
        try { return Ok(); }
        catch (System.Exception ex)
        {
            return InternalServerError(ex);
        }
    }
}";
        var (_, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        result.Issues.Should().Contain(i => i.Id == "NF409");
    }

    [Fact]
    public async Task Does_not_modify_already_modern_return_types()
    {
        var source = @"
using System.Threading.Tasks;
public class FooController
{
    public IActionResult Get() => null;
    public async Task<IActionResult> GetAsync() => null;
}";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        output.Should().Be(source);
        result.Transformations.Should().BeEmpty();
        result.Issues.Should().BeEmpty();
    }

    [Theory]
    [InlineData("IHttpActionResult")]
    [InlineData("ResponseMessage")]
    public void IsApplicable_returns_true_for_legacy_patterns(string pattern)
    {
        var tree = RuleTestHelpers.ParseSource($"class C {{ void M() {{ var x = new object(); }} {pattern} y = null; }}");
        _rule.IsApplicable(tree).Should().BeTrue();
    }

    [Fact]
    public void IsApplicable_returns_false_for_modern_only_source()
    {
        var tree = RuleTestHelpers.ParseSource(
            "class C { System.Threading.Tasks.Task<IActionResult> M() => null; }");
        _rule.IsApplicable(tree).Should().BeFalse();
    }
}
