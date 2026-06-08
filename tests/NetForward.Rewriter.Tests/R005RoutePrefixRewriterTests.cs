using FluentAssertions;
using NetForward.Rewriter.Rewriters;
using Xunit;

namespace NetForward.Rewriter.Tests;

public class R005RoutePrefixRewriterTests
{
    private readonly R005RoutePrefixRewriter _rule = new();

    [Fact]
    public async Task Replaces_RoutePrefix_with_Route_on_controller()
    {
        var source = @"
using Microsoft.AspNetCore.Mvc;

[RoutePrefix(""api/orders"")]
public class OrdersController : ControllerBase
{
    [HttpGet]
    [Route("""")]
    public IActionResult GetAll() => Ok();
}";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        output.Should().NotContain("[RoutePrefix");
        output.Should().Contain("[Route(\"api/orders\")]");
        result.Transformations.Should().Contain(t =>
            t.Description.Contains("api/orders") && t.RuleId == "R005");
    }

    [Fact]
    public async Task Preserves_action_route_attributes_unchanged()
    {
        var source = @"
[RoutePrefix(""api/products"")]
public class ProductsController
{
    [Route("""")]
    public IActionResult GetAll() => null;

    [Route(""{id}"")]
    public IActionResult GetById(int id) => null;

    [Route(""active"")]
    public IActionResult GetActive() => null;
}";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        // Action routes stay as-is
        output.Should().Contain("[Route(\"\")]");
        output.Should().Contain("[Route(\"{id}\")]");
        output.Should().Contain("[Route(\"active\")]");
        // Controller gets the prefix as Route
        output.Should().Contain("[Route(\"api/products\")]");
        output.Should().NotContain("[RoutePrefix");
    }

    [Fact]
    public async Task Does_not_modify_class_with_regular_Route_attribute()
    {
        var source = @"
[Route(""api/[controller]"")]
public class FooController
{
    [HttpGet]
    public IActionResult Get() => null;
}";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        // Already uses [Route], no [RoutePrefix] — nothing to do
        output.Should().Be(source);
        result.Transformations.Should().BeEmpty();
    }

    [Fact]
    public async Task Handles_multiple_controllers_in_same_file()
    {
        var source = @"
[RoutePrefix(""api/orders"")]
public class OrdersController
{
    [Route("""")]
    public IActionResult Get() => null;
}

[RoutePrefix(""api/products"")]
public class ProductsController
{
    [Route("""")]
    public IActionResult Get() => null;
}";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        output.Should().Contain("[Route(\"api/orders\")]");
        output.Should().Contain("[Route(\"api/products\")]");
        output.Should().NotContain("[RoutePrefix");
        result.Transformations.Should().HaveCount(2);
    }

    [Fact]
    public void IsApplicable_returns_true_when_RoutePrefix_present()
    {
        var tree = RuleTestHelpers.ParseSource("[RoutePrefix(\"api\")] class C {}");
        _rule.IsApplicable(tree).Should().BeTrue();
    }

    [Fact]
    public void IsApplicable_returns_false_when_no_RoutePrefix()
    {
        var tree = RuleTestHelpers.ParseSource("[Route(\"api\")] class C {}");
        _rule.IsApplicable(tree).Should().BeFalse();
    }
}
