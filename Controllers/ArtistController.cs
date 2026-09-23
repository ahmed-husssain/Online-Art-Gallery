using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Project.Models;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;

namespace Project.Controllers
{
    public class ArtistController : Controller
    {
        private readonly MyContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public ArtistController(MyContext context, IHttpClientFactory httpClientFactory, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _webHostEnvironment = webHostEnvironment;
        }


        public IActionResult Index()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            var role = HttpContext.Session.GetString("Role");
            
            // Allow if user is logged in and is a User
            if (userId == null)
                return RedirectToAction("Index", "Home");

            var artworks = _context.products.Where(p => p.ArtistId == userId).ToList();
            
            ViewBag.TotalArtworks = artworks.Count;
            ViewBag.ApprovedArtworks = artworks.Count(a => a.IsApproved);
            ViewBag.PendingArtworks = artworks.Count(a => !a.IsApproved);
            
            return View(artworks);
        }


        public IActionResult Upload()
        {
            var role = HttpContext.Session.GetString("Role");
            if (role != "User" && role != "Admin" && role != "Artist")
                return RedirectToAction("Index", "Home");
            return View();
        }

        [HttpPost]
        public IActionResult Upload(Product product, IFormFile image)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            var role = HttpContext.Session.GetString("Role");
            
            if (userId == null || (role != "User" && role != "Admin" && role != "Artist"))
                return RedirectToAction("Index", "Home");

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

                product.ArtistId = userId;
                product.IsApproved = false;

                _context.products.Add(product);
                _context.SaveChanges();
                
                TempData["Success"] = "Artwork uploaded successfully! It is now pending admin approval.";
                return RedirectToAction("Index");
            }
            return View(product);
        }

        public IActionResult Ideas()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            var role = HttpContext.Session.GetString("Role");
            
            if (userId == null || (role != "User" && role != "Admin" && role != "Artist"))
                return RedirectToAction("Index", "Home");

            return View();
        }
        
        [HttpPost]
        public async Task<IActionResult> GenerateIdea([FromBody] GenerateImageRequest request)
        {
             // Mirrors AIController Logic for backend generation & saving
             try
            {
                if (string.IsNullOrWhiteSpace(request.Prompt))
                    return Json(new { success = false, error = "Invalid prompt" });

                var userId = HttpContext.Session.GetInt32("UserId");
                if (userId == null) return Json(new { success = false, error = "Unauthorized" });

                var client = _httpClientFactory.CreateClient();
                // Add user-agent
                client.DefaultRequestHeaders.Add("User-Agent", "ArtGalleryProject/1.0");

                var finalPrompt = request.Prompt;

                // 1. Generate Prompt if requested
                if (finalPrompt == "GENERATE_RANDOM")
                {
                    try 
                    {
                        var metaPrompt = "Write a creative, detailed, and artistic description for a digital artwork in one paragraph. Focus on visual details, lighting, and style. Do not include any intro/outro text.";
                        var textUrl = $"https://text.pollinations.ai/{Uri.EscapeDataString(metaPrompt)}?model=openai";
                        finalPrompt = await client.GetStringAsync(textUrl);
                        
                        // Fallback if API returns empty
                        if (string.IsNullOrWhiteSpace(finalPrompt)) 
                            finalPrompt = "A mysterious abstract masterpiece of colors and light";
                    }
                    catch 
                    {
                        finalPrompt = "A futuristic city in the clouds at sunset, vivid colors";
                    }
                }

                var apiKey = Environment.GetEnvironmentVariable("POLLINATION_API_KEY");

                // 2. Generate Image using the (generated) prompt
                // Construct Pollinations URL (Server-Side)
                var enhancedPrompt = finalPrompt + ", artistic concept, sketch idea, detailed illustration, masterpiece";
                var encodedPrompt = Uri.EscapeDataString(enhancedPrompt);
                var seed = new Random().Next(0, 1000000);
                var url = $"https://gen.pollinations.ai/image/{encodedPrompt}?model=flux&width=1024&height=576&seed={seed}&nologo=true"; // 16:9 ratio

                if (!string.IsNullOrEmpty(apiKey))
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                }

                var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    return Json(new { success = false, error = $"API Error: {response.StatusCode} - {errorContent}" });
                }

                var imageBytes = await response.Content.ReadAsByteArrayAsync();

                // Save Locally
                var webRoot = _webHostEnvironment.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                var folderPath = Path.Combine(webRoot, "images", "ai");
                
                if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);

                var fileName = $"idea_{Guid.NewGuid()}.jpg";
                var filePath = Path.Combine(folderPath, fileName);
                
                await System.IO.File.WriteAllBytesAsync(filePath, imageBytes);
                var localUrl = $"/images/ai/{fileName}";
                
                return Json(new { success = true, imageUrl = localUrl, prompt = finalPrompt });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        public IActionResult Delete(int id)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            var artwork = _context.products.FirstOrDefault(p => p.Id == id && p.ArtistId == userId);
            
            if (artwork != null)
            {
                _context.products.Remove(artwork);
                _context.SaveChanges();
                TempData["Success"] = "Artwork deleted.";
            }
            return RedirectToAction("Index");
        }
    }
}
