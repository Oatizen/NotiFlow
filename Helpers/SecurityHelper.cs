using System;
using System.Security.Cryptography;
using System.Text;

namespace NotiFlow.Helpers
{
    /// <summary>
    /// 提供基于 Windows DPAPI 的本地敏感数据安全加解密服务。
    /// 加密密钥由当前登录的 Windows 用户账号绑定，脱离当前设备或用户环境无法解密。
    /// </summary>
    public static class SecurityHelper
    {
        private static readonly byte[] OptionalEntropy = Encoding.UTF8.GetBytes("NotiFlow_Email_Security_Entropy_v1");

        /// <summary>
        /// 使用 Windows DPAPI 加密明文字符串并返回 Base64 密文字符串。
        /// </summary>
        /// <param name="plainText">待加密的明文字符串（如邮箱授权码）</param>
        /// <returns>Base64 编码的密文字符串，若输入为空则返回空字符串</returns>
        public static string EncryptString(string? plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return string.Empty;

            try
            {
                byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
                byte[] cipherBytes = ProtectedData.Protect(
                    plainBytes,
                    OptionalEntropy,
                    DataProtectionScope.CurrentUser);

                return Convert.ToBase64String(cipherBytes);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SecurityHelper] DPAPI 加密失败: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// 使用 Windows DPAPI 解密 Base64 密文字符串并返回明文。
        /// </summary>
        /// <param name="cipherText">Base64 编码的密文字符串</param>
        /// <returns>解密后的明文字符串，若解密失败或输入为空则返回空字符串</returns>
        public static string DecryptString(string? cipherText)
        {
            if (string.IsNullOrEmpty(cipherText))
                return string.Empty;

            try
            {
                byte[] cipherBytes = Convert.FromBase64String(cipherText);
                byte[] plainBytes = ProtectedData.Unprotect(
                    cipherBytes,
                    OptionalEntropy,
                    DataProtectionScope.CurrentUser);

                return Encoding.UTF8.GetString(plainBytes);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SecurityHelper] DPAPI 解密失败: {ex.Message}");
                return string.Empty;
            }
        }
    }
}
