using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;

namespace BlockBox.Helpers
{
    internal static class GlassBrushHelper
    {
        private static Brush? _sharedBrush;
        private static bool _creationAttempted;
        private static bool _errorReported;
        private static bool _errorCheckScheduled;
        private static readonly List<Action> _errorCallbacks = new();

        public static Brush? CreateGlassBrush()
        {
#if LIQUID_GLASS
            if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
                return null;

            if (_creationAttempted)
                return _sharedBrush;

            _creationAttempted = true;
            try
            {
                var brushType = Type.GetType("LiquidGlassWinUI.LiquidGlassBrush, LiquidGlassWinUI", throwOnError: true);
                _sharedBrush = Activator.CreateInstance(brushType!) as Brush;
                if (_sharedBrush == null)
                    Debug.WriteLine("[GlassTheme] LiquidGlassBrush 实例未实现 Brush。");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GlassTheme] LiquidGlassBrush 创建失败: {ex}");
            }

            return _sharedBrush;
#else
            return null;
#endif
        }

        public static void ScheduleLastErrorCheck(DispatcherQueue dispatcherQueue, Action? onError = null)
        {
#if LIQUID_GLASS
            if (_sharedBrush == null)
                return;

            if (onError != null)
            {
                if (_errorReported)
                    onError();
                else
                    _errorCallbacks.Add(onError);
            }

            if (_errorReported || _errorCheckScheduled)
                return;

            _errorCheckScheduled = true;
            var timer = dispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(250);
            timer.IsRepeating = true;
            int attempts = 0;
            timer.Tick += (_, _) =>
            {
                attempts++;
                try
                {
                    // LiquidGlassWinUI exposes LastError as a static property.
                    // Reading it as an instance property throws and previously hid
                    // the real shader/native-hook failure behind the red fallback.
                    PropertyInfo? lastErrorProperty = _sharedBrush.GetType().GetProperty("LastError", BindingFlags.Static | BindingFlags.Public);
                    object? lastError = lastErrorProperty?.GetValue(null);
                    string message = lastError?.ToString() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        timer.Stop();
                        _errorReported = true;
                        _errorCheckScheduled = false;
                        Debug.WriteLine($"[GlassTheme] LiquidGlassBrush.LastError: {message}");
                        foreach (var callback in _errorCallbacks.ToArray())
                            callback();
                        _errorCallbacks.Clear();
                    }
                    else if (attempts >= 20)
                    {
                        timer.Stop();
                        _errorCheckScheduled = false;
                        Debug.WriteLine("[GlassTheme] LiquidGlassBrush.LastError: <empty after 5s>");
                    }
                }
                catch (Exception ex)
                {
                    timer.Stop();
                    _errorReported = true;
                    _errorCheckScheduled = false;
                    Debug.WriteLine($"[GlassTheme] 读取 LiquidGlassBrush.LastError 失败: {ex}");
                }
            };
            timer.Start();
#endif
        }
    }
}
