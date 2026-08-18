namespace Logistics.Api.Common.Entities;

public class Vehicle
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string PlateNumber { get; set; } = string.Empty; // Format Baku: B-9999-XYZ
    public string ModelType { get; set; } = string.Empty;   // Tronton, BlindVan, dll
    public double MaxWeightCapacityKg { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}