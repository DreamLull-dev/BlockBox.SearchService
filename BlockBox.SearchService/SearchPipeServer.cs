using Microsoft.Win32.SafeHandles;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Text.Json;

namespace BlockBox.SearchService;

internal sealed class SearchPipeServer(string? clientSid)
{
    private const uint PipeAccessDuplex = 0x00000003;
    private const uint FileFlagOverlapped = 0x40000000;
    private const uint PipeTypeByte = 0;
    private const uint PipeReadModeByte = 0;
    private const uint PipeWait = 0;
    private const uint PipeRejectRemoteClients = 0x00000008;
    private const uint PipeUnlimitedInstances = 255;
    private SearchIndexStore? _store;

    internal void SetStore(SearchIndexStore store) => Volatile.Write(ref _store, store);

    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        string sddl = BuildPipeSecurityDescriptor(clientSid);
        ServiceLog.Write(clientSid is null
            ? "Search pipe accepts authenticated local users (no client.sid)."
            : "Search pipe is restricted to the configured client SID.");
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var pipe = CreateServerPipe(sddl);
                await pipe.WaitForConnectionAsync(cancellationToken);
                await HandleClientAsync(pipe, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // A single bad client (or a failed query) must not stop the service: drop the
                // connection, log it, and keep accepting requests.
                ServiceLog.Write("Search pipe connection failed", ex);
                try { await Task.Delay(250, cancellationToken); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    /// <summary>
    /// 有 client.sid（脚本安装）时只放行该用户；没有（伴侣 MSIX 安装不会执行安装脚本）时
    /// 放行本机已认证用户。管道本身带 PIPE_REJECT_REMOTE_CLIENTS，协议也只返回文件名。
    /// </summary>
    private static string BuildPipeSecurityDescriptor(string? clientSid)
    {
        var descriptor = new StringBuilder("D:P(A;;GA;;;SY)(A;;GA;;;BA)");
        if (!string.IsNullOrWhiteSpace(clientSid))
        {
            try
            {
                var sid = new System.Security.Principal.SecurityIdentifier(clientSid).Value;
                descriptor.Append("(A;;GA;;;").Append(sid).Append(')');
                return descriptor.ToString();
            }
            catch (ArgumentException)
            {
                // 配置写坏了就退回到“已认证用户”，否则服务会完全连不上。
            }
        }
        descriptor.Append("(A;;GA;;;AU)");
        return descriptor.ToString();
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
        while (pipe.IsConnected && !cancellationToken.IsCancellationRequested)
        {
            string? line = await ReadBoundedLineAsync(reader, cancellationToken);
            if (line is null) return;
            SearchResponse response;
            try
            {
                SearchRequest? request = JsonSerializer.Deserialize<SearchRequest>(line);
                response = Process(request);
            }
            catch (JsonException)
            {
                response = new("", "error", "Invalid request");
            }
            catch (Exception ex)
            {
                ServiceLog.Write("Search request failed", ex);
                response = new("", "error", "搜索服务处理请求时出错，请查看服务日志。");
            }
            await writer.WriteLineAsync(JsonSerializer.Serialize(response).AsMemory(), cancellationToken);
        }
    }

    private SearchResponse Process(SearchRequest? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Id)) return new("", "error", "Invalid request");
        var store = Volatile.Read(ref _store);
        if (store is null)
            return new(request.Id, "initializing", "搜索索引正在初始化，请稍后重试。");
        switch (request.Operation)
        {
            case "status":
                var indexStatus = store.GetStatus();
                return new(request.Id, indexStatus.State,
                    indexStatus.State == "ready" ? $"{indexStatus.ReadyVolumes}/{indexStatus.TotalVolumes} 个卷已就绪"
                    : indexStatus.State == "indexing" ? $"正在建立索引（{indexStatus.ReadyVolumes}/{indexStatus.TotalVolumes}）"
                    : "尚无可用的 NTFS 索引卷。");
            case "search":
                if (request.Query is { Length: > PipeProtocol.MaxQueryLength })
                    return new(request.Id, "error", "Query must contain 0 to 512 characters.");
                // An empty query is valid and means "list everything of this kind", like
                // Everything does before the user types anything.
                string query = request.Query?.Trim() ?? "";
                string kind = string.IsNullOrWhiteSpace(request.Kind) ? "all" : request.Kind;
                if (kind is not ("all" or "application" or "image" or "audio" or "video" or "archive"))
                    return new(request.Id, "error", "Unsupported file type filter.");
                int limit = Math.Clamp(request.Limit, 1, PipeProtocol.MaxPageSize);
                SearchSort sort = request.Sort switch
                {
                    "size" => SearchSort.Size,
                    "time" => SearchSort.Time,
                    _ => SearchSort.Name
                };
                var rows = store.SearchNames(query, kind, limit, request.AfterName, request.AfterVolumeId,
                    request.AfterFileId, request.AfterSize, request.AfterModified, request.Descending, sort);
                bool hasMore = rows.Count > limit;
                var page = rows.Take(limit).ToList();
                if (page.Count == 0 && !hasMore)
                {
                    var pending = store.GetStatus();
                    if (pending.ReadyVolumes < pending.TotalVolumes)
                        return new(request.Id, "ok", $"无匹配结果（索引 {pending.ReadyVolumes}/{pending.TotalVolumes}）", page, false);
                    if (query.Length == 0)
                        return new(request.Id, "ok", "索引中还没有文件。", page, false);
                }
                var last = page.LastOrDefault();
                return new(request.Id, "ok", Results: page, HasMore: hasMore,
                    NextName: last?.Name, NextVolumeId: last?.VolumeId, NextFileId: last?.FileId,
                    NextSize: last?.Size, NextModified: last?.ModifiedTicks);
            default:
                return new(request.Id, "error", "Unsupported operation");
        }
    }

    private static async Task<string?> ReadBoundedLineAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        char[] one = new char[1];
        while (builder.Length <= 4096)
        {
            int count = await reader.ReadAsync(one.AsMemory(), cancellationToken);
            if (count == 0) return builder.Length == 0 ? null : builder.ToString();
            if (one[0] == '\n') return builder.ToString().TrimEnd('\r');
            builder.Append(one[0]);
        }
        throw new InvalidDataException("Named pipe request exceeds the maximum message size.");
    }

    private static NamedPipeServerStream CreateServerPipe(string sddl)
    {
        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl, 1, out IntPtr descriptor, out _))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Cannot build named pipe ACL");
        try
        {
            var attributes = new SecurityAttributes
            {
                Length = Marshal.SizeOf<SecurityAttributes>(),
                SecurityDescriptor = descriptor,
                InheritHandle = false
            };
            SafePipeHandle handle = CreateNamedPipeW("\\\\.\\pipe\\" + PipeProtocol.PipeName,
                PipeAccessDuplex | FileFlagOverlapped,
                PipeTypeByte | PipeReadModeByte | PipeWait | PipeRejectRemoteClients,
                PipeUnlimitedInstances, 64 * 1024, 64 * 1024, 0, ref attributes);
            if (handle.IsInvalid) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Cannot create search named pipe");
            return new NamedPipeServerStream(PipeDirection.InOut, isAsync: true, isConnected: false, handle);
        }
        finally { LocalFree(descriptor); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes { public int Length; public IntPtr SecurityDescriptor; [MarshalAs(UnmanagedType.Bool)] public bool InheritHandle; }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string sddl, uint revision, out IntPtr descriptor, out uint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr LocalFree(IntPtr memory);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafePipeHandle CreateNamedPipeW(string name, uint openMode, uint pipeMode, uint maxInstances,
        uint outBufferSize, uint inBufferSize, uint defaultTimeout, ref SecurityAttributes securityAttributes);
}
