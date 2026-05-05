using FluentAssertions;
using NetForward.Rewriter.Rewriters;
using Xunit;

namespace NetForward.Rewriter.Tests;

public class R001NamespaceRewriterTests
{
    private readonly R001NamespaceRewriter _rule = new();

    [Fact]
    public async Task Rewrites_SystemWebMvc_to_AspNetCoreMvc()
    {
        var source = @"
using System.Web.Mvc;

namespace MyApp.Controllers
{
    public class HomeController : Controller { }
}";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        output.Should().Contain("using Microsoft.AspNetCore.Mvc;");
        output.Should().NotContain("using System.Web.Mvc;");
        result.Transformations.Should().HaveCount(1);
        result.Transformations[0].RuleId.Should().Be("R001");
    }

    [Fact]
    public async Task Rewrites_SystemWebHttp_to_AspNetCoreMvc()
    {
        var source = @"
using System.Web.Http;

namespace MyApp.Controllers
{
    public class ValuesController : ApiController { }
}";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        output.Should().Contain("using Microsoft.AspNetCore.Mvc;");
        output.Should().NotContain("using System.Web.Http;");
        result.Transformations.Should().HaveCount(1);
    }

    [Fact]
    public async Task Rewrites_multiple_legacy_usings_in_one_pass()
    {
        var source = @"
using System.Web.Mvc;
using System.Web.Http;
using System.Web.Routing;
using System.Configuration;
using System.Collections.Generic;
";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        output.Should().Contain("using Microsoft.AspNetCore.Mvc;");
        output.Should().Contain("using Microsoft.AspNetCore.Routing;");
        output.Should().Contain("using Microsoft.Extensions.Configuration;");
        // Non-legacy namespace should be unchanged
        output.Should().Contain("using System.Collections.Generic;");
        result.Transformations.Should().HaveCount(4);
    }

    [Fact]
    public async Task Rewrites_SystemWebMvcFilters_subnamespace()
    {
        var source = @"using System.Web.Mvc.Filters;";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        output.Should().Contain("Microsoft.AspNetCore.Mvc.Filters");
        result.Transformations.Should().HaveCount(1);
    }

    [Fact]
    public async Task Does_not_modify_already_modern_namespaces()
    {
        var source = @"
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
";
        var (output, result) = await RuleTestHelpers.ApplyRuleAsync(_rule, source);

        output.Should().Be(source);
        result.Transformations.Should().BeEmpty();
    }

    [Fact]
    public void IsApplicable_returns_true_for_legacy_namespaces()
    {
        var tree = RuleTestHelpers.ParseSource("using System.Web.Mvc;");
        _rule.IsApplicable(tree).Should().BeTrue();
    }

    [Fact]
    public void IsApplicable_returns_false_for_modern_only_source()
    {
        var tree = RuleTestHelpers.ParseSource("using Microsoft.AspNetCore.Mvc;");
        _rule.IsApplicable(tree).Should().BeFalse();
    }
}
