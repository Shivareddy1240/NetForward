using System.Collections.Generic;
using System.Web.Http;
using LegacyMvcWithEf.Services;

namespace LegacyMvcWithEf.Controllers
{
    [RoutePrefix("api/products")]
    public class ProductsApiController : ApiController
    {
        private readonly IOrderService _orderService;

        public ProductsApiController(IOrderService orderService)
        {
            _orderService = orderService;
        }

        [HttpGet]
        [Route("")]
        public IHttpActionResult GetAll()
        {
            var orders = _orderService.GetAll();
            return Ok(orders);
        }

        [HttpGet]
        [Route("{id:int}")]
        public IHttpActionResult GetById(int id)
        {
            var order = _orderService.GetById(id);
            if (order == null)
                return NotFound();

            return Ok(order);
        }

        [HttpPost]
        [Route("")]
        public IHttpActionResult Create(Models.Order order)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            _orderService.Create(order);
            return Ok(order);
        }

        [HttpDelete]
        [Route("{id:int}")]
        public IHttpActionResult Delete(int id)
        {
            var order = _orderService.GetById(id);
            if (order == null)
                return NotFound();

            _orderService.Delete(id);
            return Ok();
        }

        [HttpGet]
        [Route("error")]
        public IHttpActionResult TriggerError()
        {
            return InternalServerError();
        }
    }
}
