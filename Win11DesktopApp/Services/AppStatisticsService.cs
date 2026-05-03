using System;
using System.IO;

namespace Win11DesktopApp.Services
{
    public sealed class AppStatisticsSnapshot
    {
        public int TotalEmployeesCreated { get; set; }
        public int GeneratedDocumentsCount { get; set; }
        public int TotalProgramRunMinutes { get; set; }
    }

    public sealed class AppStatisticsService
    {
        private const string StatisticsFileName = "app_statistics.json";
        private readonly FolderService _folderService;
        private readonly object _lock = new();
        private DateTime? _sessionStartedUtc;

        public AppStatisticsService(FolderService folderService)
        {
            _folderService = folderService ?? throw new ArgumentNullException(nameof(folderService));
        }

        public void StartSession()
        {
            lock (_lock)
            {
                _sessionStartedUtc ??= DateTime.UtcNow;
            }
        }

        public void StopSession()
        {
            lock (_lock)
            {
                if (_sessionStartedUtc == null)
                    return;

                var elapsedMinutes = (int)Math.Max(0, Math.Round((DateTime.UtcNow - _sessionStartedUtc.Value).TotalMinutes));
                _sessionStartedUtc = null;
                if (elapsedMinutes <= 0)
                    return;

                var stats = LoadUnlocked();
                stats.TotalProgramRunMinutes += elapsedMinutes;
                SaveUnlocked(stats);
            }
        }

        public void RecordEmployeeCreated()
        {
            Update(stats => stats.TotalEmployeesCreated++);
        }

        public void RecordDocumentGenerated()
        {
            Update(stats => stats.GeneratedDocumentsCount++);
        }

        public AppStatisticsSnapshot GetSnapshot()
        {
            lock (_lock)
            {
                var stats = LoadUnlocked();
                var currentSessionMinutes = _sessionStartedUtc == null
                    ? 0
                    : (int)Math.Max(0, Math.Round((DateTime.UtcNow - _sessionStartedUtc.Value).TotalMinutes));

                return new AppStatisticsSnapshot
                {
                    TotalEmployeesCreated = stats.TotalEmployeesCreated,
                    GeneratedDocumentsCount = stats.GeneratedDocumentsCount,
                    TotalProgramRunMinutes = stats.TotalProgramRunMinutes + currentSessionMinutes
                };
            }
        }

        private void Update(Action<AppStatisticsSnapshot> update)
        {
            lock (_lock)
            {
                var stats = LoadUnlocked();
                update(stats);
                SaveUnlocked(stats);
            }
        }

        private AppStatisticsSnapshot LoadUnlocked()
        {
            var path = StatisticsPath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return new AppStatisticsSnapshot();

            try
            {
                return SafeFileService.ReadJsonOrDefault(path, new AppStatisticsSnapshot());
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("AppStatisticsService.Load", ex.Message);
                return new AppStatisticsSnapshot();
            }
        }

        private void SaveUnlocked(AppStatisticsSnapshot stats)
        {
            var path = StatisticsPath;
            if (string.IsNullOrWhiteSpace(path))
                return;

            try
            {
                SafeFileService.WriteJsonAtomic(path, stats);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("AppStatisticsService.Save", ex.Message);
            }
        }

        private string StatisticsPath
        {
            get
            {
                var root = _folderService.RootPath;
                return string.IsNullOrWhiteSpace(root) ? string.Empty : Path.Combine(root, StatisticsFileName);
            }
        }
    }
}
