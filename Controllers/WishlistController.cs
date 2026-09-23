using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Project.Models;

namespace Project.Controllers
{
    public class WishlistController : Controller
    {
        private readonly MyContext _context;

        public WishlistController(MyContext context)
        {
            _context = context;
        }

        public IActionResult Index()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Login", "Auth");

            var wishlist = _context.WishlistItems
                .Where(w => w.UserId == userId)
                .Include(w => w.Product)
                .ToList();

            return View(wishlist);
        }

        public IActionResult AddToWishlist(int id)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Login", "Auth");

            var exists = _context.WishlistItems.Any(w => w.UserId == userId && w.ProductId == id);
            if (!exists)
            {
                var item = new WishlistItem
                {
                    UserId = userId.Value,
                    ProductId = id
                };
                _context.WishlistItems.Add(item);
                _context.SaveChanges();
                TempData["Success"] = "Added to wishlist!";
            }
            else
            {
                TempData["Info"] = "Item is already in your wishlist!";
            }

            var referer = Request.Headers["Referer"].ToString();
            if (!string.IsNullOrEmpty(referer) && Uri.IsWellFormedUriString(referer, UriKind.RelativeOrAbsolute))
            {
                return Redirect(referer);
            }

            return RedirectToAction("Index", "Product");
        }

        public IActionResult RemoveFromWishlist(int id)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Login", "Auth");

            var item = _context.WishlistItems.FirstOrDefault(w => w.UserId == userId && w.ProductId == id);
            if (item != null)
            {
                _context.WishlistItems.Remove(item);
                _context.SaveChanges();
            }

            return RedirectToAction("Index");
        }
        [HttpPost]
        public IActionResult Toggle(int productId)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
            {
                return Json(new { success = false, message = "Please login first." });
            }

            var item = _context.WishlistItems.FirstOrDefault(w => w.UserId == userId && w.ProductId == productId);
            bool added = false;

            if (item == null)
            {
                var newItem = new WishlistItem
                {
                    UserId = userId.Value,
                    ProductId = productId
                };
                _context.WishlistItems.Add(newItem);
                added = true;
            }
            else
            {
                _context.WishlistItems.Remove(item);
                added = false;
            }

            _context.SaveChanges();

            var count = _context.WishlistItems.Count(w => w.UserId == userId);

            return Json(new { success = true, added = added, count = count });
        }
    }
}
