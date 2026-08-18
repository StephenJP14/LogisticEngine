using Microsoft.AspNetCore.SignalR;

namespace Logistics.Api.Common.Hubs;

public interface IFleetTrackingClient
{
    Task ReceiveFleetLocation(FleetLocationUpdate update);
}

public record FleetLocationUpdate(
    string VehiclePlate,
    double Latitude,
    double Longitude,
    double SpeedKmh,
    double HeadingDegrees,
    bool IsOnDuty,
    string? ActiveManifestNumber,
    bool HasAlert,
    string? AlertType,
    DateTime Timestamp
);

public class FleetTrackingHub : Hub<IFleetTrackingClient>
{
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
    }

    // Klien (FE) memilih truk spesifik yang ingin dipantau
    public async Task WatchVehicle(string vehiclePlate)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"fleet:{vehiclePlate.ToUpperInvariant()}");
    }

    public async Task UnwatchVehicle(string vehiclePlate)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"fleet:{vehiclePlate.ToUpperInvariant()}");
    }
}