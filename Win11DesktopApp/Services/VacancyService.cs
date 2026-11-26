using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    /// <summary>
    /// Stores the "Вільні позиції" page in {Root}/Vacancies: one json file per position,
    /// one text file per firm note and one small settings file. Small separate files keep
    /// OneDrive conflicts away when two or three PCs work at the same time.
    /// </summary>
    public class VacancyService
    {
        private const string FolderName = "Vacancies";
        private const string FirmNotesFolderName = "_firms";
        private const string DeletedFolderName = "_deleted";
        private const string SettingsFileName = "_settings.json";

        private readonly Func<string> _rootProvider;

        public VacancyService(FolderService folderService)
            : this(() => folderService.RootPath)
        {
        }

        internal VacancyService(Func<string> rootProvider)
        {
            _rootProvider = rootProvider;
        }

        public string Folder
        {
            get
            {
                var root = _rootProvider();
                return string.IsNullOrWhiteSpace(root) ? string.Empty : Path.Combine(root, FolderName);
            }
        }

        public List<Vacancy> GetAll()
        {
            var folder = Folder;
            var result = new List<Vacancy>();
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                return result;

            foreach (var path in Directory.EnumerateFiles(folder, "*.json"))
            {
                if (Path.GetFileName(path).StartsWith("_", StringComparison.Ordinal))
                    continue;
                try
                {
                    var vacancy = SafeFileService.ReadJson<Vacancy>(path);
                    if (vacancy == null || string.IsNullOrWhiteSpace(vacancy.Id))
                        continue;
                    vacancy.ManualFound ??= new List<VacancyManualPerson>();
                    vacancy.ExcludedEmployeeIds ??= new List<string>();
                    result.Add(vacancy);
                }
                catch (Exception ex)
                {
                    LoggingService.LogWarning("VacancyService.GetAll", $"Skipped '{Path.GetFileName(path)}': {ex.Message}");
                }
            }

            return result;
        }

        public void Save(Vacancy vacancy)
        {
            var folder = EnsureFolder();
            vacancy.UpdatedAt = DateTime.Now;
            SafeFileService.WriteJsonAtomic(Path.Combine(folder, SafeId(vacancy.Id) + ".json"), vacancy);
        }

        /// <summary>Moves the file to Vacancies\_deleted, so a mistake can still be undone by hand.</summary>
        public void Delete(string id)
        {
            var folder = Folder;
            if (string.IsNullOrEmpty(folder))
                return;
            var path = Path.Combine(folder, SafeId(id) + ".json");
            if (!File.Exists(path))
                return;
            var deleted = Path.Combine(folder, DeletedFolderName);
            Directory.CreateDirectory(deleted);
            var target = Path.Combine(deleted, $"{SafeId(id)}_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            File.Move(path, target);
        }

        public VacancyPageSettings GetPageSettings()
        {
            var folder = Folder;
            if (string.IsNullOrEmpty(folder))
                return new VacancyPageSettings();
            var path = Path.Combine(folder, SettingsFileName);
            return File.Exists(path)
                ? SafeFileService.ReadJsonOrDefault(path, new VacancyPageSettings())
                : new VacancyPageSettings();
        }

        public void SavePageSettings(VacancyPageSettings settings)
        {
            var folder = EnsureFolder();
            SafeFileService.WriteJsonAtomic(Path.Combine(folder, SettingsFileName), settings);
        }

        public string GetFirmNote(string firmName)
        {
            var folder = Folder;
            if (string.IsNullOrEmpty(folder) || string.IsNullOrWhiteSpace(firmName))
                return string.Empty;
            var path = Path.Combine(folder, FirmNotesFolderName, SafeFileName(firmName) + ".txt");
            return File.Exists(path) ? SafeFileService.ReadAllTextShared(path, Encoding.UTF8) : string.Empty;
        }

        public void SaveFirmNote(string firmName, string text)
        {
            if (string.IsNullOrWhiteSpace(firmName))
                return;
            var folder = Path.Combine(EnsureFolder(), FirmNotesFolderName);
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, SafeFileName(firmName) + ".txt");
            if (string.IsNullOrWhiteSpace(text))
            {
                if (File.Exists(path))
                    File.Delete(path);
                return;
            }
            SafeFileService.WriteTextAtomic(path, text, new UTF8Encoding(false));
        }

        private string EnsureFolder()
        {
            var folder = Folder;
            if (string.IsNullOrEmpty(folder))
                throw new InvalidOperationException("Root folder is not set.");
            Directory.CreateDirectory(folder);
            return folder;
        }

        private static string SafeId(string id)
            => new string((id ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()) is { Length: > 0 } safe ? safe : "vacancy";

        internal static string SafeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var safe = new string(name.Trim().Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
            return safe.Length == 0 ? "firm" : safe;
        }
    }

    /// <summary>
    /// Counts who was found for each position. Employees are counted by themselves: an employee
    /// of the same firm whose start date is on or after the day the position was created (and not
    /// after it was closed) fills a free place of the matching gender. People added by hand come first;
    /// several positions of one firm are filled oldest first. Nothing is written anywhere.
    /// </summary>
    public static class VacancyMatcher
    {
        public static Dictionary<string, VacancyProgress> Compute(IEnumerable<Vacancy> vacancies, IEnumerable<VacancyEmployee> employees)
        {
            var ordered = vacancies.OrderBy(v => v.CreatedAt).ThenBy(v => v.Id, StringComparer.Ordinal).ToList();
            var result = ordered.ToDictionary(v => v.Id, _ => new VacancyProgress());
            var slots = ordered.ToDictionary(v => v.Id, v => new Slots(v));
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var employeeList = employees.ToList();
            var byId = employeeList.Where(e => !string.IsNullOrWhiteSpace(e.UniqueId))
                .GroupBy(e => e.UniqueId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var vacancy in ordered)
            {
                foreach (var manual in vacancy.ManualFound ?? new List<VacancyManualPerson>())
                {
                    var gender = manual.Gender;
                    var name = manual.Name;
                    if (!string.IsNullOrWhiteSpace(manual.EmployeeId))
                    {
                        taken.Add(manual.EmployeeId);
                        if (byId.TryGetValue(manual.EmployeeId, out var employee))
                        {
                            gender = employee.Gender;
                            name = employee.FullName;
                        }
                    }

                    slots[vacancy.Id].Take(gender, result[vacancy.Id]);
                    result[vacancy.Id].Found.Add(new VacancyFoundPerson("m:" + manual.Id, name, gender ?? string.Empty, false,
                        manual.EmployeeId ?? string.Empty, manual.Id, manual.AddedAt));
                }
            }

            var candidates = employeeList
                .Where(e => e.StartDate.HasValue && !string.IsNullOrWhiteSpace(e.UniqueId) && !taken.Contains(e.UniqueId))
                .OrderBy(e => e.StartDate)
                .ThenBy(e => e.FullName, StringComparer.CurrentCultureIgnoreCase);

            foreach (var employee in candidates)
            {
                var start = employee.StartDate!.Value.Date;
                foreach (var vacancy in ordered)
                {
                    if (start < vacancy.CreatedAt.Date)
                        continue;
                    if (vacancy.ClosedAt.HasValue && start > vacancy.ClosedAt.Value.Date)
                        continue;
                    if (vacancy.ExcludedEmployeeIds?.Contains(employee.UniqueId, StringComparer.OrdinalIgnoreCase) == true)
                        continue;
                    if (!slots[vacancy.Id].Take(employee.Gender, result[vacancy.Id]))
                        continue;

                    result[vacancy.Id].Found.Add(new VacancyFoundPerson("e:" + employee.UniqueId, employee.FullName, employee.Gender, true,
                        employee.UniqueId, string.Empty, employee.StartDate));
                    break;
                }
            }

            return result;
        }

        private sealed class Slots
        {
            private int _men, _women, _couplesMen, _couplesWomen, _anyone;

            public Slots(Vacancy vacancy)
            {
                _men = Math.Max(0, vacancy.Men);
                _women = Math.Max(0, vacancy.Women);
                _couplesMen = _couplesWomen = Math.Max(0, vacancy.Couples);
                _anyone = Math.Max(0, vacancy.Anyone);
            }

            /// <summary>Takes a free place for this gender. Manual people are counted even without a free place.</summary>
            public bool Take(string? gender, VacancyProgress progress)
            {
                var isFemale = string.Equals(gender, "female", StringComparison.OrdinalIgnoreCase);
                var isMale = string.Equals(gender, "male", StringComparison.OrdinalIgnoreCase);
                if (isMale || (!isFemale && _men > 0))
                {
                    if (_men > 0) { _men--; progress.MenFound++; return true; }
                    if (_couplesMen > 0) { _couplesMen--; progress.CouplesMenFound++; return true; }
                }
                if (isFemale || (!isMale && _women > 0))
                {
                    if (_women > 0) { _women--; progress.WomenFound++; return true; }
                    if (_couplesWomen > 0) { _couplesWomen--; progress.CouplesWomenFound++; return true; }
                }
                if (_anyone > 0) { _anyone--; progress.AnyoneFound++; return true; }
                if (!isMale && !isFemale)
                {
                    if (_couplesMen > 0) { _couplesMen--; progress.CouplesMenFound++; return true; }
                    if (_couplesWomen > 0) { _couplesWomen--; progress.CouplesWomenFound++; return true; }
                }
                return false;
            }
        }
    }
}
