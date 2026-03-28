using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AdminPanel
{
    public partial class ClientMirrorWindow : Window
    {
        private readonly SupabaseService _svc;
        private readonly ClientRecord _client;
        private readonly Dictionary<Guid, string> _employerNames = new();
        private List<AdminMirrorAgencyRecord> _allAgencies = new();
        private List<AdminMirrorEmployerRecord> _allEmployers = new();
        private List<AdminMirrorEmployeeRecord> _allEmployees = new();
        private bool _isUiReady;

        public ClientMirrorWindow(SupabaseService svc, ClientRecord client)
        {
            InitializeComponent();
            _svc = svc;
            _client = client;
            _isUiReady = true;
            Loaded += async (_, _) => await LoadSnapshotAsync();
        }

        private async Task LoadSnapshotAsync()
        {
            BtnRefreshMirror.IsEnabled = false;
            try
            {
                TxtHeader.Text = $"Mirror: {_client.DisplayName}";
                TxtSubHeader.Text = $"Client ID: {_client.Id}";
                TxtMirrorStatus.Text = "Loading...";

                var snapshot = await _svc.GetClientMirrorSnapshotAsync(_client.Id);

                TxtAgencyCount.Text = snapshot.Agencies.Count.ToString();
                TxtEmployerCount.Text = snapshot.Employers.Count.ToString();
                TxtEmployeeCount.Text = snapshot.Employees.Count.ToString();
                PopulateSyncHealth(snapshot.State);

                _allAgencies = snapshot.Agencies.OrderBy(item => item.DisplayName).ToList();
                _allEmployers = snapshot.Employers.OrderBy(item => item.DisplayName).ToList();

                LstAgencies.ItemsSource = _allAgencies;
                ApplyEmployerFilter();

                _employerNames.Clear();
                foreach (var employer in snapshot.Employers)
                    _employerNames[employer.EmployerId] = employer.DisplayName;

                _allEmployees = snapshot.Employees
                    .Select(AttachEmployerDisplayName)
                    .OrderBy(item => item.FullName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                ApplyEmployeeFilters();

                PopulateAgencyDetails(null);
                PopulateEmployerDetails(null);

                if (_allEmployees.Count > 0)
                    DgEmployees.SelectedIndex = 0;
                else
                    PopulateEmployeeDetails(null);
            }
            catch (Exception ex)
            {
                TxtMirrorStatus.Text = "Load failed";
                MessageBox.Show(this, $"Не вдалося завантажити mirror snapshot:\n{ex.Message}", "Mirror error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnRefreshMirror.IsEnabled = true;
            }
        }

        private static string BuildStatusText(ClientMirrorStateRecord? state)
        {
            if (state == null)
                return "Mirror not initialized";
            if (!string.IsNullOrWhiteSpace(state.LastErrorText))
                return $"Error: {state.LastErrorText}";
            if (state.LastDeltaSyncAt.HasValue)
                return "Synchronized";
            return "Waiting for first sync";
        }

        private static string FormatDate(DateTime? value)
        {
            return value.HasValue ? value.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss") : "—";
        }

        private async void BtnRefreshMirror_Click(object sender, RoutedEventArgs e)
        {
            await LoadSnapshotAsync();
        }

        private void LstAgencies_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isUiReady)
                return;

            PopulateAgencyDetails(LstAgencies.SelectedItem as AdminMirrorAgencyRecord);
            ApplyEmployerFilter();
            ApplyEmployeeFilters();
        }

        private void LstEmployers_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isUiReady)
                return;

            PopulateEmployerDetails(LstEmployers.SelectedItem as AdminMirrorEmployerRecord);
            ApplyEmployeeFilters();
        }

        private void DgEmployees_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            PopulateEmployeeDetails(DgEmployees.SelectedItem as AdminMirrorEmployeeRecord);
        }

        private void TxtEmployeeSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isUiReady)
                return;

            ApplyEmployeeFilters();
        }

        private void CmbEmployeeStatusFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isUiReady)
                return;

            ApplyEmployeeFilters();
        }

        private void ApplyEmployeeFilters()
        {
            if (!_isUiReady || DgEmployees == null || CmbEmployeeStatusFilter == null || TxtEmployeeSearch == null)
                return;

            var query = TxtEmployeeSearch.Text?.Trim() ?? string.Empty;
            var statusFilter = (CmbEmployeeStatusFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "all";
            var selectedAgency = LstAgencies.SelectedItem as AdminMirrorAgencyRecord;
            var selectedEmployer = LstEmployers.SelectedItem as AdminMirrorEmployerRecord;
            IEnumerable<AdminMirrorEmployeeRecord> filtered = _allEmployees;

            if (selectedEmployer != null)
            {
                filtered = filtered.Where(item =>
                    item.EmployerId == selectedEmployer.EmployerId
                    || (!item.EmployerId.HasValue &&
                        item.ArchivedFromFirm.Contains(selectedEmployer.DisplayName, StringComparison.OrdinalIgnoreCase)));
            }
            else if (selectedAgency != null)
            {
                var employerIds = _allEmployers
                    .Where(item => string.Equals(item.AgencyId, selectedAgency.AgencyId, StringComparison.OrdinalIgnoreCase))
                    .Select(item => item.EmployerId)
                    .ToHashSet();
                var employerNames = _allEmployers
                    .Where(item => string.Equals(item.AgencyId, selectedAgency.AgencyId, StringComparison.OrdinalIgnoreCase))
                    .Select(item => item.DisplayName)
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .ToList();

                filtered = filtered.Where(item =>
                    (item.EmployerId.HasValue && employerIds.Contains(item.EmployerId.Value))
                    || (!item.EmployerId.HasValue && employerNames.Any(name =>
                        item.ArchivedFromFirm.Contains(name, StringComparison.OrdinalIgnoreCase))));
            }

            filtered = statusFilter switch
            {
                "active" => filtered.Where(item => string.Equals(item.StatusCode, "active", StringComparison.OrdinalIgnoreCase)),
                "archived" => filtered.Where(item => string.Equals(item.StatusCode, "archived", StringComparison.OrdinalIgnoreCase)),
                "deleted" => filtered.Where(item => string.Equals(item.StatusCode, "deleted", StringComparison.OrdinalIgnoreCase)),
                _ => filtered
            };

            if (!string.IsNullOrWhiteSpace(query))
            {
                filtered = filtered.Where(item =>
                    item.FullName.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || item.EmployerDisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || item.PositionTag.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || item.Phone.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || item.Email.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || item.Status.Contains(query, StringComparison.OrdinalIgnoreCase));
            }

            DgEmployees.ItemsSource = filtered.ToList();
            if (DgEmployees.Items.Count > 0)
                DgEmployees.SelectedIndex = 0;
            else
                PopulateEmployeeDetails(null);
        }

        private void ApplyEmployerFilter()
        {
            if (!_isUiReady || LstEmployers == null)
                return;

            var selectedAgency = LstAgencies.SelectedItem as AdminMirrorAgencyRecord;
            var previouslySelectedEmployerId = (LstEmployers.SelectedItem as AdminMirrorEmployerRecord)?.EmployerId;

            var filteredEmployers = selectedAgency == null
                ? _allEmployers
                : _allEmployers
                    .Where(item => string.Equals(item.AgencyId, selectedAgency.AgencyId, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(item => item.DisplayName)
                    .ToList();

            LstEmployers.ItemsSource = filteredEmployers;

            if (previouslySelectedEmployerId.HasValue)
            {
                var match = filteredEmployers.FirstOrDefault(item => item.EmployerId == previouslySelectedEmployerId.Value);
                if (match != null)
                {
                    LstEmployers.SelectedItem = match;
                    return;
                }
            }

            LstEmployers.SelectedItem = null;
            PopulateEmployerDetails(null);
        }

        private void PopulateAgencyDetails(AdminMirrorAgencyRecord? agency)
        {
            TxtAgencyName.Text = agency?.Name ?? "—";
            TxtAgencyIco.Text = agency?.Ico ?? "—";
            TxtAgencyAddress.Text = agency?.FullAddress ?? "—";
            TxtAgencySourceUpdated.Text = FormatDate(agency?.SourceUpdatedAt);
            TxtAgencyState.Text = agency == null ? "—" : agency.IsDeleted ? "Deleted" : "Active";
        }

        private void PopulateEmployerDetails(AdminMirrorEmployerRecord? employer)
        {
            TxtEmployerName.Text = employer?.Name ?? "—";
            TxtEmployerIco.Text = employer?.Ico ?? "—";
            TxtEmployerLegalAddress.Text = employer?.LegalAddress ?? "—";
            TxtEmployerWeeklyHours.Text = employer == null ? "—" : employer.WeeklyWorkHours.ToString("0.##");
            TxtEmployerDailyHours.Text = employer == null ? "—" : employer.DailyWorkHours.ToString("0.##");
            TxtEmployerShiftCount.Text = employer == null ? "—" : employer.ShiftCount.ToString();
            TxtEmployerState.Text = employer == null ? "—" : employer.IsDeleted ? "Deleted" : "Active";
            DgEmployerAddresses.ItemsSource = employer?.Addresses.OrderBy(item => item.SortOrder).ToList();
            DgEmployerPositions.ItemsSource = employer?.Positions.OrderBy(item => item.SortOrder).ToList();
        }

        private void PopulateEmployeeDetails(AdminMirrorEmployeeRecord? employee)
        {
            TxtEmployeeName.Text = employee?.FullName ?? "—";
            TxtEmployeeStatus.Text = employee?.DisplayStatus ?? "—";
            TxtEmployeePosition.Text = employee?.PositionTag ?? "—";
            TxtEmployeeEmployerName.Text = employee?.EmployerDisplayName ?? "—";
            TxtEmployeePhone.Text = employee?.Phone ?? "—";
            TxtEmployeeEmail.Text = employee?.Email ?? "—";
            TxtEmployeeBirthDate.Text = employee?.BirthDate ?? "—";
            TxtEmployeeGender.Text = ResolveGender(employee?.Gender);
            TxtEmployeeType.Text = ResolveEmployeeType(employee?.EmployeeType);
            TxtEmployeeEuDocumentType.Text = ResolveEuDocumentType(employee?.EuDocumentType);
            TxtEmployeeDepartment.Text = employee?.Department ?? "—";
            TxtEmployeeContractType.Text = employee?.ContractType ?? "—";
            TxtEmployeeMonthlySalary.Text = FormatMoney(employee?.MonthlySalaryBrutto);
            TxtEmployeeHourlySalary.Text = FormatMoney(employee?.HourlySalary);
            TxtEmployeeLocalAddress.Text = employee?.LocalAddress ?? "—";
            TxtEmployeeAbroadAddress.Text = employee?.AbroadAddress ?? "—";
            TxtEmployeePassportNumber.Text = employee?.PassportNumber ?? "—";
            TxtEmployeePassportExpiry.Text = employee?.PassportExpiry ?? "—";
            TxtEmployeePassportCity.Text = employee?.PassportCity ?? "—";
            TxtEmployeePassportCountry.Text = employee?.PassportCountry ?? "—";
            TxtEmployeeVisaTitle.Text = ResolveVisaTitle(employee);
            TxtEmployeeVisaExpiry.Text = employee?.VisaExpiry ?? "—";
            TxtEmployeeVisaType.Text = employee?.VisaType ?? "—";
            TxtEmployeeVisaDocType.Text = employee?.VisaDocType ?? "—";
            TxtEmployeeInsuranceCompany.Text = employee?.InsuranceCompanyShort ?? "—";
            TxtEmployeeInsuranceNumber.Text = employee?.InsuranceNumber ?? "—";
            TxtEmployeeInsuranceExpiry.Text = employee?.InsuranceExpiry ?? "—";
            TxtEmployeeWorkPermitName.Text = employee?.WorkPermitName ?? "—";
            TxtEmployeeWorkPermitNumber.Text = employee?.WorkPermitNumber ?? "—";
            TxtEmployeeWorkPermitType.Text = employee?.WorkPermitType ?? "—";
            TxtEmployeeWorkPermitExpiry.Text = employee?.WorkPermitExpiry ?? "—";
            TxtEmployeeWorkPermitIssueDate.Text = employee?.WorkPermitIssueDate ?? "—";
            TxtEmployeeWorkPermitAuthority.Text = employee?.WorkPermitAuthority ?? "—";
            DgEmployeeHistory.ItemsSource = employee?.FirmHistory.OrderBy(item => item.SortOrder).ToList();
            PopulateDocumentHealth(employee);
        }

        private AdminMirrorEmployeeRecord AttachEmployerDisplayName(AdminMirrorEmployeeRecord employee)
        {
            if (employee.EmployerId.HasValue && _employerNames.TryGetValue(employee.EmployerId.Value, out var employerName))
                employee.EmployerDisplayName = employerName;
            else if (!string.IsNullOrWhiteSpace(employee.ArchivedFromFirm))
                employee.EmployerDisplayName = employee.ArchivedFromFirm;
            else
                employee.EmployerDisplayName = "—";

            employee.VisaTitleDisplay = ResolveVisaTitle(employee);

            return employee;
        }

        private static string ResolveVisaTitle(AdminMirrorEmployeeRecord? employee)
        {
            if (employee == null)
                return "—";

            var workPermitName = employee.WorkPermitName?.Trim() ?? string.Empty;
            if (ContainsAny(workPermitName, "dočasn", "docasn", "ochran"))
                return "Dočasná ochrana";
            if (ContainsAny(workPermitName, "strpění", "strpeni"))
                return "Strpění";

            var visaType = employee.VisaType?.Trim() ?? string.Empty;
            if (ContainsAny(visaType, "dočasn", "docasn", "ochran", "d/do/"))
                return "Dočasná ochrana";
            if (ContainsAny(visaType, "trval", "permanent"))
                return "Trvalý pobyt";
            if (ContainsAny(visaType, "přechodn", "prechodn", "registrac", "osvědčení", "osvedceni"))
                return "Přechodný pobyt";
            if (ContainsAny(visaType, "strpění", "strpeni", "d/vs/", "d/sd/"))
                return "Strpění";

            return string.IsNullOrWhiteSpace(visaType) ? "—" : visaType;
        }

        private static bool ContainsAny(string value, params string[] markers)
        {
            return markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
        }

        private void PopulateSyncHealth(ClientMirrorStateRecord? state)
        {
            TxtLastSync.Text = FormatDate(state?.LastDeltaSyncAt);
            TxtMirrorStatus.Text = BuildStatusText(state);
            TxtFullSync.Text = state?.LastFullSyncAt.HasValue == true ? "Completed" : "Pending";
            TxtSchemaVersion.Text = string.IsNullOrWhiteSpace(state?.SchemaVersion) ? "—" : state.SchemaVersion;
            TxtLastError.Text = string.IsNullOrWhiteSpace(state?.LastErrorText) ? "—" : state.LastErrorText;
        }

        private void PopulateDocumentHealth(AdminMirrorEmployeeRecord? employee)
        {
            if (employee == null)
            {
                var neutral = ("—", new SolidColorBrush((Color)ColorConverter.ConvertFromString("#45475A")), new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CDD6F4")));
                SetStatusBadge(BdPassportState, TxtPassportState, neutral);
                SetStatusBadge(BdVisaState, TxtVisaState, neutral);
                SetStatusBadge(BdInsuranceState, TxtInsuranceState, neutral);
                SetStatusBadge(BdWorkPermitState, TxtWorkPermitState, neutral);
                return;
            }

            SetStatusBadge(BdPassportState, TxtPassportState, ResolveDocumentStatus(employee?.PassportExpiry, required: true));
            SetStatusBadge(BdVisaState, TxtVisaState, ResolveDocumentStatus(employee?.VisaExpiry, required: !string.Equals(employee?.EmployeeType, "eu_citizen", StringComparison.OrdinalIgnoreCase)));

            var permitRequired =
                string.Equals(employee?.EmployeeType, "work_permit", StringComparison.OrdinalIgnoreCase);
            SetStatusBadge(BdInsuranceState, TxtInsuranceState, ResolveDocumentStatus(employee?.InsuranceExpiry, required: true));
            SetStatusBadge(BdWorkPermitState, TxtWorkPermitState, ResolveDocumentStatus(employee?.WorkPermitExpiry, required: permitRequired));
        }

        private static (string Label, Brush Background, Brush Foreground) ResolveDocumentStatus(string? expiry, bool required)
        {
            if (string.IsNullOrWhiteSpace(expiry))
            {
                return required
                    ? ("Missing", new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5A2D3D")), new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F38BA8")))
                    : ("Not required", new SolidColorBrush((Color)ColorConverter.ConvertFromString("#45475A")), new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CDD6F4")));
            }

            if (!TryParseDate(expiry, out var parsed))
                return ("Unknown", new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5C4A22")), new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F9E2AF")));

            var daysLeft = (parsed.Date - DateTime.Today).TotalDays;
            if (daysLeft < 0)
                return ("Expired", new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5A2D3D")), new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F38BA8")));

            if (daysLeft <= 30)
                return ("Expiring soon", new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5C4A22")), new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FAB387")));

            return ("OK", new SolidColorBrush((Color)ColorConverter.ConvertFromString("#21493D")), new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A6E3A1")));
        }

        private static void SetStatusBadge(Border border, TextBlock textBlock, (string Label, Brush Background, Brush Foreground) status)
        {
            border.Background = status.Background;
            textBlock.Foreground = status.Foreground;
            textBlock.Text = status.Label;
        }

        private static bool TryParseDate(string value, out DateTime parsed)
        {
            var formats = new[]
            {
                "dd.MM.yyyy",
                "d.M.yyyy",
                "dd.MM.yy",
                "yyyy-MM-dd",
                "yyyy-MM-ddTHH:mm:ss",
                "yyyy-MM-ddTHH:mm:ssZ",
                "yyyy-MM-ddTHH:mm:ss.fffffffZ"
            };

            return DateTime.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out parsed)
                   || DateTime.TryParse(value, CultureInfo.GetCultureInfo("cs-CZ"), DateTimeStyles.AssumeLocal, out parsed)
                   || DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out parsed);
        }

        private static string ResolveGender(string? value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "male" => "Male",
                "female" => "Female",
                _ => string.IsNullOrWhiteSpace(value) ? "—" : value
            };
        }

        private static string ResolveEmployeeType(string? value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "visa" => "Visa",
                "eu_citizen" => "EU citizen",
                "work_permit" => "Visa + work permit",
                _ => string.IsNullOrWhiteSpace(value) ? "—" : value
            };
        }

        private static string ResolveEuDocumentType(string? value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "passport" => "Passport",
                "id_card" => "ID card",
                _ => string.IsNullOrWhiteSpace(value) ? "—" : value
            };
        }

        private static string FormatMoney(decimal? value)
        {
            if (!value.HasValue)
                return "—";

            return value.Value == 0 ? "0" : value.Value.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
