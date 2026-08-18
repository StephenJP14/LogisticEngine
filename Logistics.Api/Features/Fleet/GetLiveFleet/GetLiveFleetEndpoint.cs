using System.Text.Json;
using Logistics.Api.Common.Database;
using Logistics.Api.Common.Entities;
using Logistics.Api.Common.Models;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Logistics.Api.Features.Fleet.GetLiveFleet;

public static class GetLiveFleetEndpoint
{
    public static void MapGetLiveFleet(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/fleet/{vehiclePlate}/live", async (
            string vehiclePlate,
            IConnectionMultiplexer redis,
            AppDbContext db) =>
        {
            var dbRedis = redis.GetDatabase();

            // 1. Baca snapshot terakhir dari Redis
            var snapshotData = await dbRedis.StringGetAsync($"fleet:snapshot:{vehiclePlate}");

            // Fallback ke PostgreSQL jika Redis kosong (misal setelah restart Redis)
            double lat = 0, lon = 0, speed = 0, heading = 0;
            DateTime lastPing = DateTime.MinValue;
            bool hasAlert = false;
            string? alertType = null;
            bool dataFound = false;

            if (snapshotData.HasValue)
            {
                using var doc = JsonDocument.Parse(snapshotData.ToString());
                lat = doc.RootElement.GetProperty("Latitude").GetDouble();
                lon = doc.RootElement.GetProperty("Longitude").GetDouble();
                speed = doc.RootElement.GetProperty("SpeedKmh").GetDouble();
                heading = doc.RootElement.GetProperty("HeadingDegrees").GetDouble();
                lastPing = doc.RootElement.GetProperty("LastPing").GetDateTime();
                hasAlert = doc.RootElement.GetProperty("HasAlert").GetBoolean();
                alertType = doc.RootElement.TryGetProperty("AlertType", out var at) ? at.GetString() : null;
                dataFound = true;
            }
            else
            {
                // Ambil titik terakhir dari Cold Storage di PostgreSQL
                var lastLog = await db.VehicleTelemetryLogs
                    .Where(l => l.VehiclePlate == vehiclePlate)
                    .OrderByDescending(l => l.TimestampUtc)
                    .FirstOrDefaultAsync();

                if (lastLog != null)
                {
                    lat = lastLog.Latitude;
                    lon = lastLog.Longitude;
                    speed = lastLog.SpeedKmh;
                    heading = lastLog.HeadingDegrees;
                    lastPing = lastLog.TimestampUtc;
                    hasAlert = lastLog.HasAlert;
                    alertType = lastLog.AlertType;
                    dataFound = true;
                }
            }

            if (!dataFound)
            {
                return ApiResponse.Error($"Armada {vehiclePlate} belum pernah mengirimkan data telemetri.", "VEHICLE_NOT_FOUND", StatusCodes.Status404NotFound);
            }

            // 2. Evaluasi status koneksi: Online jika ping < 3 menit yang lalu, Offline jika lebih lama
            var minutesSinceLastPing = (DateTime.UtcNow - lastPing).TotalMinutes;
            var connectivityStatus = minutesSinceLastPing <= 3.0 ? "ONLINE" : "OFFLINE_SIGNAL_LOST";

            // 3. Ambil data penugasan manifest aktif
            var activeManifest = await db.Manifests
                .Include(m => m.OriginHub)
                .Include(m => m.DestinationHub)
                .Include(m => m.Items)
                .Where(m => m.VehiclePlate == vehiclePlate &&
                           (m.Status == ManifestStatus.Draft || m.Status == ManifestStatus.Dispatched))
                .OrderByDescending(m => m.CreatedAt)
                .FirstOrDefaultAsync();

            return ApiResponse.Ok(new
            {
                vehiclePlate = vehiclePlate,
                connectivity = new
                {
                    status = connectivityStatus,
                    lastPingUtc = lastPing,
                    minutesOffline = connectivityStatus == "ONLINE" ? 0 : Math.Round(minutesSinceLastPing, 1)
                },
                lastKnownCoordinates = new
                {
                    latitude = lat,
                    longitude = lon,
                    speedKmh = connectivityStatus == "ONLINE" ? speed : 0.0,
                    headingDegrees = heading
                },
                operationalStatus = new
                {
                    isAssigned = activeManifest != null,
                    status = activeManifest != null ? "ON_DUTY" : "IDLE_STANDBY",
                    activeManifestNumber = activeManifest?.ManifestNumber,
                    driverName = activeManifest?.DriverName ?? "-",
                    originHub = activeManifest != null ? new { code = activeManifest.OriginHub.Code, name = activeManifest.OriginHub.Name } : null,
                    destinationHub = activeManifest != null ? new { code = activeManifest.DestinationHub.Code, name = activeManifest.DestinationHub.Name } : null,
                    totalPackagesCarried = activeManifest?.Items.Count ?? 0
                },
                securityAlert = new
                {
                    hasAlert = hasAlert,
                    alertType = alertType,
                    description = hasAlert ? "Kendaraan terdeteksi bergerak tanpa surat jalan/penugasan resmi." : null
                }
            });
        });

        // 4. Endpoint Baru: GET Route Playback History (Menggambar jejak rute di peta)
        app.MapGet("/api/fleet/{vehiclePlate}/history", async (
            string vehiclePlate,
            DateTime? fromUtc,
            DateTime? toUtc,
            AppDbContext db) =>
        {
            var start = fromUtc ?? DateTime.UtcNow.AddHours(-24);
            var end = toUtc ?? DateTime.UtcNow;

            var history = await db.VehicleTelemetryLogs
                .Where(l => l.VehiclePlate == vehiclePlate && l.TimestampUtc >= start && l.TimestampUtc <= end)
                .OrderBy(l => l.TimestampUtc)
                .Select(l => new
                {
                    latitude = l.Latitude,
                    longitude = l.Longitude,
                    speedKmh = l.SpeedKmh,
                    heading = l.HeadingDegrees,
                    isOnDuty = l.IsOnDuty,
                    manifestNumber = l.ActiveManifestNumber,
                    timestamp = l.TimestampUtc
                })
                .ToListAsync();

            return ApiResponse.Ok(new
            {
                vehiclePlate = vehiclePlate,
                timeRange = new { from = start, to = end },
                totalPoints = history.Count,
                routeCoordinates = history
            });
        });
    }
}