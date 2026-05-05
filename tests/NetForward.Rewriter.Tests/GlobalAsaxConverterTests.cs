using FluentAssertions;
using NetForward.Converters.Startup;
using Xunit;

namespace NetForward.Rewriter.Tests;

public class GlobalAsaxConverterTests : IDisposable
{
    private readonly string _tempDir;

    public GlobalAsaxConverterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"NetForwardGlobalAsaxTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
    }

    private string WriteGlobalAsax(string content)
    {
        var path = Path.Combine(_tempDir, "Global.asax.cs");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Produces_valid_Program_cs_scaffold()
    {
        var path = WriteGlobalAsax(@"
public class MvcApplication : System.Web.HttpApplication
{
    protected void Application_Start()
    {
        AreaRegistration.RegisterAllAreas();
    }
}");
        var outDir = Path.Combine(_tempDir, "output");
        var result = new GlobalAsaxConverter().Convert(path, outDir);

        result.ProgramCsContent.Should().Contain("var builder = WebApplication.CreateBuilder(args);");
        result.ProgramCsContent.Should().Contain("var app = builder.Build();");
        result.ProgramCsContent.Should().Contain("app.Run();");
        File.Exists(result.OutputPath).Should().BeTrue();
    }

    [Fact]
    public void Preserves_Application_Start_body_as_comment()
    {
        var path = WriteGlobalAsax(@"
public class MvcApplication : System.Web.HttpApplication
{
    protected void Application_Start()
    {
        RouteConfig.RegisterRoutes(RouteTable.Routes);
    }
}");
        var outDir = Path.Combine(_tempDir, "output");
        var result = new GlobalAsaxConverter().Convert(path, outDir);

        result.ProgramCsContent.Should().Contain("RouteConfig.RegisterRoutes");
        result.Notes.Should().Contain(n => n.Contains("Application_Start"));
    }

    [Fact]
    public void Adds_middleware_stub_for_BeginRequest_and_EndRequest()
    {
        var path = WriteGlobalAsax(@"
public class MvcApplication : System.Web.HttpApplication
{
    protected void Application_BeginRequest() { }
    protected void Application_EndRequest() { }
}");
        var outDir = Path.Combine(_tempDir, "output");
        var result = new GlobalAsaxConverter().Convert(path, outDir);

        result.ProgramCsContent.Should().Contain("BeginRequest");
        result.Notes.Should().Contain(n => n.Contains("BeginRequest"));
    }

    [Fact]
    public void Adds_session_hint_when_Session_Start_present()
    {
        var path = WriteGlobalAsax(@"
public class MvcApplication : System.Web.HttpApplication
{
    protected void Session_Start() { }
}");
        var outDir = Path.Combine(_tempDir, "output");
        var result = new GlobalAsaxConverter().Convert(path, outDir);

        result.ProgramCsContent.Should().Contain("AddSession");
        result.Notes.Should().Contain(n => n.Contains("Session_Start"));
    }
}
