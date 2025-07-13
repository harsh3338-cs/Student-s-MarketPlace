using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Collections.Generic;

namespace StudentServicesMarketplace.Models
{
    public enum OrderStatus
    {
        PendingPayment, // Initial state before payment intent
        PendingConfirmation, // Payment intent created, awaiting client action
        PaymentProcessing, // Payment submitted, Stripe processing
        Confirmed, // Payment Succeeded, service confirmed
        InProgress, // Student working on it
        Completed,
        CancelledByClient,
        CancelledByStudent,
        PaymentFailed
    }

    public class Order
    {
        public int Id { get; set; }

        [Required]
        public int ServiceId { get; set; }
        [ForeignKey("ServiceId")]
        public virtual Service Service { get; set; }

        [Required]
        public string ClientId { get; set; } // FK to ApplicationUser (Client)
        [ForeignKey("ClientId")]
        public virtual ApplicationUser Client { get; set; }

        public DateTime OrderDate { get; set; } = DateTime.UtcNow;
        public OrderStatus Status { get; set; } = OrderStatus.PendingPayment;

        [Column(TypeName = "decimal(18, 2)")]
        public decimal PriceAtOrder { get; set; }

        [StringLength(500)]
        public string? ClientNotes { get; set; }
        public DateTime? ScheduledDateTime { get; set; }

        public virtual ICollection<PaymentTransaction> PaymentTransactions { get; set; } = new List<PaymentTransaction>();
    }
}