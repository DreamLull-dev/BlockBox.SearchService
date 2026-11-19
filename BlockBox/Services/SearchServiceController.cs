using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace BlockBox.Services
{
    internal enum SearchServiceState
    {
        /// <summary>服务没有注册（例如只装了商店版应用，还没装伴侣 MSIX）。</summary>
        NotInstalled,
        /// <summary>服务已注册但没有运行。</summary>
        Stopped,
        /// <summary>服务在运行（可以正常搜索）。</summary>
        Running,
        /// <summary>查询失败（例如连不上服务控制管理器），调用方可以退回到“直接连管道试试”。</summary>
        Unknown
    }

    /// <summary>
    /// 查询 / 启动 BlockBox 全局搜索服务。用 P/Invoke 而不是 System.ServiceProcess 包，
    /// 这样不需要额外 NuGet 依赖，普通权限也能读取服务状态。
    /// </summary>
    internal static class SearchServiceController
    {
        internal const string ServiceName = "BlockBoxGlobalSearch";

        private const uint ScManagerConnect = 0x0001;
        private const uint ServiceQueryStatus = 0x0004;
        private const uint ServiceRunning = 0x00000004;
        private const uint ServiceStartPending = 0x00000002;
        private const int ErrorServiceDoesNotExist = 1060;

        internal static SearchServiceState QueryState()
        {
            IntPtr scm = OpenSCManager(null, null, ScManagerConnect);
            if (scm == IntPtr.Zero) return SearchServiceState.Unknown;

            try
            {
                IntPtr service = OpenService(scm, ServiceName, ServiceQueryStatus);
                if (service == IntPtr.Zero)
                {
                    int error = Marshal.GetLastWin32Error();
                    return error == ErrorServiceDoesNotExist ? SearchServiceState.NotInstalled : SearchServiceState.Unknown;
                }

                try
                {
                    var status = default(ServiceStatus);
                    if (!QueryServiceStatus(service, ref status)) return SearchServiceState.Unknown;
                    return status.CurrentState is ServiceRunning or ServiceStartPending
                        ? SearchServiceState.Running
                        : SearchServiceState.Stopped;
                }
                finally { CloseServiceHandle(service); }
            }
            finally { CloseServiceHandle(scm); }
        }

        /// <summary>以管理员权限执行 sc start（会弹 UAC）。返回是否成功发起启动。</summary>
        internal static async Task<bool> TryStartAsync()
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = System.IO.Path.Combine(Environment.SystemDirectory, "sc.exe"),
                    Arguments = "start " + ServiceName,
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using Process? process = Process.Start(startInfo);
                if (process is null) return false;
                await process.WaitForExitAsync();
                return true;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                // 用户取消了 UAC，或者系统拒绝启动服务。
                return false;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ServiceStatus
        {
            public uint ServiceType;
            public uint CurrentState;
            public uint ControlsAccepted;
            public uint Win32ExitCode;
            public uint ServiceSpecificExitCode;
            public uint CheckPoint;
            public uint WaitHint;
        }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr OpenService(IntPtr scm, string serviceName, uint desiredAccess);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryServiceStatus(IntPtr service, ref ServiceStatus status);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseServiceHandle(IntPtr handle);
    }
}
