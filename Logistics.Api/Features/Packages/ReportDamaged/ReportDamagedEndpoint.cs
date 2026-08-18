using Logistics.Api.Common.Database;
using Logistics.Api.Common.Entities;
using Logistics.Api.Common.Storage;
using Logistics.Api.Common.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Logistics.Api.Features.Packages.ReportDamaged;

public static class ReportDamagedEndpoint
{
    public static void MapReportDamaged(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/packages/{trackingNumber}/damage-report", async (
            string trackingNumber,
            [FromForm] string reporterName,
            [FromForm] Guid locationHubId,
            [FromForm] string damageDescription,
            [FromForm] DamageAction actionTaken,
            IFormFile photo,
            AppDbContext db,
            IStorageService storageService) =>
        {
            if (string.IsNullOrWhiteSpace(reporterName) || photo == null || photo.Length == 0)
            {
                return ApiResponse.Error("Nama pelapor dan foto bukti kerusakan wajib diisi.", "INVALID_INPUT", StatusCodes.Status400BadRequest);
            }

            var package = await db.Packages
                .Include(p => p.OriginHub)
                .Include(p => p.DestinationHub)
                .FirstOrDefaultAsync(p => p.TrackingNumber == trackingNumber);

            if (package == null) return ApiResponse.Error("Paket tidak ditemukan.", "PACKAGE_NOT_FOUND", StatusCodes.Status404NotFound);

            var locationHub = await db.Hubs.FindAsync(locationHubId);
            if (locationHub == null) return ApiResponse.Error("Lokasi pelaporan (Hub ID) tidak valid.", "INVALID_HUB_ID", StatusCodes.Status400BadRequest);

            // Upload foto kerusakan ke MinIO
            using var stream = photo.OpenReadStream();
            var photoUrl = await storageService.UploadFileAsync(stream, photo.FileName, photo.ContentType);

            string? replacementTrackingNumber = null;
            var now = DateTime.UtcNow;

            // Jika diputuskan untuk kirim barang pengganti (Replacement Dispatch)
            if (actionTaken == DamageAction.ReplacementDispatched)
            {
                replacementTrackingNumber = $"AWB-{DateTime.UtcNow:yyyyMMdd}-RPL-{Guid.NewGuid().ToString()[..6].ToUpper()}";

                var replacementPackage = new Package
                {
                    TrackingNumber = replacementTrackingNumber,
                    OriginHubId = package.OriginHubId,
                    DestinationHubId = package.DestinationHubId,
                    CurrentHubId = package.OriginHubId,
                    WeightKg = package.WeightKg,
                    ItemDescription = $"[REPLACEMENT FOR {package.TrackingNumber}] {package.ItemDescription}",
                    IsFragile = package.IsFragile,
                    Status = PackageStatus.Created,
                    CreatedAt = now,
                    UpdatedAt = now
                };

                replacementPackage.Checkpoints.Add(new TrackingCheckpoint
                {
                    Status = PackageStatus.Created,
                    LocationHubId = package.OriginHubId,
                    LocationName = "Origin DC - Replacement Department",
                    Notes = $"Paket pengganti dibuat otomatis atas laporan kerusakan resi {package.TrackingNumber}.",
                    ActorName = reporterName,
                    TimestampUtc = now
                });

                db.Packages.Add(replacementPackage);
            }

            // Update status paket asli & FIX CurrentHubId
            package.Status = PackageStatus.Damaged;
            package.CurrentHubId = locationHubId; // <- Lokasi ter-update ke Hub tempat kerusakan dilaporkan
            package.UpdatedAt = now;

            var damageReport = new DamageReport
            {
                PackageId = package.Id,
                ReporterName = reporterName,
                LocationHubId = locationHubId,
                PhotoUrl = photoUrl,
                DamageDescription = damageDescription,
                ActionTaken = actionTaken,
                ReplacementTrackingNumber = replacementTrackingNumber,
                ReportedAt = now
            };

            db.DamageReports.Add(damageReport);

            db.TrackingCheckpoints.Add(new TrackingCheckpoint
            {
                PackageId = package.Id,
                Status = PackageStatus.Damaged,
                LocationHubId = locationHubId,
                LocationName = $"{locationHub.Name} ({locationHub.Code})",
                Notes = $"INSIDEN RUSAK: {damageDescription}. Tindakan: {actionTaken}. (Foto: {photoUrl})",
                ActorName = reporterName,
                TimestampUtc = now
            });

            await db.SaveChangesAsync();

            return ApiResponse.Ok(new
            {
                message = "Laporan kerusakan berhasil dicatat.",
                originalTrackingNumber = package.TrackingNumber,
                status = package.Status.ToString(),
                currentLocation = locationHub.Name,
                actionTaken = actionTaken.ToString(),
                damagePhotoUrl = photoUrl,
                replacementTrackingNumber = replacementTrackingNumber
            });
        }).DisableAntiforgery();
    }
}