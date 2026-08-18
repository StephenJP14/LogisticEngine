namespace Logistics.Api.Common.Entities;

public enum UserRole
{
    SystemAdmin = 1,     // Head Office (Akses Semua)
    Dispatcher = 2,      // Pengelola Surat Jalan & Armada
    WarehouseStaff = 3,  // Petugas Loading & Scan Paket
    Driver = 4,          // Kurir
    StoreManager = 5     // Kepala Gerai / Toko Tujuan
}

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public Guid? AssignedHubId { get; set; } // Null jika admin pusat, isi Guid jika ditugaskan di gudang spesifik
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}