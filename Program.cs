using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StudentServicesMarketplace.Data;
using StudentServicesMarketplace.Models;
using StudentServicesMarketplace.Configuration; // For StripeSettings
using StudentServicesMarketplace.Interfaces;   // For IPaymentProcessor, IPaymentService
using StudentServicesMarketplace.Services;    // For StripePaymentProcessor, PaymentService
using Stripe; // For StripeConfiguration in services

var builder = WebApplication.CreateBuilder(args);

// 1. Configure Services
// Add DbContext
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString)); // Or .UseSqlite for SQLite

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// Add Identity
builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = false; // Set to true for production after setting up email confirmation
                                                    // Configure password policies as needed for production
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Password.RequiredLength = 6;
})
    .AddRoles<IdentityRole>() // Enable role management
    .AddEntityFrameworkStores<ApplicationDbContext>();

// Configure StripeSettings from appsettings.json
builder.Services.Configure<StripeSettings>(builder.Configuration.GetSection("StripeSettings"));

// Register custom services for Dependency Injection
builder.Services.AddScoped<IPaymentProcessor, StripePaymentProcessor>();
builder.Services.AddScoped<IPaymentService, PaymentService>();

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages(); // For Identity UI scaffolding

// Configure session (optional, but can be useful)
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

var app = builder.Build();

// 2. Configure HTTP Request Pipeline
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts(); // Enforce HTTPS in production
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// Stripe Webhook Configuration: Must be before UseAuthentication/UseAuthorization if webhook doesn't need auth.
// However, our current StripeWebhookController is an API controller and typically doesn't need session/auth cookies.
// If it did, order might matter. For now, this is fine.
// Make sure Stripe CLI or ngrok is configured correctly for local webhook testing.

app.UseAuthentication(); // Who are you?
app.UseAuthorization();  // Are you allowed?

app.UseSession(); // If using sessions

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages(); // Maps Identity Razor Pages

// Seed database (optional but good for development)
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

        await context.Database.MigrateAsync(); // Apply pending migrations

        // TODO: Implement a more robust DbInitializer class for complex seeding
        // For now, roles are seeded in ApplicationDbContext.OnModelCreating
        // You might want to seed an admin user here.
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred seeding the DB or applying migrations.");
    }
}

app.Run();