namespace NetForward.Core.Models;

/// <summary>
/// The classification of a legacy .NET project. Drives which migration path we recommend.
/// </summary>
public enum ProjectType
{
    Unknown = 0,

    /// <summary>Class library (no UI, no web).</summary>
    ClassLibrary,

    /// <summary>ASP.NET MVC 3/4/5 application (System.Web.Mvc).</summary>
    AspNetMvc,

    /// <summary>ASP.NET Web API 2 application (System.Web.Http).</summary>
    AspNetWebApi,

    /// <summary>Classic ASP.NET Web Forms (.aspx).</summary>
    WebForms,

    /// <summary>WCF service host or library.</summary>
    Wcf,

    /// <summary>ASMX web service (legacy SOAP).</summary>
    Asmx,

    /// <summary>Windows Forms desktop application.</summary>
    WinForms,

    /// <summary>WPF desktop application.</summary>
    Wpf,

    /// <summary>Console application.</summary>
    Console,

    /// <summary>Test project (MSTest, NUnit, xUnit).</summary>
    Test,

    /// <summary>Already an SDK-style modern .NET project — no work needed.</summary>
    AlreadyModern
}
