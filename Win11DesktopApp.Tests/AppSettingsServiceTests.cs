using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Win11DesktopApp.Services;
using Xunit;

namespace Win11DesktopApp.Tests
{
    public class AppSettingsServiceTests
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        [Fact]
        public void AppVersion_ShouldHaveDefaultValue()
        {
            var settings = new AppSettingsService.AppSettings();

            Assert.Equal(AppSettingsService.CurrentAppVersion, settings.AppVersion);
        }

        [Fact]
        public void WorkspaceUiSettings_RoundTrip_KeepsCurrencyAndColumns()
        {
            var settings = new AppSettingsService.AppSettings
            {
                SalaryDisplayCurrency = "EUR",
                ShowStatPaid = true,
                SalaryHoursCustomPrecision = true,
                SalaryColumnWidthByKey = { ["hours"] = 88 },
                EmployeeReportColumns =
                {
                    new AppSettingsService.ReportColumnSetting { Key = "Name", Width = 140, IsVisible = true }
                }
            };

            var snapshot = AppSettingsService.WorkspaceUiSettings.FromActive(settings);
            var target = new AppSettingsService.AppSettings
            {
                SalaryDisplayCurrency = "CZK",
                ShowStatPaid = false
            };
            snapshot.ApplyTo(target);

            Assert.Equal("EUR", target.SalaryDisplayCurrency);
            Assert.True(target.ShowStatPaid);
            Assert.True(target.SalaryHoursCustomPrecision);
            Assert.Equal(88, target.SalaryColumnWidthByKey["hours"]);
            Assert.Single(target.EmployeeReportColumns);
            Assert.Equal("Name", target.EmployeeReportColumns[0].Key);
        }

        [Fact]
        public void WorkspaceUiSettings_Defaults_ResetPreviousWorkspaceValues()
        {
            var leftover = new AppSettingsService.AppSettings
            {
                SalaryDisplayCurrency = "PLN",
                ShowStatAdvances = true,
                SalaryColumnWidthByKey = { ["paid"] = 200 }
            };

            AppSettingsService.WorkspaceUiSettings.CreateDefaults().ApplyTo(leftover);

            Assert.Equal("CZK", leftover.SalaryDisplayCurrency);
            Assert.False(leftover.ShowStatAdvances);
            Assert.Empty(leftover.SalaryColumnWidthByKey);
        }

        [Fact]
        public void LoadSettings_ApplyPending_RestoresTargetWorkspaceUiWithoutCopyingSource()
        {
            var root = CreateTempSettingsRoot();
            try
            {
                var folderA = Path.Combine(root, "Stavba");
                var folderB = Path.Combine(root, "WorkNet");
                Directory.CreateDirectory(folderA);
                Directory.CreateDirectory(folderB);
                var pathA = AppSettingsService.NormalizeWorkspacePath(folderA);
                var pathB = AppSettingsService.NormalizeWorkspacePath(folderB);

                WriteSettings(root, new AppSettingsService.AppSettings
                {
                    AppVersion = AppSettingsService.CurrentAppVersion,
                    RootFolderPath = pathA,
                    PendingWorkspacePath = pathB,
                    SalaryDisplayCurrency = "EUR",
                    ShowStatPaid = true,
                    SalaryColumnWidthByKey = { ["hours"] = 80 },
                    Workspaces =
                    {
                        new AppSettingsService.WorkspaceSetting { Path = pathA, WorkspaceId = "ws_a", DisplayName = "Stavba" },
                        new AppSettingsService.WorkspaceSetting { Path = pathB, WorkspaceId = "ws_b", DisplayName = "WorkNet" }
                    },
                    WorkspaceUiById =
                    {
                        ["ws_a"] = Ui("EUR", 80, showPaid: true),
                        [pathA] = Ui("EUR", 80, showPaid: true),
                        ["ws_b"] = Ui("CZK", 140, showPaid: false),
                        [pathB] = Ui("CZK", 140, showPaid: false)
                    }
                });

                var service = new AppSettingsService(root, suppressStartupNotifications: true);

                Assert.Equal(pathB, AppSettingsService.NormalizeWorkspacePath(service.Settings.RootFolderPath));
                Assert.True(string.IsNullOrWhiteSpace(service.Settings.PendingWorkspacePath));
                Assert.Equal("CZK", service.Settings.SalaryDisplayCurrency);
                Assert.False(service.Settings.ShowStatPaid);
                Assert.Equal(140, service.Settings.SalaryColumnWidthByKey["hours"]);

                var persisted = ReadSettings(root);
                Assert.Equal("CZK", persisted.SalaryDisplayCurrency);
                Assert.Equal("EUR", FindUi(persisted, "ws_a", pathA)!.SalaryDisplayCurrency);
                Assert.Equal(80, FindUi(persisted, "ws_a", pathA)!.SalaryColumnWidthByKey["hours"]);
                Assert.Equal("CZK", FindUi(persisted, "ws_b", pathB)!.SalaryDisplayCurrency);
                Assert.Equal(140, FindUi(persisted, "ws_b", pathB)!.SalaryColumnWidthByKey["hours"]);
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Fact]
        public async Task ShutdownDuringHandoff_DoesNotRevertWorkspaceOrCopyUi()
        {
            var root = CreateTempSettingsRoot();
            try
            {
                var folderA = Path.Combine(root, "Stavba");
                var folderB = Path.Combine(root, "WorkNet");
                Directory.CreateDirectory(folderA);
                Directory.CreateDirectory(folderB);
                var pathA = AppSettingsService.NormalizeWorkspacePath(folderA);
                var pathB = AppSettingsService.NormalizeWorkspacePath(folderB);

                WriteSettings(root, new AppSettingsService.AppSettings
                {
                    AppVersion = AppSettingsService.CurrentAppVersion,
                    RootFolderPath = pathA,
                    SalaryDisplayCurrency = "EUR",
                    ShowStatPaid = true,
                    SalaryColumnWidthByKey = { ["hours"] = 80 },
                    Workspaces =
                    {
                        new AppSettingsService.WorkspaceSetting { Path = pathA, WorkspaceId = "ws_a", DisplayName = "Stavba" },
                        new AppSettingsService.WorkspaceSetting { Path = pathB, WorkspaceId = "ws_b", DisplayName = "WorkNet" }
                    },
                    WorkspaceUiById =
                    {
                        ["ws_a"] = Ui("EUR", 80, showPaid: true),
                        [pathA] = Ui("EUR", 80, showPaid: true),
                        ["ws_b"] = Ui("CZK", 140, showPaid: false),
                        [pathB] = Ui("CZK", 140, showPaid: false)
                    }
                });

                var oldProcess = new AppSettingsService(root, suppressStartupNotifications: true);
                oldProcess.BeginWorkspaceHandoff();
                oldProcess.Settings.PendingWorkspacePath = pathB;
                await oldProcess.SaveSettingsImmediate();

                var newProcess = new AppSettingsService(root, suppressStartupNotifications: true);
                Assert.Equal("CZK", newProcess.Settings.SalaryDisplayCurrency);
                Assert.Equal(pathB, AppSettingsService.NormalizeWorkspacePath(newProcess.Settings.RootFolderPath));

                oldProcess.Settings.SalaryDisplayCurrency = "PLN";
                oldProcess.Settings.ShowStatPaid = false;
                oldProcess.Settings.SalaryColumnWidthByKey["hours"] = 55;
                Assert.True(oldProcess.SaveSettingsForShutdown(TimeSpan.FromSeconds(5)));

                var afterShutdown = new AppSettingsService(root, suppressStartupNotifications: true);
                Assert.Equal(pathB, AppSettingsService.NormalizeWorkspacePath(afterShutdown.Settings.RootFolderPath));
                Assert.True(string.IsNullOrWhiteSpace(afterShutdown.Settings.PendingWorkspacePath));
                Assert.Equal("CZK", afterShutdown.Settings.SalaryDisplayCurrency);
                Assert.False(afterShutdown.Settings.ShowStatPaid);
                Assert.Equal(140, afterShutdown.Settings.SalaryColumnWidthByKey["hours"]);

                var persisted = ReadSettings(root);
                Assert.Equal("PLN", FindUi(persisted, "ws_a", pathA)!.SalaryDisplayCurrency);
                Assert.Equal(55, FindUi(persisted, "ws_a", pathA)!.SalaryColumnWidthByKey["hours"]);
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Fact]
        public void LoadSettings_ApplyPending_MissingTargetUi_KeepsExistingColumnWidths()
        {
            var root = CreateTempSettingsRoot();
            try
            {
                var folderA = Path.Combine(root, "Stavba");
                var folderB = Path.Combine(root, "WorkNet");
                Directory.CreateDirectory(folderA);
                Directory.CreateDirectory(folderB);
                var pathA = AppSettingsService.NormalizeWorkspacePath(folderA);
                var pathB = AppSettingsService.NormalizeWorkspacePath(folderB);

                WriteSettings(root, new AppSettingsService.AppSettings
                {
                    AppVersion = AppSettingsService.CurrentAppVersion,
                    RootFolderPath = pathA,
                    PendingWorkspacePath = pathB,
                    SalaryDisplayCurrency = "EUR",
                    SalaryColumnWidthByKey = { ["HoursWorked"] = 88 },
                    Workspaces =
                    {
                        new AppSettingsService.WorkspaceSetting { Path = pathA, WorkspaceId = "ws_a", DisplayName = "Stavba" },
                        new AppSettingsService.WorkspaceSetting { Path = pathB, WorkspaceId = "ws_b", DisplayName = "WorkNet" }
                    },
                    WorkspaceUiById =
                    {
                        ["ws_a"] = Ui("EUR", 88, showPaid: true),
                        [pathA] = Ui("EUR", 88, showPaid: true)
                    }
                });

                var service = new AppSettingsService(root, suppressStartupNotifications: true);

                Assert.Equal(pathB, AppSettingsService.NormalizeWorkspacePath(service.Settings.RootFolderPath));
                Assert.Equal("CZK", service.Settings.SalaryDisplayCurrency);
                Assert.Equal(88, service.Settings.SalaryColumnWidthByKey["HoursWorked"]);
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        [Fact]
        public void LoadSettings_PrefersWorkspaceUiSnapshotWithColumnWidths()
        {
            var root = CreateTempSettingsRoot();
            try
            {
                var folderA = Path.Combine(root, "Stavba");
                var folderB = Path.Combine(root, "WorkNet");
                Directory.CreateDirectory(folderA);
                Directory.CreateDirectory(folderB);
                var pathA = AppSettingsService.NormalizeWorkspacePath(folderA);
                var pathB = AppSettingsService.NormalizeWorkspacePath(folderB);

                WriteSettings(root, new AppSettingsService.AppSettings
                {
                    AppVersion = AppSettingsService.CurrentAppVersion,
                    RootFolderPath = pathA,
                    PendingWorkspacePath = pathB,
                    SalaryDisplayCurrency = "EUR",
                    SalaryColumnWidthByKey = { ["HoursWorked"] = 80 },
                    Workspaces =
                    {
                        new AppSettingsService.WorkspaceSetting { Path = pathA, WorkspaceId = "ws_a", DisplayName = "Stavba" },
                        new AppSettingsService.WorkspaceSetting { Path = pathB, WorkspaceId = "ws_b", DisplayName = "WorkNet" }
                    },
                    WorkspaceUiById =
                    {
                        ["ws_a"] = Ui("EUR", 80, showPaid: true),
                        [pathA] = Ui("EUR", 80, showPaid: true),
                        ["ws_b"] = new AppSettingsService.WorkspaceUiSettings { SalaryDisplayCurrency = "CZK" },
                        [pathB] = Ui("CZK", 140, showPaid: false)
                    }
                });

                var service = new AppSettingsService(root, suppressStartupNotifications: true);

                Assert.Equal("CZK", service.Settings.SalaryDisplayCurrency);
                Assert.Equal(140, service.Settings.SalaryColumnWidthByKey["hours"]);
            }
            finally
            {
                TryDeleteDirectory(root);
            }
        }

        private static string CreateTempSettingsRoot()
        {
            var root = Path.Combine(Path.GetTempPath(), "ac-ws-ui-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static void WriteSettings(string settingsDirectory, AppSettingsService.AppSettings settings)
        {
            var path = Path.Combine(settingsDirectory, "settings.json");
            File.WriteAllText(path, JsonSerializer.Serialize(settings, JsonOptions), Encoding.UTF8);
        }

        private static AppSettingsService.AppSettings ReadSettings(string settingsDirectory)
        {
            var path = Path.Combine(settingsDirectory, "settings.json");
            return JsonSerializer.Deserialize<AppSettingsService.AppSettings>(
                File.ReadAllText(path, Encoding.UTF8), JsonOptions)
                ?? new AppSettingsService.AppSettings();
        }

        private static AppSettingsService.WorkspaceUiSettings Ui(string currency, double hoursWidth, bool showPaid) =>
            new()
            {
                SalaryDisplayCurrency = currency,
                ShowStatPaid = showPaid,
                SalaryColumnWidthByKey = { ["hours"] = hoursWidth }
            };

        private static AppSettingsService.WorkspaceUiSettings? FindUi(
            AppSettingsService.AppSettings settings, string workspaceId, string path)
        {
            if (settings.WorkspaceUiById.TryGetValue(workspaceId, out var byId))
                return byId;
            return settings.WorkspaceUiById.TryGetValue(path, out var byPath) ? byPath : null;
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            }
            catch
            {
            }
        }
    }
}
