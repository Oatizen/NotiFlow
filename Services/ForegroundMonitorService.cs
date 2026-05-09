using System;
using System.Diagnostics;
using System.Windows.Threading;

namespace NotiFlow.Services
{
    /// <summary>
    /// 前台窗口轮询服务。
    /// 以 500ms 间隔检测当前前台窗口所属进程，判断是否应抑制弹幕显示。
    /// 仅在 SceneFilterMode != "Disabled" 时启动。
    /// </summary>
    public sealed class ForegroundMonitorService : IDisposable
    {
        private readonly DispatcherTimer _timer;
        private string _lastProcessName = "";

        public bool IsSceneSuppressed { get; private set; }

        public string CurrentForegroundProcess { get; private set; } = "";

        public ForegroundMonitorService()
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _timer.Tick += OnTick;
        }

        public void Start()
        {
            if (BarrageSettings.SceneFilterMode == "Disabled") return;
            _timer.Start();
        }

        public void Stop()
        {
            _timer.Stop();
            IsSceneSuppressed = false;
            CurrentForegroundProcess = "";
            _lastProcessName = "";
        }

        private void OnTick(object? sender, EventArgs e)
        {
            try
            {
                IntPtr hWnd = NativeMethods.GetForegroundWindow();
                if (hWnd == IntPtr.Zero) return;

                NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
                if (pid == 0) return;

                var proc = Process.GetProcessById((int)pid);
                string procName = proc.ProcessName;

                if (procName.Equals("NotiFlow", StringComparison.OrdinalIgnoreCase)) return;

                CurrentForegroundProcess = procName;

                if (procName.Equals(_lastProcessName, StringComparison.OrdinalIgnoreCase)) return;
                _lastProcessName = procName;

                IsSceneSuppressed = !ScopeFilter.ShouldAcceptScene(procName);
            }
            catch
            {
                // 前台窗口已销毁或权限不足，保持上次状态
            }
        }

        public void Dispose()
        {
            _timer.Stop();
        }
    }
}
