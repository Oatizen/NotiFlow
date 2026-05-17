using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NotiFlow.Services
{
    public static class IconCacheService
    {
        private static readonly string CacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NotiFlow",
            "Icons");

        private const int MaxCacheFiles = 500;

        static IconCacheService()
        {
            if (!Directory.Exists(CacheDirectory))
            {
                Directory.CreateDirectory(CacheDirectory);
            }
        }

        private static string GetSafeFileName(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return "default.png";
            
            // 使用 MD5 生成安全唯一的文件名，避免不同应用的 AUMID 包含路径特殊字符
            using var md5 = MD5.Create();
            byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(identifier.ToLowerInvariant()));
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant() + ".png";
        }

        /// <summary>
        /// 从本地缓存加载应用图标。
        /// </summary>
        public static async Task<ImageSource?> GetIconAsync(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return null;

            string filePath = Path.Combine(CacheDirectory, GetSafeFileName(identifier));
            if (!File.Exists(filePath)) return null;

            try
            {
                // 使用后台线程读取文件流，避免阻塞 UI
                byte[] fileBytes = await Task.Run(() => File.ReadAllBytes(filePath));

                ImageSource? result = null;
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad; // 立即加载入内存，解除文件占用
                        bitmap.StreamSource = new MemoryStream(fileBytes);
                        bitmap.EndInit();
                        bitmap.Freeze();
                        result = bitmap;
                        
                        // 更新文件的最后访问时间，用于后续清理策略
                        Task.Run(() => { try { File.SetLastAccessTime(filePath, DateTime.Now); } catch { } });
                    }
                    catch { }
                });

                return result;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 将 GDI+ 句柄提取的高清图标存入本地缓存。
        /// </summary>
        public static async Task CacheIconAsync(string identifier, IntPtr hIcon)
        {
            if (string.IsNullOrEmpty(identifier) || hIcon == IntPtr.Zero) return;

            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    var bitmap = Imaging.CreateBitmapSourceFromHIcon(
                        hIcon,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                        
                    await CacheIconInternalAsync(identifier, bitmap);
                }
                catch { }
            });
        }

        /// <summary>
        /// 将 WPF BitmapSource 存入本地缓存（适用于从 UWP 管线提取的图标）。
        /// </summary>
        public static async Task CacheIconAsync(string identifier, BitmapSource bitmap)
        {
            if (string.IsNullOrEmpty(identifier) || bitmap == null) return;
            await CacheIconInternalAsync(identifier, bitmap);
        }

        private static async Task CacheIconInternalAsync(string identifier, BitmapSource bitmap)
        {
            string filePath = Path.Combine(CacheDirectory, GetSafeFileName(identifier));

            try
            {
                // 如果文件已存在，说明已经缓存过了，直接跳过以节省磁盘 I/O
                // 如果想支持图标更新，可以判断文件修改时间
                if (File.Exists(filePath)) return;

                byte[] pngBytes = await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = new MemoryStream();
                    encoder.Save(stream);
                    return stream.ToArray();
                });

                await Task.Run(() =>
                {
                    File.WriteAllBytes(filePath, pngBytes);
                    CleanupCache();
                });
            }
            catch { }
        }

        /// <summary>
        /// 清理旧缓存：当文件数量超过最大限制时，删除最旧的文件（按照最后访问时间）。
        /// </summary>
        private static void CleanupCache()
        {
            try
            {
                var dir = new DirectoryInfo(CacheDirectory);
                var files = dir.GetFiles("*.png");

                if (files.Length <= MaxCacheFiles) return;

                // 按照最后访问时间升序（最旧的在前面）
                var oldFiles = files.OrderBy(f => f.LastAccessTime).Take(files.Length - MaxCacheFiles);
                foreach (var file in oldFiles)
                {
                    try
                    {
                        file.Delete();
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}
