namespace Logistics.Api.Common.Entities;

public class ProofOfDelivery
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PackageId { get; set; }
    public string RecipientName { get; set; } = string.Empty;
    public string PhotoUrl { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;

    public Package Package { get; set; } = null!;
}