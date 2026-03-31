using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Win11DesktopApp.Services
{
    public sealed class SafeFileRecoveryException : Exception
    {
        public string DestinationPath { get; }
        public string RecoveryPath { get; }

        public SafeFileRecoveryException(string destinationPath, string recoveryPath)
            : base($"Could not update '{destinationPath}'. New data was saved to recovery file '{recoveryPath}'.")
        {
            DestinationPath = destinationPath;
            RecoveryPath = recoveryPath;
        }
    }

    public static class SafeFileService
    {
        private sealed class ReplaceOutcome
        {
            public bool Success { get; init; }
            public bool RecoveryCreated { get; init; }
            public string? RecoveryPath { get; init; }
        }

        public static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };

        public static T? ReadJson<T>(string path, JsonSerializerOptions? options = null, Encoding? encoding = null)
        {
            var json = ReadAllText(path, encoding);
            return JsonSerializer.Deserialize<T>(json, options);
        }

        public static T? ReadJsonShared<T>(string path, JsonSerializerOptions? options = null, Encoding? encoding = null)
        {
            var json = ReadAllTextShared(path, encoding);
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

        public static string ReadAllTextShared(string path, Encoding? encoding = null)
        {
            return RetryHelper.Execute(() =>
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var reader = encoding == null
                    ? new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true)
                    : new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true);
                return reader.ReadToEnd();
            });
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
            WriteAtomic(path, tempPath =>
            {
                if (encoding == null)
                    File.WriteAllText(tempPath, content);
                else
                    File.WriteAllText(tempPath, content, encoding);
            });
        }

        public static void WriteBytesAtomic(string path, byte[] content)
        {
            WriteAtomic(path, tempPath => File.WriteAllBytes(tempPath, content));
        }

        public static void CopyFile(string source, string dest, bool overwrite = true)
        {
            EnsureDestinationDirectory(dest);
            RetryHelper.Execute(() => File.Copy(source, dest, overwrite));
        }

        public static void MoveFile(string source, string dest)
        {
            EnsureDestinationDirectory(dest);
            RetryHelper.Execute(() =>
            {
                PrepareFileForOverwrite(dest);
                File.Move(source, dest, overwrite: true);
            });
        }

        public static void DeleteFile(string path)
        {
            if (!File.Exists(path))
                return;

            RetryHelper.Execute(() =>
            {
                PrepareFileForOverwrite(path);
                File.Delete(path);
            });
        }

        private static void WriteAtomic(string path, Action<string> writeTempFile)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var tempPath = CreateTempPath(path);
            var shouldCleanupTemp = false;

            try
            {
                RetryHelper.Execute(() =>
                {
                    writeTempFile(tempPath);

                    var outcome = ReplaceFile(tempPath, path);
                    shouldCleanupTemp = outcome.Success || outcome.RecoveryCreated;

                    if (outcome.RecoveryCreated && !string.IsNullOrWhiteSpace(outcome.RecoveryPath))
                        throw new SafeFileRecoveryException(path, outcome.RecoveryPath);
                });
            }
            finally
            {
                if (shouldCleanupTemp)
                    CleanupTempFile(tempPath);
            }
        }

        private static ReplaceOutcome ReplaceFile(string sourcePath, string destinationPath)
        {
            PrepareFileForOverwrite(destinationPath);

            try
            {
                if (File.Exists(destinationPath))
                {
                    File.Replace(sourcePath, destinationPath, null, true);
                }
                else
                {
                    File.Move(sourcePath, destinationPath);
                }

                return new ReplaceOutcome
                {
                    Success = true
                };
            }
            catch (Exception replaceEx) when (replaceEx is IOException or UnauthorizedAccessException)
            {
                try
                {
                    PrepareFileForOverwrite(destinationPath);
                    File.Copy(sourcePath, destinationPath, overwrite: true);
                    return new ReplaceOutcome
                    {
                        Success = true
                    };
                }
                catch (Exception copyEx) when (copyEx is IOException or UnauthorizedAccessException)
                {
                    var recoveryPath = GetRecoveryPath(destinationPath);

                    try
                    {
                        PrepareFileForOverwrite(recoveryPath);
                        File.Copy(sourcePath, recoveryPath, overwrite: true);
                        return new ReplaceOutcome
                        {
                            RecoveryCreated = true,
                            RecoveryPath = recoveryPath
                        };
                    }
                    catch (Exception recoveryEx) when (recoveryEx is IOException or UnauthorizedAccessException)
                    {
                        throw new IOException(
                            $"Failed to replace '{destinationPath}' and failed to create recovery file '{recoveryPath}'.",
                            new AggregateException(replaceEx, copyEx, recoveryEx));
                    }
                }
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

        private static string GetRecoveryPath(string destinationPath)
        {
            var directory = Path.GetDirectoryName(destinationPath);
            if (string.IsNullOrWhiteSpace(directory))
                directory = Directory.GetCurrentDirectory();

            var fileName = Path.GetFileNameWithoutExtension(destinationPath);
            var extension = Path.GetExtension(destinationPath);
            return Path.Combine(directory, $"{fileName}.recovery{extension}");
        }

        private static void EnsureDestinationDirectory(string path)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
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
