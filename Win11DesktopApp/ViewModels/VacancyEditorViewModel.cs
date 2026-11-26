using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.ViewModels
{
    /// <summary>The "Нова позиція / Редагувати" form. Works on a copy until Save.</summary>
    public class VacancyEditorViewModel : ViewModelBase
    {
        /// <summary>A position from the firm card: title and hourly pay.</summary>
        public sealed record FirmPosition(string Title, decimal HourlyPay);

        private readonly IReadOnlyDictionary<string, IReadOnlyList<FirmPosition>> _firmPositions;
        private readonly Func<Vacancy, string, Task<(string Description, string Ad)?>>? _aiWriter;
        private readonly Func<string, string>? _firmNoteReader;

        public VacancyEditorViewModel(Vacancy? source, IReadOnlyList<string> firms,
            IReadOnlyDictionary<string, IReadOnlyList<FirmPosition>>? firmPositions = null,
            Func<Vacancy, string, Task<(string Description, string Ad)?>>? aiWriter = null,
            Func<string, string>? firmNoteReader = null)
        {
            _aiWriter = aiWriter;
            _firmNoteReader = firmNoteReader;
            AiWriteCommand = new AsyncRelayCommand(_ => AiWriteAsync(), _ => !IsAiBusy && _aiWriter != null);
            Firms = firms;
            _firmPositions = firmPositions ?? new Dictionary<string, IReadOnlyList<FirmPosition>>();
            IsNew = source == null;
            var v = source ?? new Vacancy();
            Id = v.Id;
            _firmName = v.FirmName;
            _position = v.Position;
            _city = v.City;
            _men = Count(v.Men);
            _women = Count(v.Women);
            _couples = Count(v.Couples);
            _anyone = Count(v.Anyone);
            _neededFrom = DateTime.TryParseExact(v.NeededFrom, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
            _pay = v.Pay;
            _conditions = v.Conditions;
            _description = v.Description;
            _notes = v.Notes;
            _adText = v.AdText;
            RefreshPositionChoices();
            LoadFirmNote();
        }

        // ===== note of the firm (shared by all its positions) =====
        private string _firmNote = string.Empty;
        private string _loadedFirmNote = string.Empty;
        public string FirmNote { get => _firmNote; set => SetProperty(ref _firmNote, value); }
        public bool IsFirmNoteChanged => !string.Equals((FirmNote ?? string.Empty).Trim(), _loadedFirmNote.Trim(), StringComparison.Ordinal);

        private void LoadFirmNote()
        {
            _loadedFirmNote = string.IsNullOrWhiteSpace(FirmName) || _firmNoteReader == null ? string.Empty : _firmNoteReader(FirmName) ?? string.Empty;
            FirmNote = _loadedFirmNote;
        }

        // ===== AI text =====
        public ICommand AiWriteCommand { get; }

        private bool _isAiBusy;
        public bool IsAiBusy
        {
            get => _isAiBusy;
            private set
            {
                if (SetProperty(ref _isAiBusy, value))
                    CommandManager.InvalidateRequerySuggested();
            }
        }

        private string _adText;
        /// <summary>Advert text; the AI fills it together with the description, saved with the position.</summary>
        public string AdText { get => _adText; set => SetProperty(ref _adText, value); }

        private string _aiInfo = string.Empty;
        public string AiInfo { get => _aiInfo; private set => SetProperty(ref _aiInfo, value); }

        /// <summary>The form as it is now, without checks (for the AI request).</summary>
        internal Vacancy Snapshot() => new()
        {
            Id = Id,
            FirmName = FirmName ?? string.Empty,
            Position = (Position ?? string.Empty).Trim(),
            City = (City ?? string.Empty).Trim(),
            Men = ParseCount(Men),
            Women = ParseCount(Women),
            Couples = ParseCount(Couples),
            Anyone = ParseCount(Anyone),
            NeededFrom = NeededFrom?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
            Pay = (Pay ?? string.Empty).Trim(),
            Conditions = (Conditions ?? string.Empty).Trim()
        };

        private async Task AiWriteAsync()
        {
            if (_aiWriter == null || IsAiBusy)
                return;
            IsAiBusy = true;
            AiInfo = Res("VacAiThinking");
            try
            {
                var result = await _aiWriter(Snapshot(), Description ?? string.Empty);
                if (result == null)
                {
                    AiInfo = string.Empty;
                    return;
                }
                if (!string.IsNullOrWhiteSpace(result.Value.Description))
                    Description = result.Value.Description;
                if (!string.IsNullOrWhiteSpace(result.Value.Ad))
                    AdText = result.Value.Ad;
                AiInfo = Res("VacAiDone");
            }
            finally
            {
                IsAiBusy = false;
            }
        }

        /// <summary>Positions added to the selected firm (firm card → positions).</summary>
        public ObservableCollection<string> PositionChoices { get; } = new();
        public bool HasPositionChoices => PositionChoices.Count > 0;

        private void RefreshPositionChoices()
        {
            PositionChoices.Clear();
            if (!string.IsNullOrWhiteSpace(FirmName) && _firmPositions.TryGetValue(FirmName, out var positions))
            {
                foreach (var title in positions.Select(position => position.Title).Where(title => !string.IsNullOrWhiteSpace(title))
                             .Distinct(StringComparer.CurrentCultureIgnoreCase))
                    PositionChoices.Add(title);
            }
            OnPropertyChanged(nameof(HasPositionChoices));
        }

        /// <summary>Picking a firm position fills an empty pay with its hourly rate.</summary>
        private void ApplyPositionPay()
        {
            if (!string.IsNullOrWhiteSpace(Pay) || string.IsNullOrWhiteSpace(FirmName) || string.IsNullOrWhiteSpace(Position)
                || !_firmPositions.TryGetValue(FirmName, out var positions))
                return;
            var match = positions.FirstOrDefault(position => string.Equals(position.Title, Position.Trim(), StringComparison.CurrentCultureIgnoreCase));
            if (match != null && match.HourlyPay > 0)
            {
                var format = Res("VacPayHourFmt");
                if (!format.Contains("{0}", StringComparison.Ordinal))
                    format = "{0} Kč/h";
                Pay = string.Format(format, match.HourlyPay.ToString("0.##", CultureInfo.CurrentCulture));
            }
        }

        public IReadOnlyList<string> Firms { get; }
        public bool IsNew { get; }
        public string Id { get; }
        public string Title => IsNew ? Res("VacEditorNewTitle") : Res("VacEditorEditTitle");

        private string _firmName;
        public string FirmName
        {
            get => _firmName;
            set
            {
                if (!SetProperty(ref _firmName, value))
                    return;
                ErrorText = string.Empty;
                RefreshPositionChoices();
                LoadFirmNote();
                if (PositionChoices.Count == 1 && string.IsNullOrWhiteSpace(Position))
                    Position = PositionChoices[0];
            }
        }
        private string _position;
        public string Position
        {
            get => _position;
            set
            {
                if (SetProperty(ref _position, value))
                    ApplyPositionPay();
            }
        }
        private string _city;
        public string City { get => _city; set => SetProperty(ref _city, value); }
        private string _men;
        public string Men { get => _men; set { if (SetProperty(ref _men, value)) OnTotalChanged(); } }
        private string _women;
        public string Women { get => _women; set { if (SetProperty(ref _women, value)) OnTotalChanged(); } }
        private string _couples;
        public string Couples { get => _couples; set { if (SetProperty(ref _couples, value)) OnTotalChanged(); } }
        private string _anyone;
        public string Anyone { get => _anyone; set { if (SetProperty(ref _anyone, value)) OnTotalChanged(); } }
        private DateTime? _neededFrom;
        public DateTime? NeededFrom { get => _neededFrom; set => SetProperty(ref _neededFrom, value); }
        private string _pay;
        public string Pay { get => _pay; set => SetProperty(ref _pay, value); }
        private string _conditions;
        public string Conditions { get => _conditions; set => SetProperty(ref _conditions, value); }
        private string _description;
        public string Description { get => _description; set => SetProperty(ref _description, value); }
        private string _notes;
        public string Notes { get => _notes; set => SetProperty(ref _notes, value); }

        private string _errorText = string.Empty;
        public string ErrorText { get => _errorText; set => SetProperty(ref _errorText, value); }

        public int TotalPeople => ParseCount(Men) + ParseCount(Women) + ParseCount(Anyone) + 2 * ParseCount(Couples);
        public string TotalText => string.Format(Res("VacEditorTotalFmt"), TotalPeople);

        private void OnTotalChanged()
        {
            ErrorText = string.Empty;
            OnPropertyChanged(nameof(TotalPeople));
            OnPropertyChanged(nameof(TotalText));
        }

        /// <summary>Checks the form; on success copies it into <paramref name="target"/>.</summary>
        public bool TryApply(Vacancy target)
        {
            if (string.IsNullOrWhiteSpace(FirmName))
            {
                ErrorText = Res("VacEditorErrorFirm");
                return false;
            }
            if (TotalPeople <= 0)
            {
                ErrorText = Res("VacEditorErrorPeople");
                return false;
            }

            target.FirmName = FirmName.Trim();
            target.Position = (Position ?? string.Empty).Trim();
            target.City = (City ?? string.Empty).Trim();
            target.Men = ParseCount(Men);
            target.Women = ParseCount(Women);
            target.Couples = ParseCount(Couples);
            target.Anyone = ParseCount(Anyone);
            target.NeededFrom = NeededFrom?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
            target.Pay = (Pay ?? string.Empty).Trim();
            target.Conditions = (Conditions ?? string.Empty).Trim();
            target.Description = (Description ?? string.Empty).Trim();
            target.Notes = (Notes ?? string.Empty).Trim();
            target.AdText = AdText ?? string.Empty;
            return true;
        }

        internal static int ParseCount(string? text)
            => int.TryParse((text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? Math.Clamp(value, 0, 999)
                : 0;

        private static string Count(int value) => value > 0 ? value.ToString(CultureInfo.InvariantCulture) : string.Empty;
    }
}
