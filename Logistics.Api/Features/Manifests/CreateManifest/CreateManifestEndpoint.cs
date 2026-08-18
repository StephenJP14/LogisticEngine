using FluentValidation;
using Logistics.Api.Common.Database;
using Logistics.Api.Common.Entities;
using Logistics.Api.Common.Models;
using Logistics.Api.Common.Utils;
using Microsoft.EntityFrameworkCore;

namespace Logistics.Api.Features.Manifests.CreateManifest;

public record CreateManifestRequest(
    Guid OriginHubId,
    Guid DestinationHubId,
    string DriverName,
    string VehiclePlate
);

public class CreateManifestValidator : AbstractValidator<CreateManifestRequest>
{
    public CreateManifestValidator()
    {
        RuleFor(x => x.OriginHubId).NotEmpty().WithMessage("Origin Hub wajib diisi.");
        RuleFor(x => x.DestinationHubId).NotEmpty().WithMessage("Destination Hub wajib diisi.");
        RuleFor(x => x.DriverName).NotEmpty().WithMessage("Nama supir wajib diisi.");
        RuleFor(x => x.VehiclePlate).NotEmpty().WithMessage("Plat nomor kendaraan wajib diisi.");
    }
}

public static class CreateManifestEndpoint
{
    public static void MapCreateManifest(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/manifests", async (
            CreateManifestRequest req,
            IValidator<CreateManifestRequest> validator,
            AppDbContext db) =>
        {
            var validationResult = await validator.ValidateAsync(req);
            if (!validationResult.IsValid)
            {
                return ApiResponse.ValidationError(validationResult.ToDictionary());
            }

            // 1. DATA CLEANING & NORMALIZATION
            var normalizedPlate = VehicleNormalizer.NormalizePlate(req.VehiclePlate);

            if (!VehicleNormalizer.IsValidStandardPlate(normalizedPlate))
            {
                return ApiResponse.Error($"Format plat nomor '{req.VehiclePlate}' tidak valid.", "INVALID_PLATE_FORMAT", StatusCodes.Status400BadRequest);
            }

            // 2. MASTER DATA VALIDATION
            var vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.PlateNumber == normalizedPlate && v.IsActive);
            if (vehicle == null)
            {
                return ApiResponse.Error($"Kendaraan dengan plat {normalizedPlate} tidak terdaftar atau tidak aktif.", "VEHICLE_NOT_FOUND", StatusCodes.Status404NotFound);
            }

            var originHubExists = await db.Hubs.AnyAsync(h => h.Id == req.OriginHubId);
            var destHubExists = await db.Hubs.AnyAsync(h => h.Id == req.DestinationHubId);
            if (!originHubExists || !destHubExists)
            {
                return ApiResponse.Error("Origin atau Destination Hub tidak valid.", "INVALID_HUB", StatusCodes.Status400BadRequest);
            }

            var today = DateTime.UtcNow.ToString("yyyyMMdd");
            var randomHex = Guid.NewGuid().ToString()[..6].ToUpper();
            var manifestNumber = $"MNF-{today}-{randomHex}";

            var manifest = new Manifest
            {
                ManifestNumber = manifestNumber,
                OriginHubId = req.OriginHubId,
                DestinationHubId = req.DestinationHubId,
                DriverName = req.DriverName,
                VehiclePlate = normalizedPlate,
                Status = ManifestStatus.Draft
            };

            db.Manifests.Add(manifest);
            await db.SaveChangesAsync();

            return ApiResponse.Created(
                data: new
                {
                    id = manifest.Id,
                    manifestNumber = manifest.ManifestNumber,
                    vehicleModel = vehicle.ModelType,
                    vehiclePlate = normalizedPlate
                },
                message: "Surat jalan (manifest) berhasil dibuat.",
                locationUri: $"/api/manifests/{manifest.ManifestNumber}"
            );
        });
    }
}