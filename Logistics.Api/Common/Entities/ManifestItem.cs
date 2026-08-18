namespace Logistics.Api.Common.Entities;

public class ManifestItem
{
    public Guid ManifestId { get; set; }
    public Guid PackageId { get; set; }
    public bool IsMissingAtArrival { get; set; } = false; // Flag jika paket hilang saat bongkar muat
    public DateTime LoadedAt { get; set; } = DateTime.UtcNow;

    public Manifest Manifest { get; set; } = null!;
    public Package Package { get; set; } = null!;
}