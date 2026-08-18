namespace Logistics.Api.Common.Entities;

public class Manifest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ManifestNumber { get; set; } = string.Empty; // e.g., MNF-202608-XYZ
    public Guid OriginHubId { get; set; }
    public Guid DestinationHubId { get; set; }
    public string DriverName { get; set; } = string.Empty;
    public string VehiclePlate { get; set; } = string.Empty;
    public ManifestStatus Status { get; set; } = ManifestStatus.Draft;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DispatchedAt { get; set; }
    public DateTime? ArrivedAt { get; set; }

    // Relasi
    public Hub OriginHub { get; set; } = null!;
    public Hub DestinationHub { get; set; } = null!;
    public ICollection<ManifestItem> Items { get; set; } = new List<ManifestItem>();
}