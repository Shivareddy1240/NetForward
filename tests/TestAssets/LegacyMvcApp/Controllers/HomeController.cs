using System.Web.Mvc;

namespace LegacyMvcApp.Controllers
{
    public class HomeController : Controller
    {
        public ActionResult Index() => View();
    }
}
