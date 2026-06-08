using FluentAssertions;
using NetForward.Rewriter.Rewriters;
using Xunit;

namespace NetForward.Rewriter.Tests;

public class R007DependencyInjectionRewriterTests
{
    private readonly R007DependencyInjectionRewriter _rule = new();

    [Fact]
    public async Task Injects_service_from_DependencyResolver_GetService_generic()
    {
        var source = @"
public class OrdersController
{
    public void Index()
    {
        var svc = DependencyResolver.Current.GetService<IOrderService>();
        svc.DoSomething();
    }
}";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        // Field and constructor are generated.
        output.Should().Contain("private readonly IOrderService _orderService");
        output.Should().Contain("public OrdersController(IOrderService orderService)");
        output.Should().Contain("_orderService = orderService");

        // The GetService<> call is replaced with the field reference.
        // Note: the variable assignment becomes `var svc = _orderService;`
        // R007 replaces the invocation only — it does not perform data-flow
        // analysis to also rename subsequent uses of the `svc` local.
        // That is a deliberate scope boundary; further uses are left as-is
        // (which compiles fine since svc is now assigned from the field).
        output.Should().Contain("var svc = _orderService");
        output.Should().NotContain("DependencyResolver");

        result.Transformations.Should().Contain(t =>
            t.Description.Contains("IOrderService") && t.RuleId == "R007");
    }

    [Fact]
    public async Task Injects_service_from_ServiceLocator_GetInstance()
    {
        var source = @"
public class HomeController
{
    public void About()
    {
        var logger = ServiceLocator.Current.GetInstance<ILogger>();
    }
}";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        output.Should().Contain("private readonly ILogger _logger");
        output.Should().Contain("public HomeController(ILogger logger)");
        output.Should().Contain("var logger = _logger");
        output.Should().NotContain("ServiceLocator");
        result.Transformations.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Adds_injection_to_existing_constructor()
    {
        var source = @"
public class OrdersController
{
    private readonly IUserService _userService;

    public OrdersController(IUserService userService)
    {
        _userService = userService;
    }

    public void Index()
    {
        var svc = DependencyResolver.Current.GetService<IOrderService>();
    }
}";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        output.Should().Contain("IUserService userService");
        output.Should().Contain("IOrderService orderService");
        output.Should().Contain("_orderService = orderService");
        output.Should().Contain("var svc = _orderService");
        output.Should().NotContain("DependencyResolver");
    }

    [Fact]
    public async Task Does_not_duplicate_injection_for_same_type_called_twice()
    {
        var source = @"
public class FooController
{
    public void A()
    {
        var svc = DependencyResolver.Current.GetService<IOrderService>();
    }
    public void B()
    {
        var svc2 = DependencyResolver.Current.GetService<IOrderService>();
    }
}";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        (output.Split("private readonly IOrderService").Length - 1).Should().Be(1);
        result.Transformations.Should().HaveCount(1);
    }

    [Fact]
    public async Task Does_not_modify_already_injected_source()
    {
        var source = @"
public class FooController
{
    private readonly IOrderService _orderService;
    public FooController(IOrderService orderService) { _orderService = orderService; }
}";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        output.Should().Be(source);
        result.Transformations.Should().BeEmpty();
    }

    [Fact]
    public void IsApplicable_returns_true_for_DependencyResolver()
    {
        var tree = RuleTestHelpers.ParseSource(
            "class C { void M() { DependencyResolver.Current.GetService<IFoo>(); } }");
        _rule.IsApplicable(tree).Should().BeTrue();
    }

    [Fact]
    public void IsApplicable_returns_false_for_clean_source()
    {
        var tree = RuleTestHelpers.ParseSource(
            "class C { readonly IFoo _foo; C(IFoo foo) { _foo = foo; } }");
        _rule.IsApplicable(tree).Should().BeFalse();
    }
}
