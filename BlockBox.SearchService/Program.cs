using System.Runtime.InteropServices;

namespace BlockBox.SearchService;

internal static class Program
{
    private const string ServiceName = "BlockBoxGlobalSearch";
    private const int StoreOpenAttempts = 3;
    private static readonly ServiceMainDelegate ServiceMainRoot = ServiceMain;
    private static readonly HandlerDelegate HandlerRoot = ServiceHandler;
    private static readonly ManualResetEventSlim StopSignal = new(false);
    private static CancellationTokenSource? _stop;
    private static IntPtr _statusHandle;
    private static ServiceStatus _status;

    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--console")
        {
            ServiceLog.EchoToConsole = true;
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
            try { return RunAsync(cts.Token).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { return 0; }
            catch (Exception ex) { ServiceLog.Write("search service stopped with an unhandled error", ex); return 1; }
        }
        var table = new[]
        {
            new ServiceTableEntry { Name = ServiceName, Main = Marshal.GetFunctionPointerForDelegate(ServiceMainRoot) },
            new ServiceTableEntry()
        };
        return StartServiceCtrlDispatcher(table) ? 0 : Marshal.GetLastWin32Error();
    }

    private static void ServiceMain(uint argc, IntPtr argv)
    {
        _statusHandle = RegisterServiceCtrlHandlerEx(ServiceName, HandlerRoot, IntPtr.Zero);
        if (_statusHandle == IntPtr.Zero) return;
        SetStatus(ServiceState.StartPending, 0);
        _stop = new CancellationTokenSource();
        SetStatus(ServiceState.Running, 0);
        try { RunAsync(_stop.Token).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ServiceLog.Write("Global search service stopped", ex); }
        SetStatus(ServiceState.Stopped, 0);
    }

    private static uint ServiceHandler(uint control, uint eventType, IntPtr eventData, IntPtr context)
    {
        if (control == 1) // SERVICE_CONTROL_STOP
        {
            SetStatus(ServiceState.StopPending, 5000U);
            _stop?.Cancel();
            StopSignal.Set();
        }
        return 0;
    }

    private static async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        string dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BlockBox", "Search");
        Directory.CreateDirectory(dataDirectory);
        string sidFile = Path.Combine(dataDirectory, "client.sid");
        string? clientSid = File.Exists(sidFile) ? File.ReadAllText(sidFile).Trim() : null;
        ServiceLog.Write($"Starting in {dataDirectory}; client SID {(clientSid is null ? "MISSING (run Install-SearchService.ps1)" : "configured")}.");

        var pipe = new SearchPipeServer(clientSid);
        Task serving = pipe.RunAsync(cancellationToken);

        // Opening the index can be slow and can fail (missing permissions, a stale WAL that has to
        // be recovered). Retry instead of crashing: the pipe keeps answering "initializing".
        SearchIndexStore? store = null;
        for (int attempt = 1; store is null && !cancellationToken.IsCancellationRequested; attempt++)
        {
            try
            {
                store = await Task.Run(() => new SearchIndexStore(Path.Combine(dataDirectory, "search-index.db")), cancellationToken);
                var indexStatus = store.GetStatus();
                ServiceLog.Write($"Search index opened: state={indexStatus.State}, ready={indexStatus.ReadyVolumes}/{indexStatus.TotalVolumes}.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                ServiceLog.Write($"Cannot open the search index (attempt {attempt}/{StoreOpenAttempts})", ex);
                if (attempt >= StoreOpenAttempts) break;
                try { await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        if (store is null)
        {
            ServiceLog.Write("Search index could not be opened; stopping the service.");
            return 1;
        }

        pipe.SetStore(store);
        var indexer = new IndexingCoordinator(store);
        Task indexing = Task.CompletedTask;
        if (clientSid is null)
        {
            ServiceLog.Write("Indexing is disabled because client.sid is missing.");
        }
        else
        {
            // Starting the indexer can perform synchronous volume/MFT work before its first
            // await. Keep it off the service entry thread so the named pipe accepts clients
            // immediately while a first-time index is being built.
            indexing = Task.Run(() => indexer.IndexFixedNtfsVolumesAsync(cancellationToken), cancellationToken);
        }

        // The pipe is the primary loop: it must stay up even if indexing fails, and indexing must
        // keep running if a client connection fails. Both end only when the service is stopped.
        try { await serving; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) { ServiceLog.Write("Search pipe server failed", ex); }
        try { await indexing; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) { ServiceLog.Write("Indexer failed", ex); }
        indexer.Stop();
        ServiceLog.Write("Search service stopped.");
        return 0;
    }

    private static void SetStatus(ServiceState state, uint waitHint)
    {
        _status.ServiceType = 0x10;
        _status.CurrentState = (uint)state;
        _status.ControlsAccepted = state == ServiceState.Running ? 1U : 0U;
        _status.Win32ExitCode = 0;
        _status.WaitHint = waitHint;
        SetServiceStatus(_statusHandle, ref _status);
    }

    private enum ServiceState : uint { StopPending = 3, Stopped = 1, StartPending = 2, Running = 4 }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ServiceTableEntry { public string? Name; public IntPtr Main; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint ServiceType, CurrentState, ControlsAccepted, Win32ExitCode, ServiceSpecificExitCode, CheckPoint, WaitHint;
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void ServiceMainDelegate(uint argc, IntPtr argv);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate uint HandlerDelegate(uint control, uint eventType, IntPtr eventData, IntPtr context);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool StartServiceCtrlDispatcher([In] ServiceTableEntry[] table);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr RegisterServiceCtrlHandlerEx(string serviceName, HandlerDelegate handler, IntPtr context);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetServiceStatus(IntPtr handle, ref ServiceStatus status);
}
