using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Win11DesktopApp.Services
{
    public static class SafeFileService
    {
        public static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };

        public static T? ReadJson<T>(string path, JsonSerializerOptions? options = null, Encoding? encoding = null)
        {
            var json = ReadAllText(path, encoding);
            return JsonSerializer.Deserialize<T>(json, options);
        }

        public static T ReadJsonOrDefault<T>(string path, T fallback, JsonSerializerOptions? options = null, Encoding? encoding = null)
        {
            if (!File.Exists(path))
                return fallback;

            var value = ReadJson<T>(path, options, encoding);
            return value is null ? fallback : value;
        }

        public static string ReadAllText(string path, Encoding? encoding = null)
        {
            return RetryHelper.Execute(() =>
                encoding == null
                    ? File.ReadAllText(path)
                    : File.ReadAllText(path, encoding));
        }

        public static byte[] ReadAllBytes(string path)
        {
            return RetryHelper.Execute(() => File.ReadAllBytes(path));
        }

        public static void WriteJsonAtomic<T>(string path, T value, JsonSerializerOptions? options = null, Encoding? encoding = null)
        {
            var json = JsonSerializer.Serialize(value, options ?? IndentedJsonOptions);
            WriteTextAtomic(path, json, encoding);
        }

        public static void WriteTextAtomic(string path, string content, Encoding? encoding = null)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var tempPath = CreateTempPath(path);

            RetryHelper.Execute(() =>
            {
                try
                {
                    if (encoding == null)
                        File.WriteAllText(tempPath, content);
                    else
                        File.WriteAllText(tempPath, content, encoding);

                    ReplaceFile(tempPath, path);
                }
                finally
                {
                    CleanupTempFile(tempPath);
                }
            });
        }

        public static void WriteBytesAtomic(string path, byte[] content)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var tempPath = CreateTempPath(path);

            RetryHelper.Execute(() =>
            {
                try
                {
                    File.WriteAllBytes(tempPath, content);
                    ReplaceFile(tempPath, path);
                }
                finally
                {
                    CleanupTempFile(tempPath);
                }
            });
        }

        private static void ReplaceFile(string sourcePath, string destinationPath)
        {
            PrepareFileForOverwrite(destinationPath);

            if (File.Exists(destinationPath))
            {
                File.Replace(sourcePath, destinationPath, null, true);
            }
            else
            {
                File.Move(sourcePath, destinationPath);
            }
        }

        private static string CreateTempPath(string destinationPath)
        {
            var directory = Path.GetDirectoryName(destinationPath);
            if (string.IsNullOrWhiteSpace(directory))
                directory = Directory.GetCurrentDirectory();

            var fileName = Path.GetFileName(destinationPath);
            return Path.Combine(directory, $"{fileName}.{Guid.NewGuid():N}.tmp");
        }

        private static void CleanupTempFile(string path)
        {
            if (!File.Exists(path))
                return;

            PrepareFileForOverwrite(path);
            File.Delete(path);
        }

        private static void PrepareFileForOverwrite(string path)
        {
            if (!File.Exists(path))
                return;

            File.SetAttributes(path, FileAttributes.Normal);
        }
    }
}
