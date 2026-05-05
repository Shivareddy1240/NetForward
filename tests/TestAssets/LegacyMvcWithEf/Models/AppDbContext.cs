using System.Data.Entity;

namespace LegacyMvcWithEf.Models
{
    public class Order
    {
        public int Id { get; set; }
        public string CustomerName { get; set; }
        public decimal Total { get; set; }
    }

    public class AppDbContext : DbContext
    {
        public AppDbContext() : base("DefaultConnection") { }
        public DbSet<Order> Orders { get; set; }
    }
}
