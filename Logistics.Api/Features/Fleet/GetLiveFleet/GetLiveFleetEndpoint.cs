using System.Text.Json;
using Logistics.Api.Common.Database;
using Logistics.Api.Common.Entities;
using Logistics.Api.Common.Models;
using Microsoft.EntityFrameworkCore;
using Logistics.Api.Common.Utils;
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
            long? cursor,          // ID terakhir yang diterima klien
            int? limit,           // Default 50, max 200
            DateTime? fromUtc,
            DateTime? toUtc,
            AppDbContext db) =>
        {
            var normalizedPlate = VehicleNormalizer.NormalizePlate(vehiclePlate);
            var pageSize = Math.Clamp(limit ?? 50, 1, 200);

            // Bangun query dasar berbasis index (VehiclePlate + Timestamp/Id)
            var query = db.VehicleTelemetryLogs
                .AsNoTracking()
                .Where(x => x.VehiclePlate == normalizedPlate);

            // Filter rentang waktu jika diisi
            if (fromUtc.HasValue)
                query = query.Where(x => x.TimestampUtc >= fromUtc.Value);
            if (toUtc.HasValue)
                query = query.Where(x => x.TimestampUtc <= toUtc.Value);

            // KEYSET / CURSOR PAGINATION LOGIC:
            // Jika cursor ada, kita query data yang ID-nya lebih kecil (halaman berikutnya)
            if (cursor.HasValue && cursor.Value > 0)
            {
                query = query.Where(x => x.Id < cursor.Value);
            }

            // Ambil pageSize + 1 untuk mengecek apakah masih ada data setelah batch ini
            var records = await query
                .OrderByDescending(x => x.Id)
                .Take(pageSize + 1)
                .Select(x => new
                {
                    x.Id,
                    x.VehiclePlate,
                    x.Latitude,
                    x.Longitude,
                    x.SpeedKmh,
                    x.HeadingDegrees,
                    x.IsOnDuty,
                    x.ActiveManifestNumber,
                    x.HasAlert,
                    x.AlertType,
                    x.TimestampUtc
                })
                .ToListAsync();

            var hasMore = records.Count > pageSize;
            var resultData = hasMore ? records.Take(pageSize).ToList() : records;
            long? nextCursor = hasMore && resultData.Count > 0 ? resultData[^1].Id : null;

            var response = new CursorResponse<object>
            {
                Status = StatusCodes.Status200OK,
                Success = true,
                Message = $"Berhasil mengambil {resultData.Count} titik log telemetri.",
                Data = resultData,
                Cursor = new CursorMeta(nextCursor, hasMore)
            };

            return Results.Ok(response);
        });
    }
}