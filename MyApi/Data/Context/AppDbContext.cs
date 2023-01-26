using Microsoft.EntityFrameworkCore;
using MyApi.Data.Model;

namespace MyApi.Data.Context;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>()
            .HasData(
                new Product
                {
                    Id = 1,
                    Name = "Gaming Mouse",
                    Description = "Gaming mouse with some RGB lightning",
                    Price = 69.99M
                },
                new Product
                {
                    Id = 2,
                    Name = "RGB mechanical keyboard",
                    Description = "Mechanical keyboard with some RGB lightning goodness",
                    Price = 99.99M
                });
    }
}