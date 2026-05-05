using System.Collections.Generic;
using LegacyMvcWithEf.Models;

namespace LegacyMvcWithEf.Services
{
    public interface IOrderService
    {
        IEnumerable<Order> GetAll();
        Order GetById(int id);
        void Create(Order order);
        void Delete(int id);
    }

    public class OrderService : IOrderService
    {
        private readonly AppDbContext _db;

        public OrderService(AppDbContext db)
        {
            _db = db;
        }

        public IEnumerable<Order> GetAll() => _db.Orders;
        public Order GetById(int id) => _db.Orders.Find(id);

        public void Create(Order order)
        {
            _db.Orders.Add(order);
            _db.SaveChanges();
        }

        public void Delete(int id)
        {
            var order = _db.Orders.Find(id);
            if (order != null)
            {
                _db.Orders.Remove(order);
                _db.SaveChanges();
            }
        }
    }
}
