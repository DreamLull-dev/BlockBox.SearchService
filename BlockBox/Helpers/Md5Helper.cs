using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace BlockBox.Helpers
{
    public static class Md5Helper
    {
        public static async Task<string> ComputeAsync(string filePath,
            CancellationToken ct = default)
        {
            return await Task.Run(() =>
            {
                using var stream = File.OpenRead(filePath);
                using var md5 = MD5.Create();
                var hash = md5.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }, ct);
        }

        public static string Compute(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }
}
