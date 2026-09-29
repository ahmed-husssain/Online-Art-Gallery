using Project.Models;
using Project.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Project.Hubs;     
using System.IO;

try
{
    var builder = WebApplication.CreateBuilder(args);

    // ----- Services -----
    builder.Services.AddControllersWithViews(options =>
    {
        options.Filters.Add<Project.Filters.GlobalViewDataFilter>();
    });
    builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));

    builder.Services.AddDbContext<MyContext>(options =>
        options.UseSqlServer(builder.Configuration.GetConnectionString("asd")));

    builder.Services.AddSession();
    builder.Services.AddSignalR();
    builder.Services.AddMemoryCache();

    DotNetEnv.Env.Load();

    builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = Microsoft.AspNetCore.Authentication.Google.GoogleDefaults.AuthenticationScheme;
    })
    .AddCookie()
    .AddGoogle(options =>
    {
        options.ClientId = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID") ?? "";
        options.ClientSecret = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET") ?? "";
        options.ClaimActions.MapJsonKey("urn:google:picture", "picture");

        options.Events.OnRedirectToAuthorizationEndpoint = context =>
        {
            context.Response.Redirect(context.RedirectUri + "&prompt=select_account");
            return Task.CompletedTask;
        };
    })
    .AddDiscord(options =>
    {
        options.ClientId = Environment.GetEnvironmentVariable("DISCORD_CLIENT_ID") ?? "";
        options.ClientSecret = Environment.GetEnvironmentVariable("DISCORD_CLIENT_SECRET") ?? "";
        options.Scope.Add("email");
    })
    .AddGitHub(options =>
    {
        options.ClientId = Environment.GetEnvironmentVariable("GITHUB_CLIENT_ID") ?? "";
        options.ClientSecret = Environment.GetEnvironmentVariable("GITHUB_CLIENT_SECRET") ?? "";
        options.Scope.Add("user:email");
    });

    builder.Services.Configure<EmailSetting>(builder.Configuration.GetSection("EmailSettings"));
    builder.Services.AddTransient<IEmailService, EmailService>();
    builder.Services.AddHttpClient();

    var app = builder.Build();

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Home/Error");
        app.UseHsts();
    }

    app.UseHttpsRedirection();
    app.UseStaticFiles();
    app.UseRouting();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseSession();

    app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}");

        app.MapHub<AuctionHub>("/auctionHub");
    app.Run();
}
catch (Exception ex)
{
    // Ensure the folder exists
    Directory.CreateDirectory("wwwroot/errorlogs");

    // Log the full exception
    File.WriteAllText("wwwroot/errorlogs/startup-error.txt", ex.ToString());

    // Re-throw so server still shows 500
    throw;
}