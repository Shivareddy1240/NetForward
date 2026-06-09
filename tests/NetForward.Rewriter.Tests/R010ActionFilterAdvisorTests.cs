using FluentAssertions;
using NetForward.Analyzer;
using NetForward.Rewriter.Rewriters;
using Xunit;

namespace NetForward.Rewriter.Tests;

public class R010ActionFilterAdvisorTests
{
    private readonly R010ActionFilterAdvisor _rule = new();

    [Fact]
    public async Task Raises_NF450_for_ActionFilterAttribute_subclass()
    {
        var source = @"
using System.Web.Mvc;

public class LogActionFilter : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext filterContext)
    {
        // log before action
    }

    public override void OnActionExecuted(ActionExecutedContext filterContext)
    {
        // log after action
    }
}";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        // Tier 3 — source is never modified
        output.Should().Be(source);
        result.Transformations.Should().BeEmpty();

        result.Issues.Should().Contain(i => i.Id == IssueIds.ActionFilterManualMigration);
        var issue = result.Issues.Single(i => i.Id == IssueIds.ActionFilterManualMigration);
        issue.Title.Should().Contain("LogActionFilter");
        issue.Recommendation.Should().Contain("OnActionExecuting");
        issue.Recommendation.Should().Contain("ActionExecutingContext");
    }

    [Fact]
    public async Task Raises_NF451_for_AuthorizationFilterAttribute_subclass()
    {
        var source = @"
public class RequireRoleFilter : AuthorizationFilterAttribute
{
    public override void OnAuthorization(AuthorizationContext filterContext)
    {
        // check role
    }
}";
        var (_, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        result.Issues.Should().Contain(i => i.Id == IssueIds.AuthFilterManualMigration);
        var issue = result.Issues.Single(i => i.Id == IssueIds.AuthFilterManualMigration);
        issue.Recommendation.Should().Contain("AuthorizationFilterContext");
        issue.Recommendation.Should().Contain("policy-based authorization");
    }

    [Fact]
    public async Task Raises_NF454_for_ExceptionFilterAttribute_subclass()
    {
        // NF454 is used for exception filters (NF452 is reserved for IHttpModule)
        var source = @"
public class GlobalExceptionFilter : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext filterContext)
    {
        // handle exception
    }
}";
        var (_, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        result.Issues.Should().Contain(i => i.Id == "NF454");
        result.Issues.Single(i => i.Id == "NF454").Recommendation
            .Should().Contain("ExceptionContext");
    }

    [Fact]
    public async Task Includes_overridden_method_names_in_issue_description()
    {
        var source = @"
public class MyFilter : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext ctx) { }
    public override void OnResultExecuting(ResultExecutingContext ctx) { }
}";
        var (_, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        result.Issues.Should().Contain(i => i.Id == IssueIds.ActionFilterManualMigration);
        var issue = result.Issues.Single(i => i.Id == IssueIds.ActionFilterManualMigration);
        issue.Description.Should().Contain("OnActionExecuting");
        issue.Description.Should().Contain("OnResultExecuting");
    }

    [Fact]
    public async Task Raises_NF450_for_IActionFilter_implementation()
    {
        var source = @"
public class MyFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context) { }
    public void OnActionExecuted(ActionExecutedContext context) { }
}";
        var (_, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        result.Issues.Should().Contain(i => i.Id == IssueIds.ActionFilterManualMigration);
    }

    [Fact]
    public async Task Does_not_flag_classes_with_no_filter_base()
    {
        var source = @"
using Microsoft.AspNetCore.Mvc;
public class FooController : ControllerBase
{
    public IActionResult Get() => Ok();
}";
        var (_, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public void IsApplicable_returns_true_for_ActionFilterAttribute()
    {
        var tree = RuleTestHelpers.ParseSource(
            "public class F : ActionFilterAttribute {}");
        _rule.IsApplicable(tree).Should().BeTrue();
    }

    [Fact]
    public void IsApplicable_returns_false_for_plain_class()
    {
        var tree = RuleTestHelpers.ParseSource(
            "public class F { public void OnActionExecuting() {} }");
        _rule.IsApplicable(tree).Should().BeFalse();
    }
}
