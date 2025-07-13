using StudentServicesMarketplace.Models; // For PaymentIntentResult (defined below)
using System.Threading.Tasks;

namespace StudentServicesMarketplace.Interfaces
{
    public class PaymentIntentResult
    {
        public bool Succeeded { get; set; }
        public string? ClientSecret { get; set; } // For Stripe.js
        public string? PaymentIntentId { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public interface IPaymentProcessor
    {
        Task<PaymentIntentResult> CreatePaymentIntentAsync(
            decimal totalAmount,
            string currency,
            int orderId,
            string description,
            string destinationAccountId, // Student's Stripe Connected Account ID
            decimal applicationFeeAmount  // Your platform fee
        );
    }
}