namespace Logistics.Api.Common.Entities;

public class TrackingCheckpoint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PackageId { get; set; }
    public PackageStatus Status { get; set; }
    public Guid LocationHubId { get; set; }
    public string LocationName { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string ActorName { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    public Package Package { get; set; } = null!;
}