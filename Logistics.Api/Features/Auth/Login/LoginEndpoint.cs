using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FluentValidation;
using Logistics.Api.Common.Database;
using Logistics.Api.Common.Entities;
using Logistics.Api.Common.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Logistics.Api.Features.Auth.Login;

public record LoginRequest(string Username, string Password);

public class LoginValidator : AbstractValidator<LoginRequest>
{
    public LoginValidator()
    {
        RuleFor(x => x.Username).NotEmpty().WithMessage("Username wajib diisi.");
        RuleFor(x => x.Password).NotEmpty().WithMessage("Password wajib diisi.");
    }
}

public static class LoginEndpoint
{
    public static void MapLogin(this IEndpointRouteBuilder app)
    {
        // 1. POST /api/auth/login
        app.MapPost("/api/auth/login", async (
            LoginRequest req,
            IValidator<LoginRequest> validator,
            AppDbContext db,
            IConfiguration config) =>
        {
            var validationResult = await validator.ValidateAsync(req);
            if (!validationResult.IsValid)
            {
                return ApiResponse.ValidationError(validationResult.ToDictionary());
            }

            var user = await db.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == req.Username.ToLower() && u.IsActive);
            if (user == null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            {
                return ApiResponse.Error("Username atau password salah.", "INVALID_CREDENTIALS", StatusCodes.Status401Unauthorized);
            }

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var expires = DateTime.UtcNow.AddHours(8);

            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new(JwtRegisteredClaimNames.Name, user.Username),
                new("fullName", user.FullName),
                new(ClaimTypes.Role, user.Role.ToString())
            };

            if (user.AssignedHubId.HasValue)
            {
                claims.Add(new("assignedHubId", user.AssignedHubId.Value.ToString()));
            }

            var token = new JwtSecurityToken(
                issuer: config["Jwt:Issuer"],
                audience: config["Jwt:Audience"],
                claims: claims,
                expires: expires,
                signingCredentials: creds
            );

            return ApiResponse.Ok(new
            {
                token = new JwtSecurityTokenHandler().WriteToken(token),
                expiresAt = expires,
                user = new
                {
                    id = user.Id,
                    username = user.Username,
                    fullName = user.FullName,
                    role = user.Role.ToString(),
                    assignedHubId = user.AssignedHubId
                }
            }, "Login berhasil.");
        }).AllowAnonymous();

        // 2. GET /api/auth/me (Ambil profil user yang sedang login dari Claims Token)
        app.MapGet("/api/auth/me", async (ClaimsPrincipal claimsUser, AppDbContext db) =>
        {
            var userIdStr = claimsUser.FindFirstValue(ClaimTypes.NameIdentifier)
                         ?? claimsUser.FindFirstValue(JwtRegisteredClaimNames.Sub);

            if (!Guid.TryParse(userIdStr, out var userId))
            {
                return ApiResponse.Error("User ID tidak valid pada token.", "INVALID_TOKEN_CLAIMS", StatusCodes.Status401Unauthorized);
            }

            var user = await db.Users.FindAsync(userId);
            if (user == null || !user.IsActive)
            {
                return ApiResponse.Error("User tidak ditemukan atau akun dinonaktifkan.", "USER_NOT_FOUND", StatusCodes.Status404NotFound);
            }

            return ApiResponse.Ok(new
            {
                id = user.Id,
                username = user.Username,
                fullName = user.FullName,
                role = user.Role.ToString(),
                assignedHubId = user.AssignedHubId,
                createdAt = user.CreatedAtUtc
            }, "Profil user berhasil diambil.");
        }).RequireAuthorization();
    }
}