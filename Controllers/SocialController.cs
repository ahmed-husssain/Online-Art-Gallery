using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Project.Models;

namespace Project.Controllers
{
    public class SocialController : Controller
    {
        private readonly MyContext _db;
        private readonly IHttpClientFactory _httpClientFactory;

        public SocialController(MyContext db, IHttpClientFactory httpClientFactory)
        {
            _db = db;
            _httpClientFactory = httpClientFactory;
        }

        public IActionResult Index()
        {
            return RedirectToAction("Community");
        }

        public IActionResult Community()
        {
            if (HttpContext.Session.GetInt32("UserId") == null)
            {
                return RedirectToAction("Login", "Auth");
            }

            ViewBag.Exhibitions = _db.Exhibitions.Where(e => e.IsActive).OrderBy(e => e.Date).Take(3).ToList();
            return View();
        }

        public async Task<IActionResult> ExhibitionDetails(int id)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (!userId.HasValue) return RedirectToAction("Login", "Auth");

            var ex = await _db.Exhibitions.FindAsync(id);
            if (ex == null) return NotFound();

            return View(ex);
        }

        public IActionResult Feed()
        {
            if (HttpContext.Session.GetInt32("UserId") == null)
            {
                return RedirectToAction("Login", "Auth");
            }

            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetSocialFeed()
        {
            try
            {
                var userId = HttpContext.Session.GetInt32("UserId");
                var posts = await _db.SocialPosts
                    .AsNoTracking()
                    .Include(p => p.User)
                    .Include(p => p.Likes)
                    .Include(p => p.Comments)
                        .ThenInclude(c => c.User)
                    .OrderByDescending(p => p.CreatedAt)
                    .Take(20)
                    .AsSplitQuery()
                    .ToListAsync();

                var result = posts.Select(p => new
                {
                    p.Id,
                    p.ImageUrl,
                    p.Caption,
                    p.CreatedAt,
                    UserName = p.User?.Name ?? "Anonymous",
                    UserAvatar = p.User?.Avatar ?? "/images/default-avatar.png",
                    LikesCount = p.Likes.Count,
                    IsLiked = userId.HasValue && p.Likes.Any(l => l.UserId == userId.Value),
                    Comments = p.Comments.OrderBy(c => c.CreatedAt).Select(c => new
                    {
                        c.Id,
                        c.Content,
                        c.CreatedAt,
                        UserName = c.User?.Name ?? "Anonymous",
                        UserAvatar = c.User?.Avatar ?? "/images/default-avatar.png"
                    }).ToList(),
                    CanDelete = userId.HasValue && (p.UserId == userId.Value || HttpContext.Session.GetString("Role") == "Admin")
                }).ToList();

                return Json(result);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> PostToSocial([FromBody] PostToSocialRequest request)
        {
            try
            {
                var userId = HttpContext.Session.GetInt32("UserId");
                if (!userId.HasValue) return Json(new { success = false, error = "Please log in" });

                var post = new SocialPost
                {
                    UserId = userId.Value,
                    ImageUrl = request.ImageUrl,
                    Caption = request.Caption,
                    CreatedAt = DateTime.Now
                };

                _db.SocialPosts.Add(post);
                await _db.SaveChangesAsync();

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> LikePost(int postId)
        {
            try
            {
                var userId = HttpContext.Session.GetInt32("UserId");
                if (!userId.HasValue) return Json(new { success = false, error = "Please log in" });

                var existingLike = await _db.PostLikes
                    .FirstOrDefaultAsync(l => l.PostId == postId && l.UserId == userId.Value);

                if (existingLike != null)
                {
                    _db.PostLikes.Remove(existingLike);
                    await _db.SaveChangesAsync();
                    return Json(new { success = true, liked = false });
                }
                else
                {
                    var like = new PostLike
                    {
                        PostId = postId,
                        UserId = userId.Value
                    };
                    _db.PostLikes.Add(like);
                    await _db.SaveChangesAsync();
                    return Json(new { success = true, liked = true });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> AddComment([FromBody] AddCommentRequest request)
        {
            try
            {
                var userId = HttpContext.Session.GetInt32("UserId");
                if (!userId.HasValue) return Json(new { success = false, error = "Please log in" });

                if (string.IsNullOrWhiteSpace(request.Content))
                    return Json(new { success = false, error = "Comment cannot be empty" });

                var comment = new PostComment
                {
                    PostId = request.PostId,
                    UserId = userId.Value,
                    Content = request.Content,
                    CreatedAt = DateTime.Now
                };

                _db.PostComments.Add(comment);
                await _db.SaveChangesAsync();

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetExternalArt()
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Add("User-Agent", "ArtGalleryApp/1.0");
                
                // Fetching more to ensure we have enough with valid images
                var response = await client.GetAsync("https://api.artic.edu/api/v1/artworks?limit=30&fields=id,title,image_id,artist_display,place_of_origin&is_public=1");
                if (!response.IsSuccessStatusCode) return Json(new { success = false, error = "API unreachable" });

                var content = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                var iiifUrl = "https://www.artic.edu/iiif/2"; 
                if (root.TryGetProperty("config", out var configNode) && configNode.TryGetProperty("iiif_url", out var iiifNode))
                {
                    var apiIiif = iiifNode.GetString();
                    if (!string.IsNullOrEmpty(apiIiif)) 
                        iiifUrl = apiIiif.TrimEnd('/');
                }

                if (!root.TryGetProperty("data", out var dataNode) || dataNode.ValueKind != JsonValueKind.Array)
                    return Json(new { success = false, error = "Invalid format" });

                var result = dataNode.EnumerateArray()
                    .Where(a => a.TryGetProperty("image_id", out var imgId) && 
                               imgId.ValueKind == JsonValueKind.String && 
                               !string.IsNullOrEmpty(imgId.GetString()))
                    .Select(a => {
                        var imgId = a.GetProperty("image_id").GetString();
                        return new
                        {
                            Id = a.GetProperty("id").GetInt32(),
                            Title = a.TryGetProperty("title", out var t) ? t.GetString() : "Untitled Masterpiece",
                            Artist = a.TryGetProperty("artist_display", out var art) ? art.GetString() : "Unknown Artist",
                            // Using a smaller, more cached size (600px) and ensuring clean slashes
                            ImageUrl = $"{iiifUrl}/{imgId}/full/600,/0/default.jpg",
                            Source = "Art Institute of Chicago",
                            SourceAvatar = "https://www.artic.edu/assets/favicon-32x32.png"
                        };
                    })
                    .Take(12) // Limit to top 12 valid images
                    .ToList();

                return Json(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> DeletePost(int postId)
        {
            try
            {
                var userId = HttpContext.Session.GetInt32("UserId");
                if (!userId.HasValue) return Json(new { success = false, error = "Please log in" });

                var post = await _db.SocialPosts.FindAsync(postId);
                if (post == null) return Json(new { success = false, error = "Post not found" });

                var isAdmin = HttpContext.Session.GetString("Role") == "Admin";
                if (post.UserId != userId.Value && !isAdmin)
                {
                    return Json(new { success = false, error = "Unauthorized" });
                }

                _db.SocialPosts.Remove(post);
                await _db.SaveChangesAsync();

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }
    }

    public class PostToSocialRequest
    {
        public string ImageUrl { get; set; } = "";
        public string? Caption { get; set; }
    }

    public class AddCommentRequest
    {
        public int PostId { get; set; }
        public string Content { get; set; } = "";
    }
}
