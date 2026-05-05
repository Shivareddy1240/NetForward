using System.Web.Mvc;
using System.Web.Routing;
using LegacyMvcWithEf.Services;

namespace LegacyMvcWithEf.Controllers
{
    [RoutePrefix("orders")]
    public class OrdersController : Controller
    {
        private readonly IOrderService _orderService;

        public OrdersController(IOrderService orderService)
        {
            _orderService = orderService;
        }

        [HttpGet]
        [Route("")]
        public ActionResult Index()
        {
            var orders = _orderService.GetAll();
            return View(orders);
        }

        [HttpGet]
        [Route("{id:int}")]
        public ActionResult Details(int id)
        {
            var order = _orderService.GetById(id);
            if (order == null)
                return HttpNotFound();

            return View(order);
        }

        [HttpPost]
        [Route("create")]
        [ValidateAntiForgeryToken]
        public ActionResult Create(Models.Order order)
        {
            if (!ModelState.IsValid)
                return HttpBadRequest();

            _orderService.Create(order);
            return RedirectToAction("Index");
        }

        [HttpPost]
        [Route("{id:int}/delete")]
        public ActionResult Delete(int id)
        {
            var order = _orderService.GetById(id);
            if (order == null)
                return HttpNotFound();

            _orderService.Delete(id);
            return RedirectToAction("Index");
        }

        [HttpGet]
        [Route("error")]
        public ActionResult ServerError()
        {
            return new HttpStatusCodeResult(500, "Internal error");
        }
    }
}
