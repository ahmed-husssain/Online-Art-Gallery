using MediatR;
using Project.Models;
using Project.Services;

namespace Project.Features.Checkout
{
    public class ProcessCheckoutCommandHandler : IRequestHandler<ProcessCheckoutCommand, CheckoutResult>
    {
        private readonly MyContext _context;
        private readonly IEmailService _emailService;

        public ProcessCheckoutCommandHandler(MyContext context, IEmailService emailService)
        {
            _context = context;
            _emailService = emailService;
        }

        public async Task<CheckoutResult> Handle(ProcessCheckoutCommand request, CancellationToken cancellationToken)
        {
            if (request.CartItems == null || request.CartItems.Count == 0)
            {
                return new CheckoutResult(false, Message: "Your cart is empty.");
            }

            // 1. Create the Order
            var order = new Order
            {
                UserId = request.UserId,
                OrderDate = DateTime.Now,
                TotalAmount = request.CartItems.Sum(x => x.Total),
                CustomerName = request.FullName,
                ShippingAddress = $"{request.Address}, {request.City}, {request.ZipCode}",
                Status = "Pending",
                IsPaid = true
            };

            // 2. Calculate platform fees and artist earnings per item
            foreach (var item in request.CartItems)
            {
                var platformFee = (item.Price * 0.10m) * item.Quantity;
                var artistEarnings = (item.Price - (item.Price * 0.10m)) * item.Quantity;

                order.OrderItems.Add(new OrderItem
                {
                    ProductId = item.ProductId,
                    ProductName = item.Name,
                    Price = item.Price,
                    Quantity = item.Quantity,
                    ImageUrl = item.ImageUrl,
                    PlatformFee = platformFee,
                    ArtistEarnings = artistEarnings
                });
            }

            // 3. Save to database asynchronously
            _context.Orders.Add(order);
            await _context.SaveChangesAsync(cancellationToken);

            // 4. Send Confirmation Email if email exists
            if (!string.IsNullOrEmpty(request.UserEmail))
            {
                var confirmLink = request.ConfirmLinkPattern.Replace("__ID__", order.Id.ToString());
                string subject = "Confirm your Art Gallery Order";
                string body = $@"
                    <div style='font-family: sans-serif; max-width: 600px; margin: 0 auto; padding: 40px; border: 1px solid #eee; border-radius: 20px; text-align: center; background-color: #05060a; color: white;'>
                        <h1 style='color: #3b82f6;'>ART GALLERY</h1>
                        <h2 style='margin-bottom: 20px;'>Thank you for your order, {request.FullName}!</h2>
                        <p style='color: #94a3b8; font-size: 16px; margin-bottom: 30px;'>Your selection of masterpieces is ready for shipment. Please confirm your order to finalize the delivery process.</p>
                        <a href='{confirmLink}' style='display: inline-block; background: #3b82f6; color: white; padding: 16px 36px; border-radius: 12px; text-decoration: none; font-weight: bold; font-size: 18px; box-shadow: 0 10px 20px rgba(59, 130, 246, 0.3);'>Confirm Order</a>
                        <p style='margin-top: 40px; font-size: 12px; color: #475569;'>© {DateTime.Now.Year} Art Gallery. Modern Art for Modern Collectors.</p>
                    </div>";

                await _emailService.SendEmailAsync(request.UserEmail, subject, body);
            }

            return new CheckoutResult(true, OrderId: order.Id);
        }
    }
}
