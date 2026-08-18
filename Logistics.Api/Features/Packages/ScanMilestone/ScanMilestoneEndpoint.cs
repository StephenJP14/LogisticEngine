using FluentValidation;
using Logistics.Api.Common.Database;
using Logistics.Api.Common.Entities;
using Logistics.Api.Common.Models;
using Microsoft.EntityFrameworkCore;

namespace Logistics.Api.Features.Packages.ScanMilestone;

public record ScanMilestoneRequest(
    string TrackingNumber,
    Guid CurrentLocationHubId,
    PackageStatus NextStatus,
    string Notes,
    string ActorName
);

public class ScanMilestoneValidator : AbstractValidator<ScanMilestoneRequest>
{
    public ScanMilestoneValidator()
    {
        RuleFor(x => x.TrackingNumber).NotEmpty();
        RuleFor(x => x.CurrentLocationHubId).NotEmpty();
        RuleFor(x => x.ActorName).NotEmpty().WithMessage("Nama petugas scan wajib diisi.");
        RuleFor(x => x.NextStatus).IsInEnum();
    }
}

public static class ScanMilestoneEndpoint
{
    public static void MapScanMilestone(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/packages/scan", async (
            ScanMilestoneRequest req,
            IValidator<ScanMilestoneRequest> validator,
            AppDbContext db,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("ScanMilestone");

            var validationResult = await validator.ValidateAsync(req);
            if (!validationResult.IsValid)
            {
                return ApiResponse.ValidationError(validationResult.ToDictionary());
            }

            var package = await db.Packages
                .Include(p => p.Checkpoints)
                .FirstOrDefaultAsync(p => p.TrackingNumber == req.TrackingNumber);

            if (package == null)
            {
                return ApiResponse.Error($"Paket dengan resi {req.TrackingNumber} tidak ditemukan.", "PACKAGE_NOT_FOUND", StatusCodes.Status404NotFound);
            }

            var locationHub = await db.Hubs.FindAsync(req.CurrentLocationHubId);
            if (locationHub == null)
            {
                return ApiResponse.Error("Hub / Lokasi scan tidak valid.", "INVALID_LOCATION", StatusCodes.Status400BadRequest);
            }

            // Validasi State Transition sederhana (tidak boleh loncat sembarangan)
            if (req.NextStatus <= package.Status && req.NextStatus != PackageStatus.Damaged && req.NextStatus != PackageStatus.Lost)
            {
                return ApiResponse.Error($"Transisi status ilegal. Status sekarang '{package.Status}', tidak dapat kembali ke '{req.NextStatus}'.", "INVALID_STATUS_TRANSITION", StatusCodes.Status400BadRequest);
            }

            // Update Package state
            package.Status = req.NextStatus;
            package.CurrentHubId = req.CurrentLocationHubId;
            package.UpdatedAt = DateTime.UtcNow;

            // Append-Only Checkpoint
            var checkpoint = new TrackingCheckpoint
            {
                PackageId = package.Id,
                Status = req.NextStatus,
                LocationHubId = req.CurrentLocationHubId,
                LocationName = $"{locationHub.Name} ({locationHub.Code})",
                Notes = req.Notes,
                ActorName = req.ActorName,
                TimestampUtc = DateTime.UtcNow
            };

            db.TrackingCheckpoints.Add(checkpoint);
            await db.SaveChangesAsync();

            logger.LogInformation("Package {TrackingNumber} transitioned to status {Status} at {Location}",
                package.TrackingNumber, package.Status, locationHub.Name);

            return ApiResponse.Ok(new
            {
                message = "Status milestone berhasil diupdate.",
                trackingNumber = package.TrackingNumber,
                currentStatus = package.Status.ToString(),
                location = locationHub.Name,
                timestamp = checkpoint.TimestampUtc
            });
        });
    }
}