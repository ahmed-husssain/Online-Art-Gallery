using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Project.Models;

namespace Project.Controllers
{
    public class AdminController : Controller
    {
        private readonly MyContext _context;
        private readonly Project.Services.IEmailService _emailService;

        public AdminController(MyContext context, Project.Services.IEmailService emailService)
        {
            _context = context;
            _emailService = emailService;
        }


        public IActionResult Feedbacks()
        {
            if (HttpContext.Session.GetString("Role") != "Admin") return RedirectToAction("Index", "Home");
            var feedbacks = _context.Feedbacks.OrderByDescending(f => f.SubmittedAt).ToList();
            return View(feedbacks);
        }

        [HttpPost]
        public async Task<IActionResult> ReplyFeedback(int id, string reply)
        {
            if (HttpContext.Session.GetString("Role") != "Admin") return RedirectToAction("Index", "Home");
            
            var feedback = await _context.Feedbacks.FindAsync(id);
            if (feedback != null)
            {
                feedback.AdminReply = reply;
                feedback.RepliedAt = DateTime.Now;
                feedback.IsReplied = true;
                _context.Feedbacks.Update(feedback);
                await _context.SaveChangesAsync();


                var safeName = System.Text.Encodings.Web.HtmlEncoder.Default.Encode(feedback.FullName ?? "");
                var safeSubject = System.Text.Encodings.Web.HtmlEncoder.Default.Encode(feedback.Subject ?? "");
                var safeReply = System.Text.Encodings.Web.HtmlEncoder.Default.Encode(reply ?? "").Replace("\n", "<br/>");

                string subject = "Reply to your inquiry: " + feedback.Subject;
                string body = $@"
                    <div style='font-family: Arial, sans-serif; padding: 20px; line-height: 1.6;'>
                        <h3>Hello {safeName},</h3>
                        <p>Thank you for reaching out to us. Here is our response to your message regarding '<strong>{safeSubject}</strong>':</p>
                        <div style='background: #f4f4f4; padding: 15px; border-left: 4px solid #4CAF50; margin: 20px 0;'>
                            {safeReply}
                        </div>
                        <p>Best Regards,<br/>Art Gallery Team</p>
                    </div>";
                
                await _emailService.SendEmailAsync(feedback.Email, subject, body);
                
                TempData["Success"] = "Reply sent successfully!";
            }
            return RedirectToAction("Feedbacks");
        }


        public IActionResult Index()
        {
            if (HttpContext.Session.GetString("Role") != "Admin") 
                return RedirectToAction("Index", "Home");

            ViewBag.TotalUsers = _context.users.Count();
            ViewBag.TotalProducts = _context.products.Count();
            ViewBag.TotalOrders = _context.Orders.Count();
            ViewBag.TotalRevenue = _context.Orders.Sum(o => (decimal?)o.TotalAmount) ?? 0m;
            ViewBag.PendingApprovals = _context.products.Count(p => !p.IsApproved && p.ArtistId != null);

            // Fetch recent orders for dashboard
            var recentOrders = _context.Orders
                .Include(o => o.User)
                .OrderByDescending(o => o.OrderDate)
                .Take(5)
                .ToList();

            return View(recentOrders);
        }


        public IActionResult Orders()
        {
            if (HttpContext.Session.GetString("Role") != "Admin") return RedirectToAction("Index", "Home");
            var orders = _context.Orders.OrderByDescending(o => o.OrderDate).ToList();
            return View(orders);
        }

        public IActionResult OrderDetails(int id)
        {
            if (HttpContext.Session.GetString("Role") != "Admin") return RedirectToAction("Index", "Home");
            
            var order = _context.Orders
                .Include(o => o.User)
                .Include(o => o.OrderItems)
                .FirstOrDefault(o => o.Id == id);

            if (order == null) return NotFound();
            return View(order);
        }


        public IActionResult Users()
        {
            if (HttpContext.Session.GetString("Role") != "Admin") return RedirectToAction("Index", "Home");
            var users = _context.users.ToList();
            return View(users);
        }

        public IActionResult AddUser()
        {
            if (HttpContext.Session.GetString("Role") != "Admin") return RedirectToAction("Index", "Home");
            return View();
        }

        [HttpPost]
        public IActionResult AddUser(User user)
        {
            if (HttpContext.Session.GetString("Role") != "Admin") return RedirectToAction("Index", "Home");
            if (ModelState.IsValid)
            {
                if (!string.IsNullOrEmpty(user.Password))
                {
                    user.Password = new Microsoft.AspNetCore.Identity.PasswordHasher<User>().HashPassword(user, user.Password);
                }
                user.IsOnboarded = true;
                _context.users.Add(user);
                _context.SaveChanges();
                TempData["Success"] = "User created successfully!";
                return RedirectToAction("Users");
            }
            return View(user);
        }

        public IActionResult EditUser(int id)
        {
            if (HttpContext.Session.GetString("Role") != "Admin") return RedirectToAction("Index", "Home");
            var user = _context.users.Find(id);
            return View(user);
        }

        [HttpPost]
        public IActionResult EditUser(User user)
        {
            if (HttpContext.Session.GetString("Role") != "Admin") return RedirectToAction("Index", "Home");
            var existing = _context.users.Find(user.UserId);
            if (existing != null)
            {
                existing.Name = user.Name;
                existing.Email = user.Email;
                if (!string.IsNullOrEmpty(user.Password) && user.Password != existing.Password)
                {
                    existing.Password = new Microsoft.AspNetCore.Identity.PasswordHasher<User>().HashPassword(existing, user.Password);
                }
                existing.Role = user.Role;
                _context.SaveChanges();
                TempData["Success"] = "User updated successfully!";
                return RedirectToAction("Users");
            }
            return View(user);
        }

        public IActionResult DeleteUser(int id)
        {
            if (HttpContext.Session.GetString("Role") != "Admin") return RedirectToAction("Index", "Home");
            var user = _context.users.Find(id);
            if (user != null)
            {
                _context.users.Remove(user);
                _context.SaveChanges();
                TempData["Success"] = "User deleted successfully!";
            }
            return RedirectToAction("Users");
        }


        public IActionResult Products()
        {
            if (HttpContext.Session.GetString("Role") != "Admin") return RedirectToAction("Index", "Home");
            var products = _context.products.ToList();
            return View(products);
        }

        public IActionResult AddProduct()
        {
            if (HttpContext.Session.GetString("Role") != "Admin") return RedirectToAction("Index", "Home");
            ViewBag.Artists = _context.users.Where(u => u.Role == "Artist").ToList();
            return View();
        }

        [HttpPost]
        public IActionResult AddProduct(Product product, IFormFile image)
        {
            if (ModelState.IsValid)
            {
                if (image != null && image.Length > 0)
                {
                    var fileName = Guid.NewGuid().ToString() + Path.GetExtension(image.FileName);
                    var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images/products", fileName);
                    Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images/products"));
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        image.CopyTo(stream);
                    }
                    product.ImageUrl = "/images/products/" + fileName;
                }


                if(product.IsAuction && product.AuctionEndTime == null) {
                    product.AuctionEndTime = DateTime.Now.AddDays(7);
                }

                product.IsApproved = true;
                _context.products.Add(product);
                _context.SaveChanges();
                TempData["Success"] = "Artwork created successfully!";
                return RedirectToAction("Products");
            }
            return View(product);
        }

        public IActionResult EditProduct(int id)
        {
            if (HttpContext.Session.GetString("Role") != "Admin") return RedirectToAction("Index", "Home");
            var product = _context.products.Find(id);
            return View(product);
        }

        [HttpPost]
        public IActionResult EditProduct(Product product, IFormFile image)
        {
            if (HttpContext.Session.GetString("Role") != "Admin") return RedirectToAction("Index", "Home");
                ViewBag.Artists = _context.users.Where(u => u.Role == "Artist").ToList();
            var existing = _context.products.Find(product.Id);
            if (existing != null)
            {
                existing.Name = product.Name;
                existing.Description = product.Description;
                existing.Price = product.Price;
                existing.IsAuction = product.IsAuction;
                existing.AuctionEndTime = product.AuctionEndTime;
                
                if(product.Price > (existing.CurrentBid ?? 0)){
                    existing.CurrentBid = null;
                    existing.HighestBidderId = null;
                    existing.BidCount = 0;
                }
                if (image != null && image.Length > 0)
                {
                    var fileName = Guid.NewGuid().ToString() + Path.GetExtension(image.FileName);
                    var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images/products", fileName);
                    Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images/products"));
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        image.CopyTo(stream);
                    }
                    existing.ImageUrl = "/images/products/" + fileName;
                }

                _context.SaveChanges();
                TempData["Success"] = "Artwork updated successfully!";
                return RedirectToAction("Products");
            }
            return View(product);
        }

        public IActionResult DeleteProduct(int id)
        {
            if (HttpContext.Session.GetString("Role") != "Admin") return RedirectToAction("Index", "Home");
            var product = _context.products.Find(id);
            if (product != null)
            {
                _context.products.Remove(product);
                _context.SaveChanges();
                TempData["Success"] = "Artwork deleted successfully!";
            }
            return RedirectToAction("Products");
        }


        public IActionResult Approvals()
        {
            if (HttpContext.Session.GetString("Role") != "Admin") return RedirectToAction("Index", "Home");
            var pending = _context.products.Where(p => !p.IsApproved).ToList();
            return View(pending);
        }

        public IActionResult ApproveProduct(int id)
        {
            if (HttpContext.Session.GetString("Role") != "Admin") return RedirectToAction("Index", "Home");
            var product = _context.products.Find(id);
            if (product != null)
            {
                product.IsApproved = true;
                _context.SaveChanges();
                TempData["Success"] = "Artwork approved and is now live!";
            }
            return RedirectToAction("Approvals");
        }


        public IActionResult Exhibitions()
        {
            if (HttpContext.Session.GetString("Role") != "Admin") return RedirectToAction("Index", "Home");
            var exhibitions = _context.Exhibitions.OrderByDescending(e => e.Date).ToList();
            return View(exhibitions);
        }

        public IActionResult AddExhibition()
        {
            if (HttpContext.Session.GetString("Role") != "Admin") return RedirectToAction("Index", "Home");
            return View();
        }

        [HttpPost]
        public IActionResult AddExhibition(Exhibition exhibition, IFormFile image)
        {
            if (HttpContext.Session.GetString("Role") != "Admin") return RedirectToAction("Index", "Home");
            if (ModelState.IsValid)
            {
                if (image != null && image.Length > 0)
                {
                    var fileName = Guid.NewGuid().ToString() + Path.GetExtension(image.FileName);
                    var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images/exhibitions", fileName);
                    Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images/exhibitions"));
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        image.CopyTo(stream);
                    }
                    exhibition.ImageUrl = "/images/exhibitions/" + fileName;
                }

                _context.Exhibitions.Add(exhibition);
                _context.SaveChanges();
                TempData["Success"] = "Exhibition added successfully!";
                return RedirectToAction("Exhibitions");
            }
            return View(exhibition);
        }

        public IActionResult DeleteExhibition(int id)
        {
            if (HttpContext.Session.GetString("Role") != "Admin") return RedirectToAction("Index", "Home");
            var exhibition = _context.Exhibitions.Find(id);
            if (exhibition != null)
            {
                _context.Exhibitions.Remove(exhibition);
                _context.SaveChanges();
                TempData["Success"] = "Exhibition deleted.";
            }
            return RedirectToAction("Exhibitions");
        }
        [HttpPost]
        public async Task<IActionResult> UpdateOrderStatus(int id, string status, DateTime? deliveryDate)
        {
            if (HttpContext.Session.GetString("Role") != "Admin") return RedirectToAction("Index", "Home");

            var order = await _context.Orders.Include(o => o.User).FirstOrDefaultAsync(o => o.Id == id);
            if (order != null)
            {
                order.Status = status;
                if (status == "Shipped" && deliveryDate != null)
                {

                    string subject = "Art Gallery: Your Masterpiece is on its way!";
                    string body = $@"
                        <div style='font-family: sans-serif; padding: 40px; background: #05060a; color: white;'>
                            <h2>Good news, {order.CustomerName}!</h2>
                            <p>Your order #{order.Id} has been shipped.</p>
                            <p><strong>Estimated Delivery:</strong> {deliveryDate?.ToString("MMM dd, yyyy")}</p>
                            <hr style='border: 1px solid #1e293b; margin: 20px 0;' />
                            <p style='font-size: 14px; color: #94a3b8;'>Thank you for choosing Art Gallery.</p>
                        </div>";
                    await _emailService.SendEmailAsync(order.User.Email, subject, body);
                }
                
                await _context.SaveChangesAsync();
                TempData["Success"] = $"Order status updated to {status}.";
            }
            return RedirectToAction("OrderDetails", new { id = id });
        }

        [HttpPost]
        public async Task<IActionResult> SendPaymentReminder(int id)
        {
            if (HttpContext.Session.GetString("Role") != "Admin") return RedirectToAction("Index", "Home");

            var order = await _context.Orders.Include(o => o.User).FirstOrDefaultAsync(o => o.Id == id);
            if (order != null && !order.IsPaid)
            {
                string subject = "ACTION REQUIRED: Payment Reminder for Order #" + order.Id;
                string body = $@"
                    <div style='font-family: sans-serif; padding: 40px; background: #05060a; color: white;'>
                        <h2 style='color: #fbbf24;'>Payment Reminder</h2>
                        <p>Hello {order.CustomerName},</p>
                        <p>This is a reminder regarding your pending payment for Order <strong>#{order.Id}</strong>.</p>
                        <div style='background: #1e293b; padding: 20px; border-radius: 12px; margin: 20px 0;'>
                            <p><strong>Total Amount Due:</strong> ${order.TotalAmount}</p>
                            <p><strong>Due Date:</strong> {order.PaymentDueDate?.ToString("MMM dd, yyyy") ?? "As soon as possible"}</p>
                        </div>
                        <p>Please complete your payment to avoid any late charges or cancellation of your order.</p>
                        <hr style='border: 1px solid #1e293b; margin: 20px 0;' />
                        <p style='font-size: 14px; color: #94a3b8;'>Thank you for your prompt attention. Art Gallery Team.</p>
                    </div>";
                
                await _emailService.SendEmailAsync(order.User.Email, subject, body);
                TempData["Success"] = "Payment reminder sent to customer.";
            }
            return RedirectToAction("OrderDetails", new { id = id });
        }
    }
}
