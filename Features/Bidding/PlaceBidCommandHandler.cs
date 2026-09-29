using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Project.Models;

namespace Project.Features.Bidding
{
    public class PlaceBidCommandHandler : IRequestHandler<PlaceBidCommand, BidResult>
    {
        private readonly MyContext _context;
        private readonly IMemoryCache _memoryCache;

        public PlaceBidCommandHandler(MyContext context, IMemoryCache memoryCache)
        {
            _context = context;
            _memoryCache = memoryCache;
        }

        public async Task<BidResult> Handle(PlaceBidCommand request, CancellationToken cancellationToken)
        {
            // 1. Idempotency Check
            if (!string.IsNullOrEmpty(request.IdempotencyKey))
            {
                if (_memoryCache.TryGetValue(request.IdempotencyKey, out BidResult? cachedResult))
                {
                    return cachedResult!;
                }
            }

            // 2. Validate Payment Card
            var user = await _context.users.FindAsync(new object[] { request.UserId }, cancellationToken);
            if (string.IsNullOrEmpty(user?.CardNumber))
            {
                return new BidResult(false, "NO_CARD", RedirectUrl: "/Home/Settings?tab=payment");
            }

            // 3. Validate Auction Status
            var product = await _context.products.FindAsync(new object[] { request.ProductId }, cancellationToken);
            if (product == null || !product.IsAuction) 
                return new BidResult(false, "Invalid auction.");

            if (product.AuctionEndTime < DateTime.Now) 
                return new BidResult(false, "Auction has ended.");

            if (product.HighestBidderId == request.UserId)
                return new BidResult(false, "You are already the highest bidder.");

            var minBid = (product.CurrentBid ?? product.Price) + 1;
            if (request.Amount < minBid) 
                return new BidResult(false, $"Bid must be at least ${minBid}");

            // 4. Create and Save Bid with Concurrency Handling
            var bid = new Bid
            {
                UserId = request.UserId,
                ProductId = request.ProductId,
                Amount = request.Amount,
                BidTime = DateTime.Now
            };

            _context.Bids.Add(bid);

            try
            {
                product.CurrentBid = request.Amount;
                product.BidCount++;
                product.HighestBidderId = request.UserId;
                _context.products.Update(product);

                await _context.SaveChangesAsync(cancellationToken);

                var response = new BidResult(true, NewBid: request.Amount, NewCount: product.BidCount);

                // Cache for idempotency (2 minutes)
                if (!string.IsNullOrEmpty(request.IdempotencyKey))
                {
                    _memoryCache.Set(request.IdempotencyKey, response, TimeSpan.FromMinutes(2));
                }

                return response;
            }
            catch (DbUpdateConcurrencyException)
            {
                return new BidResult(false, "Another collector placed a higher bid right before you! Please refresh to see the new price.");
            }
        }
    }
}
