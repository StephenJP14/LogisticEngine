using FluentValidation;
using Logistics.Api.Common.Database;
using Logistics.Api.Common.Entities;
using Logistics.Api.Common.Models;
using Logistics.Api.Common.Utils;
using Microsoft.EntityFrameworkCore;

namespace Logistics.Api.Features.Vehicles;

public record CreateVehicleRequest(string PlateNumber, string ModelType, double MaxWeightCapacityKg);
public record UpdateVehicleRequest(string ModelType, double MaxWeightCapacityKg, bool IsActive);

public class CreateVehicleValidator : AbstractValidator<CreateVehicleRequest>
{
    public CreateVehicleValidator()
    {
        RuleFor(x => x.PlateNumber).NotEmpty().WithMessage("Plat nomor wajib diisi.");
        RuleFor(x => x.ModelType).NotEmpty().MaximumLength(50);
        RuleFor(x => x.MaxWeightCapacityKg).GreaterThan(0).WithMessage("Kapasitas muatan harus lebih dari 0 kg.");
    }
}

public class UpdateVehicleValidator : AbstractValidator<UpdateVehicleRequest>
{
    public UpdateVehicleValidator()
    {
        RuleFor(x => x.ModelType).NotEmpty().MaximumLength(50);
        RuleFor(x => x.MaxWeightCapacityKg).GreaterThan(0).WithMessage("Kapasitas muatan harus lebih dari 0 kg.");
    }
}

public static class VehiclesEndpoints
{
    public static void MapVehiclesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/vehicles").RequireAuthorization(p => p.RequireRole("SystemAdmin", "Dispatcher"));

        // 1. GET /api/vehicles (Daftar Seluruh Armada)
        // 1. GET /api/vehicles dengan Search, Filter Status, & Pagination
        group.MapGet("", async (
            string? search,       // Filter Plat atau Model
            bool? isActive,       // Filter Aktif/Nonaktif
            int? page,            // Halaman (1-based)
            int? pageSize,        // Ukuran per halaman (max 100)
            AppDbContext db) =>
        {
            var currentPage = Math.Max(page ?? 1, 1);
            var size = Math.Clamp(pageSize ?? 10, 1, 100);

            var query = db.Vehicles.AsNoTracking();

            // 1. Filter Status
            if (isActive.HasValue)
            {
                query = query.Where(v => v.IsActive == isActive.Value);
            }

            // 2. Search Keyword
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToUpperInvariant();
                query = query.Where(v => EF.Functions.ILike(v.PlateNumber, $"%{term}%")
                                    || EF.Functions.ILike(v.ModelType, $"%{term}%"));
            }

            var totalItems = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalItems / (double)size);

            var vehicles = await query
                .OrderBy(v => v.PlateNumber)
                .Skip((currentPage - 1) * size)
                .Take(size)
                .Select(v => new
                {
                    v.Id,
                    v.PlateNumber,
                    v.ModelType,
                    v.MaxWeightCapacityKg,
                    v.IsActive,
                    v.CreatedAtUtc
                })
                .ToListAsync();

            var response = new PagedResponse<object>
            {
                Status = StatusCodes.Status200OK,
                Success = true,
                Message = "Daftar armada kendaraan berhasil diambil.",
                Data = vehicles,
                Meta = new PageMeta(currentPage, size, totalItems, totalPages)
            };

            return Results.Ok(response);
        });

        // 2. POST /api/vehicles (Daftarkan Kendaraan Baru)
        group.MapPost("", async (
            CreateVehicleRequest req,
            IValidator<CreateVehicleRequest> validator,
            AppDbContext db) =>
        {
            var validationResult = await validator.ValidateAsync(req);
            if (!validationResult.IsValid)
            {
                return ApiResponse.ValidationError(validationResult.ToDictionary());
            }

            // Normalisasi Format Plat
            var normalizedPlate = VehicleNormalizer.NormalizePlate(req.PlateNumber);
            if (!VehicleNormalizer.IsValidStandardPlate(normalizedPlate))
            {
                return ApiResponse.Error($"Format plat nomor '{req.PlateNumber}' tidak valid.", "INVALID_PLATE_FORMAT", StatusCodes.Status400BadRequest);
            }

            var plateExists = await db.Vehicles.AnyAsync(v => v.PlateNumber == normalizedPlate);
            if (plateExists)
            {
                return ApiResponse.Error($"Kendaraan dengan plat '{normalizedPlate}' sudah terdaftar di sistem.", "PLATE_ALREADY_EXISTS", StatusCodes.Status400BadRequest);
            }

            var newVehicle = new Vehicle
            {
                PlateNumber = normalizedPlate,
                ModelType = req.ModelType.Trim(),
                MaxWeightCapacityKg = req.MaxWeightCapacityKg,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            };

            db.Vehicles.Add(newVehicle);
            await db.SaveChangesAsync();

            return ApiResponse.Created(new
            {
                newVehicle.Id,
                newVehicle.PlateNumber,
                newVehicle.ModelType,
                newVehicle.MaxWeightCapacityKg
            }, "Armada kendaraan baru berhasil didaftarkan.", $"/api/vehicles/{newVehicle.Id}");
        });

        // 3. PUT /api/vehicles/{id} (Update Data Armada)
        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateVehicleRequest req,
            IValidator<UpdateVehicleRequest> validator,
            AppDbContext db) =>
        {
            var validationResult = await validator.ValidateAsync(req);
            if (!validationResult.IsValid)
            {
                return ApiResponse.ValidationError(validationResult.ToDictionary());
            }

            var vehicle = await db.Vehicles.FindAsync(id);
            if (vehicle == null)
            {
                return ApiResponse.Error("Kendaraan tidak ditemukan.", "VEHICLE_NOT_FOUND", StatusCodes.Status404NotFound);
            }

            vehicle.ModelType = req.ModelType.Trim();
            vehicle.MaxWeightCapacityKg = req.MaxWeightCapacityKg;
            vehicle.IsActive = req.IsActive;

            await db.SaveChangesAsync();

            return ApiResponse.Ok(new
            {
                vehicle.Id,
                vehicle.PlateNumber,
                vehicle.ModelType,
                vehicle.MaxWeightCapacityKg,
                vehicle.IsActive
            }, "Data kendaraan berhasil diperbarui.");
        });

        // 4. DELETE /api/vehicles/{id} (Soft Delete / Nonaktifkan Armada)
        group.MapDelete("/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            var vehicle = await db.Vehicles.FindAsync(id);
            if (vehicle == null)
            {
                return ApiResponse.Error("Kendaraan tidak ditemukan.", "VEHICLE_NOT_FOUND", StatusCodes.Status404NotFound);
            }

            vehicle.IsActive = false;
            await db.SaveChangesAsync();

            return ApiResponse.Ok(new { vehicle.Id, vehicle.PlateNumber, vehicle.IsActive }, "Kendaraan berhasil dinonaktifkan.");
        });
    }
}