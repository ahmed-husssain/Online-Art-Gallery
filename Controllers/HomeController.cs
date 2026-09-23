using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Project.Models;
using Project.ViewModels;
using Project.Services;
using OtpNet;
using QRCoder;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Microsoft.AspNetCore.Identity;

namespace Project.Controllers
{
    public class HomeController : Controller
    {
        private readonly MyContext db;
        private readonly IEmailService emailService;
        private readonly IPasswordHasher<User> _passwordHasher = new PasswordHasher<User>();

        public HomeController(MyContext context, IEmailService email)
        {
            db = context;
            emailService = email;
        }

        public IActionResult Index()
        {
            ViewBag.Exhibitions = db.Exhibitions.Where(e => e.IsActive).OrderBy(e => e.Date).Take(3).ToList();
            ViewBag.Artworks = db.products.Where(p => p.IsApproved).OrderByDescending(p => p.Id).Take(6).ToList();
            return View();
        }

        public IActionResult AboutContact()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> PostFeedback(string? FullName, string? Email, string Subject, string Message)
        {
            var sessionEmail = HttpContext.Session.GetString("Email");
            var sessionName = HttpContext.Session.GetString("Name");

            var finalEmail = string.IsNullOrEmpty(Email) ? sessionEmail : Email;
            var finalName = string.IsNullOrEmpty(FullName) ? sessionName : FullName;

            if (string.IsNullOrEmpty(finalEmail) || string.IsNullOrEmpty(finalName) || string.IsNullOrEmpty(Subject) || string.IsNullOrEmpty(Message))
            {
                TempData["Error"] = "Please ensure all fields are filled. You must be logged in or provide contact details.";
                return RedirectToAction("AboutContact");
            }

            var feedback = new Feedback
            {
                FullName = finalName,
                Email = finalEmail,
                Subject = Subject,
                Message = Message,
                SubmittedAt = DateTime.Now
            };

            db.Feedbacks.Add(feedback);
            await db.SaveChangesAsync();

            TempData["Success"] = "Your message has been sent successfully! We will get back to you soon.";
            return RedirectToAction("AboutContact");
        }

        public IActionResult Settings()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Login", "Auth");

            var user = db.users.Find(userId);
            if (user == null) return RedirectToAction("Login", "Auth");

            return View(user);
        }

        [HttpPost]
        public IActionResult Settings(string name, IFormFile avatarFile, string activeTab)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Login", "Auth");

            var user = db.users.Find(userId);
            if (user == null) return RedirectToAction("Login", "Auth");

            user.Name = name;

            if (avatarFile != null && avatarFile.Length > 0)
            {
                var fileName = Guid.NewGuid().ToString() + Path.GetExtension(avatarFile.FileName);
                var directoryPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "avatars");

                if (!Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                var filePath = Path.Combine(directoryPath, fileName);
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    avatarFile.CopyTo(stream);
                }
                user.Avatar = "/images/avatars/" + fileName;
            }

            db.users.Update(user);
            db.SaveChanges();

            HttpContext.Session.SetString("Name", user.Name);
            if (!string.IsNullOrEmpty(user.Avatar))
            {
                HttpContext.Session.SetString("Avatar", user.Avatar);
            }

            TempData["Success"] = "Aesthetic identity synchronized successfully.";
            return RedirectToAction("Settings", new { tab = activeTab ?? "avatar" });
        }


        [HttpGet]
        public async Task<IActionResult> ChangePassword()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Login", "Auth");

            var user = db.users.Find(userId);
            if (user == null) return RedirectToAction("Login", "Auth");

            bool hasPassword = !string.IsNullOrEmpty(user.Password);
            ViewBag.HasPassword = hasPassword;

            if (hasPassword)
            {
                Random rnd = new Random();
                string otp = rnd.Next(100000, 999999).ToString();

                user.OTP = otp;
                user.OTPExpiry = DateTime.Now.AddMinutes(10);

                db.users.Update(user);
                await db.SaveChangesAsync();

                string subject = "Password Change Verification Code";
                string body = $@"
                    <div style='font-family: Arial, sans-serif; padding: 20px;'>
                        <h2>Password Change Request</h2>
                        <p>You requested to change your password. Please use the following One-Time Password (OTP) to verify this action:</p>
                        <h1 style='color: #4CAF50; letter-spacing: 5px;'>{otp}</h1>
                        <p>This code will expire in 10 minutes.</p>
                        <p>If you did not request this, please ignore this email or contact support.</p>
                    </div>";

                await emailService.SendEmailAsync(user.Email, subject, body);

                TempData["Info"] = "A verification code has been sent to your email address.";
            }

            return View(new SetPasswordViewModel());
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ChangePassword(SetPasswordViewModel model)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Login", "Auth");

            var user = db.users.Find(userId);
            if (user == null) return RedirectToAction("Login", "Auth");

            bool hasPassword = !string.IsNullOrEmpty(user.Password);
            ViewBag.HasPassword = hasPassword;

            if (hasPassword)
            {
                if (string.IsNullOrEmpty(model.CurrentPassword))
                {
                    ModelState.AddModelError("CurrentPassword", "Current password is required.");
                    return View(model);
                }

                var verifyResult = _passwordHasher.VerifyHashedPassword(user, user.Password, model.CurrentPassword);
                if (verifyResult == PasswordVerificationResult.Failed)
                {
                    ModelState.AddModelError("CurrentPassword", "Current password is incorrect.");
                    return View(model);
                }

                if (string.IsNullOrEmpty(model.OTP))
                {
                    ModelState.AddModelError("OTP", "Verification code is required.");
                    return View(model);
                }

                if (user.OTP != model.OTP)
                {
                    ModelState.AddModelError("OTP", "Invalid verification code.");
                    return View(model);
                }

                if (user.OTPExpiry < DateTime.Now)
                {
                    ModelState.AddModelError("OTP", "Verification code has expired. Please refresh the page to get a new one.");
                    return View(model);
                }
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            bool isFirstTimePassword = string.IsNullOrEmpty(user.Password);

            user.Password = _passwordHasher.HashPassword(user, model.NewPassword);
            user.OTP = null;
            user.OTPExpiry = null;

            db.users.Update(user);
            db.SaveChanges();

            TempData["Success"] = isFirstTimePassword
                ? "Password set successfully! You can now log in with email and password."
                : "Password changed successfully!";

            return RedirectToAction("Settings", new { tab = "password" });
        }


        [HttpPost]
        public IActionResult UpdateCustomization(string theme, bool useCustomCursor, string cursorStyle)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Login", "Auth");

            var user = db.users.Find(userId);
            if (user == null) return Json(new { success = false });

            user.Theme = string.IsNullOrEmpty(theme) ? (user.Theme ?? "dark") : theme;
            user.UseCustomCursor = useCustomCursor;
            user.CursorStyle = cursorStyle ?? "eclipse";

            db.users.Update(user);
            db.SaveChanges();

            HttpContext.Session.SetString("Theme", user.Theme);
            HttpContext.Session.SetString("UseCustomCursor", user.UseCustomCursor.ToString().ToLower());
            HttpContext.Session.SetString("CursorStyle", user.CursorStyle);

            return Json(new { success = true });
        }

        [HttpPost]
        public IActionResult SaveCard(string holder, string number, string expiry, string cvc)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return Json(new { success = false });

            var user = db.users.Find(userId);
            if (user != null)
            {
                user.CardHolderName = holder;
                user.CardNumber = number;
                user.CardExpiry = expiry;
                user.CardCVC = cvc;

                db.SaveChanges();
                return Json(new { success = true });
            }
            return Json(new { success = false });
        }


        public IActionResult EnableTwoFactor()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Login", "Auth");

            var user = db.users.Find(userId);
            if (user == null) return RedirectToAction("Login", "Auth");

            if (user.IsTwoFactorEnabled)
            {
                return RedirectToAction("Settings", new { tab = "2fa" });
            }

            var key = KeyGeneration.GenerateRandomKey(20);
            var base32Secret = Base32Encoding.ToString(key);

            var qrGenerator = new QRCodeGenerator();
            var otpAuthUrl = $"otpauth://totp/Project:{user.Email}?secret={base32Secret}&issuer=Project";
            var qrCodeData = qrGenerator.CreateQrCode(otpAuthUrl, QRCodeGenerator.ECCLevel.Q);
            var qrCode = new PngByteQRCode(qrCodeData);
            var qrCodeImage = qrCode.GetGraphic(20);
            var qrCodeBase64 = Convert.ToBase64String(qrCodeImage);

            var model = new TwoFactorSetupViewModel
            {
                Secret = base32Secret,
                QrCodeImage = $"data:image/png;base64,{qrCodeBase64}"
            };

            return View(model);
        }


        [HttpPost]
        public IActionResult EnableTwoFactor(TwoFactorSetupViewModel model)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Login", "Auth");

            var user = db.users.Find(userId);
            if (user == null) return RedirectToAction("Login", "Auth");

            if (!ModelState.IsValid)
            {
                var qrGenerator = new QRCodeGenerator();
                var otpAuthUrl = $"otpauth://totp/Project:{user.Email}?secret={model.Secret}&issuer=Project";
                var qrCodeData = qrGenerator.CreateQrCode(otpAuthUrl, QRCodeGenerator.ECCLevel.Q);
                var qrCode = new PngByteQRCode(qrCodeData);
                var qrCodeImage = qrCode.GetGraphic(20);
                model.QrCodeImage = $"data:image/png;base64,{Convert.ToBase64String(qrCodeImage)}";

                return View(model);
            }

            var secretBytes = Base32Encoding.ToBytes(model.Secret);
            var totp = new Totp(secretBytes);

            bool valid = totp.VerifyTotp(model.Code, out long timeStepMatched, new VerificationWindow(2, 2));

            if (!valid)
            {
                ModelState.AddModelError("Code", "Invalid verification code.");

                var qrGenerator = new QRCodeGenerator();
                var otpAuthUrl = $"otpauth://totp/Project:{user.Email}?secret={model.Secret}&issuer=Project";
                var qrCodeData = qrGenerator.CreateQrCode(otpAuthUrl, QRCodeGenerator.ECCLevel.Q);
                var qrCode = new PngByteQRCode(qrCodeData);
                var qrCodeImage = qrCode.GetGraphic(20);
                model.QrCodeImage = $"data:image/png;base64,{Convert.ToBase64String(qrCodeImage)}";

                return View(model);
            }

            user.TwoFactorSecret = model.Secret;
            user.IsTwoFactorEnabled = true;
            db.users.Update(user);
            db.SaveChanges();

            TempData["Success"] = "Two-Factor Authentication enabled successfully.";
            return RedirectToAction("Settings", new { tab = "2fa" });
        }

        [HttpPost]
        public IActionResult DisableTwoFactor()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Login", "Auth");

            var user = db.users.Find(userId);
            if (user != null)
            {
                user.IsTwoFactorEnabled = false;
                user.TwoFactorSecret = null;
                db.users.Update(user);
                db.SaveChanges();
            }
            return RedirectToAction("Settings", new { tab = "2fa" });
        }
    }
}
