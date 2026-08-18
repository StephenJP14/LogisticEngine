using System.Text.Json.Serialization;

namespace Logistics.Api.Common.Models;

public record ValidationErrorDetail(string Field, string Message);

public class ApiResponse<T>
{
    public int Status { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Code { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public T? Data { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ValidationErrorDetail>? Details { get; set; }
}

public static class ApiResponse
{
    public static IResult Ok<T>(T data, string message = "Success", int statusCode = StatusCodes.Status200OK)
    {
        return Results.Json(new ApiResponse<T>
        {
            Status = statusCode,
            Success = true,
            Message = message,
            Data = data
        }, statusCode: statusCode);
    }

    public static IResult Created<T>(
        T data,
        string message = "Resource created successfully",
        string? locationUri = null)
    {
        var response = new ApiResponse<T>
        {
            Status = StatusCodes.Status201Created,
            Success = true,
            Message = message,
            Data = data
        };

        if (string.IsNullOrWhiteSpace(locationUri))
        {
            return Results.Json(response, statusCode: StatusCodes.Status201Created);
        }

        // Menyematkan header 'Location' sekaligus mengembalikan JSON Envelope standar
        return Results.Created(locationUri, response);
    }

    public static IResult Error(string message, string? code = null, int statusCode = StatusCodes.Status400BadRequest)
    {
        return Results.Json(new ApiResponse<object>
        {
            Status = statusCode,
            Success = false,
            Message = message,
            Code = code
        }, statusCode: statusCode);
    }

    public static IResult ValidationError(IDictionary<string, string[]> errors)
    {
        var details = errors
            .SelectMany(kvp => kvp.Value.Select(msg => new ValidationErrorDetail(
                char.ToLowerInvariant(kvp.Key[0]) + kvp.Key[1..], msg)))
            .ToList();

        return Results.Json(new ApiResponse<object>
        {
            Status = StatusCodes.Status400BadRequest,
            Success = false,
            Message = "Validasi input gagal.",
            Code = "VALIDATION_ERROR",
            Details = details
        }, statusCode: StatusCodes.Status400BadRequest);
    }
}