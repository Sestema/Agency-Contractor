using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Win11DesktopApp.Services;

public sealed class DailySqliteBackupResult
{
    public bool Created { get; init; }
    public bool Skipped { get; init; }
    public bool ExportedFromPostgres { get; init; }
    public int FilesCopied { get; init; }
    public int OldBackupsDeleted { get; init; }
    public string BackupFolderPath { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public sealed class DailySqliteBackupService
{
    private const int RetentionDays = 7;
    private static readonly TimeSpan StaleLockAfter = TimeSpan.FromHours(6);
    private static readonly SemaphoreSlim BackupGate = new(1, 1);
    private static readonly string[] ExcludedFolderNames = { "Backup", "Backups" };

    private readonly FolderService _folderService;
    private readonly AppDataStorageFactory _storageFactory;
    private readonly PostgresToSqliteBackupService _postgresToSqliteBackupService;

    public DailySqliteBackupService(
        FolderService folderService,
        AppDataStorageFactory storageFactory,
        PostgresToSqliteBackupService postgresToSqliteBackupService)
    {
        _folderService = folderService ?? throw new ArgumentNullException(nameof(folderService));
        _storageFactory = storageFactory ?? throw new ArgumentNullException(nameof(storageFactory));
        _postgresToSqliteBackupService = postgresToSqliteBackupService ?? throw new ArgumentNullException(nameof(postgresToSqliteBackupService));
    }

    public async Task<DailySqliteBackupResult> CreateTodayBackupIfNeededAsync(CancellationToken cancellationToken = default)
    {
        if (!await BackupGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return new DailySqliteBackupResult
            {
                Skipped = true,
                Message = "SQLite daily backup is already running."
            };
        }

        try
        {
            var context = PrepareBackupContext(cancellationToken);
            if (context.Result != null)
                return context.Result;

            await using var sharedLock = TryAcquireSharedBackupLock(context.BackupRoot, out var lockMessage);
            if (sharedLock == null)
            {
                return new DailySqliteBackupResult
                {
                    Skipped = true,
                    OldBackupsDeleted = context.OldBackupsDeleted,
                    Message = lockMessage
                };
            }

            // Re-check after acquiring the cross-PC lock. Another client may have finished while we waited.
            if (Directory.Exists(context.TodayBackup))
            {
                return new DailySqliteBackupResult
                {
                    Skipped = true,
                    OldBackupsDeleted = context.OldBackupsDeleted,
                    BackupFolderPath = context.TodayBackup,
                    Message = "SQLite daily backup already exists for today."
                };
            }

            var exportedFromPostgres = false;
            if (_storageFactory.IsPostgresRuntimeActiveAtStartup)
            {
                var exportResult = await _postgresToSqliteBackupService
                    .CreateBackupAsync(progress: null, cancellationToken)
                    .ConfigureAwait(false);
                if (!exportResult.Success)
                {
                    return new DailySqliteBackupResult
                    {
                        Skipped = true,
                        OldBackupsDeleted = context.OldBackupsDeleted,
                        Message = $"PostgreSQL to SQLite export failed. Daily backup was not created: {exportResult.ErrorMessage}"
                    };
                }

                exportedFromPostgres = true;
            }

            return await Task
                .Run(() => CreateTodayBackup(context, exportedFromPostgres, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            BackupGate.Release();
        }
    }

    private BackupContext PrepareBackupContext(CancellationToken cancellationToken)
    {
        var root = _folderService.RootPath;
        if (string.IsNullOrWhiteSpace(root))
        {
            return new BackupContext(new DailySqliteBackupResult
            {
                Skipped = true,
                Message = "Root folder is not selected."
            });
        }

        var sqliteFolder = _folderService.GetSqliteFolder();
        if (string.IsNullOrWhiteSpace(sqliteFolder) || !Directory.Exists(sqliteFolder))
        {
            return new BackupContext(new DailySqliteBackupResult
            {
                Skipped = true,
                Message = "SQLite folder does not exist."
            });
        }

        var backupRoot = Path.Combine(root, "Backup");
        var backupName = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var todayBackup = Path.Combine(backupRoot, backupName);
        var tempBackup = Path.Combine(backupRoot, $"{backupName}.tmp");
        Directory.CreateDirectory(backupRoot);

        var deleted = CleanupOldBackups(backupRoot, cancellationToken);

        if (Directory.Exists(todayBackup))
        {
            return new BackupContext(new DailySqliteBackupResult
            {
                Skipped = true,
                OldBackupsDeleted = deleted,
                BackupFolderPath = todayBackup,
                Message = "SQLite daily backup already exists for today."
            });
        }

        return new BackupContext(root, sqliteFolder, backupRoot, todayBackup, tempBackup, deleted);
    }

    private static DailySqliteBackupResult CreateTodayBackup(
        BackupContext context,
        bool exportedFromPostgres,
        CancellationToken cancellationToken)
    {
        SafeDeleteDirectory(context.TempBackup);
        Directory.CreateDirectory(context.TempBackup);

        try
        {
            var filesCopied = CopySqliteFiles(context.SqliteFolder, context.TempBackup, cancellationToken);
            File.WriteAllText(
                Path.Combine(context.TempBackup, "_backup_info.txt"),
                $"CreatedAtUtc={DateTime.UtcNow:O}{Environment.NewLine}Source={context.SqliteFolder}{Environment.NewLine}ExportedFromPostgres={exportedFromPostgres}{Environment.NewLine}FilesCopied={filesCopied}{Environment.NewLine}");

            if (Directory.Exists(context.TodayBackup))
                SafeDeleteDirectory(context.TodayBackup);

            Directory.Move(context.TempBackup, context.TodayBackup);

            return new DailySqliteBackupResult
            {
                Created = true,
                ExportedFromPostgres = exportedFromPostgres,
                FilesCopied = filesCopied,
                OldBackupsDeleted = context.OldBackupsDeleted,
                BackupFolderPath = context.TodayBackup,
                Message = exportedFromPostgres
                    ? $"PostgreSQL exported to SQLite and daily backup created. Files copied: {filesCopied}."
                    : $"SQLite daily backup created. Files copied: {filesCopied}."
            };
        }
        catch
        {
            SafeDeleteDirectory(context.TempBackup);
            throw;
        }
    }

    private static int CopySqliteFiles(string sqliteFolder, string destinationFolder, CancellationToken cancellationToken)
    {
        var files = EnumerateSqliteFiles(sqliteFolder).ToList();
        foreach (var sourcePath in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(sqliteFolder, sourcePath);
            var destinationPath = Path.Combine(destinationFolder, relativePath);
            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
                Directory.CreateDirectory(destinationDirectory);

            CopyFileShared(sourcePath, destinationPath);
        }

        return files.Count;
    }

    private static IEnumerable<string> EnumerateSqliteFiles(string sqliteFolder)
    {
        return Directory.EnumerateFiles(sqliteFolder, "*", SearchOption.AllDirectories)
            .Where(path => IsSqliteDatabaseFile(path) && !IsInsideExcludedFolder(sqliteFolder, path));
    }

    private static bool IsSqliteDatabaseFile(string path)
    {
        var fileName = Path.GetFileName(path);
        return fileName.EndsWith(".db", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".db-wal", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".db-shm", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInsideExcludedFolder(string root, string path)
    {
        var relativePath = Path.GetRelativePath(root, path);
        var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(part => ExcludedFolderNames.Contains(part, StringComparer.OrdinalIgnoreCase));
    }

    private static void CopyFileShared(string sourcePath, string destinationPath)
    {
        using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        source.CopyTo(destination);
    }

    private static int CleanupOldBackups(string backupRoot, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(backupRoot))
            return 0;

        var datedBackups = Directory.EnumerateDirectories(backupRoot)
            .Select(path => new
            {
                Path = path,
                Date = DateTime.TryParseExact(
                    System.IO.Path.GetFileName(path),
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var date)
                    ? date
                    : (DateTime?)null
            })
            .Where(item => item.Date.HasValue)
            .OrderByDescending(item => item.Date!.Value)
            .ToList();

        var deleted = 0;
        foreach (var item in datedBackups.Skip(RetentionDays))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryDeleteDirectory(item.Path))
                deleted++;
        }

        return deleted;
    }

    private static SharedBackupLock? TryAcquireSharedBackupLock(string backupRoot, out string message)
    {
        var lockPath = Path.Combine(backupRoot, ".daily_sqlite_backup.lock");
        try
        {
            return CreateSharedBackupLock(lockPath, out message);
        }
        catch (IOException)
        {
            if (TryDeleteStaleLock(lockPath))
                return CreateSharedBackupLock(lockPath, out message);

            message = "SQLite daily backup is already running on another PC.";
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            message = "SQLite daily backup lock is not accessible.";
            return null;
        }
    }

    private static SharedBackupLock? CreateSharedBackupLock(string lockPath, out string message)
    {
        var stream = new FileStream(lockPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using var writer = new StreamWriter(stream, leaveOpen: true);
        writer.WriteLine($"Machine={Environment.MachineName}");
        writer.WriteLine($"ProcessId={Environment.ProcessId}");
        writer.WriteLine($"CreatedAtUtc={DateTime.UtcNow:O}");
        writer.Flush();
        stream.Flush(true);
        stream.Position = 0;
        message = "SQLite daily backup lock acquired.";
        return new SharedBackupLock(lockPath, stream);
    }

    private static bool TryDeleteStaleLock(string lockPath)
    {
        try
        {
            if (!File.Exists(lockPath))
                return false;

            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(lockPath);
            if (age < StaleLockAfter)
                return false;

            File.Delete(lockPath);
            return true;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void SafeDeleteDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;

        Directory.Delete(path, recursive: true);
    }

    private static bool TryDeleteDirectory(string path)
    {
        try
        {
            SafeDeleteDirectory(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is DirectoryNotFoundException)
        {
            return false;
        }
    }

    private sealed class BackupContext
    {
        public BackupContext(DailySqliteBackupResult result)
        {
            Result = result;
        }

        public BackupContext(
            string root,
            string sqliteFolder,
            string backupRoot,
            string todayBackup,
            string tempBackup,
            int oldBackupsDeleted)
        {
            Root = root;
            SqliteFolder = sqliteFolder;
            BackupRoot = backupRoot;
            TodayBackup = todayBackup;
            TempBackup = tempBackup;
            OldBackupsDeleted = oldBackupsDeleted;
        }

        public DailySqliteBackupResult? Result { get; }
        public string Root { get; } = string.Empty;
        public string SqliteFolder { get; } = string.Empty;
        public string BackupRoot { get; } = string.Empty;
        public string TodayBackup { get; } = string.Empty;
        public string TempBackup { get; } = string.Empty;
        public int OldBackupsDeleted { get; }
    }

    private sealed class SharedBackupLock : IAsyncDisposable
    {
        private readonly string _path;
        private readonly FileStream _stream;

        public SharedBackupLock(string path, FileStream stream)
        {
            _path = path;
            _stream = stream;
        }

        public async ValueTask DisposeAsync()
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
            try
            {
                if (File.Exists(_path))
                    File.Delete(_path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }
    }
}
