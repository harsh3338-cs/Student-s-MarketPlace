using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StudentServicesMarketplace.Models
{
    public enum PaymentStatus
    {
        Pending, Succeeded, Failed, Refunded, Processing
    }

    public class PaymentTransaction
    {
        public int Id { get; set; }
        [Required] public int OrderId { get; set; }
        [ForeignKey("OrderId")] public virtual Order Order { get; set; }

        [Required, StringLength(100)] public string StripePaymentIntentId { get; set; }
        [Required, Column(TypeName = "decimal(18, 2)")] public decimal Amount { get; set; } // Total amount client paid
        [Required, StringLength(3)] public string Currency { get; set; } = "USD";
        public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
        [MaxLength(5000)] public string? GatewayResponse { get; set; } // Details from Stripe
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}