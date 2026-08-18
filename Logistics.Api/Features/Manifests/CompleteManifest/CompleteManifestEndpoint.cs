using FluentValidation;
using Logistics.Api.Common.Database;
using Logistics.Api.Common.Entities;
using Logistics.Api.Common.Models;
using Microsoft.EntityFrameworkCore;

namespace Logistics.Api.Features.Manifests.CompleteManifest;

public record CompleteManifestRequest(
    Guid UnloadLocationHubId,
    List<string> ScannedTrackingNumbers,
    string ActorName
);

public class CompleteManifestValidator : AbstractValidator<CompleteManifestRequest>
{
    public CompleteManifestValidator()
    {
        RuleFor(x => x.UnloadLocationHubId).NotEmpty();
        RuleFor(x => x.ActorName).NotEmpty();
        RuleFor(x => x.ScannedTrackingNumbers).NotNull();
    }
}

public static class CompleteManifestEndpoint
{
    public static void MapCompleteManifest(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/manifests/{manifestNumber}/complete", async (
            string manifestNumber,
            CompleteManifestRequest req,
            IValidator<CompleteManifestRequest> validator,
            AppDbContext db,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("CompleteManifest");

            var validationResult = await validator.ValidateAsync(req);
            if (!validationResult.IsValid) return ApiResponse.ValidationError(validationResult.ToDictionary());

            var manifest = await db.Manifests
                .Include(m => m.Items)
                .ThenInclude(i => i.Package)
                .FirstOrDefaultAsync(m => m.ManifestNumber == manifestNumber);

            if (manifest == null) return ApiResponse.Error("Manifest tidak ditemukan.", "MANIFEST_NOT_FOUND", StatusCodes.Status404NotFound);
            if (manifest.Status != ManifestStatus.Draft && manifest.Status != ManifestStatus.Dispatched)
                return ApiResponse.Error("Manifest sudah selesai sebelumnya.", "MANIFEST_ALREADY_COMPLETED", StatusCodes.Status400BadRequest);

            if (manifest.DestinationHubId != req.UnloadLocationHubId)
                return ApiResponse.Error("Lokasi bongkar muat tidak sesuai dengan tujuan Manifest.", "INVALID_UNLOAD_LOCATION", StatusCodes.Status400BadRequest);

            int missingCount = 0;
            var receivedAt = DateTime.UtcNow;

            // Proses Rekonsiliasi (Pencocokan data sistem vs data fisik yang di-scan)
            foreach (var item in manifest.Items)
            {
                var isScannedPhysically = req.ScannedTrackingNumbers.Contains(item.Package.TrackingNumber);

                if (isScannedPhysically)
                {
                    // 1. Paket Aman (Ada di sistem, ada wujud fisiknya)
                    item.Package.Status = PackageStatus.ReceivedAtHub;
                    item.Package.CurrentHubId = req.UnloadLocationHubId;
                    item.Package.UpdatedAt = receivedAt;

                    db.TrackingCheckpoints.Add(new TrackingCheckpoint
                    {
                        PackageId = item.PackageId,
                        Status = PackageStatus.ReceivedAtHub,
                        LocationHubId = req.UnloadLocationHubId,
                        LocationName = "Inbound Dock",
                        Notes = $"Tiba dari manifest {manifest.ManifestNumber}.",
                        ActorName = req.ActorName,
                        TimestampUtc = receivedAt
                    });
                }
                else
                {
                    // 2. Paket Hilang (Ada di sistem truk, tapi wujud fisiknya tidak di-scan)
                    item.IsMissingAtArrival = true;
                    item.Package.Status = PackageStatus.Lost;
                    item.Package.UpdatedAt = receivedAt;
                    missingCount++;

                    db.TrackingCheckpoints.Add(new TrackingCheckpoint
                    {
                        PackageId = item.PackageId,
                        Status = PackageStatus.Lost,
                        LocationHubId = req.UnloadLocationHubId,
                        LocationName = "System Reconciliation",
                        Notes = $"DISCREPANCY: Paket hilang saat pembongkaran manifest {manifest.ManifestNumber}.",
                        ActorName = "System",
                        TimestampUtc = receivedAt
                    });

                    logger.LogWarning("Discrepancy detected: Package {TrackingNumber} is missing from Manifest {ManifestNumber}",
                        item.Package.TrackingNumber, manifest.ManifestNumber);
                }
            }

            // Tutup Manifest
            manifest.Status = missingCount > 0 ? ManifestStatus.CompletedWithDiscrepancy : ManifestStatus.CompletedClean;
            manifest.ArrivedAt = receivedAt;

            await db.SaveChangesAsync();

            return ApiResponse.Ok(new
            {
                message = "Manifest berhasil ditutup.",
                manifestStatus = manifest.Status.ToString(),
                totalExpected = manifest.Items.Count,
                totalReceived = manifest.Items.Count - missingCount,
                totalMissing = missingCount
            });
        });
    }
}