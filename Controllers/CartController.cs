using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Project.Models;
using Project.Services;
using System.Text.Json;
using Project.ViewModels;
using MediatR;
using Project.Features.Checkout;

namespace Project.Controllers
{
    public class CartController : Controller
    {
        private readonly MyContext _context;
        private readonly IEmailService _emailService;
        private const string CartSessionKey = "Cart";
        private readonly IMediator _mediator;

        public CartController(MyContext context, IEmailService emailService, IMediator mediator)
        {
            _context = context;
            _emailService = emailService;
            _mediator = mediator;
        }

        public IActionResult Index()
        {
            var cart = GetCart();
            
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId != null)
            {
                var activeBids = _context.Bids
                    .Where(b => b.UserId == userId)
                    .Include(b => b.Product)
                    .OrderByDescending(b => b.BidTime)
                    .ToList();
                ViewBag.ActiveBids = activeBids;
            }
            
            return View(cart);
        }

        public IActionResult AddToCart(int id)
        {
            var product = _context.products.Find(id);
            if (product == null) return NotFound();

            var cart = GetCart();
            var cartItem = cart.FirstOrDefault(c => c.ProductId == id);

            if (cartItem != null)
            {
                cartItem.Quantity++;
            }
            else
            {
                cart.Add(new CartItem
                {
                    ProductId = product.Id,
                    Name = product.Name,
                    Price = product.Price,
                    Quantity = 1,
                    ImageUrl = product.ImageUrl
                });
            }

            SaveCart(cart);

            // Also remove from wishlist if it exists
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId != null)
            {
                var wishItem = _context.WishlistItems.FirstOrDefault(w => w.UserId == userId && w.ProductId == id);
                if (wishItem != null)
                {
                    _context.WishlistItems.Remove(wishItem);
                    _context.SaveChanges();
                }
            }

            TempData["Success"] = "Item added to cart!";
            return RedirectToAction("Index", "Product");
        }

        [HttpPost]
        public IActionResult AddToCartJson(int id)
        {
            var product = _context.products.Find(id);
            if (product == null) return Json(new { success = false, message = "Product not found" });

            var cart = GetCart();
            var cartItem = cart.FirstOrDefault(c => c.ProductId == id);

            if (cartItem != null)
            {
                cartItem.Quantity++;
            }
            else
            {
                cart.Add(new CartItem
                {
                    ProductId = product.Id,
                    Name = product.Name,
                    Price = product.Price,
                    Quantity = 1,
                    ImageUrl = product.ImageUrl
                });
            }

            SaveCart(cart);

            // Also remove from wishlist if it exists
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId != null)
            {
                var wishItem = _context.WishlistItems.FirstOrDefault(w => w.UserId == userId && w.ProductId == id);
                if (wishItem != null)
                {
                    _context.WishlistItems.Remove(wishItem);
                    _context.SaveChanges();
                }
            }

            return Json(new { success = true });
        }

        public IActionResult RemoveFromCart(int id)
        {
            var cart = GetCart();
            var item = cart.FirstOrDefault(c => c.ProductId == id);
            if (item != null)
            {
                cart.Remove(item);
                SaveCart(cart);
            }
            return RedirectToAction("Index");
        }

        public IActionResult ClearCart()
        {
            HttpContext.Session.Remove(CartSessionKey);
            return RedirectToAction("Index");
        }

        public IActionResult Checkout()
        {
            var cart = GetCart();
            if (cart.Count == 0) return RedirectToAction("Index");
            
            var viewModel = new CartViewModel
            {
                CartItems = cart,
                FullName = HttpContext.Session.GetString("Name")
            };
            
            return View(viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> ProcessCheckout(CartViewModel model)
        {
            var cart = GetCart();
            if(cart.Count == 0){
                return RedirectToAction("Index");
            }
            if(!ModelState.IsValid){
                model.CartItems = cart;
                return View("Checkout", model);
            }
            var userId = HttpContext.Session.GetInt32("UserId");
            if(userId == null)
            return RedirectToAction("Login", "Auth");
            
            var userEmail = HttpContext.Session.GetString("Email");
            var confirmLinkPattern = Url.Action("OrderConfirmed", "Cart", new { id = "__ID__" }, Request.Scheme)!;

            var command = new ProcessCheckoutCommand(
                userId.Value,
                userEmail,
                model.FullName,
                model.Address,
                model.City,
                model.ZipCode,
                cart,
                confirmLinkPattern
            );
            var result = await _mediator.Send(command);
            if (!result.Success)
            {
                ModelState.AddModelError("", result.Message ?? "Failed to process checkout.");
                model.CartItems = cart;
                return View("Checkout", model);
            }
            if (!string.IsNullOrEmpty(userEmail))
            {
                TempData["Success"] = "Order placed! Please check your email to confirm your purchase.";
            }
            HttpContext.Session.Remove(CartSessionKey);
            return RedirectToAction("OrderConfirmed", new { id = result.OrderId });
        }

        public IActionResult OrderConfirmed(int id)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            var role = HttpContext.Session.GetString("Role");
            if (userId == null) return RedirectToAction("Login", "Auth");

            var order = _context.Orders.FirstOrDefault(o => o.Id == id);
            if (order == null) return RedirectToAction("Index", "Home");
            if (order.UserId != userId && role != "Admin") return RedirectToAction("Index", "Home");

            return View(order);
        }

        [HttpPost]
        public IActionResult ConfirmOrder(int id)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            var role = HttpContext.Session.GetString("Role");
            if (userId == null) return RedirectToAction("Login", "Auth");

            var order = _context.Orders.FirstOrDefault(o => o.Id == id);
            if (order == null) return RedirectToAction("Index", "Home");
            if (order.UserId != userId && role != "Admin") return RedirectToAction("Index", "Home");

            if (order.Status == "Pending")
            {
                order.Status = "Confirmed";
                _context.SaveChanges();
                TempData["Success"] = "Order confirmed successfully!";
            }

            return RedirectToAction("OrderConfirmed", new { id = order.Id });
        }

        public IActionResult MyOrders()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Login", "Auth");

            var orders = _context.Orders
                .Where(o => o.UserId == userId)
                .OrderByDescending(o => o.OrderDate)
                .ToList();

            return View(orders);
        }

        private List<CartItem> GetCart()
        {
            var sessionCart = HttpContext.Session.GetString(CartSessionKey);
            return string.IsNullOrEmpty(sessionCart) 
                ? new List<CartItem>() 
                : JsonSerializer.Deserialize<List<CartItem>>(sessionCart);
        }

        private void SaveCart(List<CartItem> cart)
        {
            HttpContext.Session.SetString(CartSessionKey, JsonSerializer.Serialize(cart));
        }
    }
}
