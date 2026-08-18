using System.Text.Json.Serialization;

namespace Logistics.Api.Common.Models;

// Metadata untuk Offset/Standard Paging (Tabel Admin Dashboard)
public record PageMeta(int CurrentPage, int PageSize, int TotalItems, int TotalPages);

public class PagedResponse<T> : ApiResponse<IEnumerable<T>>
{
    public PageMeta Meta { get; set; } = default!;
}

// Metadata untuk Keyset/Cursor Paging (Time-Series & Telemetry Logs)
public record CursorMeta(long? NextCursor, bool HasMore);

public class CursorResponse<T> : ApiResponse<IEnumerable<T>>
{
    public CursorMeta Cursor { get; set; } = default!;
}