namespace Logistics.Api.Common.Entities;

public class Hub
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty; // misal: "DC-CGK-01", "STR-TNG-05"
    public string Name { get; set; } = string.Empty;
    public FacilityType Type { get; set; }
    public string Address { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public ICollection<Package> OriginPackages { get; set; } = new List<Package>();
    public ICollection<Package> DestinationPackages { get; set; } = new List<Package>();
    public ICollection<Package> CurrentPackages { get; set; } = new List<Package>();
}