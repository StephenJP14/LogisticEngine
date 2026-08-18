using System.Text.Json;
using Logistics.Api.Common.Database;
using Logistics.Api.Common.Entities;
using Logistics.Api.Common.Storage;
using Logistics.Api.Common.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Logistics.Api.Common.Events;

namespace Logistics.Api.Features.Packages.SubmitPod;

public static class SubmitPodEndpoint
{
    public static void MapSubmitPod(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/packages/{trackingNumber}/pod", async (
            string trackingNumber,
            [FromForm] string recipientName,
            [FromForm] double latitude,
            [FromForm] double longitude,
            IFormFile photo,
            AppDbContext db,
            IStorageService storageService,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("SubmitPod");

            if (string.IsNullOrWhiteSpace(recipientName) || photo == null || photo.Length == 0)
            {
                return ApiResponse.Error("Nama penerima dan foto bukti POD wajib diisi.", "INVALID_INPUT", StatusCodes.Status400BadRequest);
            }

            var package = await db.Packages
                .Include(p => p.DestinationHub)
                .FirstOrDefaultAsync(p => p.TrackingNumber == trackingNumber);

            if (package == null) return ApiResponse.Error("Paket tidak ditemukan.", "PACKAGE_NOT_FOUND", StatusCodes.Status404NotFound);

            if (package.Status == PackageStatus.Delivered)
            {
                return ApiResponse.Error("Paket sudah berstatus DELIVERED sebelumnya.", "INVALID_PACKAGE_STATUS", StatusCodes.Status400BadRequest);
            }

            // 1. Validasi Geofence: Maksimal radius 1.000 meter (1 KM) dari gerai tujuan
            var distanceMeters = CalculateDistanceMeters(
                latitude, longitude,
                package.DestinationHub.Latitude, package.DestinationHub.Longitude
            );

            if (distanceMeters > 1000)
            {
                return ApiResponse.Error($"Validasi Geofence gagal. Anda berada sejauh {distanceMeters:F0}m dari lokasi gerai tujuan (Maksimal 1000m).", "GEOFENCE_VIOLATION", StatusCodes.Status400BadRequest);
            }

            var now = DateTime.UtcNow;

            // 2. AUTO-HEAL: Deteksi apakah ada langkah perantara yang terlewat oleh operator
            if (package.Status != PackageStatus.OutForDelivery && package.Status != PackageStatus.ReceivedAtHub)
            {
                logger.LogWarning("Auto-Heal triggered for {TrackingNumber}: status jumped directly from {Status} to Delivered.",
                    package.TrackingNumber, package.Status);

                db.TrackingCheckpoints.Add(new TrackingCheckpoint
                {
                    PackageId = package.Id,
                    Status = package.Status,
                    LocationHubId = package.DestinationHubId,
                    LocationName = "System Audit - Auto Heal",
                    Notes = $"[AUTO_RESOLVED_UNSCANNED_INTERMEDIATE] Terdeteksi lompatan status dari '{package.Status}' langsung ke 'Delivered'. Checkpoint perantara diselesaikan otomatis oleh sistem.",
                    ActorName = "System Sentinel",
                    TimestampUtc = now.AddMilliseconds(-50)
                });
            }

            // 3. Upload Foto POD ke MinIO
            using var stream = photo.OpenReadStream();
            var photoUrl = await storageService.UploadFileAsync(stream, photo.FileName, photo.ContentType);

            // 4. Simpan Entitas POD
            var pod = new ProofOfDelivery
            {
                PackageId = package.Id,
                RecipientName = recipientName,
                PhotoUrl = photoUrl,
                Latitude = latitude,
                Longitude = longitude,
                SubmittedAt = now
            };

            // 5. Update Status & FIX CurrentHubId
            package.Status = PackageStatus.Delivered;
            package.CurrentHubId = package.DestinationHubId; // <- Lokasi fix ter-update ke Gerai Tujuan
            package.UpdatedAt = now;

            db.ProofOfDeliveries.Add(pod);

            // 6. Append Milestone Checkpoint DELIVERED
            db.TrackingCheckpoints.Add(new TrackingCheckpoint
            {
                PackageId = package.Id,
                Status = PackageStatus.Delivered,
                LocationHubId = package.DestinationHubId,
                LocationName = package.DestinationHub.Name,
                Notes = $"Diterima oleh {recipientName}. Foto POD terverifikasi ({Math.Round(distanceMeters, 1)}m dari gerai).",
                ActorName = recipientName,
                TimestampUtc = now
            });

            // 7. Simpan Event ke Outbox dalam 1 Transaksi Database (Anti Data Loss)
            var deliveredEvent = new PackageDeliveredEvent(
                package.Id,
                package.TrackingNumber,
                recipientName,
                photoUrl,
                now
            );

            db.OutboxMessages.Add(new OutboxMessage
            {
                EventType = nameof(PackageDeliveredEvent),
                Payload = JsonSerializer.Serialize(deliveredEvent),
                CreatedAtUtc = now
            });

            await db.SaveChangesAsync();

            return ApiResponse.Ok(new
            {
                message = "Proof of Delivery berhasil disubmit. Paket resmi DELIVERED.",
                trackingNumber = package.TrackingNumber,
                status = package.Status.ToString(),
                recipient = recipientName,
                photoUrl = photoUrl,
                distanceFromDestinationMeters = Math.Round(distanceMeters, 1)
            });
        }).DisableAntiforgery();
    }

    private static double CalculateDistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371e3; // Radius bumi (meter)
        var phi1 = lat1 * Math.PI / 180;
        var phi2 = lat2 * Math.PI / 180;
        var deltaPhi = (lat2 - lat1) * Math.PI / 180;
        var deltaLambda = (lon2 - lon1) * Math.PI / 180;

        var a = Math.Sin(deltaPhi / 2) * Math.Sin(deltaPhi / 2) +
                Math.Cos(phi1) * Math.Cos(phi2) *
                Math.Sin(deltaLambda / 2) * Math.Sin(deltaLambda / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return R * c;
    }
}