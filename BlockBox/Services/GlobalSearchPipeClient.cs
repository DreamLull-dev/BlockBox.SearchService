using System.IO.Pipes;
using System.IO;
using System.Collections.Generic;
using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BlockBox.Services;

internal sealed class GlobalSearchPipeClient
{
    private const string PipeName = "BlockBox.GlobalSearch.v1";
    private const int PipeConnectionTimeoutMs = 5000;
    private const int ResponseTimeoutMs = 20000;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    private readonly SemaphoreSlim _requestGate = new(1, 1);

    internal async Task<PipeSearchResponse> SendAsync(string operation, string? query = null, string? kind = null,
        int limit = 200,
        string? afterName = null, string? afterVolumeId = null, long? afterFileId = null,
        Func<bool>? shouldContinue = null, bool descending = false)
    {
        await _requestGate.WaitAsync();
        try
        {
            if (shouldContinue is not null && !shouldContinue())
                return new PipeSearchResponse { Status = "stale" };

            await using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(PipeConnectionTimeoutMs);
            using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, leaveOpen: true);
            await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
            var request = new PipeSearchRequest(Guid.NewGuid().ToString("N"), operation, query, kind, limit, afterName, afterVolumeId, afterFileId, descending);
            await writer.WriteLineAsync(JsonSerializer.Serialize(request, JsonOptions).AsMemory());
            // Never wait forever: a crashed or wedged service would otherwise hold the request gate
            // and make every later search hang silently.
            using var timeout = new CancellationTokenSource(ResponseTimeoutMs);
            string? responseLine;
            try
            {
                responseLine = await reader.ReadLineAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("搜索服务响应超时。");
            }
            if (string.IsNullOrWhiteSpace(responseLine)) throw new IOException("搜索服务未返回结果。");
            return JsonSerializer.Deserialize<PipeSearchResponse>(responseLine, JsonOptions)
                ?? throw new InvalidDataException("搜索服务返回了无效响应。");
        }
        finally { _requestGate.Release(); }
    }

    private sealed record PipeSearchRequest(string Id, string Operation, string? Query, string? Kind, int Limit,
        string? AfterName, string? AfterVolumeId, long? AfterFileId, bool Descending);
}

internal sealed class PipeSearchResponse
{
    public string Id { get; init; } = "";
    public string Status { get; init; } = "";
    public string? Message { get; init; }
    public List<PipeSearchHit>? Results { get; init; }
    public bool HasMore { get; init; }
    public string? NextName { get; init; }
    public string? NextVolumeId { get; init; }
    public long? NextFileId { get; init; }
}

internal sealed class PipeSearchHit
{
    public string VolumeId { get; init; } = "";
    public long FileId { get; init; }
    public long ParentId { get; init; }
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public bool IsDirectory { get; init; }
}
