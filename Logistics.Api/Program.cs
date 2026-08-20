using System.Text;
using Amazon.S3;
using FluentValidation;
using Logistics.Api.Common.BackgroundServices;
using Logistics.Api.Common.Database;
using Logistics.Api.Common.Entities;
using Logistics.Api.Common.Hubs;
using Logistics.Api.Common.Middleware;
using Logistics.Api.Common.Storage;
using Logistics.Api.Common.Telemetry;
using Logistics.Api.Features.Auth.Login;
using Logistics.Api.Features.Fleet.GetLiveFleet;
using Logistics.Api.Features.Fleet.PingTelemetry;
using Logistics.Api.Features.Manifests.CompleteManifest;
using Logistics.Api.Features.Manifests.CreateManifest;
using Logistics.Api.Features.Manifests.LoadPackage;
using Logistics.Api.Features.Packages.CreatePackage;
using Logistics.Api.Features.Packages.GetTrackingHistory;
using Logistics.Api.Features.Packages.ReportDamaged;
using Logistics.Api.Features.Packages.ScanMilestone;
using Logistics.Api.Features.Packages.SubmitPod;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using StackExchange.Redis;
using System.Text.Json;
using Logistics.Api.Common.Models;
using Logistics.Api.Features.Users;
using Logistics.Api.Features.Vehicles;
using Logistics.Api.Common.Filters;

var builder = WebApplication.CreateBuilder(args);

// 1. Serilog Setup
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{CorrelationId}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

builder.Host.UseSerilog();

// 2. Database Setup (PostgreSQL)
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// 3. Redis Setup (Multiplexer)
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var redisConn = configuration["Redis:ConnectionString"] ?? "localhost:6379";
    return ConnectionMultiplexer.Connect(redisConn);
});

// 4. SignalR & Background Workers
builder.Services.AddSignalR();
builder.Services.AddSingleton<TelemetryBuffer>();
builder.Services.AddHostedService<TelemetryBatchWriterService>();
builder.Services.AddHostedService<NotificationConsumerService>();

// 5. MinIO / S3 Setup
builder.Services.AddSingleton<IAmazonS3>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var s3Config = new AmazonS3Config
    {
        ServiceURL = config["Minio:Endpoint"] ?? "http://localhost:9000",
        ForcePathStyle = true,
        UseHttp = true
    };
    return new AmazonS3Client(config["Minio:AccessKey"], config["Minio:SecretKey"], s3Config);
});
builder.Services.AddScoped<IStorageService, MinioStorageService>();

// 6. JWT Authentication & Authorization Setup
var jwtKey = builder.Configuration["Jwt:Key"] ?? "SuperSecretKeyForLogisticsEngineSecurity2026_MustBeLongEnough!";
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "LogisticsEngine",
        ValidAudience = builder.Configuration["Jwt:Audience"] ?? "LogisticsEngineApp",
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
    };

    // Baca Token dari Query String khusus untuk WebSocket SignalR
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;

            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/fleet"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        },
        OnChallenge = async context =>
        {
            context.HandleResponse();
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";

            var response = new ApiResponse<object>
            {
                Status = StatusCodes.Status401Unauthorized,
                Success = false,
                Message = "Autentikasi gagal. Bearer token tidak ditemukan atau sudah kadaluwarsa.",
                Code = "UNAUTHORIZED"
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(response, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            }));
        },
        OnForbidden = async context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";

            var response = new ApiResponse<object>
            {
                Status = StatusCodes.Status403Forbidden,
                Success = false,
                Message = "Akses ditolak. Peran (role) akun Anda tidak memiliki izin untuk tindakan ini.",
                Code = "FORBIDDEN"
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(response, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            }));
        }
    };
});

builder.Services.AddAuthorization();

// 7. Register FluentValidation Validators
builder.Services.AddValidatorsFromAssemblyContaining<LoginValidator>();
builder.Services.AddValidatorsFromAssemblyContaining<CreatePackageValidator>();
builder.Services.AddValidatorsFromAssemblyContaining<CreateManifestValidator>();
builder.Services.AddValidatorsFromAssemblyContaining<CompleteManifestValidator>();
builder.Services.AddValidatorsFromAssemblyContaining<PingTelemetryValidator>();
builder.Services.AddValidatorsFromAssemblyContaining<CreateUserValidator>();
builder.Services.AddValidatorsFromAssemblyContaining<CreateVehicleValidator>();

builder.Services.AddOpenApi();

var app = builder.Build();

// 8. Middleware Pipeline
app.UseMiddleware<CorrelationIdMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseAuthorization();

// 9. Seed Master Data (Hubs, Vehicles, Users)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();

    if (!db.Hubs.Any())
    {
        var dcPriok = new Hub
        {
            Code = "DC-JKT-PRIOK",
            Name = "Distribution Center Tanjung Priok",
            Type = FacilityType.DistributionCenter,
            Address = "Jl. Pelabuhan No. 1, Jakarta Utara",
            Latitude = -6.107,
            Longitude = 106.884
        };

        var hubTangerang = new Hub
        {
            Code = "HUB-TNG-01",
            Name = "Transit Hub Tangerang",
            Type = FacilityType.TransitHub,
            Address = "Jl. Daan Mogot KM 19, Tangerang",
            Latitude = -6.178,
            Longitude = 106.631
        };

        var storeSerang = new Hub
        {
            Code = "STR-SRG-01",
            Name = "Gerai Retail Serang Kota",
            Type = FacilityType.RetailStore,
            Address = "Jl. Ahmad Yani No. 88, Serang",
            Latitude = -6.120,
            Longitude = 106.150
        };

        db.Hubs.AddRange(dcPriok, hubTangerang, storeSerang);
        db.SaveChanges();
    }

    if (!db.Vehicles.Any())
    {
        db.Vehicles.AddRange(
            new Vehicle { PlateNumber = "B-9999-XYZ", ModelType = "Tronton Box", MaxWeightCapacityKg = 15000 },
            new Vehicle { PlateNumber = "B-1111-IDL", ModelType = "BlindVan GrandMax", MaxWeightCapacityKg = 800 },
            new Vehicle { PlateNumber = "B-7777-RET", ModelType = "Engkel Box", MaxWeightCapacityKg = 2500 }
        );
        db.SaveChanges();
    }

    if (!db.Users.Any())
    {
        var hashedPassword = BCrypt.Net.BCrypt.HashPassword("password123");
        db.Users.AddRange(
            new User { Username = "admin", PasswordHash = hashedPassword, FullName = "Bapak Direktur", Role = UserRole.SystemAdmin },
            new User { Username = "dispatcher.priok", PasswordHash = hashedPassword, FullName = "Siti Dispatcher", Role = UserRole.Dispatcher },
            new User { Username = "warehouse.staff", PasswordHash = hashedPassword, FullName = "Joko Warehouse", Role = UserRole.WarehouseStaff },
            new User { Username = "anton.driver", PasswordHash = hashedPassword, FullName = "Pak Anton", Role = UserRole.Driver },
            new User { Username = "serang.manager", PasswordHash = hashedPassword, FullName = "Ibu Siti Store Manager", Role = UserRole.StoreManager }
        );
        db.SaveChanges();
    }
}

// 10. Map Endpoints dengan RBAC Protection

// Public Endpoints (Tanpa Token JWT)
app.MapLogin();
app.MapGetTrackingHistory(); // Customer tracking publik
app.MapGet("/api/hubs", async (AppDbContext db) => await db.Hubs.ToListAsync()).AllowAnonymous();

// --- PROTECTED GROUPS ---

// Group 1: Warehouse & Admin
var warehouseGroup = app.MapGroup("")
                                    .RequireAuthorization(p => p.RequireRole("WarehouseStaff", "SystemAdmin"))
                                    .AddIdempotency();
warehouseGroup.MapCreatePackage();
warehouseGroup.MapLoadPackage();

// Group 2: Dispatcher & Admin
var dispatcherGroup = app.MapGroup("")
                                    .RequireAuthorization(p => p.RequireRole("Dispatcher", "SystemAdmin"))
                                    .AddIdempotency();
dispatcherGroup.MapCreateManifest();

// Group 2B: GET MapGetLiveFleet (Tanpa Idempotency, karena GET tidak perlu idempotent)
var liveFleetGroup = app.MapGroup("")
                                    .RequireAuthorization(p => p.RequireRole("Dispatcher", "SystemAdmin"));
liveFleetGroup.MapGetLiveFleet();

// Group 3: Dispatcher, StoreManager & Admin
var completeManifestGroup = app.MapGroup("")
                                    .RequireAuthorization(p => p.RequireRole("Dispatcher", "StoreManager", "SystemAdmin"))
                                    .AddIdempotency();
completeManifestGroup.MapCompleteManifest();

// Group 4: Warehouse, Dispatcher, StoreManager & Admin
var scanGroup = app.MapGroup("")
                            .RequireAuthorization(p => p.RequireRole("WarehouseStaff", "Dispatcher", "StoreManager", "SystemAdmin"))
                            .AddIdempotency();
scanGroup.MapScanMilestone();

// Group 5: Driver, StoreManager & Admin
var podGroup = app.MapGroup("")
                            .RequireAuthorization(p => p.RequireRole("Driver", "StoreManager", "SystemAdmin"))
                            .AddIdempotency();
podGroup.MapSubmitPod();

// Group 6: Warehouse, Driver, StoreManager & Admin
var damageGroup = app.MapGroup("")
                            .RequireAuthorization(p => p.RequireRole("WarehouseStaff", "Driver", "StoreManager", "SystemAdmin"))
                            .AddIdempotency();
damageGroup.MapReportDamaged();

// Group 7: Driver & Admin
var driverGroup = app.MapGroup("").RequireAuthorization(p => p.RequireRole("Driver", "SystemAdmin"));
driverGroup.MapPingTelemetry();

// Group 8: SystemAdmin Only
var adminGroup = app.MapGroup("").RequireAuthorization(p => p.RequireRole("SystemAdmin"));
adminGroup.MapUsersEndpoints();
adminGroup.MapVehiclesEndpoints();

// SignalR Hub Map (Hanya Dispatcher dan Admin yang boleh memantau Live Peta)
app.MapHub<FleetTrackingHub>("/hubs/fleet")
    .RequireAuthorization(p => p.RequireRole("Dispatcher", "SystemAdmin"));

app.Run();