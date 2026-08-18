namespace Logistics.Api.Common.Entities;

public enum FacilityType
{
    DistributionCenter = 1,
    TransitHub = 2,
    RetailStore = 3
}

public enum PackageStatus
{
    Created = 1,
    PackedAndReady = 2,
    AssignedToManifest = 3,
    InTransit = 4,
    ReceivedAtHub = 5,
    OutForDelivery = 6,
    Delivered = 7,
    DeliveredWithIssue = 8,
    Lost = 9,
    Damaged = 10
}

public enum ManifestStatus
{
    Draft = 1,
    Dispatched = 2,
    CompletedClean = 3,
    CompletedWithDiscrepancy = 4
}

public enum DamageAction
{
    ReturnToOrigin = 1,      // Barang fisik dikirim balik ke DC asal (Reverse Logistics)
    DestroyedOnSite = 2,     // Barang hancur/mencair dan dimusnahkan di tempat (Write-off)
    ReplacementDispatched = 3 // Diterbitkan resi baru pengganti
}