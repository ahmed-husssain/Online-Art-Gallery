using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;
using Project.Models;
using Microsoft.AspNetCore.Hosting;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System;

namespace Project.Controllers
{
    public class AIController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly MyContext _db;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public AIController(IHttpClientFactory httpClientFactory, MyContext db, IWebHostEnvironment webHostEnvironment)
        {
            _httpClientFactory = httpClientFactory;
            _db = db;
            _webHostEnvironment = webHostEnvironment;
        }

        public IActionResult Index()
        {
            if (HttpContext.Session.GetInt32("UserId") == null)
            {
                return RedirectToAction("Login", "Auth");
            }

            return View();
        }

        [HttpPost]
        public async Task<IActionResult> GenerateImage([FromBody] GenerateImageRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Prompt))
                {
                    return Json(new { success = false, error = "Please enter a prompt" });
                }

                var apiKey = Environment.GetEnvironmentVariable("POLLINATION_API_KEY");
                if (string.IsNullOrEmpty(apiKey))
                {
                    // Fallback or error if key is missing. 
                    // Given the user specifically asked to fix the key usage, we should error or log.
                    Console.WriteLine("POLLINATION_API_KEY is missing from environment variables.");
                }

                string enhancedPrompt = request.Prompt + ", artistic style, digital art, stylized illustration, creative masterpiece, vibrant color palette, painterly texture, avoid photorealism";
                var encodedPrompt = Uri.EscapeDataString(enhancedPrompt);
                
                // Using the 'flux' model as requested, with random seed for variety
                var seed = new Random().Next(0, 1000000);
                var url = $"https://gen.pollinations.ai/image/{encodedPrompt}?model=flux&width=1024&height=1024&seed={seed}&nologo=true";

                var client = _httpClientFactory.CreateClient();
                if (!string.IsNullOrEmpty(apiKey))
                {
                    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                }

                var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    return Json(new { success = false, error = $"API Error: {response.StatusCode} - {errorContent}" });
                }

                var imageBytes = await response.Content.ReadAsByteArrayAsync();

                // Save locally
                var webRoot = _webHostEnvironment.WebRootPath;
                // Ensure webRoot is not null (it might be in some test envs, but rarely in web apps)
                if (string.IsNullOrEmpty(webRoot)) 
                {
                     webRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                }

                var folderPath = Path.Combine(webRoot, "images", "ai");
                if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);

                var fileName = $"ai_{Guid.NewGuid()}.jpg";
                var filePath = Path.Combine(folderPath, fileName);
                
                await System.IO.File.WriteAllBytesAsync(filePath, imageBytes);
                
                var localUrl = $"/images/ai/{fileName}";

                bool saved = SaveToGallery(localUrl, request.Prompt, out string dbError);

                return Json(new { 
                    success = true, 
                    imageUrl = localUrl, 
                    dbSaved = saved, 
                    dbError = dbError 
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception: {ex.Message}");
                return Json(new { success = false, error = $"An error occurred: {ex.Message}" });
            }
        }

        private bool SaveToGallery(string imageUrl, string prompt, out string error)
        {
            error = "";
            try 
            {
                var userId = HttpContext.Session.GetInt32("UserId");
                if (!userId.HasValue)
                {
                    error = "Session 'UserId' is missing. Please log in again.";
                    return false;
                }

                var newArt = new AIGeneratedArt
                {
                    UserId = userId.Value,
                    ImageUrl = imageUrl,
                    Prompt = prompt,
                    CreatedAt = DateTime.Now
                };

                _db.aiGeneratedArts.Add(newArt);
                _db.SaveChanges();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.InnerException?.Message ?? ex.Message;
                Console.WriteLine($"Error saving gallery: {error}");
                return false;
            }
        }

        [HttpGet]
        public IActionResult GetGallery()
        {
            try
            {
                var userId = HttpContext.Session.GetInt32("UserId");
                if (!userId.HasValue) return Json(new List<AIGeneratedArt>());

                var gallery = _db.aiGeneratedArts
                    .Where(a => a.UserId == userId.Value)
                    .OrderByDescending(a => a.CreatedAt)
                    .Take(20)
                    .ToList();

                return Json(gallery);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting gallery: {ex.Message}");
                return Json(new List<AIGeneratedArt>());
            }
        }
    }

    public class GenerateImageRequest
    {
        public string Prompt { get; set; } = "";
    }
}
