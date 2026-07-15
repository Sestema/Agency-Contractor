using System;
using System.Collections.Generic;
using System.IO;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    public sealed class FirmFinanceRenameService
    {
        private readonly AppDataStorageFactory _storageFactory;
        private readonly FolderService _folderService;
        private readonly LocalDbService _localDbService;
        private readonly SharedOperationLockService _sharedOperationLockService;
        public event Action? FirmRenamed;

        public FirmFinanceRenameService(
            AppDataStorageFactory storageFactory,
            FolderService folderService,
            LocalDbService localDbService,
            SharedOperationLockService sharedOperationLockService)
        {
            _storageFactory = storageFactory ?? throw new ArgumentNullException(nameof(storageFactory));
            _folderService = folderService ?? throw new ArgumentNullException(nameof(folderService));
            _localDbService = localDbService ?? throw new ArgumentNullException(nameof(localDbService));
            _sharedOperationLockService = sharedOperationLockService
                ?? throw new ArgumentNullException(nameof(sharedOperationLockService));
        }

        public FirmFinanceRenameResult Rename(
            EmployerCompany company,
            string oldName,
            string newName,
            int effectiveYear,
            int effectiveMonth)
        {
            if (company == null)
                throw new ArgumentNullException(nameof(company));

            using var renameLock = _sharedOperationLockService.TryAcquire(
                $"firm-rename-{company.Id:N}",
                TimeSpan.FromSeconds(30));
            if (renameLock == null)
            {
                throw new InvalidOperationException(
                    "Фірма зараз змінюється на іншому ПК. Дочекайтеся завершення синхронізації та повторіть спробу.");
            }

            var oldFolder = _folderService.GetCompanyFolder(oldName);
            var newFolder = _folderService.GetCompanyFolder(newName);
            var storage = _storageFactory.CreateMonthPaymentsStorage();
            var result = storage.RenameFirmReferences(
                oldName,
                newName,
                effectiveYear,
                effectiveMonth,
                oldFolder,
                newFolder);
            try
            {
                if (!_storageFactory.IsPostgresExplicitlyEnabled)
                {
                    _localDbService.RenameCurrentFirmReferences(
                        oldName,
                        newName,
                        oldFolder,
                        newFolder);
                }
            }
            catch
            {
                try
                {
                    storage.RenameFirmReferences(
                        newName,
                        oldName,
                        effectiveYear,
                        effectiveMonth,
                        newFolder,
                        oldFolder);
                }
                catch (Exception rollbackEx)
                {
                    LoggingService.LogError("FirmFinanceRenameService.Rollback", rollbackEx);
                }
                throw;
            }
            FirmRenamed?.Invoke();
            return result;
        }

        public FirmSalaryRepairResult RepairCompanySalaryLinks(
            EmployerCompany company,
            IReadOnlyCollection<string> otherCurrentCompanyNames)
        {
            if (company == null)
                throw new ArgumentNullException(nameof(company));

            otherCurrentCompanyNames ??= Array.Empty<string>();
            var reservedNames = new HashSet<string>(otherCurrentCompanyNames, StringComparer.OrdinalIgnoreCase);

            var currentFolder = _folderService.GetCompanyFolder(company.Name);
            if (string.IsNullOrWhiteSpace(currentFolder) || !Directory.Exists(currentFolder))
                return new FirmSalaryRepairResult();

            var storage = _storageFactory.CreateMonthPaymentsStorage();
            var folderPrefixes = BuildCompanyFolderPrefixes(company, reservedNames);
            var entryPathsUpdated = 0;
            var normalizedCurrentFolder = NormalizeFolderPrefix(currentFolder);

            foreach (var folderPrefix in folderPrefixes)
            {
                if (string.Equals(folderPrefix, normalizedCurrentFolder, StringComparison.OrdinalIgnoreCase))
                    continue;

                entryPathsUpdated += storage.RepairEmployeeFolderPrefixes(folderPrefix, normalizedCurrentFolder);
            }

            // IMPORTANT: this method must never modify company.NameHistory. Earlier versions
            // (0.1.87-0.1.89) scanned the salary storage for firm names whose employee folders
            // happened to live under this company's folder prefixes and auto-adopted them into
            // NameHistory. On real-world data that silently merged unrelated companies: their
            // salary rows were remapped to the wrong firm, employees "jumped" between companies,
            // hours vanished and whole firms were rendered as inactive (red). NameHistory may only
            // be extended by an explicit user-initiated rename (RecordCompanyNameChange).
            if (entryPathsUpdated > 0)
            {
                FirmRenamed?.Invoke();
                LoggingService.LogInfo(
                    "FirmFinanceRenameService.RepairCompanySalaryLinks",
                    $"Company '{company.Name}': pathsUpdated={entryPathsUpdated}.");
            }

            return new FirmSalaryRepairResult
            {
                EntryPathsUpdated = entryPathsUpdated
            };
        }

        private HashSet<string> BuildCompanyFolderPrefixes(
            EmployerCompany company,
            IReadOnlyCollection<string> reservedNames)
        {
            // Only ever build prefixes from names that are known to genuinely belong to
            // THIS company (its current name + its own recorded NameHistory). We must never
            // scan every distinct firm_name that exists anywhere in the whole salary storage
            // and "adopt" whichever ones aren't the exact current name of some other company:
            // that previously caused unrelated firms (e.g. a completely different company
            // sharing no history with this one) to be merged into this company's folder,
            // silently moving their salary entries into the wrong company.
            var prefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void AddFolderForName(string? firmName)
            {
                if (string.IsNullOrWhiteSpace(firmName))
                    return;

                // Never adopt a name that currently belongs to a different, still-active
                // company - that would merge that company's own salary folder into ours.
                if (reservedNames.Contains(firmName))
                    return;

                var folder = _folderService.GetCompanyFolder(firmName);
                if (!string.IsNullOrWhiteSpace(folder))
                    prefixes.Add(NormalizeFolderPrefix(folder));
            }

            AddFolderForName(company.Name);
            foreach (var period in company.NameHistory ?? new List<CompanyNamePeriod>())
                AddFolderForName(period.Name);

            var currentFolder = _folderService.GetCompanyFolder(company.Name);
            if (!string.IsNullOrWhiteSpace(currentFolder) && Directory.Exists(currentFolder))
                prefixes.Add(NormalizeFolderPrefix(currentFolder));

            return prefixes;
        }

        private static string NormalizeFolderPrefix(string folder)
            => folder.Replace('/', '\\').TrimEnd('\\');
    }
}
