namespace Logistics.Api.Common.Entities;

public class VehicleTelemetryLog
{
    public long Id { get; set; }
    public string VehiclePlate { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double SpeedKmh { get; set; }
    public double HeadingDegrees { get; set; }
    public bool IsOnDuty { get; set; }
    public string? ActiveManifestNumber { get; set; }
    public bool HasAlert { get; set; }
    public string? AlertType { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}