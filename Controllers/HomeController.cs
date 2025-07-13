using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StudentServicesMarketplace.Data;
using StudentServicesMarketplace.Models;
using System.Diagnostics;
using StudentServicesMarketplace.Models.ViewModels;

namespace StudentServicesMarketplace.Controllers
{
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<HomeController> _logger;


        public HomeController(ILogger<HomeController> logger, ApplicationDbContext context)
        {
            _logger = logger;
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var featuredServices = await _context.Services
                .Include(s => s.Student)
                .Where(s => s.IsActive)
                .OrderByDescending(s => s.DatePosted)
                .Take(6)
                .ToListAsync();

            // Fetch Ads for display
            ViewBag.HeaderAds = await _context.Advertisements
                .Where(a => a.IsActive && a.Placement == AdPlacement.Header)
                .OrderBy(a => a.DisplayOrder).ToListAsync();
            ViewBag.SidebarAds = await _context.Advertisements
                .Where(a => a.IsActive && a.Placement == AdPlacement.Sidebar)
                .OrderBy(a => a.DisplayOrder).ToListAsync();


            return View(featuredServices);
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}