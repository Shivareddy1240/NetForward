using FluentAssertions;
using NetForward.Rewriter.Rewriters;
using Xunit;

namespace NetForward.Rewriter.Tests;

public class R002ControllerBaseRewriterTests
{
    private readonly R002ControllerBaseRewriter _rule = new();

    [Fact]
    public async Task Replaces_ApiController_with_ControllerBase()
    {
        var source = @"
using Microsoft.AspNetCore.Mvc;

namespace MyApp.Controllers
{
    public class ValuesController : ApiController
    {
        public IHttpActionResult Get() => Ok();
    }
}";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        output.Should().Contain(": ControllerBase");
        output.Should().NotContain(": ApiController");
        result.Transformations.Should().Contain(t => t.Description.Contains("ControllerBase"));
    }

    [Fact]
    public async Task Adds_ApiController_attribute_when_missing()
    {
        var source = @"
namespace MyApp.Controllers
{
    public class ValuesController : ApiController { }
}";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        output.Should().Contain("[ApiController]");
        result.Transformations.Should().Contain(t => t.Description.Contains("[ApiController]"));
    }

    [Fact]
    public async Task Adds_Route_attribute_when_missing()
    {
        var source = @"
namespace MyApp.Controllers
{
    public class OrdersController : ApiController { }
}";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        output.Should().Contain("[Route(");
        result.Transformations.Should().Contain(t => t.Description.Contains("Route"));
    }

    [Fact]
    public async Task Does_not_double_add_ApiController_if_already_present()
    {
        var source = @"
using Microsoft.AspNetCore.Mvc;

namespace MyApp.Controllers
{
    [ApiController]
    [Route(""[controller]"")]
    public class ValuesController : ApiController { }
}";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        // Should not add a second [ApiController]
        var apiControllerCount = output.Split("[ApiController]").Length - 1;
        apiControllerCount.Should().Be(1);
    }

    [Fact]
    public async Task Does_not_modify_MVC_Controller_base_class()
    {
        var source = @"
namespace MyApp.Controllers
{
    public class HomeController : Controller { }
}";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        // MVC Controller stays as-is; only ApiController is swapped
        output.Should().Contain(": Controller");
        result.Transformations.Should().BeEmpty();
    }

    [Fact]
    public void IsApplicable_returns_true_for_ApiController()
    {
        var tree = RuleTestHelpers.ParseSource("public class Foo : ApiController {}");
        _rule.IsApplicable(tree).Should().BeTrue();
    }

    [Fact]
    public void IsApplicable_returns_false_when_no_controller_base()
    {
        var tree = RuleTestHelpers.ParseSource("public class Foo {}");
        _rule.IsApplicable(tree).Should().BeFalse();
    }
}
