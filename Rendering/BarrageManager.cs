using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using NotiFlow.Models;
using NotiFlow.Services;

namespace NotiFlow.Rendering
{
    /// <summary>
    /// 多显示器弹幕渲染管理中心。
    /// 为每个活跃显示器维护独立的顶层合成窗口，实现物理多屏隔离、精准定位与无缝流转。
    /// </summary>
    public class BarrageManager : IDisposable
    {
        public static BarrageManager? Instance { get; private set; }

        private readonly Dictionary<string, BarrageOverlayWindow> _windows = new(StringComparer.OrdinalIgnoreCase);
        private bool _disposed;

        public bool IsAnyWindowVisible => _windows.Values.Any(w => w.IsVisible);

        public BarrageManager()
        {
            Instance = this;

            ScreenService.DisplaySettingsChanged += OnDisplaySettingsChanged;

            if (NotificationService.Instance != null)
            {
                NotificationService.Instance.OnNotificationReceived += OnNotificationReceived;
            }
        }

        private void OnDisplaySettingsChanged()
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                if (_disposed) return;
                SyncWindows();
            });
        }

        private void OnNotificationReceived(NotificationMessage msg)
        {
            if (!BarrageSettings.IsWorking) return;

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                if (_disposed) return;
                EnqueueNotification(msg);
            });
        }

        /// <summary>
        /// 同步当前系统连接并启用的所有显示器窗口。
        /// </summary>
        public void SyncWindows()
        {
            if (_disposed) return;

            // 重新枚举系统屏幕并与用户保存的偏好合并
            var currentMonitors = ScreenService.GetMergedMonitors(BarrageSettings.Monitors);
            BarrageSettings.Monitors = currentMonitors;

            var enabledMonitors = currentMonitors.Where(m => m.IsEnabled).ToList();
            if (enabledMonitors.Count == 0)
            {
                var primary = currentMonitors.FirstOrDefault(m => m.IsPrimary) ?? currentMonitors.FirstOrDefault();
                if (primary != null)
                {
                    enabledMonitors.Add(primary);
                }
            }

            var enabledDeviceNames = new HashSet<string>(enabledMonitors.Select(m => m.DeviceName), StringComparer.OrdinalIgnoreCase);

            // 1. 关闭并移除已失效或已禁用的显示器窗口
            var keysToRemove = _windows.Keys.Where(k => !enabledDeviceNames.Contains(k)).ToList();
            foreach (var key in keysToRemove)
            {
                if (_windows.TryGetValue(key, out var win))
                {
                    win.Dispose();
                    _windows.Remove(key);
                }
            }

            // 2. 为新增或发生位置/分辨率变动的显示器创建/更新窗口
            foreach (var monitor in enabledMonitors)
            {
                if (_windows.TryGetValue(monitor.DeviceName, out var existingWin))
                {
                    existingWin.UpdateBounds(monitor);
                }
                else
                {
                    var newWin = new BarrageOverlayWindow(monitor);
                    newWin.OnBarrageSequenceExit += HandleBarrageSequenceExit;
                    _windows[monitor.DeviceName] = newWin;
                }
            }
        }

        /// <summary>
        /// 派发新通知消息到各屏幕。
        /// </summary>
        public void EnqueueNotification(NotificationMessage msg)
        {
            SyncWindows();

            var orderedActiveMonitors = BarrageSettings.Monitors
                .Where(m => m.IsEnabled && _windows.ContainsKey(m.DeviceName))
                .ToList();

            if (orderedActiveMonitors.Count == 0) return;

            if (BarrageSettings.MultiMonitorMode == "Sequential")
            {
                // 按顺序流转模式：从第 1 个显示器开始启动
                var firstMon = orderedActiveMonitors[0];
                if (_windows.TryGetValue(firstMon.DeviceName, out var win))
                {
                    win.EnqueueBarrage(msg, 0, orderedActiveMonitors);
                }
            }
            else
            {
                // 同时显示模式：在所有启用的显示器窗口上并发触发弹幕
                foreach (var mon in orderedActiveMonitors)
                {
                    if (_windows.TryGetValue(mon.DeviceName, out var win))
                    {
                        win.EnqueueBarrage(msg, -1, null);
                    }
                }
            }
        }

        /// <summary>
        /// 当某个显示器的弹幕完全移出屏幕时触发的接力流转回调。
        /// </summary>
        private void HandleBarrageSequenceExit(NotificationMessage msg, int nextSequenceIndex, List<MonitorSettingItemDto> sequence)
        {
            if (_disposed || !BarrageSettings.IsWorking) return;

            if (nextSequenceIndex >= 0 && nextSequenceIndex < sequence.Count)
            {
                var nextMon = sequence[nextSequenceIndex];
                if (_windows.TryGetValue(nextMon.DeviceName, out var nextWin))
                {
                    nextWin.EnqueueBarrage(msg, nextSequenceIndex, sequence);
                }
            }
        }

        public void ShowAll()
        {
            SyncWindows();
            foreach (var win in _windows.Values)
            {
                win.Show();
            }
        }

        public void HideAll()
        {
            foreach (var win in _windows.Values)
            {
                win.Hide();
            }
        }

        public void ApplyCaptureSetting()
        {
            foreach (var win in _windows.Values)
            {
                win.ApplyCaptureSetting();
            }
        }

        public void ReRegisterHotKey()
        {
            var primaryWin = _windows.Values.FirstOrDefault(w => w.Monitor.IsPrimary) ?? _windows.Values.FirstOrDefault();
            primaryWin?.ReRegisterHotKey();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            ScreenService.DisplaySettingsChanged -= OnDisplaySettingsChanged;

            foreach (var win in _windows.Values)
            {
                win.Dispose();
            }
            _windows.Clear();
        }
    }
}
