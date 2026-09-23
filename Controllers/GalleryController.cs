using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Project.Models;

namespace Project.Controllers
{
    public class GalleryController : Controller
    {
        private readonly MyContext _context;

        public GalleryController(MyContext context)
        {
            _context = context;
        }


        public IActionResult MyCollection()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Login", "Auth");


            var wishlist = _context.WishlistItems
                .Where(w => w.UserId == userId)
                .Include(w => w.Product)
                .Select(w => w.Product)
                .ToList();


            var purchased = _context.OrderItems
                .Include(oi => oi.Order)
                .Include(oi => oi.Product)
                .Where(oi => oi.Order.UserId == userId && (oi.Order.Status == "Confirmed" || oi.Order.Status == "Pending"))
                .Select(oi => oi.Product)
                .Where(p => p != null)
                .Distinct()
                .ToList();

            ViewBag.Wishlist = wishlist;
            ViewBag.Purchased = purchased;

            return View();
        }

        [HttpPost]
        public IActionResult ToggleWishlist(int productId)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return Json(new { success = false, message = "Please login first." });

            var existing = _context.WishlistItems.FirstOrDefault(w => w.UserId == userId && w.ProductId == productId);
            if (existing != null)
            {
                _context.WishlistItems.Remove(existing);
                _context.SaveChanges();
                return Json(new { success = true, added = false });
            }
            else
            {
                _context.WishlistItems.Add(new WishlistItem { UserId = userId.Value, ProductId = productId });
                _context.SaveChanges();
                return Json(new { success = true, added = true });
            }
        }
    }
}
