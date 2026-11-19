using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BlockBox.Helpers
{
    /// <summary>
    /// 启动时间线日志：记录 OnLaunched 各阶段的耗时。写文件失败会立刻停用日志，
    /// 绝不重复抛异常，也绝不阻塞启动。
    /// 日志位置优先用应用自己的数据目录（那里一定可写），其次 %LOCALAPPDATA%\BlockBox，
    /// 最后才用临时目录；可以用环境变量 BLOCKBOX_STARTUP_LOG 强制指定。
    /// </summary>
    public static class StartupLog
    {
        private const long MaxBytes = 512 * 1024;
        private static readonly object Gate = new();
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        // Signalled by Done(). A timed semaphore wait returns false on timeout and throws
        // nothing, unlike Task.Delay(..., token): the TaskCanceledException it raised when the
        // watchdog stopped was caught here but still broke the debugger on every shutdown.
        private static readonly SemaphoreSlim WatchdogStop = new(0, 1);
        private static bool _started;
        private static bool _disabled;
        private static string? _path;
        private static string _phase = "（未开始）";
        private static long _phaseAt;

        private static string? ResolvePath()
        {
            if (_path is not null) return _path;
            string? forced = Environment.GetEnvironmentVariable("BLOCKBOX_STARTUP_LOG");
            if (!string.IsNullOrWhiteSpace(forced))
                return _path = forced;

            foreach (string directory in Candidates())
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(directory)) continue;
                    Directory.CreateDirectory(directory);
                    return _path = Path.Combine(directory, "startup.log");
                }
                catch
                {
                    // 换下一个候选目录。
                }
            }
            return null;
        }

        private static string?[] Candidates() => new[]
        {
            TryGetDataRoot(),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlockBox"),
            Path.Combine(Path.GetTempPath(), "BlockBox")
        };

        private static string? TryGetDataRoot()
        {
            try
            {
                string root = AppDataPaths.Root;
                return string.IsNullOrWhiteSpace(root) ? null : root;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[StartupLog] 无法解析数据目录: " + ex.Message);
                return null;
            }
        }

        /// <summary>每次启动清空重建，只保留本次启动的记录。</summary>
        public static void Begin()
        {
            if (_started) return;
            _started = true;
            Interlocked.Exchange(ref _phaseAt, Clock.ElapsedMilliseconds);
            string? path = ResolvePath();
            if (path is null) { _disabled = true; return; }
            try
            {
                File.WriteAllText(path,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} pid={Environment.ProcessId} start{Environment.NewLine}");
            }
            catch (Exception ex)
            {
                _disabled = true;
                Debug.WriteLine("[StartupLog] 初始化失败: " + ex.Message);
                return;
            }

            _ = Task.Run(async () =>
            {
                long lastReported = 0;
                while (true)
                {
                    if (await WatchdogStop.WaitAsync(TimeSpan.FromSeconds(5))) return;
                    if (_disabled) return;
                    long now = Clock.ElapsedMilliseconds;
                    if (now - Interlocked.Read(ref _phaseAt) < 15000 || now - lastReported < 15000) continue;
                    lastReported = now;
                    Append($"{(now - Interlocked.Read(ref _phaseAt)) / 1000,4} s 无进展，仍在: {_phase}");
                }
            });
        }

        public static void Step(string message)
        {
            Interlocked.Exchange(ref _phaseAt, Clock.ElapsedMilliseconds);
            _phase = message;
            Append($"{Clock.ElapsedMilliseconds,7} ms  {message}");
        }

        public static void Error(string where, Exception exception) =>
            Step($"!! {where}: {exception.GetType().Name}: {exception.Message}");

        public static void Done()
        {
            try { WatchdogStop.Release(); }
            catch (SemaphoreFullException) { }
        }

        private static void Append(string line)
        {
            Debug.WriteLine("[Startup] " + line);
            if (_disabled) return;
            try
            {
                string? path = ResolvePath();
                if (path is null) { _disabled = true; return; }
                lock (Gate)
                {
                    var info = new FileInfo(path);
                    if (info.Exists && info.Length > MaxBytes) return;
                    File.AppendAllText(path, line + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                // 只失败一次：之后彻底停用，避免每步都抛异常（调试器会一次次停下来）。
                _disabled = true;
                Debug.WriteLine("[StartupLog] 写入失败，已停用: " + ex.Message);
            }
        }
    }
}
