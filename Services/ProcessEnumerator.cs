using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Windows;

namespace NotiFlow.Services
{
    /// <summary>
    /// 可供 UI 进程选择器使用的进程窗口信息。
    /// 后续 ProcessPickerWindow 直接绑定此类型的列表。
    /// </summary>
    public class WindowProcessInfo
    {
        /// <summary>进程可执行文件名（如 "powerpnt"），不含扩展名</summary>
        public string ProcessName { get; set; } = "";

        /// <summary>主窗口标题（如 "presentation1 - PowerPoint"）</summary>
        public string MainWindowTitle { get; set; } = "";

        /// <summary>可执行文件完整路径（用于提取图标）</summary>
        public string ExecutablePath { get; set; } = "";

        public int ProcessId { get; set; }
    }

    /// <summary>
    /// 进程枚举工具。
    /// 为 ProcessPickerWindow（后续 UI）枚举当前所有拥有可见窗口的进程。
    /// </summary>
    public static class ProcessEnumerator
    {
        /// <summary>
        /// 枚举当前所有拥有可见窗口的进程，按进程名排序。
        /// 每个进程只出现一次（按 PID 去重）。
        /// </summary>
        public static List<WindowProcessInfo> EnumerateWindowProcesses()
        {
            var result = new List<WindowProcessInfo>();
            var seen = new HashSet<int>(); // PID 去重
            int selfPid = Environment.ProcessId;

            NativeMethods.EnumWindows((hWnd, _) =>
            {
                if (!NativeMethods.IsWindowVisible(hWnd)) return true;

                NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
                if (pid == 0 || seen.Contains((int)pid)) return true;
                if ((int)pid == selfPid) return true; // 跳过自身

                int length = NativeMethods.GetWindowTextLength(hWnd);
                if (length == 0) return true;

                var sb = new StringBuilder(length + 1);
                NativeMethods.GetWindowText(hWnd, sb, sb.Capacity);
                string title = sb.ToString();
                if (string.IsNullOrWhiteSpace(title)) return true;

                seen.Add((int)pid);

                try
                {
                    var proc = Process.GetProcessById((int)pid);
                    string procName = proc.ProcessName;
                    string exePath = "";
                    try { exePath = proc.MainModule?.FileName ?? ""; }
                    catch { /* 权限不足，跳过 */ }

                    result.Add(new WindowProcessInfo
                    {
                        ProcessName = procName,
                        MainWindowTitle = title,
                        ExecutablePath = exePath,
                        ProcessId = (int)pid
                    });
                }
                catch
                {
                    // 权限不足或进程已退出
                }

                return true;
            }, IntPtr.Zero);

            result.Sort((a, b) => string.Compare(a.ProcessName, b.ProcessName, StringComparison.OrdinalIgnoreCase));
            return result;
        }
    }
}
