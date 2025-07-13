using Microsoft.AspNetCore.Identity;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace StudentServicesMarketplace.Models
{
    public class ApplicationUser : IdentityUser
    {
        [Required]
        [StringLength(50)]
        public string FirstName { get; set; }

        [Required]
        [StringLength(50)]
        public string LastName { get; set; }

        // For Stripe Connect (Service Providers / Students)
        [StringLength(100)]
        public string? StripeConnectedAccountId { get; set; } // Stores the ID like "acct_xxxxxxxxxxxxxx"
        public bool StripeOnboardingComplete { get; set; } = false;
        public bool DetailsSubmitted { get; set; } = false; // Track if they've submitted details to Stripe

        [StringLength(255)] // Max path length
        public string? ProfilePictureUrl { get; set; }

        // Navigation properties
        public virtual ICollection<Service> ServicesProvided { get; set; } = new List<Service>();
        public virtual ICollection<Order> OrdersPlaced { get; set; } = new List<Order>();
        public virtual ICollection<SupportTicket> SupportTickets { get; set; } = new List<SupportTicket>();
    }
}