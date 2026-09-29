using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Project.Models;
using MediatR;
using Project.Features.Bidding;

namespace Project.Controllers
{
    public class ProductController : Controller
    {
        private readonly MyContext _context;
        private readonly IMemoryCache _memoryCache;
        private readonly IMediator _mediator;

        public ProductController(MyContext context, IMemoryCache memoryCache, IMediator mediator)
        {
            _context = context;
            _memoryCache = memoryCache;
            _mediator = mediator;
        }

        public async Task<IActionResult> Index(string searchString, string sortOrder, int? page)
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
                products = products.Where(s => EF.Functions.Like(s.Name,  searchString) || EF.Functions.Like(s.Description, "%" + searchString + "%"));
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
                ViewBag.WishlistProductIds = await _context.WishlistItems
                    .Where(w => w.UserId == userId)
                    .Select(w => w.ProductId)
                    .ToListAsync();
            }
            else
            {
                ViewBag.WishlistProductIds = new List<int>();
            }

            int pageSize = 12;
            int pageNumber = page ?? 1;

            var totalItems = await products.CountAsync();
            ViewBag.CurrentPage = pageNumber;
            ViewBag.TotalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
            ViewBag.TotalItems = totalItems; 

            var pagedProducts = await products
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return View(pagedProducts);
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
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
            return Json(new 
            { 
                success = false, message = "Please login to place a bid."
            });
            var idempotencyKey = Request.Headers["X-Idempotency-Key"].ToString();
            var result = await _mediator.Send(new PlaceBidCommand(productId, amount, userId.Value, idempotencyKey));
            return Json(result); 
        }
    }
}
