using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Win11DesktopApp.Services;

public sealed class SharedOperationLockService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(250);

    private readonly FolderService _folderService;
    private readonly CurrentProfileService _currentProfileService;

    public SharedOperationLockService(FolderService folderService, CurrentProfileService currentProfileService)
    {
        _folderService = folderService;
        _currentProfileService = currentProfileService;
    }

    public IDisposable? TryAcquire(string operation, TimeSpan timeout)
    {
        var folder = _folderService.GetLocksFolder();
        if (string.IsNullOrWhiteSpace(folder))
            return null;

        Directory.CreateDirectory(folder);
        var lockPath = Path.Combine(folder, Sanitize(operation) + ".lock");
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow <= deadline)
        {
            var acquired = TryCreateLock(lockPath, operation);
            if (acquired != null)
                return acquired;

            Thread.Sleep(RetryDelay);
        }

        return null;
    }

    /// <summary>
    /// Async acquire — use from UI flows so waiting on a contended OneDrive lock
    /// does not freeze the dispatcher (unlike <see cref="TryAcquire"/> + Thread.Sleep).
    /// </summary>
    public async Task<IDisposable?> TryAcquireAsync(string operation, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var folder = _folderService.GetLocksFolder();
        if (string.IsNullOrWhiteSpace(folder))
            return null;

        Directory.CreateDirectory(folder);
        var lockPath = Path.Combine(folder, Sanitize(operation) + ".lock");
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow <= deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var acquired = TryCreateLock(lockPath, operation);
            if (acquired != null)
                return acquired;

            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                break;

            var delay = remaining < RetryDelay ? remaining : RetryDelay;
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private IDisposable? TryCreateLock(string lockPath, string operation)
    {
        TryRemoveStaleLock(lockPath);

        try
        {
            var stream = new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var info = new SharedOperationLockInfo
            {
                Operation = operation,
                ActorName = GetCurrentActorName(),
                MachineName = Environment.MachineName,
                MachineId = LicenseService.GetMachineId(),
                CreatedAtUtc = DateTime.UtcNow
            };
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(JsonSerializer.Serialize(info));
            }

            return new SharedOperationLock(lockPath);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void TryRemoveStaleLock(string lockPath)
    {
        try
        {
            if (!File.Exists(lockPath))
                return;

            var info = JsonSerializer.Deserialize<SharedOperationLockInfo>(
                SafeFileService.ReadAllTextShared(lockPath));
            var created = info?.CreatedAtUtc ?? File.GetCreationTimeUtc(lockPath);
            if (DateTime.UtcNow - created > TimeSpan.FromMinutes(3))
                SafeFileService.DeleteFile(lockPath);
        }
        catch
        {
            try
            {
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(lockPath) > TimeSpan.FromMinutes(3))
                    SafeFileService.DeleteFile(lockPath);
            }
            catch
            {
                // Another PC may be creating or deleting the lock at the same time.
            }
        }
    }

    private string GetCurrentActorName()
    {
        var profile = _currentProfileService.CurrentProfile;
        var fullName = string.Join(" ", new[] { profile?.FirstName, profile?.LastName }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
        return string.IsNullOrWhiteSpace(fullName) ? Environment.UserName : fullName;
    }

    private static string Sanitize(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');
        return string.IsNullOrWhiteSpace(value) ? "operation" : value;
    }

    private sealed class SharedOperationLockInfo
    {
        public string Operation { get; set; } = string.Empty;
        public string ActorName { get; set; } = string.Empty;
        public string MachineName { get; set; } = string.Empty;
        public string MachineId { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
    }

    private sealed class SharedOperationLock : IDisposable
    {
        private readonly string _lockPath;
        private bool _disposed;

        public SharedOperationLock(string lockPath)
        {
            _lockPath = lockPath;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            try { SafeFileService.DeleteFile(_lockPath); }
            catch { }
        }
    }
}
