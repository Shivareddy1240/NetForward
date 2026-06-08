using FluentAssertions;
using NetForward.Compatibility;
using NetForward.Rewriter.Pipeline;
using NetForward.Rewriter.Rewriters;
using Xunit;

namespace NetForward.Rewriter.Tests;

/// <summary>
/// Integration tests that run the full Sprint 1 + Sprint 2 rule chain
/// against synthetic source strings representing the LegacyMvcWithEf patterns.
///
/// We don't use MSBuildWorkspace here (no SDK needed, fast) — instead we
/// compose the rules directly and run them against parsed source trees.
/// The compilation verification integration test lives separately and
/// requires a real SDK install.
/// </summary>
public class PipelineIntegrationTests
{
    private static readonly IReadOnlyList<IRewriteRule> AllSprint1And2Rules =
    [
        new R001NamespaceRewriter(),
        new R002ControllerBaseRewriter(),
        new R003ActionResultRewriter(),
        new R004AttributeConversionRewriter(),
        new R005RoutePrefixRewriter(),
        new R006IHttpActionResultRewriter(),
    ];

    private static async Task<string> RunAllRulesAsync(string source)
    {
        var catalog = new YamlCompatibilityCatalog();
        var options = new RewriteOptions
        {
            DryRun = true,
            OutputRoot = Path.GetTempPath(),
            MaxTier = RewriteTier.Tier2
        };

        var tree = RuleTestHelpers.ParseSource(source);
        var context = RuleTestHelpers.BuildContext(tree, options);

        var currentTree = tree;
        foreach (var rule in AllSprint1And2Rules)
        {
            if (!rule.IsApplicable(currentTree)) continue;
            var result = await rule.ApplyAsync(context, currentTree);
            currentTree = result.OutputTree;
        }

        return currentTree.ToString();
    }

    [Fact]
    public async Task Full_chain_migrates_MVC5_controller_correctly()
    {
        var source = @"
using System.Web.Mvc;

[RoutePrefix(""orders"")]
public class OrdersController : Controller
{
    [HttpGet]
    [Route("""")]
    public ActionResult Index()
    {
        return View();
    }

    [HttpGet]
    [Route(""{id:int}"")]
    public ActionResult Details(int id)
    {
        if (id <= 0)
            return HttpNotFound();
        return View();
    }

    [HttpPost]
    [Route(""create"")]
    [ValidateAntiForgeryToken]
    public ActionResult Create()
    {
        if (!ModelState.IsValid)
            return HttpBadRequest();
        return RedirectToAction(""Index"");
    }

    [HttpGet]
    [Route(""error"")]
    public ActionResult Error()
    {
        return new HttpStatusCodeResult(503);
    }
}";
        var output = await RunAllRulesAsync(source);

        // R001: namespace fixed
        output.Should().Contain("using Microsoft.AspNetCore.Mvc;");
        output.Should().NotContain("using System.Web.Mvc;");

        // R003: action results updated
        output.Should().Contain("NotFound()");
        output.Should().Contain("BadRequest()");
        output.Should().Contain("StatusCode(503)");
        output.Should().NotContain("HttpNotFound()");
        output.Should().NotContain("HttpBadRequest()");
        output.Should().NotContain("HttpStatusCodeResult");

        // R005: RoutePrefix consolidated
        output.Should().Contain("[Route(\"orders\")]");
        output.Should().NotContain("[RoutePrefix");
    }

    [Fact]
    public async Task Full_chain_migrates_WebApi2_controller_correctly()
    {
        var source = @"
using System.Web.Http;

[RoutePrefix(""api/products"")]
public class ProductsApiController : ApiController
{
    [HttpGet]
    [Route("""")]
    public IHttpActionResult GetAll()
    {
        return Ok(new[] { ""a"", ""b"" });
    }

    [HttpGet]
    [Route(""{id:int}"")]
    public IHttpActionResult GetById(int id)
    {
        if (id <= 0) return NotFound();
        return Ok(id);
    }

    [HttpGet]
    [Route(""error"")]
    public IHttpActionResult TriggerError()
    {
        return InternalServerError();
    }

    [HttpDelete]
    [Route(""{id:int}"")]
    public async System.Threading.Tasks.Task<IHttpActionResult> DeleteAsync(int id)
    {
        return Ok();
    }
}";
        var output = await RunAllRulesAsync(source);

        // R001: namespace fixed
        output.Should().Contain("using Microsoft.AspNetCore.Mvc;");
        output.Should().NotContain("using System.Web.Http;");

        // R002: ApiController → ControllerBase + attributes
        output.Should().Contain(": ControllerBase");
        output.Should().Contain("[ApiController]");
        output.Should().NotContain(": ApiController");

        // R003 + R006: IHttpActionResult → IActionResult throughout
        output.Should().NotContain("IHttpActionResult");
        output.Should().Contain("IActionResult");

        // R005: RoutePrefix consolidated
        output.Should().Contain("[Route(\"api/products\")]");
        output.Should().NotContain("[RoutePrefix");

        // R003: InternalServerError → StatusCode(500)
        output.Should().Contain("StatusCode(500)");

        // R006: Task<IHttpActionResult> → Task<IActionResult>
        output.Should().Contain("Task<IActionResult>");
    }

    [Fact]
    public async Task Full_chain_removes_legacy_attributes_and_raises_issues()
    {
        var source = @"
using System.Web.Mvc;

[HandleError]
public class FooController : Controller
{
    [OutputCache(Duration = 30)]
    [ChildActionOnly]
    public ActionResult Partial() => View();

    public ActionResult Get([System.Web.Http.FromUri] string filter) => View();
}";
        var catalog = new YamlCompatibilityCatalog();
        var options = new RewriteOptions
        {
            DryRun = true,
            OutputRoot = Path.GetTempPath(),
            MaxTier = RewriteTier.Tier2
        };
        var tree = RuleTestHelpers.ParseSource(source);
        var context = RuleTestHelpers.BuildContext(tree, options);

        var currentTree = tree;
        var allIssues = new List<NetForward.Core.Models.MigrationIssue>();

        foreach (var rule in AllSprint1And2Rules)
        {
            if (!rule.IsApplicable(currentTree)) continue;
            var result = await rule.ApplyAsync(context, currentTree);
            currentTree = result.OutputTree;
            allIssues.AddRange(result.Issues);
        }

        var output = currentTree.ToString();

        // Removed attributes
        output.Should().NotContain("[HandleError]");
        output.Should().NotContain("[OutputCache");
        output.Should().NotContain("[ChildActionOnly]");

        // Issues raised
        allIssues.Should().Contain(i => i.Id == "NF406"); // HandleError
        allIssues.Should().Contain(i => i.Id == "NF405"); // OutputCache
        allIssues.Should().Contain(i => i.Id == "NF404"); // ChildActionOnly

        // FromUri → FromQuery
        output.Should().Contain("FromQuery");
    }

    [Fact]
    public async Task Rules_are_idempotent_when_run_twice()
    {
        var source = @"
using System.Web.Mvc;
public class HomeController : Controller
{
    public ActionResult Index() => View();
    public ActionResult NotFoundAction() => HttpNotFound();
}";
        var output1 = await RunAllRulesAsync(source);
        var output2 = await RunAllRulesAsync(output1);

        // Running the rules a second time on already-migrated source
        // should produce identical output (no double-transformations).
        output2.Should().Be(output1);
    }
}
