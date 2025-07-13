using Microsoft.Extensions.Options;
using Stripe;
using StudentServicesMarketplace.Configuration;
using StudentServicesMarketplace.Interfaces;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
// Ensure 'Models' namespace is not ambiguous if PaymentIntentResult is also in Models
// using PaymentIntentResult = StudentServicesMarketplace.Interfaces.PaymentIntentResult;


namespace StudentServicesMarketplace.Services
{
    public class StripePaymentProcessor : IPaymentProcessor
    {
        private readonly StripeSettings _stripeSettings;

        public StripePaymentProcessor(IOptions<StripeSettings> stripeSettings)
        {
            _stripeSettings = stripeSettings.Value;
            StripeConfiguration.ApiKey = _stripeSettings.SecretKey;
        }

        public async Task<PaymentIntentResult> CreatePaymentIntentAsync(
            decimal totalAmount,
            string currency,
            int orderId,
            string description,
            string destinationAccountId,
            decimal applicationFeeAmount)
        {
            try
            {
                if (string.IsNullOrEmpty(destinationAccountId))
                {
                    return new PaymentIntentResult { Succeeded = false, ErrorMessage = "Service provider payment account not set up or invalid." };
                }

                var options = new PaymentIntentCreateOptions
                {
                    Amount = (long)(totalAmount * 100), // Total amount client pays
                    Currency = currency.ToLower(),
                    Description = description,
                    ApplicationFeeAmount = (long)(applicationFeeAmount * 100), // Your platform fee
                    TransferData = new PaymentIntentTransferDataOptions
                    {
                        Destination = destinationAccountId,
                    },
                    Metadata = new Dictionary<string, string>
                    {
                        { "order_id", orderId.ToString() },
                        { "platform_fee_amount_decimal", applicationFeeAmount.ToString() } // Store for easier reference
                    },
                    AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions { Enabled = true },
                };
                var service = new PaymentIntentService();
                PaymentIntent paymentIntent = await service.CreateAsync(options);

                return new PaymentIntentResult
                {
                    Succeeded = true,
                    ClientSecret = paymentIntent.ClientSecret,
                    PaymentIntentId = paymentIntent.Id
                };
            }
            catch (StripeException e)
            {
                // Log e.StripeError.Message
                return new PaymentIntentResult { Succeeded = false, ErrorMessage = e.StripeError?.Message ?? e.Message };
            }
            catch (Exception ex)
            {
                // Log general exception
                return new PaymentIntentResult { Succeeded = false, ErrorMessage = $"An unexpected error occurred: {ex.Message}" };
            }
        }
    }
}