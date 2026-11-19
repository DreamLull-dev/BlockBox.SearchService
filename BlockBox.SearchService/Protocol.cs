using System.Text.Json.Serialization;

namespace BlockBox.SearchService;

/// <summary>One JSON object per pipe line. Requests never accept paths to open or commands.</summary>
public sealed record SearchRequest(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("query")] string? Query = null,
    [property: JsonPropertyName("kind")] string? Kind = null,
    [property: JsonPropertyName("limit")] int Limit = 100,
    [property: JsonPropertyName("afterName")] string? AfterName = null,
    [property: JsonPropertyName("afterVolumeId")] string? AfterVolumeId = null,
    [property: JsonPropertyName("afterFileId")] long? AfterFileId = null,
    [property: JsonPropertyName("descending")] bool Descending = false);

public sealed record SearchResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("message")] string? Message = null,
    [property: JsonPropertyName("results")] IReadOnlyList<SearchHit>? Results = null,
    [property: JsonPropertyName("hasMore")] bool HasMore = false,
    [property: JsonPropertyName("nextName")] string? NextName = null,
    [property: JsonPropertyName("nextVolumeId")] string? NextVolumeId = null,
    [property: JsonPropertyName("nextFileId")] long? NextFileId = null);

public sealed record SearchHit(
    [property: JsonPropertyName("volumeId")] string VolumeId,
    [property: JsonPropertyName("fileId")] long FileId,
    [property: JsonPropertyName("parentId")] long ParentId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("isDirectory")] bool IsDirectory);

public static class PipeProtocol
{
    public const string PipeName = "BlockBox.GlobalSearch.v1";
    public const int MaxQueryLength = 512;
    // 单页上限：UI 常规分页用 500，按时间/大小排序时会一次性拉更多来补齐结果。
    public const int MaxPageSize = 5000;
}
