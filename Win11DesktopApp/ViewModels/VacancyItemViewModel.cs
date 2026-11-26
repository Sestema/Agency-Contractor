using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.ViewModels
{
    /// <summary>One position in the list and in the details panel.</summary>
    public class VacancyItemViewModel : ViewModelBase
    {
        public VacancyItemViewModel(Vacancy model)
        {
            Model = model;
        }

        public Vacancy Model { get; private set; }
        public string Id => Model.Id;
        public string FirmName => Model.FirmName;
        public string Position => Model.Position;
        public string Title => string.IsNullOrWhiteSpace(Model.Position) ? Model.FirmName : $"{Model.FirmName} · {Model.Position}";
        public string Status => Model.Status;
        public bool IsClosed => Model.Status == VacancyStatus.Closed;
        public bool IsPaused => Model.Status == VacancyStatus.Paused;

        public int Needed => Model.TotalNeeded;
        public int Found { get; private set; }
        public int Remaining => Math.Max(0, Needed - Found);
        public bool IsFilled => Needed > 0 && Found >= Needed;
        public double ProgressRatio => Needed <= 0 ? 0 : Math.Min(1.0, (double)Found / Needed);

        public bool HasMen => Model.Men > 0;
        public bool HasWomen => Model.Women > 0;
        public bool HasCouples => Model.Couples > 0;
        public bool HasAnyone => Model.Anyone > 0;
        public string MenChip => string.Format(Res("VacChipMenFmt"), Model.Men);
        public string WomenChip => string.Format(Res("VacChipWomenFmt"), Model.Women);
        public string CouplesChip => string.Format(Res("VacChipCouplesFmt"), Model.Couples);
        public string AnyoneChip => string.Format(Res("VacChipAnyoneFmt"), Model.Anyone);

        public string ProgressText => string.Format(Res("VacProgressFmt"), Found, Needed, Remaining);
        public string BigProgressText => $"{Found} / {Needed}";

        /// <summary>People still missing in each group (for the PDF).</summary>
        public int MenLeft { get; private set; }
        public int WomenLeft { get; private set; }
        public int CouplesLeft { get; private set; }
        public int AnyoneLeft { get; private set; }

        /// <summary>"чоловіки 3/4 · жінки 1/2 · пари 0/2".</summary>
        public string BreakdownText { get; private set; } = string.Empty;

        public string NeededFromText => FormatDate(Model.NeededFrom);
        public bool HasNeededFrom => NeededFromText.Length > 0;
        public string CreatedText => Model.CreatedAt.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        public string Pay => Model.Pay;
        public bool HasPay => !string.IsNullOrWhiteSpace(Model.Pay);
        public string City => Model.City;
        public bool HasCity => !string.IsNullOrWhiteSpace(Model.City);
        public string Conditions => Model.Conditions;
        public bool HasConditions => !string.IsNullOrWhiteSpace(Model.Conditions);
        public string Description => Model.Description;
        public bool HasDescription => !string.IsNullOrWhiteSpace(Model.Description);
        public string Notes => Model.Notes;
        public bool HasNotes => !string.IsNullOrWhiteSpace(Model.Notes);
        public string AdText => Model.AdText;
        public bool HasAdText => !string.IsNullOrWhiteSpace(Model.AdText);

        public IReadOnlyList<VacancyFoundRow> FoundPeople { get; private set; } = Array.Empty<VacancyFoundRow>();

        public void Apply(Vacancy model, VacancyProgress? progress)
        {
            Model = model;
            progress ??= new VacancyProgress();
            Found = progress.FoundCount;
            FoundPeople = progress.Found
                .OrderBy(person => person.Date ?? DateTime.MaxValue)
                .ThenBy(person => person.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(person => new VacancyFoundRow(person))
                .ToList();

            var parts = new List<string>();
            if (model.Men > 0) parts.Add(string.Format(Res("VacBreakMenFmt"), progress.MenFound, model.Men));
            if (model.Women > 0) parts.Add(string.Format(Res("VacBreakWomenFmt"), progress.WomenFound, model.Women));
            if (model.Couples > 0)
                parts.Add(string.Format(Res("VacBreakCouplesFmt"), Math.Min(progress.CouplesMenFound, progress.CouplesWomenFound), model.Couples));
            if (model.Anyone > 0) parts.Add(string.Format(Res("VacBreakAnyoneFmt"), progress.AnyoneFound, model.Anyone));
            BreakdownText = string.Join(" · ", parts);

            MenLeft = Math.Max(0, model.Men - progress.MenFound);
            WomenLeft = Math.Max(0, model.Women - progress.WomenFound);
            CouplesLeft = Math.Max(0, model.Couples - Math.Min(progress.CouplesMenFound, progress.CouplesWomenFound));
            AnyoneLeft = Math.Max(0, model.Anyone - progress.AnyoneFound);
            OnPropertyChanged(string.Empty);
        }

        internal static string FormatDate(string value)
            => DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)
                : string.Empty;
    }

    public class VacancyFoundRow
    {
        public VacancyFoundRow(VacancyFoundPerson person)
        {
            Person = person;
        }

        public VacancyFoundPerson Person { get; }
        public string Name => Person.Name;
        public bool IsAuto => Person.IsAuto;
        public bool IsFemale => string.Equals(Person.Gender, "female", StringComparison.OrdinalIgnoreCase);
        public string GenderText => IsFemale ? ViewModelBaseRes("VacGenderFemaleShort")
            : string.Equals(Person.Gender, "male", StringComparison.OrdinalIgnoreCase) ? ViewModelBaseRes("VacGenderMaleShort") : string.Empty;
        public string DateText => Person.Date?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? string.Empty;
        public string SourceText => IsAuto ? ViewModelBaseRes("VacFoundAuto") : ViewModelBaseRes("VacFoundManual");

        private static string ViewModelBaseRes(string key) => System.Windows.Application.Current?.TryFindResource(key) as string ?? key;
    }
}
