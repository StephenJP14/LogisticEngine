namespace Logistics.Api.Common.Entities;

public class DamageReport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PackageId { get; set; }
    public string ReporterName { get; set; } = string.Empty;
    public Guid LocationHubId { get; set; }
    public string PhotoUrl { get; set; } = string.Empty;
    public string DamageDescription { get; set; } = string.Empty;
    public DamageAction ActionTaken { get; set; }
    public string? ReplacementTrackingNumber { get; set; }
    public DateTime ReportedAt { get; set; } = DateTime.UtcNow;

    public Package Package { get; set; } = null!;
}