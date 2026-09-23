using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using OtpNet;
using Project.Models;
using Project.Services;
using Project.ViewModels;
using System.Drawing;
using System.Security.Claims;

namespace Project.Controllers
{
    public class AuthController : Controller
    {
        private readonly MyContext _db;
        private readonly IEmailService _emailService;
        private readonly PasswordHasher<User> _passwordHasher = new PasswordHasher<User>();
        private readonly IMemoryCache _memoryCache;

        public AuthController(MyContext db, IEmailService emailService, IMemoryCache memoryCache)
        {
            _db = db;
            _emailService = emailService;
            _memoryCache = memoryCache;
        }

        #region Rate Limiting
        private bool IsBlocked(string key, int limit = 5, int blockMinutes = 5)
        {
            var attempts = _memoryCache.GetOrCreate(key, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(blockMinutes);
                return 0;
            });

            if (attempts >= limit) return true;

            _memoryCache.Set(key, attempts + 1, TimeSpan.FromMinutes(blockMinutes));
            return false;
        }
        #endregion

        #region Session Management
        private async Task SetUserSessionAsync(User user)
        {
            HttpContext.Session.SetInt32("UserId", user.UserId);
            HttpContext.Session.SetString("Email", user.Email);
            HttpContext.Session.SetString("Name", user.Name);
            HttpContext.Session.SetString("Role", user.Role);
            if (!string.IsNullOrEmpty(user.Avatar))
                HttpContext.Session.SetString("Avatar", user.Avatar);

            if (string.IsNullOrEmpty(user.Theme))
            {
                user.Theme = "dark";
                _db.users.Update(user);
                await _db.SaveChangesAsync();
            }
            HttpContext.Session.SetString("Theme", user.Theme);
            HttpContext.Session.SetString("UseCustomCursor", user.UseCustomCursor.ToString().ToLower());
        }
        #endregion

        #region External User Handling
        private async Task<User> UpsertExternalUserAsync(string email, string name, string avatarUrl)
        {
            var localAvatar = !string.IsNullOrEmpty(avatarUrl) ? await DownloadImageAsync(avatarUrl) : string.Empty;

            var user = _db.users.FirstOrDefault(u => u.Email == email);
            if (user == null)
            {
                user = new User
                {
                    Email = email,
                    Name = name,
                    Role = "User",
                    Avatar = localAvatar ?? string.Empty,
                    Password = null,
                    Age = null,
                    Sex = null,
                    Interests = null,
                    Theme = "dark",
                    CursorStyle = "eclipse",
                    UseCustomCursor = true
                };
                _db.users.Add(user);
            }
            else
            {
                user.Name = name;
                if (!string.IsNullOrEmpty(localAvatar))
                    user.Avatar = localAvatar;
                _db.users.Update(user);
            }

            await _db.SaveChangesAsync();
            return user;
        }

        private async Task<IActionResult> ExternalLoginResponseAsync(Func<ClaimsPrincipal, (string Email, string Name, string AvatarUrl)> extractor)
        {
            // Get the result of the external login
            var result = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            if (result?.Principal == null)
                return RedirectToAction("Login");

            // Extract user info from claims safely
            var (email, name, avatarUrl) = extractor(result.Principal);

            if (string.IsNullOrEmpty(email))
                return RedirectToAction("Login"); // Email is required

            // Upsert the user safely (new or existing)
            var user = await UpsertExternalUserAsync(email, name, avatarUrl);

            // Set session for the user
            await SetUserSessionAsync(user);

            // Sign in the user with cookie authentication
            var claims = new List<Claim>
    {
        new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
        new Claim(ClaimTypes.Name, user.Name),
        new Claim(ClaimTypes.Email, user.Email),
        new Claim(ClaimTypes.Role, user.Role)
    };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

            // Redirect based on role/onboarding
            return GetLoginRedirect(user);
        }

        #endregion

        #region Image Handling
        private async Task<string> DownloadImageAsync(string url, int size = 256)
        {
            try
            {
                using var client = new HttpClient();
                var data = await client.GetByteArrayAsync(url);

                using var ms = new MemoryStream(data);
                using var image = Image.FromStream(ms);
                using var resized = new Bitmap(image, new Size(size, size));

                var fileName = Guid.NewGuid() + ".png";
                var directoryPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images/avatars");
                Directory.CreateDirectory(directoryPath);

                var filePath = Path.Combine(directoryPath, fileName);
                resized.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);

                return "/images/avatars/" + fileName;
            }
            catch
            {
                return string.Empty;
            }
        }
        #endregion

        #region Login & Register
        public IActionResult Index() => RedirectToAction("Login");
        public IActionResult Login() => View();
        public IActionResult Register() { ViewBag.ShowRegister = true; return View("Login"); }
        public async Task<IActionResult> Logout()
        {
            HttpContext.Session.Clear();
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View("Login");
            }

            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var rateKey = $"login-{model.Email}-{ip}";

            if (IsBlocked(rateKey))
            {
                ViewBag.Error = "Too many failed login attempts. Try again later.";
                return View();
            }
            if (string.IsNullOrEmpty(model.Email) || string.IsNullOrEmpty(model.Password))
            {
                ViewBag.Error = "Email and password are required";
                return View();
            }

            var user = _db.users.FirstOrDefault(x => x.Email == model.Email);
            if (user == null || string.IsNullOrEmpty(user.Password))
            {
                ViewBag.Error = "Invalid email or password";
                return View();
            }

            var result = _passwordHasher.VerifyHashedPassword(user, user.Password, model.Password);
            if (result == PasswordVerificationResult.Failed)
            {
                ViewBag.Error = "Invalid email or password";
                return View();
            }

            _memoryCache.Remove(rateKey);

            if (user.IsTwoFactorEnabled)
            {
                HttpContext.Session.Clear();
                HttpContext.Session.SetInt32("2fa_UserId", user.UserId);
                return RedirectToAction("VerifyTwoFactor");
            }

            await SetUserSessionAsync(user);
            return GetLoginRedirect(user);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.ShowRegister = true;
                return View("Login", model);
            }

            if (_db.users.Any(x => x.Email == model.Email))
            {
                ModelState.AddModelError("Email", "Email already Registered");
                ViewBag.ShowRegister = true;
                return View("Login");
            }

            var user = new User
            {
                Name = model.Name,
                Email = model.Email,
                Role = "User",
                IsOnboarded = false,
                Password = _passwordHasher.HashPassword(null, model.Password)
            };

            _db.users.Add(user);
            await _db.SaveChangesAsync();
            TempData["Success"] = "Account created successfully! Please sign in.";
            return RedirectToAction("Login");
        }
        #endregion

        #region OAuth Logins
        public IActionResult GoogleLogin() => Challenge(new AuthenticationProperties { RedirectUri = Url.Action("GoogleResponse") }, GoogleDefaults.AuthenticationScheme);
        public IActionResult GithubLogin() => Challenge(new AuthenticationProperties { RedirectUri = Url.Action("GithubResponse") }, "GitHub");
        public IActionResult DiscordLogin() => Challenge(new AuthenticationProperties { RedirectUri = Url.Action("DiscordResponse") }, "Discord");

        public Task<IActionResult> GoogleResponse() =>
      ExternalLoginResponseAsync(p => (
          p.FindFirstValue(ClaimTypes.Email),
          p.FindFirstValue(ClaimTypes.Name),
          p.FindFirstValue("urn:google:picture")
      ));

        public Task<IActionResult> GithubResponse() =>
            ExternalLoginResponseAsync(p => (
                p.FindFirstValue(ClaimTypes.Email),
                p.FindFirstValue(ClaimTypes.Name),
                p.FindFirstValue("urn:github:avatar:url") ?? p.FindFirstValue("avatar_url")
            ));

        public Task<IActionResult> DiscordResponse() =>
            ExternalLoginResponseAsync(p =>
            {
                var email = p.FindFirstValue(ClaimTypes.Email);
                var name = p.FindFirstValue(ClaimTypes.Name);
                var userId = p.FindFirstValue(ClaimTypes.NameIdentifier);
                var avatarHash = p.FindFirstValue("urn:discord:avatar:hash");
                var avatarUrl = !string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(avatarHash)
                    ? $"https://cdn.discordapp.com/avatars/{userId}/{avatarHash}.png"
                    : string.Empty;
                return (email, name, avatarUrl);
            });

        #endregion

        #region Password Reset
        [HttpGet] public IActionResult ForgotPassword() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(string email)
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var rateKey = $"forgot-{ip}";
            if (IsBlocked(rateKey, 3, 10))
            {
                ViewBag.Message = "Too many password reset requests. Try again later.";
                return View();
            }

            if (string.IsNullOrEmpty(email))
            {
                ViewBag.Message = "Please enter your email.";
                return View();
            }

            var user = _db.users.FirstOrDefault(u => u.Email == email);
            if (user != null)
            {
                var rawToken = Guid.NewGuid().ToString();
                user.ResetToken = _passwordHasher.HashPassword(user, rawToken);
                user.ResetTokenExpiry = DateTime.Now.AddHours(1);
                _db.users.Update(user);
                await _db.SaveChangesAsync();

                var resetLink = Url.Action("ResetPassword", "Auth", new { email = user.Email, token = rawToken }, Request.Scheme);
                await _emailService.SendEmailAsync(user.Email, "Reset your password", $"Click here: <a href='{resetLink}'>Reset Password</a>");
            }

            ViewBag.Message = "If an account exists with this email, a reset link has been sent.";
            return View();
        }

        [HttpGet]
        public IActionResult ResetPassword(string email, string token)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token))
                return RedirectToAction("ForgotPassword");

            return View(new ChangePasswordViewModel { Email = email, Token = token });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(ChangePasswordViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var user = _db.users.FirstOrDefault(u => u.Email == model.Email);
            if (user == null || user.ResetToken == null || user.ResetTokenExpiry < DateTime.Now)
            {
                ModelState.AddModelError("", "Invalid or expired token.");
                return View(model);
            }

            var tokenResult = _passwordHasher.VerifyHashedPassword(user, user.ResetToken, model.Token);
            if (tokenResult == PasswordVerificationResult.Failed)
            {
                ModelState.AddModelError("", "Invalid or expired token.");
                return View(model);
            }

            user.Password = _passwordHasher.HashPassword(user, model.NewPassword);
            user.ResetToken = null;
            user.ResetTokenExpiry = null;
            _db.users.Update(user);
            await _db.SaveChangesAsync();

            return RedirectToAction("ResetPasswordSuccess");
        }

        public IActionResult ResetPasswordSuccess() => View();
        #endregion

        #region Two Factor
        public IActionResult VerifyTwoFactor()
        {
            if (HttpContext.Session.GetInt32("2fa_UserId") == null) return RedirectToAction("Login");
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyTwoFactor(VerifyTwoFactorViewModel model)
        {
            await HttpContext.Session.LoadAsync();
            var userId = HttpContext.Session.GetInt32("2fa_UserId");
            if (userId == null) return RedirectToAction("Login");

            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var rateKey = $"2fa-{ip}";

            if (IsBlocked(rateKey))
            {
                TempData["Error"] = "Too many invalid 2FA attempts. Try again later";
                return View(model);
            }

            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Invalid verification code.";
                return View(model);
            }

            var user = _db.users.Find(userId);
            if (user == null || string.IsNullOrEmpty(user.TwoFactorSecret))
                return RedirectToAction("Login");

            var totp = new Totp(Base32Encoding.ToBytes(user.TwoFactorSecret));
            var cleanCode = model.Code?.Replace(" ", "").Trim();

            if (string.IsNullOrEmpty(cleanCode))
            {
                TempData["Error"] = "Invalid verification code.";
                return View(model);
            }

            if (!totp.VerifyTotp(cleanCode, out long _, new VerificationWindow(1, 1)))
            {
                TempData["Error"] = "Invalid verification code.";
                return View(model);
            }


            HttpContext.Session.Remove("2fa_UserId");
            await SetUserSessionAsync(user);
            await HttpContext.Session.CommitAsync();

            return GetLoginRedirect(user);
        }
        #endregion

        #region Onboarding
        public IActionResult Onboarding()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Login");

            var user = _db.users.Find(userId);
            bool isDataMissing = user != null && (user.Age == null || string.IsNullOrEmpty(user.Sex) || string.IsNullOrEmpty(user.Interests));
            if (user == null || (user.IsOnboarded && !isDataMissing)) return RedirectToAction("Index", "Home");

            return View(user);
        }

        [HttpPost]
        public async Task<IActionResult> Onboarding(int Age, string Sex, string Interests, string Role)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("Login");

            var user = _db.users.Find(userId);
            if (user == null) return RedirectToAction("Login");

            user.Age = Age;
            user.Sex = Sex;
            user.Interests = Interests;
            user.Role = Role;
            user.IsOnboarded = true;
            _db.users.Update(user);
            await _db.SaveChangesAsync();

            await SetUserSessionAsync(user);

            return GetLoginRedirect(user);
        }
        #endregion

        private IActionResult GetLoginRedirect(User user)
        {
            bool isDataMissing = user.Age == null || string.IsNullOrEmpty(user.Sex) || string.IsNullOrEmpty(user.Interests);

            if ((!user.IsOnboarded || isDataMissing) && user.Role != "Admin")
                return RedirectToAction("Onboarding");

            return user.Role switch
            {
                "Admin" => RedirectToAction("Index", "Admin"),
                "Artist" => RedirectToAction("Index", "Artist"),
                _ => RedirectToAction("Index", "Home")
            };
        }
    }
}
