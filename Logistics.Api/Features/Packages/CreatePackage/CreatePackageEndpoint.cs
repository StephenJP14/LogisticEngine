using FluentValidation;
using Logistics.Api.Common.Database;
using Logistics.Api.Common.Entities;
using Logistics.Api.Common.Models;
using Microsoft.EntityFrameworkCore;

namespace Logistics.Api.Features.Packages.CreatePackage;

public record CreatePackageRequest(
    Guid OriginHubId,
    Guid DestinationHubId,
    decimal WeightKg,
    string ItemDescription,
    bool IsFragile
);

public record CreatePackageResponse(
    Guid Id,
    string TrackingNumber,
    string Status,
    DateTime CreatedAt
);

public class CreatePackageValidator : AbstractValidator<CreatePackageRequest>
{
    public CreatePackageValidator()
    {
        RuleFor(x => x.OriginHubId).NotEmpty().WithMessage("Origin Hub wajib diisi.");
        RuleFor(x => x.DestinationHubId).NotEmpty().WithMessage("Destination Hub wajib diisi.");
        RuleFor(x => x.WeightKg).GreaterThan(0).WithMessage("Berat paket harus lebih dari 0 kg.");
        RuleFor(x => x.ItemDescription).NotEmpty().MaximumLength(250);
    }
}

public static class CreatePackageEndpoint
{
    public static void MapCreatePackage(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/packages", async (
            CreatePackageRequest req,
            IValidator<CreatePackageRequest> validator,
            AppDbContext db,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("CreatePackage");

            var validationResult = await validator.ValidateAsync(req);
            if (!validationResult.IsValid)
            {
                return ApiResponse.ValidationError(validationResult.ToDictionary());
            }

            var originExists = await db.Hubs.AnyAsync(h => h.Id == req.OriginHubId);
            var destExists = await db.Hubs.AnyAsync(h => h.Id == req.DestinationHubId);

            if (!originExists || !destExists)
            {
                return ApiResponse.Error("Origin atau Destination Hub tidak valid.", "INVALID_HUB", StatusCodes.Status400BadRequest);
            }

            // Generate Tracking Number unik: AWB-YYYYMMDD-RandomHex
            var trackingNumber = $"AWB-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpper()}";

            var package = new Package
            {
                TrackingNumber = trackingNumber,
                OriginHubId = req.OriginHubId,
                DestinationHubId = req.DestinationHubId,
                CurrentHubId = req.OriginHubId,
                WeightKg = req.WeightKg,
                ItemDescription = req.ItemDescription,
                IsFragile = req.IsFragile,
                Status = PackageStatus.Created
            };

            // Tambahkan Initial Checkpoint Log
            package.Checkpoints.Add(new TrackingCheckpoint
            {
                Status = PackageStatus.Created,
                LocationHubId = req.OriginHubId,
                LocationName = "Origin Distribution Center",
                Notes = "Paket berhasil didaftarkan dan menunggu proses packing.",
                ActorName = "System Registration"
            });

            db.Packages.Add(package);
            await db.SaveChangesAsync();

            logger.LogInformation("Package created successfully with Tracking Number: {TrackingNumber}", package.TrackingNumber);

            var responseData = new CreatePackageResponse(
                package.Id,
                package.TrackingNumber,
                package.Status.ToString(),
                package.CreatedAt
            );

            return ApiResponse.Created(
                data: responseData,
                message: "Paket berhasil didaftarkan.",
                locationUri: $"/api/packages/{package.TrackingNumber}"
            );
        });
    }
}