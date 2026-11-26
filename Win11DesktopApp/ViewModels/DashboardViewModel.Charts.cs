using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Media;
using Win11DesktopApp.Controls;

namespace Win11DesktopApp.ViewModels
{
    /// <summary>One firm line in the "hours by firm" block.</summary>
    public sealed class FirmHoursItem
    {
        public string FirmName { get; init; } = string.Empty;
        public decimal Hours { get; init; }
        public double Ratio { get; init; }
        public string HoursText { get; init; } = string.Empty;
    }

    /// <summary>The four chart blocks under the salary table: data, order and the height split.</summary>
    public partial class DashboardViewModel
    {
        public const string ChartPayouts = "payouts";
        public const string ChartFirms = "firms";
        public const string ChartHours = "hours";
        public const string ChartWho = "who";
        private static readonly string[] DefaultChartOrder = { ChartPayouts, ChartFirms, ChartHours, ChartWho };
        private const int FirmsShownInRing = 3;
        private const int FirmsShownInHours = 6;
        private const int MonthsInPayouts = 12;

        private static readonly Brush[] FirmPalette = { Frozen("#378ADD"), Frozen("#7F77DD"), Frozen("#1D9E75"), Frozen("#B4B2A9") };
        public static Brush PayoutBrush { get; } = Frozen("#1D9E75");
        public static Brush PeopleBrush { get; } = Frozen("#378ADD");
        public static Brush HoursBrush { get; } = Frozen("#BA7517");

        public ObservableCollection<string> SalaryChartBlocks { get; } = new(DefaultChartOrder);

        private IReadOnlyList<ChartMonthBar> _payoutBars = Array.Empty<ChartMonthBar>();
        public IReadOnlyList<ChartMonthBar> PayoutBars { get => _payoutBars; private set => SetProperty(ref _payoutBars, value); }

        private IReadOnlyList<ChartSegment> _firmPeopleSegments = Array.Empty<ChartSegment>();
        public IReadOnlyList<ChartSegment> FirmPeopleSegments { get => _firmPeopleSegments; private set => SetProperty(ref _firmPeopleSegments, value); }

        private string _firmPeopleTotalText = "0";
        public string FirmPeopleTotalText { get => _firmPeopleTotalText; private set => SetProperty(ref _firmPeopleTotalText, value); }

        private IReadOnlyList<FirmHoursItem> _firmHoursItems = Array.Empty<FirmHoursItem>();
        public IReadOnlyList<FirmHoursItem> FirmHoursItems { get => _firmHoursItems; private set => SetProperty(ref _firmHoursItems, value); }

        private string _firmHoursCaption = string.Empty;
        public string FirmHoursCaption { get => _firmHoursCaption; private set => SetProperty(ref _firmHoursCaption, value); }

        private IReadOnlyList<ChartSegment> _documentSegments = Array.Empty<ChartSegment>();
        public IReadOnlyList<ChartSegment> DocumentSegments { get => _documentSegments; private set => SetProperty(ref _documentSegments, value); }

        private IReadOnlyList<ChartSegment> _genderSegments = Array.Empty<ChartSegment>();
        public IReadOnlyList<ChartSegment> GenderSegments { get => _genderSegments; private set => SetProperty(ref _genderSegments, value); }

        // ===== Headline texts (minimal style A): a big number and one short line per block =====
        private string _payoutsTotalText = "—";
        public string PayoutsTotalText { get => _payoutsTotalText; private set => SetProperty(ref _payoutsTotalText, value); }
        private string _payoutsSubText = string.Empty;
        public string PayoutsSubText { get => _payoutsSubText; private set => SetProperty(ref _payoutsSubText, value); }
        private string _payoutsMinMaxText = string.Empty;
        public string PayoutsMinMaxText { get => _payoutsMinMaxText; private set => SetProperty(ref _payoutsMinMaxText, value); }

        private IReadOnlyList<FirmHoursItem> _firmPeopleItems = Array.Empty<FirmHoursItem>();
        /// <summary>Top firms by people as thin bars (count in HoursText).</summary>
        public IReadOnlyList<FirmHoursItem> FirmPeopleItems { get => _firmPeopleItems; private set => SetProperty(ref _firmPeopleItems, value); }
        private string _firmPeopleSubText = string.Empty;
        public string FirmPeopleSubText { get => _firmPeopleSubText; private set => SetProperty(ref _firmPeopleSubText, value); }
        private string _firmPeopleMoreText = string.Empty;
        public string FirmPeopleMoreText { get => _firmPeopleMoreText; private set => SetProperty(ref _firmPeopleMoreText, value); }

        private string _hoursTotalText = "—";
        public string HoursTotalText { get => _hoursTotalText; private set => SetProperty(ref _hoursTotalText, value); }
        private string _hoursSubText = string.Empty;
        public string HoursSubText { get => _hoursSubText; private set => SetProperty(ref _hoursSubText, value); }

        private string _visaShareText = "—";
        public string VisaShareText { get => _visaShareText; private set => SetProperty(ref _visaShareText, value); }
        private string _documentSummaryText = string.Empty;
        public string DocumentSummaryText { get => _documentSummaryText; private set => SetProperty(ref _documentSummaryText, value); }
        private string _genderSummaryText = string.Empty;
        public string GenderSummaryText { get => _genderSummaryText; private set => SetProperty(ref _genderSummaryText, value); }

        private void LoadChartOrder()
        {
            var saved = _appSettingsService.Settings.DashSalaryChartsOrder ?? new List<string>();
            var order = saved.Where(key => DefaultChartOrder.Contains(key)).Distinct().ToList();
            order.AddRange(DefaultChartOrder.Where(key => !order.Contains(key)));
            SalaryChartBlocks.Clear();
            foreach (var key in order)
                SalaryChartBlocks.Add(key);
        }

        /// <summary>Puts the dragged block where the target block is; the order is remembered.</summary>
        public void MoveSalaryChartBlock(string? source, string? target)
        {
            if (source == null || target == null || source == target)
                return;
            var from = SalaryChartBlocks.IndexOf(source);
            var to = SalaryChartBlocks.IndexOf(target);
            if (from < 0 || to < 0)
                return;

            SalaryChartBlocks.Move(from, to);
            _appSettingsService.Settings.DashSalaryChartsOrder = SalaryChartBlocks.ToList();
            _appSettingsService.SaveSettings();
        }

        private void ApplyChartData(DashboardData data)
        {
            PayoutBars = BuildPayoutBars(data.SalaryMonths);
            FirmPeopleSegments = BuildFirmPeopleSegments(data.CompanyStats.Select(firm => (firm.CompanyName, firm.EmployeeCount)), Res("DashChartOthers") ?? "інші");
            FirmPeopleTotalText = data.CompanyStats.Sum(firm => firm.EmployeeCount).ToString(CultureInfo.InvariantCulture);

            var hoursMonth = data.SalaryMonths
                .Where(month => month.HoursByFirm.Values.Sum() > 0)
                .OrderByDescending(month => month.MonthKey)
                .FirstOrDefault();
            FirmHoursItems = hoursMonth == null ? Array.Empty<FirmHoursItem>() : BuildFirmHours(hoursMonth.HoursByFirm);
            FirmHoursCaption = hoursMonth?.MonthLabel ?? string.Empty;

            DocumentSegments = new[]
            {
                Segment(Res("DashChartVisa") ?? "віза", data.EmployeeTypeCounts.GetValueOrDefault("visa"), "#7F77DD"),
                Segment(Res("DashChartEu") ?? "ЄС", data.EmployeeTypeCounts.GetValueOrDefault("eu_citizen"), "#AFA9EC"),
                Segment(Res("DashChartPermit") ?? "дозвіл", data.EmployeeTypeCounts.GetValueOrDefault("work_permit"), "#CECBF6"),
                Segment(Res("DashChartOtherType") ?? "інше", data.EmployeeTypeCounts
                    .Where(pair => pair.Key is not ("visa" or "eu_citizen" or "work_permit")).Sum(pair => pair.Value), "#B4B2A9")
            }.Where(segment => segment.Value > 0).ToList();

            GenderSegments = new[]
            {
                Segment(Res("DashChartWomen") ?? "жінки", data.GenderCounts.GetValueOrDefault("female"), "#D4537E"),
                Segment(Res("DashChartMen") ?? "чоловіки", data.GenderCounts.GetValueOrDefault("male"), "#7F77DD")
            }.Where(segment => segment.Value > 0).ToList();

            ApplyHeadlines(data, hoursMonth);
        }

        private void ApplyHeadlines(DashboardData data, SalaryMonthSummary? hoursMonth)
        {
            var million = Res("DashChartMillionShort") ?? "млн";
            var started = data.SalaryMonths.Where(month => !month.IsNotStarted && month.TotalNet > 0)
                .OrderBy(month => month.MonthKey).TakeLast(MonthsInPayouts).ToList();
            PayoutsTotalText = started.Count == 0 ? "—" : FormatCompact(started.Sum(month => month.TotalNet), million);
            if (started.Count > 0)
            {
                var last = started[^1];
                var average = started.Average(month => month.TotalNet);
                PayoutsSubText = string.Format(Res("DashChartPayoutsSubFmt") ?? "{0} {1} · середньо {2}",
                    ShortMonth(last.MonthKey), FormatCompact(last.TotalNet, million, withUnit: false), FormatCompact(average, million, withUnit: false));
                var min = started.MinBy(month => month.TotalNet)!;
                var max = started.MaxBy(month => month.TotalNet)!;
                PayoutsMinMaxText = string.Format(Res("DashChartMinMaxFmt") ?? "мін {0} {1} · макс {2} {3}",
                    ShortMonth(min.MonthKey), FormatCompact(min.TotalNet, million, withUnit: false),
                    ShortMonth(max.MonthKey), FormatCompact(max.TotalNet, million, withUnit: false));
            }
            else
            {
                PayoutsSubText = string.Empty;
                PayoutsMinMaxText = string.Empty;
            }

            var firms = data.CompanyStats.Where(firm => firm.EmployeeCount > 0).OrderByDescending(firm => firm.EmployeeCount)
                .ThenBy(firm => firm.CompanyName, StringComparer.CurrentCultureIgnoreCase).ToList();
            var people = firms.Sum(firm => firm.EmployeeCount);
            var topFirms = firms.Take(5).ToList();
            var maxPeople = topFirms.Count == 0 ? 1 : topFirms[0].EmployeeCount;
            FirmPeopleItems = topFirms.Select(firm => new FirmHoursItem
            {
                FirmName = firm.CompanyName,
                Hours = firm.EmployeeCount,
                Ratio = (double)firm.EmployeeCount / maxPeople,
                HoursText = firm.EmployeeCount.ToString(CultureInfo.InvariantCulture)
            }).ToList();
            FirmPeopleSubText = string.Format(Res("DashChartPeopleSubFmt") ?? "людей · {0} фірм", firms.Count);
            var restFirms = firms.Skip(5).ToList();
            FirmPeopleMoreText = restFirms.Count == 0 ? string.Empty
                : string.Format(Res("DashChartMoreFirmsFmt") ?? "ще {0} фірм · {1}", restFirms.Count, restFirms.Sum(firm => firm.EmployeeCount));

            if (hoursMonth != null)
            {
                var totalHours = hoursMonth.HoursByFirm.Values.Sum();
                HoursTotalText = SalaryViewModel.FormatMoney(decimal.Round(totalHours));
                var perPerson = hoursMonth.TotalEntries > 0 ? decimal.Round(totalHours / hoursMonth.TotalEntries) : 0m;
                HoursSubText = string.Format(Res("DashChartHoursSubFmt") ?? "{0} · ≈ {1} на людину", hoursMonth.MonthLabel, SalaryViewModel.FormatMoney(perPerson));
            }
            else
            {
                HoursTotalText = "—";
                HoursSubText = string.Empty;
            }

            var visa = data.EmployeeTypeCounts.GetValueOrDefault("visa");
            var eu = data.EmployeeTypeCounts.GetValueOrDefault("eu_citizen");
            var permit = data.EmployeeTypeCounts.GetValueOrDefault("work_permit");
            var allTypes = data.EmployeeTypeCounts.Values.Sum();
            var other = allTypes - visa - eu - permit;
            VisaShareText = allTypes == 0 ? "—" : $"{Math.Round(100.0 * visa / allTypes)}%";
            DocumentSummaryText = string.Format(Res("DashChartDocsSummaryFmt") ?? "віза {0} · ЄС {1} · дозвіл {2} · інше {3}", visa, eu, permit, other);
            GenderSummaryText = string.Format(Res("DashChartGenderSummaryFmt") ?? "жінки {0} · чоловіки {1}",
                data.GenderCounts.GetValueOrDefault("female"), data.GenderCounts.GetValueOrDefault("male"));
        }

        /// <summary>"44,2 млн Kč" for millions, otherwise "362 712 Kč".</summary>
        internal static string FormatCompact(decimal amount, string millionWord, bool withUnit = true)
        {
            if (Math.Abs(amount) >= 1_000_000m)
            {
                var value = (amount / 1_000_000m).ToString(Math.Abs(amount) >= 10_000_000m ? "0.0" : "0.00", CultureInfo.InvariantCulture).Replace('.', ',');
                return withUnit ? $"{value} {millionWord}" : value;
            }

            return SalaryViewModel.FormatMoney(decimal.Round(amount));
        }

        /// <summary>Last 12 months, oldest first: "to pay" with firm expenses on top. Months not started count as 0.</summary>
        internal static IReadOnlyList<ChartMonthBar> BuildPayoutBars(IEnumerable<SalaryMonthSummary> months)
        {
            return months
                .OrderByDescending(month => month.MonthKey)
                .Take(MonthsInPayouts)
                .OrderBy(month => month.MonthKey)
                .Select(month => new ChartMonthBar
                {
                    Label = ShortMonth(month.MonthKey),
                    Primary = month.IsNotStarted ? 0 : (double)Math.Max(0m, month.TotalNet),
                    Secondary = (double)Math.Max(0m, month.TotalExpenses)
                })
                .ToList();
        }

        /// <summary>The biggest firms by people, the rest joined as "others".</summary>
        internal static IReadOnlyList<ChartSegment> BuildFirmPeopleSegments(IEnumerable<(string Name, int People)> firms, string othersLabel)
        {
            var ordered = firms.Where(firm => firm.People > 0).OrderByDescending(firm => firm.People).ThenBy(firm => firm.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            var result = ordered.Take(FirmsShownInRing)
                .Select((firm, index) => new ChartSegment { Label = firm.Name, Value = firm.People, Brush = FirmPalette[index], ValueText = firm.People.ToString(CultureInfo.InvariantCulture) })
                .ToList();
            var rest = ordered.Skip(FirmsShownInRing).Sum(firm => firm.People);
            if (rest > 0)
                result.Add(new ChartSegment { Label = othersLabel, Value = rest, Brush = FirmPalette[^1], ValueText = rest.ToString(CultureInfo.InvariantCulture) });
            return result;
        }

        internal static IReadOnlyList<FirmHoursItem> BuildFirmHours(IReadOnlyDictionary<string, decimal> hoursByFirm)
        {
            var ordered = hoursByFirm.Where(pair => pair.Value > 0).OrderByDescending(pair => pair.Value).Take(FirmsShownInHours).ToList();
            var max = ordered.Count == 0 ? 1m : ordered[0].Value;
            return ordered.Select(pair => new FirmHoursItem
            {
                FirmName = pair.Key,
                Hours = pair.Value,
                Ratio = (double)(pair.Value / max),
                HoursText = SalaryViewModel.FormatMoney(decimal.Round(pair.Value))
            }).ToList();
        }

        private static ChartSegment Segment(string label, int value, string color)
            => new() { Label = label, Value = value, Brush = Frozen(color), ValueText = value.ToString(CultureInfo.InvariantCulture) };

        private static string ShortMonth(string monthKey)
        {
            if (monthKey.Length >= 7 && int.TryParse(monthKey.Substring(5, 2), out var month) && month is >= 1 and <= 12)
                return CultureInfo.CurrentUICulture.DateTimeFormat.GetAbbreviatedMonthName(month).TrimEnd('.');
            return monthKey;
        }

        private static Brush Frozen(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }
    }
}
