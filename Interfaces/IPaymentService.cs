using StudentServicesMarketplace.Models;
using System.Threading.Tasks;
// No direct Stripe types here to keep interface clean from specific gateway
// Stripe.Event will be handled within the implementation.

namespace StudentServicesMarketplace.Interfaces
{
    public interface IPaymentService
    {
        Task<PaymentIntentResult> InitiatePaymentAsync(Order order, ApplicationUser studentProvider);
        Task<bool> HandlePaymentWebhookAsync(string jsonPayload, string stripeSignatureHeader);
        Task RecordPaymentTransactionAsync(Order order, string paymentIntentId, decimal amount, string currency, PaymentStatus status, string gatewayResponse);
    }
}