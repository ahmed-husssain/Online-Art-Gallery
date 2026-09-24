# 🎨 Online Art Gallery & Live Auction Marketplace

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)
![C#](https://img.shields.io/badge/C%23-239120?style=for-the-badge&logo=c-sharp&logoColor=white)
![Entity Framework Core](https://img.shields.io/badge/EF%20Core-8.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)
![SQL Server](https://img.shields.io/badge/SQL%20Server-CC292B?style=for-the-badge&logo=microsoft-sql-server&logoColor=white)
![Architecture](https://img.shields.io/badge/Architecture-ASP.NET%20MVC-blue?style=for-the-badge)

A high-performance luxury art marketplace and live auction platform built with **ASP.NET Core 8 MVC**, **Entity Framework Core 8**, and **SQL Server**. Features a bespoke obsidian/cyber-indigo aesthetic, real-time bid processing, multi-provider OAuth, RFC 6238 TOTP two-factor authentication, and an artist studio.

> 🚀 **GitHub Repository**: [https://github.com/ahmed-husssain/Online-Art-Gallery](https://github.com/ahmed-husssain/Online-Art-Gallery)

---

## ⚡ Building in Public: Backend Performance Sprint Series

This codebase serves as the live foundation for a public **backend systems engineering sprint series**. Rather than presenting a static portfolio demo, this project is being stress-tested, profiled, and refactored every 3 to 4 days to solve real-world distributed bottlenecks.

### 📊 Day 0 Empirical Baseline (`GET /Product`):
The following metrics were measured on a clean local run against the live ASP.NET Core Kestrel server (`http://localhost:5240/Product`) querying SQL Server:

| Metric | Measured Day 0 Baseline | Sprint 1 Optimization Target |
| :--- | :--- | :--- |
| **Cold Start Request** | **`939.9 ms`** | Slash as much as possible (< 25 ms) |
| **Average Response Latency** | **`352.8 ms`** | Single-digit ms target |
| **Max Contention Spike** | **`1,298.1 ms`** | Contention eliminated |
| **EF Core `DbCommand` Execution** | **`70 ms`** (Cold table scan) | `0 ms` (100% offloaded to cache on hits) |
| **In-Memory Cache Lookup** | *N/A (Direct DB query)* | **`0.246 ms`** (Measured in RAM) |

### 🧪 Reproduce the Benchmark Locally:
You can verify these numbers on your own machine in 15 seconds:
```powershell
# 1. Start the application
dotnet run --urls="http://localhost:5240"

# 2. In another terminal, run the benchmark suite
powershell -ExecutionPolicy Bypass -File .\run_clean_benchmark.ps1
```

### 🗺️ The 4-Day Public Sprint Roadmap:
* **Day 04 (Sprint 1)**: Query Optimization & Redis Cache-Aside &mdash; Slashing catalog latency from 352.8 ms down as close to zero as possible.
* **Day 08 (Sprint 2)**: Auction Concurrency & Race Conditions &mdash; Simulating 500 simultaneous bids; benchmarking pessimistic row locks vs. EF Core `[Timestamp] RowVersion` optimistic concurrency tokens.
* **Day 12 (Sprint 3)**: 100,000 Row Database Indexing &mdash; Seeding 100k artworks; profiling clustered vs covering non-clustered index seek execution plans.
* **Day 16 (Sprint 4)**: Modular Monolith & Outbox Pattern &mdash; Decoupling Auction, Catalog, and Order boundaries via MediatR domain events with transactional outbox guarantees.

---

## 🏗️ Architecture & Design Patterns

The application follows the **ASP.NET Core Model-View-Controller (MVC)** architectural pattern, structured for clean separation of concerns and maintainability.

```
                      ┌───────────────────────────────────────────────┐
                      │              Incoming Request                 │
                      │             (GET /Product/Index)              │
                      └──────────────────────┬────────────────────────┘
                                             │
                       Middleware Pipeline (Auth, Session, Routing)
                                             │
                                             ▼
                      ┌───────────────────────────────────────────────┐
                      │                 CONTROLLER                    │
                      │    Controllers/ProductController.cs           │
                      │    (Handles HTTP, parameters, business logic) │
                      └──────────────┬─────────────────▲──────────────┘
                                     │                 │
              Queries & Updates      │                 │ Passes Model / DTO
                                     ▼                 │
                      ┌─────────────────────────┐      │
                      │          MODEL          │──────┘
                      │  Models/Product.cs      │
                      │  Models/MyContext.cs    │
                      │  (EF Core Unit of Work) │
                      └──────────────┬──────────┘
                                     │
                             Translates to SQL
                                     ▼
                      ┌─────────────────────────┐
                      │       SQL SERVER        │
                      │    OnlineArtGalleryDb   │
                      └─────────────────────────┘
                                     │
                                     │ Returns HTML
                                     ▼
                      ┌───────────────────────────────────────────────┐
                      │                  VIEW (UI)                    │
                      │    Views/Product/Index.cshtml                 │
                      │    (Razor syntax + Server-side HTML render)   │
                      └───────────────────────────────────────────────┘
```

### Key Design Patterns Implemented:

1. **Model-View-Controller (MVC)**:
   - **Models** (`/Models`): Domain entities (`Product`, `User`, `Bid`, `Order`, `Review`) and EF Core DbContext.
   - **Views** (`/Views`): Server-rendered Razor views styled with custom CSS.
   - **Controllers** (`/Controllers`): Coordinate HTTP request flow, session state, authorization, and view rendering.
2. **Repository & Unit of Work Pattern**:
   - `MyContext : DbContext` acts as the **Unit of Work**, coordinating transactional persistence.
   - `DbSet<Product>`, `DbSet<Bid>`, etc. act as **Repositories**, abstracting SQL table queries into strongly-typed LINQ queries.
3. **Dependency Injection (IoC)**:
   - Configured in `Program.cs`. Services such as `MyContext`, `IEmailService`, and `HttpClient` are registered into the `IServiceCollection` and injected via controller constructors (`public ProductController(MyContext context)`).
4. **Middleware Pipeline Pattern**:
   - The ASP.NET Core request pipeline operates on a chain of responsibility:
     `UseHttpsRedirection()` ➔ `UseStaticFiles()` ➔ `UseRouting()` ➔ `UseAuthentication()` ➔ `UseAuthorization()` ➔ `UseSession()`.
5. **Action Filter Pattern**:
   - `Project.Filters.GlobalViewDataFilter` globally injects cart counts, notifications, and navigation state before views render.
6. **Options Pattern**:
   - Strongly-typed configuration binding (`EmailSetting`) maps sections from `appsettings.json` into injectable POCOs.

---

## 🗂️ Project Directory Structure

```
E-Project/
├── Controllers/                 # MVC Controllers handling HTTP endpoints
│   ├── AdminController.cs       # Administrative curation & approvals
│   ├── AIController.cs          # AI art generation integration
│   ├── ArtistController.cs     # Artist dashboard & portfolio management
│   ├── AuthController.cs       # Identity, OAuth2, and TOTP 2FA
│   ├── CartController.cs       # Shopping cart & checkout flow
│   ├── GalleryController.cs    # User collections & wishlist
│   ├── HomeController.cs       # Landing page, settings, exhibitions
│   ├── ProductController.cs    # Catalog browsing, filtering & live bidding
│   ├── SocialController.cs     # Community feed & social comments
│   └── WishlistController.cs   # Saved artworks management
├── Models/                      # EF Core Entities & Database Context
│   ├── Bid.cs                  # Auction bids & timestamps
│   ├── MyContext.cs            # EF Core DbContext (Unit of Work)
│   ├── Product.cs              # Artwork catalog, pricing & auction state
│   ├── User.cs                 # User accounts, hashed passwords, roles
│   └── ...                     # CartItem, Exhibition, Feedback, Review
├── Views/                       # Razor Views (.cshtml)
│   ├── Home/                   # Hero landing, about, settings
│   ├── Product/                # Marketplace catalog & artwork details
│   ├── Artist/                 # Artist creation studio
│   ├── Shared/                 # Layouts, navigation, partials
│   └── ...
├── Services/                    # Domain services & external clients
│   ├── EmailService.cs         # SMTP notification engine
│   └── IEmailService.cs        # Contract interface
├── wwwroot/                     # Static Web Assets
│   ├── css/                    # Custom CSS styling (dark obsidian theme)
│   ├── images/products/        # Seeded artwork images
│   └── images/carousel/        # High-res carousel presentation assets
├── benchmark_live_api.ps1       # Comprehensive API benchmarking suite
├── run_clean_benchmark.ps1      # 10-run reproducibility latency test
├── test_cache_speed.ps1         # RAM in-memory lookup vs DB test
└── Program.cs                   # Application entry point & service composition
```

---

## 📸 Visual Showcase

| View | Preview | Description |
| :--- | :--- | :--- |
| **Hero Landing** | `screen_home.png` | Dark obsidian hero with glowing typography and interactive artwork showcase. |
| **Marketplace Catalog** | `screen_gallery.png` | Filterable artwork grid with dynamic pricing, categories, and auction tags. |
| **Security & 2FA** | `screen_settings.png` | Profile sanctuary featuring RFC 6238 TOTP authenticator setup. |
| **Brand Craftsmanship** | `screen_about.png` | Philosophy, exhibition announcements, and platform narrative. |
| **Artist Studio** | `screen_artist.png` | Artist portfolio studio for publishing, managing, and tracking sales. |

---

## ⚙️ Quick Start & Local Setup

### Prerequisites
* [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
* [SQL Server](https://www.microsoft.com/sql-server/) (or LocalDB / SQLEXPRESS)

### 1. Clone the Repository
```bash
git clone https://github.com/ahmed-husssain/Online-Art-Gallery.git
cd Online-Art-Gallery
```

### 2. Configure Database Connection
Update the connection string in `appsettings.Development.json` (or `appsettings.json`) to point to your SQL Server instance:
```json
{
  "ConnectionStrings": {
    "asd": "Server=.\\SQLEXPRESS;Database=OnlineArtGalleryDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True;"
  }
}
```

### 3. Apply Migrations & Update Database
```bash
dotnet ef database update
```

### 4. Run the Application
```bash
dotnet run
```
Navigate to `http://localhost:5240` in your browser.

---

## 👥 User Roles & Access Control

* **Collector (User)**: Browse artwork catalog, filter by price/category, participate in live timed auctions, checkout via cart, and configure 2FA security.
* **Artist**: Everything a Collector can do, plus dedicated access to the **Artist Studio** to upload creations, set auction reserves, and manage artwork listings.
* **Administrator**: Platform governance, artwork curation approvals, user management, and order audit fulfillment.

---

## 📄 License & Author

Developed by **Ahmed Hussain**. Built in public to document real-world distributed backend engineering and performance optimization.

* GitHub: [@ahmed-husssain](https://github.com/ahmed-husssain)
* Project Repo: [Online-Art-Gallery](https://github.com/ahmed-husssain/Online-Art-Gallery)
