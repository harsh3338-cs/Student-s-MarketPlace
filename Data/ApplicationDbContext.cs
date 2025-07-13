using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using StudentServicesMarketplace.Models;

namespace StudentServicesMarketplace.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Service> Services { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<Advertisement> Advertisements { get; set; }
        public DbSet<PaymentTransaction> PaymentTransactions { get; set; }
        public DbSet<SupportTicket> SupportTickets { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Configure relationships explicitly if needed, though many are inferred.
            builder.Entity<Service>()
                .HasOne(s => s.Student)
                .WithMany(u => u.ServicesProvided)
                .HasForeignKey(s => s.StudentId)
                .OnDelete(DeleteBehavior.Restrict); // Prevent deleting student if they have services

            builder.Entity<Order>()
                .HasOne(o => o.Client)
                .WithMany(u => u.OrdersPlaced)
                .HasForeignKey(o => o.ClientId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Order>()
                .HasOne(o => o.Service)
                .WithMany(s => s.Orders)
                .HasForeignKey(o => o.ServiceId)
                .OnDelete(DeleteBehavior.Cascade); // If service is deleted, orders for it are too (or Restrict)

            builder.Entity<PaymentTransaction>()
                .HasOne(pt => pt.Order)
                .WithMany(o => o.PaymentTransactions)
                .HasForeignKey(pt => pt.OrderId);

            builder.Entity<SupportTicket>()
                .HasOne(st => st.User)
                .WithMany(u => u.SupportTickets)
                .HasForeignKey(st => st.UserId);

            builder.Entity<SupportTicket>()
                .HasOne(st => st.AdminRepliedBy)
                .WithMany() // No inverse navigation property on ApplicationUser for admin replies
                .HasForeignKey(st => st.AdminRepliedById)
                .IsRequired(false); // AdminRepliedById can be null

            // Seed Roles
            builder.Entity<IdentityRole>().HasData(
                new IdentityRole { Id = "1", Name = "Admin", NormalizedName = "ADMIN" },
                new IdentityRole { Id = "2", Name = "Student", NormalizedName = "STUDENT" }, // Service Provider
                new IdentityRole { Id = "3", Name = "Client", NormalizedName = "CLIENT" }
            );

            // Seed Sample Ads (optional)
            builder.Entity<Advertisement>().HasData(
                new Advertisement { Id = 1, Title = "Campus Bookstore Sale", ImageUrl = "https://via.placeholder.com/300x250.png?text=Bookstore+Ad", TargetUrl = "#", Placement = AdPlacement.Sidebar, IsActive = true, DisplayOrder = 1 },
                new Advertisement { Id = 2, Title = "Tech Gadgets Discount", ImageUrl = "https://via.placeholder.com/728x90.png?text=Tech+Ad", TargetUrl = "#", Placement = AdPlacement.Header, IsActive = true, DisplayOrder = 1 }
            );
        }
    }
}