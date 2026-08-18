using Logistics.Api.Common.Database;
using Logistics.Api.Common.Entities;
using Logistics.Api.Common.Models;
using Microsoft.EntityFrameworkCore;

namespace Logistics.Api.Features.Manifests.LoadPackage;

public record LoadPackageRequest(string TrackingNumber, string ActorName);

public static class LoadPackageEndpoint
{
    public static void MapLoadPackage(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/manifests/{manifestNumber}/load", async (
            string manifestNumber,
            LoadPackageRequest req,
            AppDbContext db) =>
        {
            var manifest = await db.Manifests
                .Include(m => m.Items)
                .FirstOrDefaultAsync(m => m.ManifestNumber == manifestNumber);

            if (manifest == null) return ApiResponse.Error("Manifest tidak ditemukan.", "MANIFEST_NOT_FOUND", StatusCodes.Status404NotFound);
            if (manifest.Status != ManifestStatus.Draft) return ApiResponse.Error("Manifest sudah berangkat atau ditutup.", "INVALID_MANIFEST_STATUS", StatusCodes.Status400BadRequest);

            var package = await db.Packages.FirstOrDefaultAsync(p => p.TrackingNumber == req.TrackingNumber);
            if (package == null) return ApiResponse.Error("Paket tidak ditemukan.", "PACKAGE_NOT_FOUND", StatusCodes.Status404NotFound);

            // Pastikan paket sedang berada di Hub awal manifest ini
            if (package.CurrentHubId != manifest.OriginHubId)
            {
                return ApiResponse.Error("Posisi paket saat ini tidak berada di Hub keberangkatan Manifest ini.", "INVALID_PACKAGE_POSITION", StatusCodes.Status400BadRequest);
            }

            // Tambahkan ke truk
            if (!manifest.Items.Any(i => i.PackageId == package.Id))
            {
                manifest.Items.Add(new ManifestItem { PackageId = package.Id });

                // Ubah status paket otomatis
                package.Status = PackageStatus.AssignedToManifest;
                package.UpdatedAt = DateTime.UtcNow;

                db.TrackingCheckpoints.Add(new TrackingCheckpoint
                {
                    PackageId = package.Id,
                    Status = PackageStatus.AssignedToManifest,
                    LocationHubId = manifest.OriginHubId,
                    LocationName = "Loading Dock",
                    Notes = $"Diload ke truk {manifest.VehiclePlate} (Manifest: {manifest.ManifestNumber})",
                    ActorName = req.ActorName
                });

                await db.SaveChangesAsync();
            }

            return ApiResponse.Ok(new
            {
                message = $"Paket {package.TrackingNumber} berhasil diload ke Manifest {manifest.ManifestNumber}.",
                packageId = package.Id,
                manifestId = manifest.Id
            });
        });
    }
}