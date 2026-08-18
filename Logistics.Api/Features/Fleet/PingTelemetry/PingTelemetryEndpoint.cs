using System.Text.Json;
using FluentValidation;
using Logistics.Api.Common.Database;
using Logistics.Api.Common.Entities;
using Logistics.Api.Common.Models;
using Logistics.Api.Common.Hubs;
using Logistics.Api.Common.Telemetry;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Logistics.Api.Features.Fleet.PingTelemetry;

public record PingTelemetryRequest(
    string VehiclePlate,
    double Latitude,
    double Longitude,
    double SpeedKmh,
    double HeadingDegrees
);

public class PingTelemetryValidator : AbstractValidator<PingTelemetryRequest>
{
    public PingTelemetryValidator()
    {
        RuleFor(x => x.VehiclePlate).NotEmpty().WithMessage("Plat nomor kendaraan wajib diisi.");
        RuleFor(x => x.Latitude).InclusiveBetween(-90, 90);
        RuleFor(x => x.Longitude).InclusiveBetween(-180, 180);
        RuleFor(x => x.SpeedKmh).GreaterThanOrEqualTo(0);
        RuleFor(x => x.HeadingDegrees).InclusiveBetween(0, 360);
    }
}

public static class PingTelemetryEndpoint
{
    public static void MapPingTelemetry(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/telemetry/ping", async (
            PingTelemetryRequest req,
            IValidator<PingTelemetryRequest> validator,
            IConnectionMultiplexer redis,
            IHubContext<FleetTrackingHub, IFleetTrackingClient> hubContext,
            [FromServices] TelemetryBuffer buffer, // <- Inject TelemetryBuffer di sini
            AppDbContext db,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("PingTelemetry");

            var validationResult = await validator.ValidateAsync(req);
            if (!validationResult.IsValid)
            {
                return ApiResponse.ValidationError(validationResult.ToDictionary());
            }

            var now = DateTime.UtcNow;
            var dbRedis = redis.GetDatabase();

            // 1. Cek status penugasan di DB
            var activeManifest = await db.Manifests
                .Where(m => m.VehiclePlate == req.VehiclePlate &&
                           (m.Status == ManifestStatus.Draft || m.Status == ManifestStatus.Dispatched))
                .OrderByDescending(m => m.CreatedAt)
                .FirstOrDefaultAsync();

            var isOnDuty = activeManifest != null;
            var isUnauthorized = !isOnDuty && req.SpeedKmh > 5.0;
            string? alertType = isUnauthorized ? "UNAUTHORIZED_MOVEMENT" : null;

            if (isUnauthorized)
            {
                logger.LogWarning("SECURITY ALERT: Unauthorized movement on {VehiclePlate} ({Speed} km/h)", req.VehiclePlate, req.SpeedKmh);
            }

            // 2. HOT PATH: Simpan snapshot ke Redis (In-Memory, sub-millisecond)
            await dbRedis.GeoAddAsync("active_fleet:locations", new GeoEntry(req.Longitude, req.Latitude, req.VehiclePlate));

            var snapshotPayload = new
            {
                VehiclePlate = req.VehiclePlate,
                Latitude = req.Latitude,
                Longitude = req.Longitude,
                SpeedKmh = req.SpeedKmh,
                HeadingDegrees = req.HeadingDegrees,
                LastPing = now,
                IsOnDuty = isOnDuty,
                ActiveManifestNumber = activeManifest?.ManifestNumber,
                HasAlert = isUnauthorized,
                AlertType = alertType
            };

            await dbRedis.StringSetAsync($"fleet:snapshot:{req.VehiclePlate}", JsonSerializer.Serialize(snapshotPayload));

            // 3. COLD PATH BUFFER: Masukkan ke channel memori (tanpa menunggu write disk Postgres)
            buffer.Enqueue(new VehicleTelemetryLog
            {
                VehiclePlate = req.VehiclePlate,
                Latitude = req.Latitude,
                Longitude = req.Longitude,
                SpeedKmh = req.SpeedKmh,
                HeadingDegrees = req.HeadingDegrees,
                IsOnDuty = isOnDuty,
                ActiveManifestNumber = activeManifest?.ManifestNumber,
                HasAlert = isUnauthorized,
                AlertType = alertType,
                TimestampUtc = now
            });

            // 4. REALTIME BROADCAST via SignalR WebSocket
            var updateDto = new FleetLocationUpdate(
                req.VehiclePlate,
                req.Latitude,
                req.Longitude,
                req.SpeedKmh,
                req.HeadingDegrees,
                isOnDuty,
                activeManifest?.ManifestNumber,
                isUnauthorized,
                alertType,
                now
            );

            // 1. Broadcast ke Peta Global HQ
            await hubContext.Clients.All.ReceiveFleetLocation(updateDto);

            // 2. Broadcast khusus ke Group Klien yang sedang membuka detail truk ini
            await hubContext.Clients.Group($"fleet:{req.VehiclePlate}").ReceiveFleetLocation(updateDto);

            return ApiResponse.Ok(new
            {
                message = "Telemetry ping recorded in Hot & Cold storage.",
                vehiclePlate = req.VehiclePlate,
                isOnDuty = isOnDuty,
                activeManifest = activeManifest?.ManifestNumber,
                hasAlert = isUnauthorized,
                timestamp = now
            });
        });
    }
}