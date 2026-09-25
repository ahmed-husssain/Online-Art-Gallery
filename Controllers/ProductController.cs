using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Project.Models;

namespace Project.Controllers
{
    public class ProductController : Controller
    {
        private readonly MyContext _context;
        private readonly IMemoryCache _memoryCache;

        public ProductController(MyContext context, IMemoryCache memoryCache)
        {
            _context = context;
            _memoryCache = memoryCache;
        }

        public IActionResult Index(string searchString, string sortOrder)
        {
            ViewData["CurrentFilter"] = searchString;
            ViewData["PriceSortParm"] = sortOrder == "price_asc" ? "price_desc" : "price_asc";
            ViewData["CurrentSort"] = sortOrder;

            var isAdmin = HttpContext.Session.GetString("Role") == "Admin";
    var products = _context.products.AsNoTracking().AsQueryable();

    if (!isAdmin)
    {
        products = products.Where(p => p.IsApproved);
    }

            if (!string.IsNullOrEmpty(searchString))
            {
                products = products.Where(s => s.Name.Contains(searchString) || s.Description.Contains(searchString));
            }

            switch (sortOrder)
            {
                case "price_desc":
                    products = products.OrderByDescending(s => s.Price);
                    break;
                case "price_asc":
                    products = products.OrderBy(s => s.Price);
                    break;
                case "name_desc":
                    products = products.OrderByDescending(s => s.Name);
                    break;
                default:
                    products = products.OrderBy(s => s.Name);
                    break;
            }

            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId != null)
            {
                ViewBag.WishlistProductIds = _context.WishlistItems
                    .Where(w => w.UserId == userId)
                    .Select(w => w.ProductId)
                    .ToList();
            }
            else
            {
                ViewBag.WishlistProductIds = new List<int>();
            }

            return View(products.ToList());
        }

        public IActionResult Details(int id)
        {
            var product = _context.products
                .Include(p => p.Reviews)
                .ThenInclude(r => r.User)
                .FirstOrDefault(p => p.Id == id);

            if (product == null)
            {
                return NotFound();
            }

            if(product.IsAuction && product.HighestBidderId != null) {
                var bidder = _context.users.Find(product.HighestBidderId);
                ViewBag.HighestBidder = bidder; 
            }

            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId != null)
            {
                ViewBag.IsInWishlist = _context.WishlistItems.Any(w => w.UserId == userId && w.ProductId == id);
            }
            else
            {
                ViewBag.IsInWishlist = false;
            }

            return View(product);
        }

        [HttpPost]
        public async Task<IActionResult> PostReview(int productId, int rating, string comment)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Login", "Auth");

            if (rating < 1 || rating > 5) return RedirectToAction("Details", new { id = productId });

            var existingReview = await _context.Reviews.FirstOrDefaultAsync(r => r.ProductId == productId && r.UserId == userId.Value);
            if (existingReview != null)
            {
                existingReview.Rating = rating;
                existingReview.Comment = comment;
                existingReview.CreatedAt = DateTime.Now;
                _context.Reviews.Update(existingReview);
                await _context.SaveChangesAsync();
                TempData["Success"] = "Your review has been updated!";
                return RedirectToAction("Details", new { id = productId });
            }

            var review = new Review
            {
                ProductId = productId,
                UserId = userId.Value,
                Rating = rating,
                Comment = comment,
                CreatedAt = DateTime.Now
            };

            _context.Reviews.Add(review);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Thank you for your review!";
            return RedirectToAction("Details", new { id = productId });
        }

        [HttpPost]
        public async Task<IActionResult> PlaceBid(int productId, decimal amount)
        {
            var idempotencyKey = Request.Headers["X-Idempotency-Key"].ToString();

            if(!string.IsNullOrEmpty(idempotencyKey)){
                if(_memoryCache.TryGetValue(idempotencyKey, out object cachedResponse)){
                    return Json(cachedResponse);
                }
            }
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return Json(new { success = false, message = "Please login to place a bid." });

            var user = await _context.users.FindAsync(userId);
            if (string.IsNullOrEmpty(user?.CardNumber))
            {

                return Json(new { success = false, message = "NO_CARD", redirectUrl = "/Home/Settings?tab=payment" });
            }

            var product = await _context.products.FindAsync(productId);
            if (product == null || !product.IsAuction) return Json(new { success = false, message = "Invalid auction." });

            if (product.AuctionEndTime < DateTime.Now) return Json(new { success = false, message = "Auction has ended." });

            if (product.HighestBidderId == userId)
            {
                return Json(new { success = false, message = "You are already the highest bidder." });
            }

            var minBid = (product.CurrentBid ?? product.Price) + 1;
            if (amount < minBid) return Json(new { success = false, message = $"Bid must be at least ${minBid}" });


            var bid = new Bid
            {
                UserId = user.UserId,
                ProductId = productId,
                Amount = amount,
                BidTime = DateTime.Now
            };

            _context.Bids.Add(bid);

        try{
            product.CurrentBid = amount;
            product.BidCount++;
            product.HighestBidderId = user.UserId;
            _context.products.Update(product);

            await _context.SaveChangesAsync();

            var response = new {success = true, newBid = amount, newCount = product.BidCount };
            if(!string.IsNullOrEmpty(idempotencyKey)){
                _memoryCache.Set(idempotencyKey, response, TimeSpan.FromMinutes(2));
            }
            return Json(response);
            }
            catch(DbUpdateConcurrencyException){
                return Json(new
                {
                    success = false,
                    message = "Another collector placed a higher bid right before you! Please refresh to see the new price."
                });
            }
            
        }
    }
}
