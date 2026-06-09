using FluentAssertions;
using NetForward.Analyzer;
using NetForward.Rewriter.Rewriters;
using Xunit;

namespace NetForward.Rewriter.Tests;

public class R011HttpModuleHandlerAdvisorTests
{
    private readonly R011HttpModuleHandlerAdvisor _rule = new();

    [Fact]
    public async Task Raises_NF452_for_IHttpModule_implementation()
    {
        var source = @"
using System.Web;

public class RequestLoggingModule : IHttpModule
{
    public void Init(HttpApplication context)
    {
        context.BeginRequest += OnBeginRequest;
        context.EndRequest += OnEndRequest;
    }

    private void OnBeginRequest(object sender, EventArgs e) { }
    private void OnEndRequest(object sender, EventArgs e) { }

    public void Dispose() { }
}";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        // Tier 3 — source never modified
        output.Should().Be(source);
        result.Transformations.Should().BeEmpty();

        result.Issues.Should().Contain(i => i.Id == IssueIds.HttpModuleToMiddleware);
        var issue = result.Issues.Single(i => i.Id == IssueIds.HttpModuleToMiddleware);
        issue.Title.Should().Contain("RequestLoggingModule");
        issue.Recommendation.Should().Contain("RequestDelegate _next");
        issue.Recommendation.Should().Contain("InvokeAsync");
        issue.Recommendation.Should().Contain("UseMiddleware");
    }

    [Fact]
    public async Task Middleware_stub_includes_BeginRequest_and_EndRequest_hints()
    {
        var source = @"
public class AuditModule : IHttpModule
{
    public void Init(HttpApplication context)
    {
        context.BeginRequest += OnBegin;
        context.EndRequest += OnEnd;
    }
    public void Dispose() { }
}";
        var (_, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        var issue = result.Issues.Single(i => i.Id == IssueIds.HttpModuleToMiddleware);
        issue.Recommendation.Should().Contain("BeginRequest logic here");
        issue.Recommendation.Should().Contain("EndRequest logic here");
    }

    [Fact]
    public async Task Raises_NF453_for_IHttpHandler_implementation()
    {
        var source = @"
using System.Web;

public class ImageResizeHandler : IHttpHandler
{
    public bool IsReusable => false;

    public void ProcessRequest(HttpContext context)
    {
        context.Response.ContentType = ""image/png"";
    }
}";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        output.Should().Be(source);
        result.Issues.Should().Contain(i => i.Id == IssueIds.HttpHandlerToEndpoint);

        var issue = result.Issues.Single(i => i.Id == IssueIds.HttpHandlerToEndpoint);
        issue.Title.Should().Contain("ImageResizeHandler");
        issue.Recommendation.Should().Contain("app.MapGet");
        issue.Recommendation.Should().Contain("ProcessRequest");
    }

    [Fact]
    public async Task Raises_NF453_for_IHttpAsyncHandler_implementation()
    {
        var source = @"
public class AsyncDownloadHandler : IHttpAsyncHandler
{
    public bool IsReusable => false;
    public IAsyncResult BeginProcessRequest(HttpContext ctx, AsyncCallback cb, object state) => null;
    public void EndProcessRequest(IAsyncResult result) { }
    public void ProcessRequest(HttpContext ctx) { }
}";
        var (_, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        result.Issues.Should().Contain(i => i.Id == IssueIds.HttpHandlerToEndpoint);
        result.Issues.Single(i => i.Id == IssueIds.HttpHandlerToEndpoint)
            .Recommendation.Should().Contain("MapGet");
    }

    [Fact]
    public async Task Handler_stub_contains_endpoint_route_based_on_class_name()
    {
        var source = @"
public class ReportDownloadHandler : IHttpHandler
{
    public bool IsReusable => false;
    public void ProcessRequest(HttpContext context) { }
}";
        var (_, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        var issue = result.Issues.Single(i => i.Id == IssueIds.HttpHandlerToEndpoint);
        // Route name is derived from class name (lowercased, "Handler" stripped)
        issue.Recommendation.Should().Contain("reportdownload");
    }

    [Fact]
    public async Task Does_not_flag_classes_with_no_module_or_handler_base()
    {
        var source = @"
public class OrderService
{
    public void Process() { }
}";
        var (_, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public async Task Handles_multiple_modules_in_same_file()
    {
        var source = @"
public class ModuleA : IHttpModule
{
    public void Init(HttpApplication ctx) { }
    public void Dispose() { }
}

public class ModuleB : IHttpModule
{
    public void Init(HttpApplication ctx) { }
    public void Dispose() { }
}";
        var (_, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        result.Issues.Should().HaveCount(2);
        result.Issues.Select(i => i.Title)
            .Should().Contain(t => t.Contains("ModuleA"))
            .And.Contain(t => t.Contains("ModuleB"));
    }

    [Fact]
    public void IsApplicable_returns_true_for_IHttpModule()
    {
        var tree = RuleTestHelpers.ParseSource(
            "public class M : IHttpModule { public void Init(HttpApplication a) {} public void Dispose() {} }");
        _rule.IsApplicable(tree).Should().BeTrue();
    }

    [Fact]
    public void IsApplicable_returns_true_for_IHttpHandler()
    {
        var tree = RuleTestHelpers.ParseSource(
            "public class H : IHttpHandler { public bool IsReusable => false; public void ProcessRequest(HttpContext c) {} }");
        _rule.IsApplicable(tree).Should().BeTrue();
    }

    [Fact]
    public void IsApplicable_returns_false_for_unrelated_class()
    {
        var tree = RuleTestHelpers.ParseSource("public class Foo {}");
        _rule.IsApplicable(tree).Should().BeFalse();
    }
}
