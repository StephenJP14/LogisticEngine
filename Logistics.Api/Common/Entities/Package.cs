namespace Logistics.Api.Common.Entities;

public class Package
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TrackingNumber { get; set; } = string.Empty; // e.g., "AWB-202608-XXXXXX"
    public Guid OriginHubId { get; set; }
    public Guid DestinationHubId { get; set; }
    public Guid CurrentHubId { get; set; }
    public PackageStatus Status { get; set; } = PackageStatus.Created;
    public decimal WeightKg { get; set; }
    public string ItemDescription { get; set; } = string.Empty;
    public bool IsFragile { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Relasi
    public Hub OriginHub { get; set; } = null!;
    public Hub DestinationHub { get; set; } = null!;
    public Hub CurrentHub { get; set; } = null!;
    public ICollection<TrackingCheckpoint> Checkpoints { get; set; } = new List<TrackingCheckpoint>();
}