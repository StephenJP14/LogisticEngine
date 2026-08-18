using Logistics.Api.Common.Database;
using Logistics.Api.Common.Models;
using Microsoft.EntityFrameworkCore;

namespace Logistics.Api.Features.Packages.GetTrackingHistory;

public static class GetTrackingHistoryEndpoint
{
    public static void MapGetTrackingHistory(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/packages/{trackingNumber}/tracking", async (string trackingNumber, AppDbContext db) =>
        {
            var package = await db.Packages
                .Include(p => p.OriginHub)
                .Include(p => p.DestinationHub)
                .Include(p => p.CurrentHub)
                .Include(p => p.Checkpoints.OrderByDescending(c => c.TimestampUtc))
                .FirstOrDefaultAsync(p => p.TrackingNumber == trackingNumber);

            if (package == null)
            {
                return ApiResponse.Error($"Resi {trackingNumber} tidak ditemukan.", "PACKAGE_NOT_FOUND", StatusCodes.Status404NotFound);
            }

            // Ambil POD jika sudah delivered
            var pod = await db.ProofOfDeliveries.FirstOrDefaultAsync(p => p.PackageId == package.Id);

            // Ambil Laporan Kerusakan jika ada insiden
            var damageReport = await db.DamageReports.FirstOrDefaultAsync(d => d.PackageId == package.Id);

            var response = new
            {
                trackingNumber = package.TrackingNumber,
                status = package.Status.ToString(),
                itemDescription = package.ItemDescription,
                weightKg = package.WeightKg,
                isFragile = package.IsFragile,
                origin = new { code = package.OriginHub.Code, name = package.OriginHub.Name },
                destination = new { code = package.DestinationHub.Code, name = package.DestinationHub.Name },
                currentLocation = new { code = package.CurrentHub.Code, name = package.CurrentHub.Name },

                // Bukti Serah Terima (Jika Delivered)
                proofOfDelivery = pod == null ? null : new
                {
                    recipientName = pod.RecipientName,
                    photoUrl = pod.PhotoUrl,
                    latitude = pod.Latitude,
                    longitude = pod.Longitude,
                    deliveredAt = pod.SubmittedAt
                },

                // Laporan Kerusakan / RMA (Jika Damaged)
                damageIncident = damageReport == null ? null : new
                {
                    reportedBy = damageReport.ReporterName,
                    actionTaken = damageReport.ActionTaken.ToString(),
                    damageDescription = damageReport.DamageDescription,
                    photoUrl = damageReport.PhotoUrl,
                    replacementTrackingNumber = damageReport.ReplacementTrackingNumber,
                    reportedAt = damageReport.ReportedAt
                },

                history = package.Checkpoints.Select(c => new
                {
                    status = c.Status.ToString(),
                    location = c.LocationName,
                    notes = c.Notes,
                    updatedBy = c.ActorName,
                    timestamp = c.TimestampUtc
                })
            };

            return ApiResponse.Ok(response);
        });
    }
}