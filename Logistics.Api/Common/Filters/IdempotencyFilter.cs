using System.Text.Json;
using Logistics.Api.Common.Models;
using StackExchange.Redis;

namespace Logistics.Api.Common.Filters;

public class CachedIdempotentResponse
{
    public string Status { get; set; } = "PENDING"; // PENDING | COMPLETED
    public int StatusCode { get; set; }
    public string? Body { get; set; }
}

public class IdempotencyFilter : IEndpointFilter
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<IdempotencyFilter> _logger;

    public IdempotencyFilter(IConnectionMultiplexer redis, ILogger<IdempotencyFilter> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var redisDb = _redis.GetDatabase();

        // 1. Ambil Header Idempotency-Key (Case-insensitive)
        if (!httpContext.Request.Headers.TryGetValue("Idempotency-Key", out var idempotencyKey) ||
            string.IsNullOrWhiteSpace(idempotencyKey))
        {
            // Jika header tidak dikirim, lanjutkan eksekusi normal
            return await next(context);
        }

        var redisKey = $"idempotency:{idempotencyKey.ToString().Trim()}";

        // 2. Cek apakah key sudah pernah diproses di Redis
        var existingData = await redisDb.StringGetAsync(redisKey);
        if (existingData.HasValue)
        {
            var cached = JsonSerializer.Deserialize<CachedIdempotentResponse>(existingData.ToString());

            if (cached != null)
            {
                // Jika masih berstatus PENDING (sedang diproses oleh thread lain)
                if (cached.Status == "PENDING")
                {
                    _logger.LogWarning("Idempotent request {Key} is currently being processed concurrently.", idempotencyKey.ToString());
                    return ApiResponse.Error(
                        "Permintaan dengan Idempotency-Key ini sedang dalam proses. Harap tunggu.",
                        "CONCURRENT_REQUEST",
                        StatusCodes.Status409Conflict);
                }

                // Jika sudah COMPLETED, kembalikan hasil yang tersimpan di cache
                _logger.LogInformation("Idempotent hit for key {Key}. Returning cached response (Status: {StatusCode}).",
                    idempotencyKey.ToString(), cached.StatusCode);

                httpContext.Response.Headers.Append("X-Cache-Lookup", "HIT-IDEMPOTENT");
                return Results.Content(cached.Body ?? "{}", "application/json", statusCode: cached.StatusCode);
            }
        }

        // 3. Kunci State Awal: Tandai sebagai PENDING (TTL 60 detik untuk mencegah dead-lock jika server crash)
        var pendingPayload = JsonSerializer.Serialize(new CachedIdempotentResponse
        {
            Status = "PENDING",
            StatusCode = StatusCodes.Status202Accepted
        });

        var lockAcquired = await redisDb.StringSetAsync(redisKey, pendingPayload, TimeSpan.FromSeconds(60), When.NotExists);
        if (!lockAcquired)
        {
            return ApiResponse.Error(
                "Permintaan dengan Idempotency-Key ini sedang dalam proses.",
                "CONCURRENT_REQUEST",
                StatusCodes.Status409Conflict);
        }

        object? result;
        try
        {
            // 4. Eksekusi Business Logic Asli
            result = await next(context);
        }
        catch (Exception)
        {
            // Jika eksekusi gagal/exception, hapus key agar klien dapat mencoba lagi
            await redisDb.KeyDeleteAsync(redisKey);
            throw;
        }

        // 5. Ekstraksi Status Code dan Body untuk Disimpan ke Redis Cache
        int statusCode = StatusCodes.Status200OK;
        string serializedBody = "{}";

        if (result is IStatusCodeHttpResult statusResult && statusResult.StatusCode.HasValue)
        {
            statusCode = statusResult.StatusCode.Value;
        }

        if (result is IValueHttpResult valueResult && valueResult.Value != null)
        {
            serializedBody = JsonSerializer.Serialize(valueResult.Value, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });
        }

        // Hanya simpan respons sukses (2xx) atau client errors (4xx) selama 24 jam
        if (statusCode >= 200 && statusCode < 500)
        {
            var completedPayload = JsonSerializer.Serialize(new CachedIdempotentResponse
            {
                Status = "COMPLETED",
                StatusCode = statusCode,
                Body = serializedBody
            });

            await redisDb.StringSetAsync(redisKey, completedPayload, TimeSpan.FromHours(24));
        }
        else
        {
            // Jika terjadi server error (500), hapus key agar bisa di-retry
            await redisDb.KeyDeleteAsync(redisKey);
        }

        return result;
    }
}

public static class IdempotencyFilterExtensions
{
    public static RouteGroupBuilder AddIdempotency(this RouteGroupBuilder group)
    {
        return group.AddEndpointFilter<IdempotencyFilter>();
    }

    public static RouteHandlerBuilder AddIdempotency(this RouteHandlerBuilder builder)
    {
        return builder.AddEndpointFilter<IdempotencyFilter>();
    }
}