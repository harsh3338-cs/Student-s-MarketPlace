using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StudentServicesMarketplace.Data;
using StudentServicesMarketplace.Models;
using StudentServicesMarketplace.Interfaces; // For IPaymentService
using StudentServicesMarketplace.Configuration; // For StripeSettings
using System; // For DateTime
using System.Linq;
using System.Threading.Tasks;

namespace StudentServicesMarketplace.Controllers
{
    [Authorize]
    public class OrdersController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IPaymentService _paymentService;
        private readonly StripeSettings _stripeSettings;

        public OrdersController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IPaymentService paymentService,
            IOptions<StripeSettings> stripeSettings)
        {
            _context = context;
            _userManager = userManager;
            _paymentService = paymentService;
            _stripeSettings = stripeSettings.Value;
        }

        // GET: Orders/Create/ForService/5
        [Authorize(Roles = "Client")]
        // Controllers/OrdersController.cs

        // GET: Orders/Create/ForService/5
        [Authorize(Roles = "Client")]
        public async Task<IActionResult> Create(int serviceId)
        {
            var service = await _context.Services
                .Include(s => s.Student) // Eager load student
                .FirstOrDefaultAsync(s => s.Id == serviceId && s.IsActive);

            if (service == null) return NotFound("Service not found or is inactive.");

            var currentUser = await _userManager.GetUserAsync(User);
            if (service.StudentId == currentUser.Id)
            {
                TempData["ErrorMessage"] = "You cannot order your own service.";
                return RedirectToAction("Details", "Services", new { id = serviceId });
            }

            // CRITICAL CHECK: Ensure provider is ready for payment BEFORE showing the order creation form
            if (service.Student == null || !service.Student.StripeOnboardingComplete || string.IsNullOrEmpty(service.Student.StripeConnectedAccountId))
            {
                TempData["ErrorMessage"] = "The service provider is not currently set up to receive payments for this service. Please try again later or contact support.";
                return RedirectToAction("Details", "Services", new { id = serviceId }); // Redirect back to service details
            }

            var order = new Order // ViewModel or data for the view
            {
                ServiceId = service.Id,
                Service = service, // For display in the Create view
                PriceAtOrder = service.Price
            };
            return View(order); // This view is the form for client notes, etc.
        }

        // POST: Orders/Create (This now only creates the order and redirects to ConfirmPayment)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Client")]
        public async Task<IActionResult> Create([Bind("ServiceId,ClientNotes,ScheduledDateTime")] Order orderDataFromForm) // Renamed for clarity
        {
            var currentUser = await _userManager.GetUserAsync(User);
            var service = await _context.Services
                .Include(s => s.Student)
                .FirstOrDefaultAsync(s => s.Id == orderDataFromForm.ServiceId);

            if (service == null || !service.IsActive) ModelState.AddModelError("", "Service is no longer available.");
            if (service?.StudentId == currentUser.Id) ModelState.AddModelError("", "You cannot order your own service.");
            if (service?.Student == null || !service.Student.StripeOnboardingComplete || string.IsNullOrEmpty(service.Student.StripeConnectedAccountId))
            {
                ModelState.AddModelError("", "The service provider is not currently set up to receive payments.");
            }

            if (ModelState.IsValid)
            {
                var order = new Order // Create the actual order entity
                {
                    ServiceId = orderDataFromForm.ServiceId,
                    ClientId = currentUser.Id,
                    ClientNotes = orderDataFromForm.ClientNotes,
                    ScheduledDateTime = orderDataFromForm.ScheduledDateTime,
                    OrderDate = DateTime.UtcNow,
                    Status = OrderStatus.PendingPayment, // Initial status, client needs to confirm payment
                    PriceAtOrder = service?.Price ?? 0
                };

                _context.Add(order);
                await _context.SaveChangesAsync(); // Save order to get an Order.Id

                TempData["InfoMessage"] = "Order placed. Please review and proceed to payment.";
                return RedirectToAction(nameof(ConfirmPayment), new { orderId = order.Id });
            }

            // If ModelState is invalid, repopulate necessary data for the Create view
            if (service != null)
            {
                // Need to pass back something the Create view can use for service details
                // Consider using a ViewModel for the Create page if it gets complex
                ViewBag.ServiceTitle = service.Title;
                ViewBag.ServicePrice = service.Price;
            }
            return View("Create", orderDataFromForm); // Return to the order creation form with errors
        }


        // GET: Orders/ConfirmPayment/{orderId}
        [Authorize(Roles = "Client")]
        public async Task<IActionResult> ConfirmPayment(int orderId)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            var order = await _context.Orders
                .Include(o => o.Service)
                    .ThenInclude(s => s.Student) // Ensure student is loaded for Stripe check
                .FirstOrDefaultAsync(o => o.Id == orderId && o.ClientId == currentUser.Id);

            if (order == null)
            {
                TempData["ErrorMessage"] = "Order not found or you do not have permission to view it.";
                return RedirectToAction(nameof(MyOrders));
            }

            if (order.Status != OrderStatus.PendingPayment)
            {
                TempData["WarningMessage"] = $"This order's payment process is already {order.Status}.";
                return RedirectToAction(nameof(Details), new { id = orderId });
            }

            // Double check if provider is still ready for payment
            if (order.Service.Student == null || !order.Service.Student.StripeOnboardingComplete || string.IsNullOrEmpty(order.Service.Student.StripeConnectedAccountId))
            {
                TempData["ErrorMessage"] = "The service provider is currently unable to accept payments for this service. Please try again later or contact support.";
                order.Status = OrderStatus.PaymentFailed; // Or a new status like "ProviderPaymentIssue"
                _context.Update(order);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Details), new { id = orderId });
            }

            decimal platformFeePercentage = 0.10m; // 10%
            ViewBag.Price = order.PriceAtOrder;
            ViewBag.PlatformFee = Math.Round(order.PriceAtOrder * platformFeePercentage, 2);
            ViewBag.TotalAmount = order.PriceAtOrder; // Total client pays, fee handled by Stripe Connect transfer
            ViewBag.PlatformFeePercentageDisplay = platformFeePercentage * 100;


            return View(order);
        }

        // POST: Orders/InitiateStripePayment
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Client")]
        public async Task<IActionResult> InitiateStripePayment(int orderId)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            var order = await _context.Orders
                .Include(o => o.Service)
                    .ThenInclude(s => s.Student) // Load student for Stripe Connect details
                .FirstOrDefaultAsync(o => o.Id == orderId && o.ClientId == currentUser.Id);

            if (order == null)
            {
                TempData["ErrorMessage"] = "Order not found or access denied.";
                return RedirectToAction(nameof(MyOrders));
            }

            if (order.Status != OrderStatus.PendingPayment)
            {
                TempData["WarningMessage"] = $"Payment for this order cannot be initiated as its status is {order.Status}.";
                return RedirectToAction(nameof(Details), new { id = orderId });
            }

            // Ensure service provider is still set up for payments
            if (order.Service?.Student == null || !order.Service.Student.StripeOnboardingComplete || string.IsNullOrEmpty(order.Service.Student.StripeConnectedAccountId))
            {
                TempData["ErrorMessage"] = "The service provider is not currently set up to receive payments. Please try again later.";
                order.Status = OrderStatus.PaymentFailed; // Or a specific status
                _context.Update(order);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(ConfirmPayment), new { orderId = order.Id });
            }

            // PaymentService will set status to PendingConfirmation and save changes
            var paymentIntentResult = await _paymentService.InitiatePaymentAsync(order, order.Service.Student);

            if (paymentIntentResult.Succeeded && !string.IsNullOrEmpty(paymentIntentResult.ClientSecret))
            {
                ViewBag.ClientSecret = paymentIntentResult.ClientSecret;
                ViewBag.PublishableKey = _stripeSettings.PublishableKey;
                ViewBag.OrderId = order.Id;
                ViewBag.OrderAmount = order.PriceAtOrder;
                ViewBag.OrderDescription = $"Order #{order.Id} for: {order.Service.Title}";

                return View("ProcessPayment", order); // Redirect to the view that hosts Stripe Elements
            }
            else
            {
                TempData["ErrorMessage"] = $"Could not initiate payment: {paymentIntentResult.ErrorMessage}. Please try again or contact support.";
                // PaymentService would have set order status to PaymentFailed and saved.
                return RedirectToAction(nameof(ConfirmPayment), new { orderId = order.Id });
            }
        }


        // GET: /Orders/PaymentSuccess
        public async Task<IActionResult> PaymentSuccess(int orderId, string payment_intent, string payment_intent_client_secret, string redirect_status)
        {
            var order = await _context.Orders.Include(o => o.Service).FirstOrDefaultAsync(o => o.Id == orderId);
            if (order == null) return NotFound();

            if (redirect_status == "succeeded")
            {
                TempData["SuccessMessage"] = "Payment submitted successfully! Your order status will update shortly once confirmed by the payment provider.";
                if (order.Status < OrderStatus.Confirmed && order.Status != OrderStatus.PaymentProcessing) // Avoid regressing from Processing
                {
                    order.Status = OrderStatus.PaymentProcessing;
                    _context.Update(order);
                    await _context.SaveChangesAsync();
                }
            }
            else
            {
                TempData["WarningMessage"] = $"Payment status: {redirect_status}. We will update your order once fully processed. Please check 'My Orders' for the latest status.";
            }
            return RedirectToAction("Details", new { id = orderId });
        }

        // GET: /Orders/PaymentCancel
        public async Task<IActionResult> PaymentCancel(int orderId)
        {
            var order = await _context.Orders.Include(o => o.Service).FirstOrDefaultAsync(o => o.Id == orderId);
            if (order == null) return RedirectToAction("Index", "Home");

            TempData["ErrorMessage"] = "Payment was cancelled or not completed. Your order has not been processed for payment.";

            if (order.Status == OrderStatus.PendingConfirmation || order.Status == OrderStatus.PendingPayment)
            {
                order.Status = OrderStatus.PaymentFailed; // Mark as failed if payment was abandoned
                _context.Update(order);
                await _context.SaveChangesAsync();
            }
            // Redirect back to the confirmation page or order details
            return RedirectToAction(nameof(ConfirmPayment), new { orderId = order.Id });
        }

        // GET: Orders/MyOrders (Client's orders)
        [Authorize(Roles = "Client")]
        public async Task<IActionResult> MyOrders()
        {
            var currentUser = await _userManager.GetUserAsync(User);
            var orders = await _context.Orders
                .Include(o => o.Service).ThenInclude(s => s.Student)
                .Where(o => o.ClientId == currentUser.Id)
                .OrderByDescending(o => o.OrderDate)
                .ToListAsync();
            return View(orders);
        }

        // GET: Orders/IncomingOrders (Student's orders for their services)
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> IncomingOrders()
        {
            var currentUser = await _userManager.GetUserAsync(User);
            var orders = await _context.Orders
                .Include(o => o.Service)
                .Include(o => o.Client)
                .Where(o => o.Service.StudentId == currentUser.Id)
                .OrderByDescending(o => o.OrderDate)
                .ToListAsync();
            return View(orders);
        }

        // GET: Orders/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();
            var order = await _context.Orders
                .Include(o => o.Service).ThenInclude(s => s.Student)
                .Include(o => o.Client)
                .Include(o => o.PaymentTransactions) // Removed OrderByDescending here, will do in view
                .FirstOrDefaultAsync(m => m.Id == id);

            if (order == null) return NotFound();

            var currentUser = await _userManager.GetUserAsync(User);
            bool isClient = order.ClientId == currentUser.Id;
            bool isStudentProvider = order.Service.StudentId == currentUser.Id;

            if (!isClient && !isStudentProvider && !User.IsInRole("Admin")) return Forbid();

            return View(order);
        }

        // POST: Orders/UpdateStatus/5 (Student updates order status)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Student,Admin")]
        public async Task<IActionResult> UpdateStatus(int orderId, OrderStatus newStatus)
        {
            var order = await _context.Orders.Include(o => o.Service).FirstOrDefaultAsync(o => o.Id == orderId);
            if (order == null) return NotFound();

            var currentUser = await _userManager.GetUserAsync(User);
            bool isAdmin = User.IsInRole("Admin");
            if (order.Service.StudentId != currentUser.Id && !isAdmin) return Forbid();

            // Basic validation for status transition
            if (newStatus == OrderStatus.Completed && order.Status < OrderStatus.Confirmed)
            {
                TempData["ErrorMessage"] = "Cannot mark order as complete if payment is not confirmed.";
                return RedirectToAction(nameof(Details), new { id = orderId });
            }
            // Add more robust transition logic as needed here or in a service

            order.Status = newStatus;
            _context.Update(order);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Order status updated to {newStatus}.";

            // TODO: Notify client/student of status change via email or in-app notification

            if (isAdmin && order.Service.StudentId != currentUser.Id)
            {
                return RedirectToAction(nameof(Details), new { id = orderId });
            }
            return RedirectToAction(nameof(IncomingOrders));
        }
    }
}