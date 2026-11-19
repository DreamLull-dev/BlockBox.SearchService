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
    [property: JsonPropertyName("afterSize")] long? AfterSize = null,
    [property: JsonPropertyName("afterModified")] long? AfterModified = null,
    [property: JsonPropertyName("descending")] bool Descending = false,
    [property: JsonPropertyName("sort")] string? Sort = null);

public sealed record SearchResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("message")] string? Message = null,
    [property: JsonPropertyName("results")] IReadOnlyList<SearchHit>? Results = null,
    [property: JsonPropertyName("hasMore")] bool HasMore = false,
    [property: JsonPropertyName("nextName")] string? NextName = null,
    [property: JsonPropertyName("nextVolumeId")] string? NextVolumeId = null,
    [property: JsonPropertyName("nextFileId")] long? NextFileId = null,
    [property: JsonPropertyName("nextSize")] long? NextSize = null,
    [property: JsonPropertyName("nextModified")] long? NextModified = null);

public sealed record SearchHit(
    [property: JsonPropertyName("volumeId")] string VolumeId,
    [property: JsonPropertyName("fileId")] long FileId,
    [property: JsonPropertyName("parentId")] long ParentId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("isDirectory")] bool IsDirectory,
    [property: JsonPropertyName("size")] long Size = -1,
    [property: JsonPropertyName("modified")] long ModifiedTicks = 0);

public static class PipeProtocol
{
    public const string PipeName = "BlockBox.GlobalSearch.v1";
    public const int MaxQueryLength = 512;
    public const int MaxPageSize = 5000;
}
