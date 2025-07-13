using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe; // For Stripe types
using StudentServicesMarketplace.Configuration;
using StudentServicesMarketplace.Data;
using StudentServicesMarketplace.Interfaces;
using StudentServicesMarketplace.Models;
using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace StudentServicesMarketplace.Services
{
    public class PaymentService : IPaymentService
    {
        private readonly IPaymentProcessor _paymentProcessor;
        private readonly ApplicationDbContext _context;
        private readonly ILogger<PaymentService> _logger;
        private readonly StripeSettings _stripeSettings; // Keep this if WebhookSecret is used here

        public PaymentService(
            IPaymentProcessor paymentProcessor,
            ApplicationDbContext context,
            ILogger<PaymentService> logger,
            IOptions<StripeSettings> stripeSettings) // Inject IOptions<StripeSettings>
        {
            _paymentProcessor = paymentProcessor;
            _context = context;
            _logger = logger;
            _stripeSettings = stripeSettings.Value; // Get the value
        }

        public async Task<PaymentIntentResult> InitiatePaymentAsync(Order order, ApplicationUser studentProvider)
        {
            if (order == null) throw new ArgumentNullException(nameof(order));
            if (studentProvider == null) throw new ArgumentNullException(nameof(studentProvider));

            if (string.IsNullOrEmpty(studentProvider.StripeConnectedAccountId) || !studentProvider.StripeOnboardingComplete)
            {
                return new PaymentIntentResult { Succeeded = false, ErrorMessage = "The service provider is not yet fully set up to receive payments." };
            }

            var serviceDetails = await _context.Services.FindAsync(order.ServiceId);
            if (serviceDetails == null)
            {
                _logger.LogError("Service with ID {ServiceId} not found for order {OrderId}.", order.ServiceId, order.Id);
                throw new InvalidOperationException("Service not found for the order.");
            }
            var description = $"Order #{order.Id} for service: {serviceDetails.Title}";

            decimal totalAmount = order.PriceAtOrder;
            decimal platformFee = Math.Round(totalAmount * 0.10m, 2); // 10% platform fee

            var result = await _paymentProcessor.CreatePaymentIntentAsync(
                totalAmount,
                "usd", // Or your default currency
                order.Id,
                description,
                studentProvider.StripeConnectedAccountId,
                platformFee
            );

            if (result.Succeeded && !string.IsNullOrEmpty(result.PaymentIntentId))
            {
                // Record transaction first
                await RecordPaymentTransactionAsync(order, result.PaymentIntentId, totalAmount, "usd", Models.PaymentStatus.Pending, "PaymentIntent created");

                // Update order status
                order.Status = OrderStatus.PendingConfirmation; // Client is about to see Stripe Elements
                _context.Update(order); // Mark order as modified

                await _context.SaveChangesAsync(); // Save both new transaction and order status update
                _logger.LogInformation("PaymentIntent {PaymentIntentId} created for Order {OrderId}. Status set to PendingConfirmation.", result.PaymentIntentId, order.Id);
            }
            else
            {
                _logger.LogError("Payment intent creation failed for Order ID {OrderId}: {ErrorMessage}", order.Id, result.ErrorMessage);
                order.Status = OrderStatus.PaymentFailed; // Mark order as payment failed
                _context.Update(order);
                await _context.SaveChangesAsync();
            }
            return result;
        }

        public async Task RecordPaymentTransactionAsync(Order order, string paymentIntentId, decimal amount, string currency, Models.PaymentStatus status, string gatewayResponse)
        {
            var transaction = new PaymentTransaction
            {
                OrderId = order.Id, // Ensure OrderId is set
                // Order = order, // EF Core can link via OrderId
                StripePaymentIntentId = paymentIntentId,
                Amount = amount,
                Currency = currency,
                Status = status,
                GatewayResponse = gatewayResponse.Length > 4990 ? gatewayResponse.Substring(0, 4990) : gatewayResponse, // Truncate if too long for DB
                Timestamp = DateTime.UtcNow
            };
            _context.PaymentTransactions.Add(transaction);
            // SaveChanges will be called by the method that calls this, or by InitiatePaymentAsync
        }

        public async Task<bool> HandlePaymentWebhookAsync(string jsonPayload, string stripeSignatureHeader)
        {
            Event stripeEvent;
            try
            {
                if (string.IsNullOrEmpty(_stripeSettings.WebhookSecret))
                {
                    _logger.LogError("Stripe Webhook secret is not configured.");
                    return false;
                }
                stripeEvent = EventUtility.ConstructEvent(jsonPayload, stripeSignatureHeader, _stripeSettings.WebhookSecret);
            }
            catch (StripeException e)
            {
                _logger.LogError(e, "Error constructing Stripe event from webhook. Signature or payload issue.");
                return false;
            }

            _logger.LogInformation("Received Stripe Webhook: Type='{StripeEventType}', Id='{StripeEventId}'", stripeEvent.Type, stripeEvent.Id);

            PaymentIntent paymentIntent = null;

            if (stripeEvent.Data.Object is PaymentIntent pi)
            {
                paymentIntent = pi;
            }
            else if (stripeEvent.Data.Object is Charge charge && !string.IsNullOrEmpty(charge.PaymentIntentId))
            {
                var piService = new PaymentIntentService();
                try
                {
                    paymentIntent = await piService.GetAsync(charge.PaymentIntentId);
                }
                catch (StripeException ex)
                {
                    _logger.LogError(ex, "Failed to retrieve PaymentIntent {PaymentIntentId} from Charge event.", charge.PaymentIntentId);
                }
            }
            // Add other ways to get PI if needed, e.g., from a SetupIntent or Subscription related event

            if (paymentIntent == null &&
                (stripeEvent.Type == EventTypes.PaymentIntentSucceeded ||
                 stripeEvent.Type == EventTypes.PaymentIntentPaymentFailed ||
                 stripeEvent.Type == EventTypes.PaymentIntentProcessing))
            {
                _logger.LogError("PaymentIntent object was null in critical webhook type {StripeEventType}. Event ID: {StripeEventId}", stripeEvent.Type, stripeEvent.Id);
                return false;
            }

            switch (stripeEvent.Type)
            {
                case EventTypes.PaymentIntentSucceeded:
                    if (paymentIntent != null)
                    {
                        await UpdateOrderAndTransactionStatus(paymentIntent.Id, Models.PaymentStatus.Succeeded, OrderStatus.Confirmed, "Payment succeeded via webhook.");
                        _logger.LogInformation("PaymentIntent {PaymentIntentId} succeeded.", paymentIntent.Id);
                        // TODO: Fulfill the order (e.g., notify student, grant access if digital)
                    }
                    break;

                case EventTypes.PaymentIntentPaymentFailed:
                    if (paymentIntent != null)
                    {
                        var failureMessage = $"Payment failed: {paymentIntent.LastPaymentError?.Message ?? "No specific error message."}";
                        await UpdateOrderAndTransactionStatus(paymentIntent.Id, Models.PaymentStatus.Failed, OrderStatus.PaymentFailed, failureMessage);
                        _logger.LogError("PaymentIntent {PaymentIntentId} failed: {FailureMessage}", paymentIntent.Id, failureMessage);
                    }
                    break;

                case EventTypes.PaymentIntentProcessing:
                    if (paymentIntent != null)
                    {
                        await UpdateOrderAndTransactionStatus(paymentIntent.Id, Models.PaymentStatus.Processing, OrderStatus.PaymentProcessing, "Payment processing.");
                        _logger.LogInformation("PaymentIntent {PaymentIntentId} is processing.", paymentIntent.Id);
                    }
                    break;

                case EventTypes.AccountUpdated:
                    if (stripeEvent.Data.Object is Account account)
                    {
                        var user = await _context.Users.FirstOrDefaultAsync(u => u.StripeConnectedAccountId == account.Id);
                        if (user != null)
                        {
                            user.StripeOnboardingComplete = account.ChargesEnabled && account.PayoutsEnabled && account.DetailsSubmitted;
                            user.DetailsSubmitted = account.DetailsSubmitted;
                            _context.Update(user); // Explicitly mark user as updated
                            await _context.SaveChangesAsync();
                            _logger.LogInformation("Stripe Connected Account {AccountId} for user {UserEmail} updated. OnboardingComplete: {IsOnboardingComplete}, DetailsSubmitted: {AreDetailsSubmitted}", account.Id, user.Email, user.StripeOnboardingComplete, user.DetailsSubmitted);
                        }
                    }
                    break;

                default:
                    _logger.LogInformation("Unhandled Stripe event type: {StripeEventType}", stripeEvent.Type);
                    break;
            }
            return true;
        }

        private async Task UpdateOrderAndTransactionStatus(string paymentIntentId, Models.PaymentStatus transactionStatus, OrderStatus newOrderStatus, string gatewayResponseMessage)
        {
            var transaction = await _context.PaymentTransactions
                                    .Include(t => t.Order) // Ensure Order is loaded
                                    .FirstOrDefaultAsync(t => t.StripePaymentIntentId == paymentIntentId);

            if (transaction != null)
            {
                bool transactionChanged = transaction.Status != transactionStatus;
                transaction.Status = transactionStatus;
                transaction.GatewayResponse = gatewayResponseMessage.Length > 4990 ? gatewayResponseMessage.Substring(0, 4990) : gatewayResponseMessage;
                transaction.Timestamp = DateTime.UtcNow; // Update timestamp on status change

                _context.Update(transaction); // Mark transaction as modified

                if (transaction.Order != null)
                {
                    bool orderStatusChanged = false;
                    // More robust status update logic
                    if (newOrderStatus > transaction.Order.Status ||
                        newOrderStatus == OrderStatus.PaymentFailed ||
                        newOrderStatus == OrderStatus.CancelledByClient ||
                        newOrderStatus == OrderStatus.CancelledByStudent)
                    {
                        // Avoid regressing from a terminal state like Completed unless it's a specific cancellation/refund
                        if (!(transaction.Order.Status == OrderStatus.Completed && newOrderStatus < OrderStatus.Completed && newOrderStatus != OrderStatus.CancelledByClient && newOrderStatus != OrderStatus.CancelledByStudent))
                        {
                            if (transaction.Order.Status != newOrderStatus)
                            {
                                transaction.Order.Status = newOrderStatus;
                                orderStatusChanged = true;
                                _context.Update(transaction.Order); // Mark order as modified
                            }
                        }
                    }
                    if (orderStatusChanged)
                    {
                        _logger.LogInformation("Order {OrderId} status updated to {NewOrderStatus} due to payment {PaymentIntentId} ({TransactionStatus}).",
                                           transaction.OrderId, transaction.Order.Status, paymentIntentId, transactionStatus);
                    }
                }
                else { _logger.LogWarning("Order not found for transaction with PaymentIntentId {PaymentIntentId} during status update.", paymentIntentId); }

                await _context.SaveChangesAsync(); // Save all accumulated changes
            }
            else
            {
                _logger.LogWarning("PaymentTransaction not found for StripePaymentIntentId: {PaymentIntentId} during status update.", paymentIntentId);
            }
        }
    }
}