namespace Logistics.Api.Common.Events;

public record PackageDeliveredEvent(
    Guid PackageId,
    string TrackingNumber,
    string RecipientName,
    string PhotoUrl,
    DateTime DeliveredAt
);

public record PackageDamagedEvent(
    Guid PackageId,
    string TrackingNumber,
    string ReporterName,
    string Description,
    string? ReplacementTrackingNumber,
    DateTime ReportedAt
);