using System;

namespace Win11DesktopApp.Services
{
    public static class DatabaseStorageModes
    {
        public const string Sqlite = "Sqlite";
        public const string Postgres = "Postgres";
        public static readonly bool PostgresRuntimeStorageEnabled = true;
    }

    public sealed class AppDataStorageFactory
    {
        private readonly AppSettingsService _settingsService;
        private readonly FolderService _folderService;
        private readonly CoreDbService _coreDbService;
        private readonly LocalDbService _localDbService;
        private readonly SalaryDbService _salaryDbService;
        private readonly bool _isPostgresRuntimeActiveAtStartup;

        public AppDataStorageFactory(
            AppSettingsService settingsService,
            FolderService folderService,
            CoreDbService coreDbService,
            LocalDbService localDbService,
            SalaryDbService salaryDbService)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _folderService = folderService ?? throw new ArgumentNullException(nameof(folderService));
            _coreDbService = coreDbService ?? throw new ArgumentNullException(nameof(coreDbService));
            _localDbService = localDbService ?? throw new ArgumentNullException(nameof(localDbService));
            _salaryDbService = salaryDbService ?? throw new ArgumentNullException(nameof(salaryDbService));
            _isPostgresRuntimeActiveAtStartup = IsPostgresEnabled(_settingsService.Settings);
        }

        public string CurrentMode => NormalizeMode(_settingsService.Settings.DatabaseStorageMode);

        public bool IsSqliteMode => !IsPostgresExplicitlyEnabled;

        public bool IsPostgresRuntimeActiveAtStartup => _isPostgresRuntimeActiveAtStartup;

        public string ActiveRuntimeModeAtStartup =>
            _isPostgresRuntimeActiveAtStartup ? DatabaseStorageModes.Postgres : DatabaseStorageModes.Sqlite;

        public bool IsPostgresExplicitlyEnabled
        {
            get
            {
                return IsPostgresEnabled(_settingsService.Settings);
            }
        }

        public ICoreDatabaseStorage CreateCoreDatabaseStorage()
        {
            if (IsPostgresExplicitlyEnabled)
                return new PostgresCoreDatabaseStorage(_settingsService);

            return new SqliteCoreDatabaseStorage(_coreDbService);
        }

        public IFinanceMonthPaymentsStorage CreateMonthPaymentsStorage()
        {
            if (IsPostgresExplicitlyEnabled)
                return new PostgresFinanceMonthPaymentsStorage(_settingsService);

            EnsureSqliteMode("month payments");
            return new SqliteFinanceMonthPaymentsStorage(_salaryDbService);
        }

        public IFinanceAdvancesStorage CreateAdvancesStorage()
        {
            if (IsPostgresExplicitlyEnabled)
                return new PostgresFinanceAdvancesStorage(_settingsService, _folderService);

            EnsureSqliteMode("advances");
            return new SqliteFinanceAdvancesStorage(_localDbService);
        }

        public IFinanceCustomFieldsStorage CreateCustomFieldsStorage()
        {
            if (IsPostgresExplicitlyEnabled)
                return new PostgresFinanceCustomFieldsStorage(_settingsService);

            EnsureSqliteMode("custom salary fields");
            return new SqliteFinanceCustomFieldsStorage(_localDbService);
        }

        public IFinanceReportsStorage CreateReportsStorage()
        {
            if (IsPostgresExplicitlyEnabled)
                return new PostgresFinanceReportsStorage(_settingsService, _folderService);

            EnsureSqliteMode("salary reports");
            return new SqliteFinanceReportsStorage(_localDbService);
        }

        public IFinanceSalaryHistoryStorage CreateSalaryHistoryStorage()
        {
            if (IsPostgresExplicitlyEnabled)
                return new PostgresFinanceSalaryHistoryStorage(_settingsService, _folderService);

            EnsureSqliteMode("salary history");
            return new SqliteFinanceSalaryHistoryStorage(_localDbService);
        }

        public IActivityLogStorage CreateActivityLogStorage()
        {
            if (IsPostgresExplicitlyEnabled)
                return new PostgresActivityLogStorage(_settingsService);

            EnsureSqliteMode("activity log");
            return new SqliteActivityLogStorage(_localDbService);
        }

        public IArchiveLogStorage CreateArchiveLogStorage()
        {
            if (IsPostgresExplicitlyEnabled)
                return new PostgresArchiveLogStorage(_settingsService);

            EnsureSqliteMode("archive log");
            return new SqliteArchiveLogStorage(_localDbService);
        }

        public IEmployeeHistoryStorage CreateEmployeeHistoryStorage()
        {
            if (IsPostgresExplicitlyEnabled)
                return new PostgresEmployeeHistoryStorage(_settingsService);

            EnsureSqliteMode("employee history");
            return new SqliteEmployeeHistoryStorage(_localDbService);
        }

        private void EnsureSqliteMode(string storageName)
        {
            var configuredMode = _settingsService.Settings.DatabaseStorageMode;
            if (!IsPostgresExplicitlyEnabled)
            {
                if (string.Equals(CurrentMode, DatabaseStorageModes.Postgres, StringComparison.OrdinalIgnoreCase))
                {
                    LoggingService.LogWarning(
                        "AppDataStorageFactory",
                        $"PostgreSQL mode is requested for {storageName}, but migration/enabled markers are missing; using SQLite storage.");
                }

                return;
            }

            LoggingService.LogWarning(
                "AppDataStorageFactory",
                $"DatabaseStorageMode '{configuredMode}' is enabled for {storageName}, but PostgreSQL storage is not implemented yet; using SQLite storage.");
        }

        private static string NormalizeMode(string? mode)
        {
            if (string.Equals(mode, DatabaseStorageModes.Postgres, StringComparison.OrdinalIgnoreCase))
                return DatabaseStorageModes.Postgres;

            return DatabaseStorageModes.Sqlite;
        }

        private static bool IsPostgresEnabled(AppSettingsService.AppSettings settings)
        {
            return DatabaseStorageModes.PostgresRuntimeStorageEnabled
                && string.Equals(NormalizeMode(settings.DatabaseStorageMode), DatabaseStorageModes.Postgres, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(settings.PostgresConnectionString)
                && !string.IsNullOrWhiteSpace(settings.PostgresMigrationCompletedAtUtc)
                && !string.IsNullOrWhiteSpace(settings.PostgresEnabledAtUtc);
        }
    }
}
