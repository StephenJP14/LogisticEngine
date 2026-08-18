using Logistics.Api.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace Logistics.Api.Common.Database;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Hub> Hubs => Set<Hub>();
    public DbSet<Package> Packages => Set<Package>();
    public DbSet<TrackingCheckpoint> TrackingCheckpoints => Set<TrackingCheckpoint>();
    public DbSet<Manifest> Manifests => Set<Manifest>();
    public DbSet<ManifestItem> ManifestItems => Set<ManifestItem>();
    public DbSet<ProofOfDelivery> ProofOfDeliveries => Set<ProofOfDelivery>();
    public DbSet<DamageReport> DamageReports => Set<DamageReport>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<VehicleTelemetryLog> VehicleTelemetryLogs => Set<VehicleTelemetryLog>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Hub
        modelBuilder.Entity<Hub>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.Code).IsUnique();
            b.Property(x => x.Code).HasMaxLength(50).IsRequired();
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        });

        // Package
        modelBuilder.Entity<Package>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.TrackingNumber).IsUnique();
            b.Property(x => x.TrackingNumber).HasMaxLength(60).IsRequired();
            b.Property(x => x.WeightKg).HasPrecision(10, 2);

            b.HasOne(x => x.OriginHub).WithMany(h => h.OriginPackages).HasForeignKey(x => x.OriginHubId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.DestinationHub).WithMany(h => h.DestinationPackages).HasForeignKey(x => x.DestinationHubId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.CurrentHub).WithMany(h => h.CurrentPackages).HasForeignKey(x => x.CurrentHubId).OnDelete(DeleteBehavior.Restrict);
        });

        // Checkpoint
        modelBuilder.Entity<TrackingCheckpoint>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.PackageId);
            b.HasOne(x => x.Package).WithMany(p => p.Checkpoints).HasForeignKey(x => x.PackageId).OnDelete(DeleteBehavior.Cascade);
        });

        // Manifest
        modelBuilder.Entity<Manifest>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.ManifestNumber).IsUnique();
            b.HasOne(x => x.OriginHub).WithMany().HasForeignKey(x => x.OriginHubId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.DestinationHub).WithMany().HasForeignKey(x => x.DestinationHubId).OnDelete(DeleteBehavior.Restrict);
        });

        // ManifestItem
        modelBuilder.Entity<ManifestItem>(b =>
        {
            b.HasKey(x => new { x.ManifestId, x.PackageId });
            b.HasOne(x => x.Manifest).WithMany(m => m.Items).HasForeignKey(x => x.ManifestId);
            b.HasOne(x => x.Package).WithMany().HasForeignKey(x => x.PackageId);
        });

        // ProofOfDelivery
        modelBuilder.Entity<ProofOfDelivery>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasOne(x => x.Package).WithMany().HasForeignKey(x => x.PackageId);
        });

        // DamageReport
        modelBuilder.Entity<DamageReport>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasOne(x => x.Package).WithMany().HasForeignKey(x => x.PackageId);
        });

        // Message/Notification RabbitMQ
        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.ProcessedAtUtc); // Index untuk mempercepat polling background worker
        });

        // Vehicle Tracking/Telemetry
        modelBuilder.Entity<VehicleTelemetryLog>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.VehiclePlate, x.TimestampUtc }); // Index komposit untuk query history rentang tanggal
            b.Property(x => x.VehiclePlate).HasMaxLength(30).IsRequired();
        });

        // Master Data: User
        modelBuilder.Entity<User>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.Username).IsUnique(); // Username tidak boleh kembar
        });

        // Master Data: Vehicle
        modelBuilder.Entity<Vehicle>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.PlateNumber).IsUnique(); // Plat nomor tidak boleh kembar
        });
    }
}