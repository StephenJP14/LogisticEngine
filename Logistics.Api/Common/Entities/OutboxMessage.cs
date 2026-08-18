namespace Logistics.Api.Common.Entities;

public class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string EventType { get; set; } = string.Empty; // e.g., "PackageDelivered"
    public string Payload { get; set; } = string.Empty;   // JSON serialized event
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAtUtc { get; set; }         // Null jika belum dikirim ke RabbitMQ
    public string? Error { get; set; }
}