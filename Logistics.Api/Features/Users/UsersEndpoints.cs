using FluentValidation;
using Logistics.Api.Common.Database;
using Logistics.Api.Common.Entities;
using Logistics.Api.Common.Models;
using Microsoft.EntityFrameworkCore;

namespace Logistics.Api.Features.Users;

public record CreateUserRequest(string Username, string Password, string FullName, UserRole Role, Guid? AssignedHubId);
public record UpdateUserRequest(string FullName, UserRole Role, Guid? AssignedHubId, bool IsActive);

public class CreateUserValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserValidator()
    {
        RuleFor(x => x.Username).NotEmpty().MinimumLength(3).MaximumLength(50);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(6).WithMessage("Password minimal 6 karakter.");
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Role).IsInEnum().WithMessage("Role yang dipilih tidak valid.");
    }
}

public class UpdateUserValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Role).IsInEnum().WithMessage("Role yang dipilih tidak valid.");
    }
}

public static class UsersEndpoints
{
    public static void MapUsersEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users")
                       .RequireAuthorization(p => p.RequireRole("SystemAdmin"));

        // 1. GET /api/users (Daftar Seluruh Karyawan)
        group.MapGet("", async (AppDbContext db) =>
        {
            var users = await db.Users
                .OrderBy(u => u.Role)
                .ThenBy(u => u.FullName)
                .Select(u => new
                {
                    u.Id,
                    u.Username,
                    u.FullName,
                    Role = u.Role.ToString(),
                    u.AssignedHubId,
                    u.IsActive,
                    u.CreatedAtUtc
                })
                .ToListAsync();

            return ApiResponse.Ok(users, "Daftar user berhasil diambil.");
        });

        // 2. POST /api/users (Daftarkan User Baru)
        group.MapPost("", async (
            CreateUserRequest req,
            IValidator<CreateUserRequest> validator,
            AppDbContext db) =>
        {
            var validationResult = await validator.ValidateAsync(req);
            if (!validationResult.IsValid)
            {
                return ApiResponse.ValidationError(validationResult.ToDictionary());
            }

            var usernameExists = await db.Users.AnyAsync(u => u.Username.ToLower() == req.Username.ToLower());
            if (usernameExists)
            {
                return ApiResponse.Error($"Username '{req.Username}' sudah digunakan.", "USERNAME_TAKEN", StatusCodes.Status400BadRequest);
            }

            if (req.AssignedHubId.HasValue)
            {
                var hubExists = await db.Hubs.AnyAsync(h => h.Id == req.AssignedHubId.Value);
                if (!hubExists)
                {
                    return ApiResponse.Error("Assigned Hub tidak valid.", "INVALID_HUB", StatusCodes.Status400BadRequest);
                }
            }

            var newUser = new User
            {
                Username = req.Username.Trim().ToLower(),
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
                FullName = req.FullName.Trim(),
                Role = req.Role,
                AssignedHubId = req.AssignedHubId,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            };

            db.Users.Add(newUser);
            await db.SaveChangesAsync();

            return ApiResponse.Created(new
            {
                newUser.Id,
                newUser.Username,
                newUser.FullName,
                Role = newUser.Role.ToString(),
                newUser.AssignedHubId
            }, "User baru berhasil didaftarkan.", $"/api/users/{newUser.Id}");
        });

        // 3. PUT /api/users/{id} (Update Data Karyawan)
        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateUserRequest req,
            IValidator<UpdateUserRequest> validator,
            AppDbContext db) =>
        {
            var validationResult = await validator.ValidateAsync(req);
            if (!validationResult.IsValid)
            {
                return ApiResponse.ValidationError(validationResult.ToDictionary());
            }

            var user = await db.Users.FindAsync(id);
            if (user == null)
            {
                return ApiResponse.Error("User tidak ditemukan.", "USER_NOT_FOUND", StatusCodes.Status404NotFound);
            }

            if (req.AssignedHubId.HasValue)
            {
                var hubExists = await db.Hubs.AnyAsync(h => h.Id == req.AssignedHubId.Value);
                if (!hubExists)
                {
                    return ApiResponse.Error("Assigned Hub tidak valid.", "INVALID_HUB", StatusCodes.Status400BadRequest);
                }
            }

            user.FullName = req.FullName.Trim();
            user.Role = req.Role;
            user.AssignedHubId = req.AssignedHubId;
            user.IsActive = req.IsActive;

            await db.SaveChangesAsync();

            return ApiResponse.Ok(new
            {
                user.Id,
                user.Username,
                user.FullName,
                Role = user.Role.ToString(),
                user.AssignedHubId,
                user.IsActive
            }, "Data user berhasil diperbarui.");
        });

        // 4. DELETE /api/users/{id} (Soft Delete / Nonaktifkan User)
        group.MapDelete("/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            var user = await db.Users.FindAsync(id);
            if (user == null)
            {
                return ApiResponse.Error("User tidak ditemukan.", "USER_NOT_FOUND", StatusCodes.Status404NotFound);
            }

            user.IsActive = false;
            await db.SaveChangesAsync();

            return ApiResponse.Ok(new { user.Id, user.Username, user.IsActive }, "User berhasil dinonaktifkan.");
        });
    }
}