using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StudentServicesMarketplace.Data;
using StudentServicesMarketplace.Models;
using System.Linq;
using System.Threading.Tasks;

namespace StudentServicesMarketplace.Controllers
{
    public class ServicesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public ServicesController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // GET: Services (Browse All)
        public async Task<IActionResult> Index(string searchString, ServiceCategory? category)
        {
            ViewData["CurrentFilter"] = searchString;
            ViewData["CurrentCategory"] = category;

            var servicesQuery = _context.Services
                                   .Include(s => s.Student)
                                   .Where(s => s.IsActive);

            if (!string.IsNullOrEmpty(searchString))
            {
                servicesQuery = servicesQuery.Where(s => s.Title.Contains(searchString) || s.Description.Contains(searchString));
            }

            if (category.HasValue)
            {
                servicesQuery = servicesQuery.Where(s => s.Category == category.Value);
            }

            return View(await servicesQuery.OrderByDescending(s => s.DatePosted).ToListAsync());
        }

        // GET: Services/Details/5
        // Controllers/ServicesController.cs

        // GET: Services/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var service = await _context.Services
                .Include(s => s.Student) // Eager load student details
                .FirstOrDefaultAsync(m => m.Id == id); // Show even if inactive for owner/admin, filter on display

            if (service == null) return NotFound();

            // Check if the service provider (student) has completed Stripe onboarding
            bool providerReadyForPayment = service.Student != null &&
                                           service.Student.StripeOnboardingComplete &&
                                           !string.IsNullOrEmpty(service.Student.StripeConnectedAccountId);
            ViewBag.ProviderReadyForPayment = providerReadyForPayment;

            // Determine if the current user is the owner of the service
            var currentUserId = _userManager.GetUserId(User);
            ViewBag.IsOwner = (User.Identity.IsAuthenticated && service.StudentId == currentUserId);


            // Only show active services to non-owners/non-admins
            if (!service.IsActive && !ViewBag.IsOwner && !User.IsInRole("Admin"))
            {
                TempData["InfoMessage"] = "This service is currently not active.";
                return RedirectToAction(nameof(Index)); // Or show a specific "not active" page
            }


            return View(service);
        }

        // GET: Services/Create
        [Authorize(Roles = "Student")]
        public IActionResult Create()
        {
            // Optionally check if student has completed Stripe onboarding before allowing service creation
            // var currentUser = await _userManager.GetUserAsync(User);
            // if (currentUser != null && !currentUser.StripeOnboardingComplete) {
            //    TempData["ErrorMessage"] = "Please complete your payment setup before posting services.";
            //    return RedirectToAction("Onboard", "StripeOnboarding");
            // }
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> Create([Bind("Title,Description,Category,Price,ImageUrl")] Service service)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null) return Challenge();

            // Ensure student has completed Stripe onboarding
            //if (!currentUser.StripeOnboardingComplete || string.IsNullOrEmpty(currentUser.StripeConnectedAccountId))
            //{
            //    TempData["ErrorMessage"] = "You must complete your payment setup with Stripe before you can post services. Please go to 'Manage Payments'.";
            //    // Redirect them to a page where they can manage their Stripe onboarding
            //    return RedirectToAction("Onboard", "StripeOnboarding");
            //}


            service.StudentId = currentUser.Id;
            service.DatePosted = DateTime.UtcNow;
            service.IsActive = true;

            ModelState.Remove("StudentId");
            ModelState.Remove("Student");

            if (ModelState.IsValid)
            {
                _context.Add(service);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Service created successfully!";
                return RedirectToAction(nameof(MyServices));
            }
            return View(service);
        }

        // GET: Services/MyServices (Student's own services)
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> MyServices()
        {
            var currentUser = await _userManager.GetUserAsync(User);
            var services = await _context.Services
                .Where(s => s.StudentId == currentUser.Id)
                .OrderByDescending(s => s.DatePosted)
                .ToListAsync();
            return View(services);
        }

        // GET: Services/Edit/5
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var service = await _context.Services.FindAsync(id);
            if (service == null) return NotFound();

            var currentUser = await _userManager.GetUserAsync(User);
            if (service.StudentId != currentUser.Id) return Forbid();

            return View(service);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Title,Description,Category,Price,ImageUrl,IsActive")] Service service)
        {
            if (id != service.Id) return NotFound();

            var serviceToUpdate = await _context.Services.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
            if (serviceToUpdate == null) return NotFound();

            var currentUser = await _userManager.GetUserAsync(User);
            if (serviceToUpdate.StudentId != currentUser.Id) return Forbid();

            service.StudentId = serviceToUpdate.StudentId; // Preserve original StudentId
            service.DatePosted = serviceToUpdate.DatePosted; // Preserve original DatePosted

            ModelState.Remove("Student");

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(service);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = "Service updated successfully!";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!ServiceExists(service.Id)) return NotFound(); else throw;
                }
                return RedirectToAction(nameof(MyServices));
            }
            return View(service);
        }

        // GET: Services/Delete/5
        [Authorize(Roles = "Student,Admin")]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();
            var service = await _context.Services.Include(s => s.Student).FirstOrDefaultAsync(m => m.Id == id);
            if (service == null) return NotFound();
            var currentUser = await _userManager.GetUserAsync(User);
            if (service.StudentId != currentUser.Id && !User.IsInRole("Admin")) return Forbid();
            return View(service);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Student,Admin")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var service = await _context.Services.FindAsync(id);
            if (service == null) return NotFound();
            var currentUser = await _userManager.GetUserAsync(User);
            if (service.StudentId != currentUser.Id && !User.IsInRole("Admin")) return Forbid();

            // Check for existing orders before deleting - or handle via cascade delete in DB
            var hasOrders = await _context.Orders.AnyAsync(o => o.ServiceId == id && o.Status != OrderStatus.Completed && o.Status != OrderStatus.CancelledByClient && o.Status != OrderStatus.CancelledByStudent);
            if (hasOrders)
            {
                TempData["ErrorMessage"] = "Cannot delete service with active orders. Please complete or cancel orders first.";
                return RedirectToAction(nameof(Delete), new { id = id });
            }

            _context.Services.Remove(service);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Service deleted successfully.";

            if (User.IsInRole("Admin") && service.StudentId != currentUser.Id) return RedirectToAction(nameof(Index));
            return RedirectToAction(nameof(MyServices));
        }



        private bool ServiceExists(int id) => _context.Services.Any(e => e.Id == id);
    }
}