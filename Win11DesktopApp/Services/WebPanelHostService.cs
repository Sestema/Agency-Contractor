using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Win11DesktopApp.Converters;
using Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.Helpers;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    public sealed class WebPanelHostService
    {
        private readonly AppSettingsService _settingsService;
        private readonly CompanyService _companyService;
        private readonly EmployeeService _employeeService;
        private readonly TemplateService _templateService;
        private readonly FinanceService _financeService;
        private readonly AppStatisticsService _appStatisticsService;
        private readonly KeepAwakeService _keepAwakeService;
        private WebApplication? _app;

        public WebPanelHostService(
            AppSettingsService settingsService,
            CompanyService companyService,
            EmployeeService employeeService,
            TemplateService templateService,
            FinanceService financeService,
            AppStatisticsService appStatisticsService,
            KeepAwakeService keepAwakeService)
        {
            _settingsService = settingsService;
            _companyService = companyService;
            _employeeService = employeeService;
            _templateService = templateService;
            _financeService = financeService;
            _appStatisticsService = appStatisticsService;
            _keepAwakeService = keepAwakeService;
        }

        public bool IsRunning => _app != null;
        public string? Url { get; private set; }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (IsRunning)
                return;

            var settings = _settingsService.Settings;
            if (!settings.WebPanelEnabled)
            {
                LoggingService.LogInfo("WebPanelHostService", "Web panel is disabled in settings.");
                return;
            }

            var bindAddress = ResolveLocalBindAddress(settings.WebPanelBindAddress);
            var port = NormalizePort(settings.WebPanelPort);
            Url = $"http://{bindAddress}:{port}";

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ApplicationName = typeof(WebPanelHostService).Assembly.GetName().Name,
                Args = Array.Empty<string>()
            });

            builder.WebHost.UseUrls(Url);
            builder.Services.AddSingleton(_settingsService);
            builder.Services.AddSingleton(sp => new WebAuditService(new FolderService(_settingsService)));
            builder.Services.AddRateLimiter(options =>
            {
                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 120,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0
                        }));
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            });

            var app = builder.Build();
            app.UseMiddleware<HostWhitelistMiddleware>();
            app.UseRateLimiter();
            app.Use(async (context, next) =>
            {
                await next(context).ConfigureAwait(false);
                var audit = context.RequestServices.GetRequiredService<WebAuditService>();
                await audit.LogAsync(
                    "request",
                    context.Connection.RemoteIpAddress?.ToString(),
                    context.Request.Path,
                    context.Response.StatusCode).ConfigureAwait(false);
            });

            MapEndpoints(app);

            await app.StartAsync(cancellationToken).ConfigureAwait(false);
            _app = app;

            if (settings.WebPanelPreventSleep)
                _keepAwakeService.Start();

            LoggingService.LogInfo("WebPanelHostService", $"Started at {Url}.");
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            var app = _app;
            if (app == null)
                return;

            _app = null;
            try
            {
                await app.StopAsync(cancellationToken).ConfigureAwait(false);
                await app.DisposeAsync().ConfigureAwait(false);
                LoggingService.LogInfo("WebPanelHostService", "Stopped.");
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("WebPanelHostService.Stop", ex.Message);
            }
            finally
            {
                _keepAwakeService.Stop();
                Url = null;
            }
        }

        private void MapEndpoints(WebApplication app)
        {
            app.MapGet("/", () => Results.Content(BuildHomePageHtml(), "text/html; charset=utf-8"));

            app.MapGet("/healthz", () => Results.Ok(new
            {
                status = "ok",
                webPanel = "running",
                utc = DateTime.UtcNow
            }));

            app.MapGet("/api/v1/version", () => Results.Ok(new
            {
                version = AppSettingsService.CurrentAppVersion,
                webApi = "v1",
                mode = "read-only"
            }));

            app.MapGet("/api/v1/firms", () =>
            {
                var firms = SnapshotCompanies()
                    .Select(company => new
                    {
                        id = company.Id,
                        name = company.Name,
                        ico = company.ICO,
                        legalAddress = company.LegalAddress,
                        agencyName = company.Agency?.Name ?? string.Empty,
                        hiddenFromYear = company.HiddenFromYear,
                        hiddenFromMonth = company.HiddenFromMonth,
                        isVisibleNow = _companyService.IsCompanyVisible(company),
                        weeklyWorkHours = company.WeeklyWorkHours,
                        dailyWorkHours = company.DailyWorkHours,
                        shiftCount = company.ShiftCount
                    })
                    .ToList();

                return Results.Ok(firms);
            });

            app.MapGet("/api/v1/employees", () =>
            {
                var employees = SnapshotCompanies()
                    .SelectMany(company => _employeeService.GetEmployeesForFirm(company.Name))
                    .Select(MapEmployee)
                    .ToList();

                return Results.Ok(employees);
            });

            app.MapGet("/api/v1/report/employees", () =>
            {
                var companies = SnapshotCompanies();
                var companyByName = companies.ToDictionary(company => company.Name, StringComparer.OrdinalIgnoreCase);
                var rows = new List<object>();
                var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var company in companies)
                {
                    foreach (var summary in _employeeService.GetEmployeesForFirm(company.Name))
                    {
                        var data = LoadEmployeeDataForReport(summary.EmployeeFolder);
                        var rowKey = BuildReportRowKey(summary.UniqueId, company.Name, summary.StartDate, summary.EndDate, summary.EmployeeFolder);
                        if (added.Add(rowKey))
                            rows.Add(MapEmployeeReport(summary, data, company.Agency?.Name ?? string.Empty, false, summary.StartDate, summary.EndDate));
                    }
                }

                foreach (var archived in _employeeService.GetArchivedEmployees()
                    .Concat(_employeeService.GetActiveEmployeeFirmHistory()))
                {
                    if (string.IsNullOrWhiteSpace(archived.FirmName))
                        continue;

                    companyByName.TryGetValue(archived.FirmName, out var company);
                    var data = LoadEmployeeDataForReport(archived.EmployeeFolder);
                    var rowKey = BuildReportRowKey(archived.UniqueId, archived.FirmName, archived.StartDate, archived.EndDate, archived.EmployeeFolder);
                    if (added.Add(rowKey))
                        rows.Add(MapEmployeeReport(archived, data, company?.Agency?.Name ?? string.Empty));
                }

                return Results.Ok(rows);
            });

            app.MapGet("/api/v1/dashboard", () =>
            {
                try
                {
                    return Results.Ok(BuildDashboardModel());
                }
                catch (Exception ex)
                {
                    LoggingService.LogError("WebPanelHostService.BuildDashboardModel", ex);
                    return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status503ServiceUnavailable);
                }
            });

            app.MapGet("/api/v1/employees/{id}", (string id) =>
            {
                var employee = FindEmployeeSummaryById(id);
                if (employee == null || string.IsNullOrWhiteSpace(employee.EmployeeFolder))
                    return Results.NotFound();

                var data = _employeeService.LoadEmployeeData(employee.EmployeeFolder);
                if (data == null)
                    return Results.NotFound();

                return Results.Ok(MapEmployeeProfile(employee, data));
            });

            app.MapGet("/api/v1/employees/{id}/photo", (string id) =>
            {
                var employee = FindEmployeeSummaryById(id);

                if (employee == null || string.IsNullOrWhiteSpace(employee.PhotoPath) || !File.Exists(employee.PhotoPath))
                    return Results.NotFound();

                var extension = Path.GetExtension(employee.PhotoPath).ToLowerInvariant();
                var contentType = extension switch
                {
                    ".png" => "image/png",
                    ".webp" => "image/webp",
                    ".gif" => "image/gif",
                    _ => "image/jpeg"
                };

                return Results.File(employee.PhotoPath, contentType);
            });

            app.MapGet("/api/v1/employees/{id}/documents/{kind}", (string id, string kind) =>
            {
                var employee = FindEmployeeSummaryById(id);
                if (employee == null || string.IsNullOrWhiteSpace(employee.EmployeeFolder))
                    return Results.NotFound();

                var data = _employeeService.LoadEmployeeData(employee.EmployeeFolder);
                if (data == null)
                    return Results.NotFound();

                var fileName = ResolveEmployeeDocumentFileName(data, kind);
                if (string.IsNullOrWhiteSpace(fileName))
                    return Results.NotFound();

                var fullPath = Path.GetFullPath(Path.Combine(employee.EmployeeFolder, fileName));
                var rootPath = Path.GetFullPath(employee.EmployeeFolder);
                var relativePath = Path.GetRelativePath(rootPath, fullPath);
                if (relativePath.StartsWith("..", StringComparison.Ordinal)
                    || Path.IsPathRooted(relativePath)
                    || !File.Exists(fullPath))
                    return Results.NotFound();

                return Results.File(fullPath, ResolveDocumentContentType(fullPath));
            });

            app.MapGet("/api/v1/finance/months", () =>
            {
                var now = DateTime.Now;
                var months = _financeService.GetAvailableSalaryMonths()
                    .Concat(new[] { (year: now.Year, month: now.Month) })
                    .Distinct()
                    .OrderByDescending(item => item.year)
                    .ThenByDescending(item => item.month)
                    .ToList();
                return Results.Ok(months.Select(m => new { year = m.year, month = m.month }).ToList());
            });

            app.MapGet("/api/v1/finance/screen", (HttpRequest request) =>
            {
                if (!int.TryParse(request.Query["year"], out var year)
                    || !int.TryParse(request.Query["month"], out var month))
                {
                    var now = DateTime.Now;
                    year = now.Year;
                    month = now.Month;
                }

                if (month < 1 || month > 12)
                    return Results.BadRequest(new { error = "month must be between 1 and 12" });

                var firm = request.Query["firm"].ToString();
                var search = request.Query["search"].ToString();

                try
                {
                    return Results.Ok(BuildFinanceScreenModel(year, month, firm, search));
                }
                catch (Exception ex)
                {
                    LoggingService.LogError("WebPanelHostService.BuildFinanceScreen", ex);
                    return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status503ServiceUnavailable);
                }
            });

            app.MapGet("/api/v1/finance/payments", (HttpRequest request) =>
            {
                if (!int.TryParse(request.Query["year"], out var year)
                    || !int.TryParse(request.Query["month"], out var month))
                {
                    return Results.BadRequest(new { error = "year and month are required" });
                }

                var firm = request.Query["firm"].ToString();
                var result = _financeService.TryLoadAllFirmPayments(year, month);

                if (!result.success)
                    return Results.Json(new { error = result.errorMessage ?? "failed to load payments" }, statusCode: StatusCodes.Status503ServiceUnavailable);

                var entries = result.entries;
                var expenses = result.expenses;

                if (!string.IsNullOrWhiteSpace(firm))
                {
                    entries = entries
                        .Where(entry => string.Equals(entry.FirmName, firm, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    expenses = expenses
                        .Where(expense => string.Equals(expense.FirmName, firm, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }

                entries = entries
                    .OrderBy(entry => entry.FirmName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                expenses = expenses
                    .OrderBy(expense => expense.FirmName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(expense => expense.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return Results.Ok(new
                {
                    year,
                    month,
                    firm,
                    entries = entries.Select(MapSalaryEntryWeb).ToList(),
                    expenses = expenses.Select(MapFirmExpenseWeb).ToList()
                });
            });
        }

        private object BuildDashboardModel()
        {
            var companies = SnapshotCompanies()
                .Where(company => _companyService.IsCompanyVisible(company))
                .ToList();
            var visibleFirmNames = new HashSet<string>(companies.Select(company => company.Name), StringComparer.OrdinalIgnoreCase);
            var now = DateTime.Today;
            var activeEmployees = new List<EmployeeSummary>();
            var expiringDocs = new List<object>();
            var companyStats = new List<object>();
            var addedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var addedFallbacks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var allTimeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var allTimeFallbacks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var monthlyAdded = new List<object>();
            var monthlyArchived = new List<object>();
            var totalProblems = 0;
            var expiredCount = 0;
            var totalTemplates = 0;

            foreach (var company in companies)
            {
                var employees = _employeeService.GetEmployeesForFirm(company.Name) ?? new List<EmployeeSummary>();
                activeEmployees.AddRange(employees);
                var companyProblems = 0;
                var templateCount = 0;

                try
                {
                    templateCount = (_templateService.GetTemplates(company.Name) ?? new List<TemplateEntry>()).Count;
                }
                catch (Exception ex)
                {
                    LoggingService.LogWarning("WebPanelHostService.DashboardTemplates", ex.Message);
                }

                foreach (var employee in employees)
                {
                    AddDashboardEmployeeIdentity(allTimeIds, allTimeFallbacks, employee.UniqueId, company.Name, employee.FullName, employee.StartDate);

                    if (IsDashboardDateInCurrentMonth(employee.StartDate, now)
                        && AddDashboardEmployeeIdentity(addedIds, addedFallbacks, employee.UniqueId, company.Name, employee.FullName, employee.StartDate))
                    {
                        monthlyAdded.Add(MapDashboardMovement(employee.FullName, company.Name, employee.StartDate, employee.UniqueId, "Додано", "#22c55e"));
                    }

                    CheckDashboardExpiry(employee.PassportExpiry, employee.FullName, "Паспорт", company.Name, employee.UniqueId, expiringDocs, ref companyProblems, ref expiredCount);
                    if (!string.Equals(employee.EmployeeType, "eu_citizen", StringComparison.OrdinalIgnoreCase))
                        CheckDashboardExpiry(employee.VisaExpiry, employee.FullName, "Віза", company.Name, employee.UniqueId, expiringDocs, ref companyProblems, ref expiredCount);
                    CheckDashboardExpiry(employee.InsuranceExpiry, employee.FullName, "Страховка", company.Name, employee.UniqueId, expiringDocs, ref companyProblems, ref expiredCount);
                    if (string.Equals(employee.EmployeeType, "work_permit", StringComparison.OrdinalIgnoreCase))
                        CheckDashboardExpiry(employee.WorkPermitExpiry, employee.FullName, "Дозвіл на роботу", company.Name, employee.UniqueId, expiringDocs, ref companyProblems, ref expiredCount);
                }

                totalProblems += companyProblems;
                totalTemplates += templateCount;
                companyStats.Add(new
                {
                    companyName = company.Name,
                    employeeCount = employees.Count,
                    problemCount = companyProblems,
                    templateCount
                });
            }

            try
            {
                foreach (var archived in _employeeService.GetArchivedEmployees())
                {
                    if (!string.IsNullOrWhiteSpace(archived.FirmName) && visibleFirmNames.Count > 0 && !visibleFirmNames.Contains(archived.FirmName))
                        continue;

                    AddDashboardEmployeeIdentity(allTimeIds, allTimeFallbacks, archived.UniqueId, archived.FirmName, archived.FullName, archived.StartDate);

                    if (IsDashboardDateInCurrentMonth(archived.StartDate, now)
                        && AddDashboardEmployeeIdentity(addedIds, addedFallbacks, archived.UniqueId, archived.FirmName, archived.FullName, archived.StartDate))
                    {
                        monthlyAdded.Add(MapDashboardMovement(archived.FullName, archived.FirmName, archived.StartDate, archived.UniqueId, "Додано", "#22c55e"));
                    }

                    if (IsDashboardDateInCurrentMonth(archived.EndDate, now))
                        monthlyArchived.Add(MapDashboardMovement(archived.FullName, archived.FirmName, archived.EndDate, archived.UniqueId, "Архів", "#ef4444"));
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("WebPanelHostService.DashboardArchived", ex.Message);
            }

            var salaryMonths = BuildDashboardSalaryMonths(out var salaryTotalText);
            var allTimeEmployees = allTimeIds.Count + allTimeFallbacks.Count;
            var generatedDocuments = 0;
            var programMinutes = 0;
            try
            {
                var statistics = _appStatisticsService.GetSnapshot();
                allTimeEmployees = Math.Max(allTimeEmployees, statistics.TotalEmployeesCreated);
                generatedDocuments = statistics.GeneratedDocumentsCount;
                programMinutes = statistics.TotalProgramRunMinutes;
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("WebPanelHostService.DashboardEfficiency", ex.Message);
            }

            var savedMinutes = CalculateDashboardSavedMinutes(allTimeEmployees, generatedDocuments);
            var archivedCount = monthlyArchived.Count;
            var movementMax = Math.Max(1, Math.Max(addedIds.Count + addedFallbacks.Count, archivedCount));

            return new
            {
                totals = new
                {
                    employees = activeEmployees.Count,
                    companies = companies.Count,
                    problems = totalProblems,
                    templates = totalTemplates,
                    monthlyAdded = addedIds.Count + addedFallbacks.Count,
                    monthlyArchived = archivedCount,
                    monthlyMovementText = $"+{addedIds.Count + addedFallbacks.Count} / -{archivedCount}",
                    movementMax,
                    problemTrend = expiredCount > 0 ? $"{expiredCount} прострочено" : "Все добре",
                    totalEmployeesAllTime = allTimeEmployees,
                    generatedDocuments,
                    programMinutes,
                    savedMinutes,
                    programTimeText = FormatDashboardDuration(programMinutes),
                    savedTimeText = FormatDashboardDuration(savedMinutes),
                    efficiencyMaxMinutes = Math.Max(1, Math.Max(programMinutes, savedMinutes))
                },
                monthlyAdded,
                monthlyArchived,
                salaryTotalText,
                salaryMonths,
                expiringDocs = expiringDocs
                    .OrderBy(item => GetDashboardSeverityOrder((string)(item.GetType().GetProperty("severity")?.GetValue(item) ?? string.Empty)))
                    .Take(15),
                companyStats
            };
        }

        private static int GetDashboardSeverityOrder(string severity)
            => severity == "Expired" ? 0 : severity == "Critical" ? 1 : 2;

        private List<object> BuildDashboardSalaryMonths(out string salaryTotalText)
        {
            var result = new List<object>();
            salaryTotalText = string.Empty;
            decimal grandGross = 0;

            try
            {
                foreach (var (year, month) in _financeService.GetAvailableSalaryMonths().OrderByDescending(item => item.year).ThenByDescending(item => item.month))
                {
                    var (entries, expenses) = _financeService.LoadAllFirmPayments(year, month);
                    if (entries.Count == 0)
                        continue;

                    decimal totalGross = 0;
                    decimal totalNet = 0;
                    decimal totalPaid = 0;
                    var paidEntries = 0;

                    foreach (var entry in entries)
                    {
                        var net = entry.SavedNetSalary > 0 ? entry.SavedNetSalary : entry.GrossSalary - entry.Advance;
                        totalGross += entry.GrossSalary;
                        totalNet += net;
                        if (entry.IsPaid)
                        {
                            paidEntries++;
                            totalPaid += net;
                        }
                    }

                    var totalExpenses = expenses.Sum(expense => expense.Amount);
                    var isFullyPaid = paidEntries > 0 && paidEntries == entries.Count;
                    var statusColor = isFullyPaid ? "#22c55e" : paidEntries > 0 ? "#f59e0b" : "#ef4444";
                    var paidRatio = totalNet > 0 ? Math.Min(1m, totalPaid / totalNet) : 0m;
                    grandGross += totalGross;

                    result.Add(new
                    {
                        monthKey = $"{year:D4}-{month:D2}",
                        monthLabel = FormatDashboardMonthLabel(year, month),
                        totalGross,
                        totalNet,
                        totalPaid,
                        totalExpenses,
                        grandTotal = totalNet + totalExpenses,
                        totalEntries = entries.Count,
                        paidEntries,
                        paidRatio,
                        statusColor,
                        statusIcon = isFullyPaid ? "✓" : paidEntries > 0 ? "!" : "×",
                        countText = $"{paidEntries}/{entries.Count} працівників"
                    });
                }

                salaryTotalText = grandGross > 0 ? $"Загально: {grandGross:N0} CZK" : string.Empty;
            }
            catch (Exception ex)
            {
                LoggingService.LogError("WebPanelHostService.DashboardSalary", ex);
            }

            return result;
        }

        private static void CheckDashboardExpiry(string dateStr, string empName, string docType, string companyName, string uniqueId, List<object> list, ref int problemCount, ref int expiredCount)
        {
            var severity = DateParsingHelper.GetSeverity(dateStr);
            if (severity is not ("Expired" or "Critical" or "Warning"))
                return;

            problemCount++;
            var days = DateParsingHelper.GetDaysRemaining(dateStr);
            string label;
            string color;
            if (severity == "Expired")
            {
                expiredCount++;
                label = "Прострочено";
                color = "#ef4444";
            }
            else if (severity == "Critical")
            {
                label = $"{days} дн.";
                color = "#f97316";
            }
            else
            {
                label = $"{days} дн.";
                color = "#f59e0b";
            }

            list.Add(new
            {
                title = empName,
                subtitle = docType,
                companyName,
                uniqueId,
                severity,
                severityLabel = label,
                severityColor = color,
                daysLeft = days
            });
        }

        private static bool IsDashboardDateInCurrentMonth(string dateText, DateTime now)
        {
            var date = DateParsingHelper.TryParseDate(dateText);
            return date != null && date.Value.Year == now.Year && date.Value.Month == now.Month;
        }

        private static bool AddDashboardEmployeeIdentity(HashSet<string> ids, HashSet<string> fallbacks, string uniqueId, string firmName, string fullName, string startDate)
        {
            if (!string.IsNullOrWhiteSpace(uniqueId))
                return ids.Add(uniqueId);
            return fallbacks.Add($"{firmName}|{fullName}|{startDate}");
        }

        private static object MapDashboardMovement(string fullName, string firmName, string dateText, string uniqueId, string statusText, string statusColor)
            => new
            {
                fullName = fullName ?? string.Empty,
                firmName = firmName ?? string.Empty,
                dateText = FormatDashboardDate(dateText),
                uniqueId = uniqueId ?? string.Empty,
                statusText,
                statusColor
            };

        private static string FormatDashboardDate(string dateText)
        {
            var parsed = DateParsingHelper.TryParseDate(dateText);
            return parsed?.ToString("dd.MM.yyyy") ?? (dateText ?? string.Empty);
        }

        private static int CalculateDashboardSavedMinutes(int employeesAllTime, int generatedDocuments)
            => employeesAllTime * 15 + generatedDocuments * 8;

        private static string FormatDashboardDuration(int totalMinutes)
        {
            if (totalMinutes <= 0)
                return "0 хв";
            var hours = totalMinutes / 60;
            var minutes = totalMinutes % 60;
            if (hours <= 0)
                return $"{minutes} хв";
            return minutes > 0 ? $"{hours} год {minutes} хв" : $"{hours} год";
        }

        private static string FormatDashboardMonthLabel(int year, int month)
        {
            try
            {
                var dt = new DateTime(year, month, 1);
                var culture = System.Threading.Thread.CurrentThread.CurrentUICulture;
                var name = dt.ToString("MMMM", culture);
                return char.ToUpper(name[0]) + name[1..] + " " + year;
            }
            catch
            {
                return $"{month:D2}.{year}";
            }
        }

        private EmployeeSummary? FindEmployeeSummaryById(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return null;

            var active = SnapshotCompanies()
                .SelectMany(company => _employeeService.GetEmployeesForFirm(company.Name))
                .FirstOrDefault(item => string.Equals(item.UniqueId, id, StringComparison.OrdinalIgnoreCase));
            if (active != null)
                return active;

            var archived = _employeeService.GetArchivedEmployees()
                .FirstOrDefault(item => string.Equals(item.UniqueId, id, StringComparison.OrdinalIgnoreCase));
            if (archived == null)
                return null;

            return new EmployeeSummary
            {
                UniqueId = archived.UniqueId,
                FullName = archived.FullName,
                FirmName = archived.FirmName,
                PositionTitle = archived.PositionTitle,
                StartDate = archived.StartDate,
                EndDate = archived.EndDate,
                EmployeeFolder = archived.EmployeeFolder,
                PhotoPath = archived.PhotoPath,
                HasPhoto = archived.HasPhoto,
                Status = "Archived"
            };
        }

        private List<EmployerCompany> SnapshotCompanies()
        {
            if (Application.Current?.Dispatcher?.CheckAccess() == true)
                return _companyService.Companies.ToList();

            return Application.Current?.Dispatcher?.Invoke(() => _companyService.Companies.ToList())
                ?? new List<EmployerCompany>();
        }

        private static object MapEmployee(EmployeeSummary employee)
        {
            return new
            {
                id = employee.UniqueId,
                fullName = employee.FullName,
                firmName = employee.FirmName,
                positionTitle = employee.PositionTitle,
                startDate = employee.StartDate,
                endDate = employee.EndDate,
                contractType = employee.ContractType,
                status = employee.Status,
                employeeType = employee.EmployeeType,
                hasPhoto = employee.HasPhoto,
                hasPassport = employee.HasPassport,
                hasVisa = employee.HasVisa,
                hasInsurance = employee.HasInsurance,
                photoUrl = employee.HasPhoto && !string.IsNullOrWhiteSpace(employee.UniqueId)
                    ? $"/api/v1/employees/{Uri.EscapeDataString(employee.UniqueId)}/photo"
                    : string.Empty,
                passportNumber = employee.PassportNumber,
                visaNumber = employee.VisaNumber,
                insuranceNumber = employee.InsuranceNumber,
                passportExpiry = employee.PassportExpiry,
                visaExpiry = employee.VisaExpiry,
                insuranceExpiry = employee.InsuranceExpiry,
                workPermitName = employee.WorkPermitName,
                workPermitExpiry = employee.WorkPermitExpiry,
                phone = employee.Phone,
                email = employee.Email
            };
        }

        private static object MapSalaryEntryWeb(SalaryEntry entry)
        {
            var gross = Math.Round(entry.HoursWorked * entry.HourlyRate, 2, MidpointRounding.AwayFromZero);
            return new
            {
                employeeId = entry.EmployeeId,
                fullName = entry.FullName,
                firmName = entry.FirmName,
                hoursWorked = entry.HoursWorked,
                hourlyRate = entry.HourlyRate,
                advance = entry.Advance,
                grossSalary = entry.GrossSalary,
                netSalary = entry.NetSalary,
                savedNetSalary = entry.SavedNetSalary,
                status = entry.Status,
                isPaid = entry.IsPaid,
                isFinished = entry.IsFinished,
                note = entry.Note,
                colorTag = entry.ColorTag,
                customValues = entry.CustomValues
                    .Where(item => !string.IsNullOrWhiteSpace(item.Key))
                    .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase)
            };
        }

        private static object MapFirmExpenseWeb(FirmExpense expense)
        {
            return new
            {
                id = expense.Id,
                firmName = expense.FirmName,
                name = expense.Name,
                amount = expense.Amount
            };
        }

        private object BuildFinanceScreenModel(int year, int month, string? firmFilter, string? searchText)
        {
            var allEntries = BuildFinanceEntriesForWeb(year, month);
            var allFirmNames = allEntries
                .Select(entry => entry.FirmName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var fieldList = GetFinanceFieldsSafe(allFirmNames);

            foreach (var entry in allEntries)
            {
                entry.FieldDefinitions = fieldList;
                entry.RecalcNet();
            }

            try
            {
                ApplyAdvanceSumsToFinanceEntries(allEntries, year, month);
                foreach (var entry in allEntries)
                    entry.RecalcNet();
            }
            catch (Exception ex)
            {
                // Keep the web panel usable even if advances/debt data has a transient issue.
                LoggingService.LogError("WebPanelHostService.ApplyAdvanceSumsToFinanceEntries", ex);
            }

            var hasFirmFilter = !string.IsNullOrWhiteSpace(firmFilter);
            var hasSearch = !string.IsNullOrWhiteSpace(searchText);
            var visibleEntries = allEntries.AsEnumerable();
            if (hasFirmFilter)
                visibleEntries = visibleEntries.Where(entry => string.Equals(entry.FirmName, firmFilter, StringComparison.OrdinalIgnoreCase));
            if (hasSearch)
            {
                var search = searchText!.Trim();
                visibleEntries = visibleEntries.Where(entry =>
                    (entry.FullName?.Contains(search, StringComparison.OrdinalIgnoreCase) == true)
                    || (entry.FirmName?.Contains(search, StringComparison.OrdinalIgnoreCase) == true));
            }

            var visible = visibleEntries.ToList();
            var expenses = GetFinanceExpensesSafe(year, month, hasFirmFilter ? firmFilter : null);
            var totalExpenses = expenses.Sum(expense => expense.Amount);
            var totalNet = visible.Sum(entry => entry.NetSalary);
            var totalGross = visible.Sum(entry => entry.GrossSalary);
            var totalHours = visible.Sum(entry => entry.HoursWorked);
            var paidCount = visible.Count(entry => entry.IsPaid);

            var summarySource = hasSearch ? visible : allEntries;
            var firmSummaries = summarySource
                .GroupBy(entry => entry.FirmName)
                .OrderByDescending(group => group.Sum(entry => entry.GrossSalary))
                .Select(group => new
                {
                    firmName = group.Key,
                    totalGross = group.Sum(entry => entry.GrossSalary),
                    totalNet = group.Sum(entry => entry.NetSalary),
                    totalHours = group.Sum(entry => entry.HoursWorked),
                    employeeCount = group.Count(),
                    paidCount = group.Count(entry => entry.IsPaid),
                    isSelected = hasFirmFilter && string.Equals(group.Key, firmFilter, StringComparison.OrdinalIgnoreCase)
                })
                .ToList();

            return new
            {
                year,
                month,
                monthKey = $"{year:D4}-{month:D2}",
                selectedFirm = firmFilter ?? string.Empty,
                search = searchText ?? string.Empty,
                nextMonthExists = _financeService.MonthDataExists(new DateTime(year, month, 1).AddMonths(1).Year, new DateTime(year, month, 1).AddMonths(1).Month),
                columns = fieldList.Select(field => new
                {
                    id = field.Id,
                    name = field.Name,
                    operation = field.Operation.ToString().ToLowerInvariant(),
                    order = field.Order
                }).ToList(),
                totals = new
                {
                    totalEmployees = visible.Count,
                    totalHours,
                    totalGross,
                    totalNet,
                    totalExpenses,
                    grandTotal = totalNet + totalExpenses,
                    paidCount,
                    paidDisplay = $"{paidCount}/{visible.Count}",
                    allPaid = visible.Count > 0 && paidCount == visible.Count,
                    statPaid = visible.Where(entry => entry.IsPaid).Sum(entry => entry.NetSalary),
                    statRemaining = visible.Where(entry => !entry.IsPaid).Sum(entry => entry.NetSalary),
                    statAdvances = visible.Sum(entry => entry.Advance),
                    statCustomAdd = SumCustomValuesByOperation(visible, FieldOperation.Add),
                    statCustomSub = SumCustomValuesByOperation(visible, FieldOperation.Subtract)
                },
                firms = allFirmNames,
                firmSummaries,
                entries = visible
                    .OrderBy(entry => entry.FirmName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase)
                    .Select(entry => MapSalaryEntryWeb(entry))
                    .ToList(),
                expenses = expenses
                    .OrderBy(expense => expense.FirmName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(expense => expense.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(MapFirmExpenseWeb)
                    .ToList()
            };
        }

        private List<CustomSalaryField> GetFinanceFieldsSafe(IReadOnlyList<string> allFirmNames)
        {
            try
            {
                var fields = _financeService.GetActiveFields(allFirmNames);
                return fields.Count > 0 ? fields : _financeService.GetCustomFields();
            }
            catch (Exception ex)
            {
                LoggingService.LogError("WebPanelHostService.GetFinanceFieldsSafe", ex);
                return new List<CustomSalaryField>();
            }
        }

        private List<FirmExpense> GetFinanceExpensesSafe(int year, int month, string? firmName)
        {
            try
            {
                return string.IsNullOrWhiteSpace(firmName)
                    ? _financeService.GetFirmExpenses(year, month)
                    : _financeService.GetFirmExpenses(year, month, firmName);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("WebPanelHostService.GetFinanceExpensesSafe", ex);
                return new List<FirmExpense>();
            }
        }

        private List<SalaryEntry> BuildFinanceEntriesForWeb(int year, int month)
        {
            var monthEnd = new DateTime(year, month, 1).AddMonths(1).AddDays(-1);
            var companies = SnapshotCompanies()
                .Where(company => _companyService.IsCompanyVisibleForPeriod(company, year, month))
                .ToList();
            var allHistory = _employeeService.GetArchivedEmployees()
                .Concat(_employeeService.GetActiveEmployeeFirmHistory())
                .ToList();
            var employmentByKey = new Dictionary<string, List<(string StartDate, string EndDate)>>(StringComparer.OrdinalIgnoreCase);
            var activeFoldersByFirm = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var company in companies)
            {
                var activeFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var employee in _employeeService.GetEmployeesForFirm(company.Name))
                {
                    var key = BuildFinanceEmployeeFirmKey(employee.UniqueId, employee.EmployeeFolder, company.Name);
                    AddFinanceEmploymentPeriod(employmentByKey, key, employee.StartDate, employee.EndDate);
                    if (employee.Status == "Active")
                        activeFolders.Add(NormalizeFinanceEmployeePath(employee.EmployeeFolder));
                }
                activeFoldersByFirm[company.Name] = activeFolders;
            }

            foreach (var archived in allHistory)
            {
                if (string.IsNullOrWhiteSpace(archived.FirmName))
                    continue;

                var key = BuildFinanceEmployeeFirmKey(archived.UniqueId, archived.EmployeeFolder, archived.FirmName);
                AddFinanceEmploymentPeriod(employmentByKey, key, archived.StartDate, archived.EndDate);
            }

            var prev = new DateTime(year, month, 1).AddMonths(-1);
            var prevMonthResult = _financeService.TryLoadAllFirmPayments(prev.Year, prev.Month);
            var prevEntries = prevMonthResult.success ? prevMonthResult.entries : new List<SalaryEntry>();
            var prevNotes = prevEntries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Note))
                .GroupBy(entry => BuildFinanceEmployeeFirmKey(entry.EmployeeId, entry.EmployeeFolder, entry.FirmName), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last().Note, StringComparer.OrdinalIgnoreCase);

            var currentMonthResult = _financeService.TryLoadAllFirmPayments(year, month);
            var savedEntries = currentMonthResult.success ? currentMonthResult.entries : new List<SalaryEntry>();
            var entries = new List<SalaryEntry>();
            var existingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in savedEntries)
            {
                var key = BuildFinanceEmployeeFirmKey(entry.EmployeeId, entry.EmployeeFolder, entry.FirmName);
                if (existingKeys.Contains(key))
                    continue;

                if (employmentByKey.TryGetValue(key, out var periods)
                    && !periods.Any(period => FinanceWorkedInMonth(period.StartDate, period.EndDate, year, month)))
                {
                    continue;
                }

                entry.EmployeeFolder = _financeService.ResolveEmployeeFolder(entry.EmployeeFolder, entry.EmployeeId);
                entry.IsFinished = !activeFoldersByFirm.TryGetValue(entry.FirmName, out var activeFolders)
                    || !activeFolders.Contains(NormalizeFinanceEmployeePath(entry.EmployeeFolder));
                entries.Add(entry);
                existingKeys.Add(key);
            }

            foreach (var company in companies)
            {
                foreach (var employee in _employeeService.GetEmployeesForFirm(company.Name))
                {
                    if (employee.Status != "Active" || !FinanceWorkedInMonth(employee.StartDate, employee.EndDate, year, month))
                        continue;

                    var key = BuildFinanceEmployeeFirmKey(employee.UniqueId, employee.EmployeeFolder, company.Name);
                    if (existingKeys.Contains(key))
                        continue;

                    prevNotes.TryGetValue(key, out var inheritedNote);
                    var entry = new SalaryEntry
                    {
                        EmployeeId = employee.UniqueId,
                        EmployeeFolder = employee.EmployeeFolder,
                        FullName = employee.FullName,
                        FirmName = company.Name,
                        HourlyRate = TryGetFinanceHourlyRate(prevEntries, employee.UniqueId, employee.EmployeeFolder, company.Name, out var previousRate)
                            ? previousRate
                            : GetFinanceDefaultRate(employee.EmployeeFolder),
                        HoursWorked = 0,
                        Note = inheritedNote ?? string.Empty,
                        IsFinished = false
                    };
                    entries.Add(entry);
                    existingKeys.Add(key);
                }
            }

            foreach (var archived in allHistory)
            {
                if (string.IsNullOrWhiteSpace(archived.FirmName)
                    || !FinanceWorkedInMonth(archived.StartDate, archived.EndDate, year, month))
                {
                    continue;
                }

                var key = BuildFinanceEmployeeFirmKey(archived.UniqueId, archived.EmployeeFolder, archived.FirmName);
                if (existingKeys.Contains(key))
                    continue;

                prevNotes.TryGetValue(key, out var inheritedNote);
                var historyRecord = TryGetFinanceSalaryHistoryRecord(archived.EmployeeFolder, archived.UniqueId, archived.FirmName, year, month);
                var entry = historyRecord != null
                    ? new SalaryEntry
                    {
                        EmployeeId = archived.UniqueId,
                        EmployeeFolder = archived.EmployeeFolder,
                        FullName = string.IsNullOrWhiteSpace(historyRecord.FullName) ? archived.FullName : historyRecord.FullName,
                        FirmName = archived.FirmName,
                        HoursWorked = historyRecord.HoursWorked,
                        HourlyRate = historyRecord.HourlyRate,
                        Advance = historyRecord.Advance,
                        SavedNetSalary = historyRecord.NetSalary,
                        Status = "paid",
                        Note = historyRecord.Note ?? string.Empty,
                        CustomValues = new Dictionary<string, decimal>(historyRecord.CustomValues, StringComparer.OrdinalIgnoreCase),
                        IsFinished = true
                    }
                    : new SalaryEntry
                    {
                        EmployeeId = archived.UniqueId,
                        EmployeeFolder = archived.EmployeeFolder,
                        FullName = archived.FullName,
                        FirmName = archived.FirmName,
                        HourlyRate = TryGetFinanceHourlyRate(prevEntries, archived.UniqueId, archived.EmployeeFolder, archived.FirmName, out var previousRate)
                            ? previousRate
                            : GetFinanceDefaultRate(archived.EmployeeFolder),
                        HoursWorked = 0,
                        Note = inheritedNote ?? string.Empty,
                        IsFinished = true
                    };

                entries.Add(entry);
                existingKeys.Add(key);
            }

            return entries;
        }

        private void ApplyAdvanceSumsToFinanceEntries(IReadOnlyList<SalaryEntry> entries, int year, int month)
        {
            var monthKey = $"{year:D4}-{month:D2}";
            var requests = entries
                .Select(entry => (
                    requestKey: BuildFinanceEmployeeFirmKey(entry.EmployeeId, entry.EmployeeFolder, entry.FirmName),
                    employeeId: entry.EmployeeId,
                    employeeFolder: entry.EmployeeFolder,
                    firmName: entry.FirmName))
                .Distinct()
                .ToList();

            var currentAdvancesByRequest = _financeService.GetTotalAdvancesForEmployeeFirms(requests, monthKey);
            var carriedDebtByRequest = _financeService.CalculateCarriedDebtForEntries(requests, year, month);

            foreach (var entry in entries)
            {
                var requestKey = BuildFinanceEmployeeFirmKey(entry.EmployeeId, entry.EmployeeFolder, entry.FirmName);
                currentAdvancesByRequest.TryGetValue(requestKey, out var currentAdvances);
                carriedDebtByRequest.TryGetValue(requestKey, out var carriedDebt);
                entry.Advance = currentAdvances + carriedDebt;
            }
        }

        private static decimal SumCustomValuesByOperation(IEnumerable<SalaryEntry> entries, FieldOperation operation)
        {
            decimal sum = 0;
            foreach (var entry in entries)
            {
                if (entry.FieldDefinitions == null)
                    continue;

                foreach (var field in entry.FieldDefinitions.Where(field => field.Operation == operation))
                {
                    if (entry.CustomValues.TryGetValue(field.Id, out var value) && value != 0)
                        sum += value;
                }
            }
            return sum;
        }

        private SalaryHistoryRecord? TryGetFinanceSalaryHistoryRecord(string employeeFolder, string? employeeId, string firmName, int year, int month)
        {
            try
            {
                var resolvedFolder = _financeService.ResolveEmployeeFolder(employeeFolder, employeeId);
                var history = _financeService.SalaryHistoryService.LoadSalaryHistoryFromResolvedFolder(resolvedFolder, employeeId);
                return history.FirstOrDefault(record =>
                    record.Year == year
                    && record.Month == month
                    && string.Equals(record.FirmName, firmName, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                LoggingService.LogError("WebPanelHostService.TryGetFinanceSalaryHistoryRecord", ex);
                return null;
            }
        }

        private bool TryGetFinanceHourlyRate(IReadOnlyList<SalaryEntry> sourceEntries, string? employeeId, string employeeFolder, string firmName, out decimal hourlyRate)
        {
            for (var i = sourceEntries.Count - 1; i >= 0; i--)
            {
                var entry = sourceEntries[i];
                if (!string.Equals(entry.FirmName, firmName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.IsNullOrWhiteSpace(employeeId)
                    && !string.IsNullOrWhiteSpace(entry.EmployeeId)
                    && string.Equals(entry.EmployeeId, employeeId, StringComparison.OrdinalIgnoreCase))
                {
                    hourlyRate = entry.HourlyRate;
                    return true;
                }

                var existingFolder = NormalizeFinanceEmployeePath(_financeService.ResolveEmployeeFolder(entry.EmployeeFolder, entry.EmployeeId));
                var currentFolder = NormalizeFinanceEmployeePath(_financeService.ResolveEmployeeFolder(employeeFolder, employeeId));
                if (string.Equals(existingFolder, currentFolder, StringComparison.OrdinalIgnoreCase))
                {
                    hourlyRate = entry.HourlyRate;
                    return true;
                }
            }

            hourlyRate = 0;
            return false;
        }

        private decimal GetFinanceDefaultRate(string employeeFolder)
        {
            try
            {
                var data = _employeeService.LoadEmployeeData(employeeFolder);
                if (data != null && data.HourlySalary > 0)
                    return data.HourlySalary;
            }
            catch (Exception ex)
            {
                LoggingService.LogError("WebPanelHostService.GetFinanceDefaultRate", ex);
            }

            return 160;
        }

        private static void AddFinanceEmploymentPeriod(Dictionary<string, List<(string StartDate, string EndDate)>> employmentByKey, string key, string? startDate, string? endDate)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(startDate))
                return;

            if (!employmentByKey.TryGetValue(key, out var periods))
            {
                periods = new List<(string StartDate, string EndDate)>();
                employmentByKey[key] = periods;
            }

            if (!periods.Any(period =>
                    string.Equals(period.StartDate, startDate, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(period.EndDate, endDate ?? string.Empty, StringComparison.OrdinalIgnoreCase)))
            {
                periods.Add((startDate, endDate ?? string.Empty));
            }
        }

        private static bool FinanceWorkedInMonth(string? startDate, string? endDate, int year, int month)
        {
            var monthStart = new DateTime(year, month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            var start = DateParsingHelper.TryParseDate(startDate ?? string.Empty);
            if (start == null || start.Value > monthEnd)
                return false;

            if (string.IsNullOrWhiteSpace(endDate))
                return true;

            var end = DateParsingHelper.TryParseDate(endDate ?? string.Empty);
            return end == null || end.Value >= monthStart;
        }

        private static string BuildFinanceEmployeeFirmKey(string? employeeId, string? employeeFolder, string? firmName)
        {
            var identity = !string.IsNullOrWhiteSpace(employeeId)
                ? employeeId.Trim()
                : NormalizeFinanceEmployeePath(employeeFolder);

            if (string.IsNullOrWhiteSpace(identity))
                identity = Path.GetFileName((employeeFolder ?? string.Empty).TrimEnd('\\', '/'));

            return identity + "|" + (firmName ?? string.Empty);
        }

        private static string NormalizeFinanceEmployeePath(string? path)
            => (path ?? string.Empty).Replace('/', '\\').Trim().TrimEnd('\\');

        private EmployeeData? LoadEmployeeDataForReport(string employeeFolder)
        {
            if (string.IsNullOrWhiteSpace(employeeFolder))
                return null;

            try
            {
                return _employeeService.LoadEmployeeData(employeeFolder);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("WebPanelHostService.LoadEmployeeDataForReport", ex.Message);
                return null;
            }
        }

        private static string BuildReportRowKey(string uniqueId, string firmName, string startDate, string endDate, string employeeFolder)
        {
            if (!string.IsNullOrWhiteSpace(uniqueId))
                return string.Join("|", uniqueId.Trim(), firmName.Trim(), startDate.Trim(), endDate.Trim());

            return string.Join("|", firmName.Trim(), startDate.Trim(), endDate.Trim(), (employeeFolder ?? string.Empty).Trim());
        }

        private static string DocResourceString(string key, string fallback)
        {
            return Application.Current?.Resources[key] as string ?? fallback;
        }

        private static string GetDocTypeDisplay(string? type)
        {
            var normalized = type ?? string.Empty;
            var key = normalized switch
            {
                "visa" => "EmpTypeVisa",
                "eu_citizen" => "EmpTypeEuCitizen",
                "work_permit" => "EmpTypeWorkPermit",
                "passport_only" => "EmpTypePassportOnly",
                _ => null
            };

            return key != null ? DocResourceString(key, normalized) : normalized;
        }

        private static string GetGenderDisplay(string? gender)
        {
            var key = string.Equals(gender, "female", StringComparison.OrdinalIgnoreCase)
                ? "GenderFemale"
                : "GenderMale";
            return DocResourceString(key, gender ?? string.Empty);
        }

        private static object MapEmployeeReport(
            EmployeeSummary summary,
            EmployeeData? data,
            string agencyName,
            bool isArchived,
            string historicalStartDate,
            string historicalEndDate)
        {
            var rawType = data?.EmployeeType ?? summary.EmployeeType;
            var documentType = GetDocTypeDisplay(rawType);
            var workPermitName = data?.WorkPermitName ?? summary.WorkPermitName;
            var employeeTypeDisplay = !string.IsNullOrWhiteSpace(workPermitName)
                ? workPermitName
                : documentType;

            return new
            {
                id = summary.UniqueId,
                fullName = summary.FullName,
                firmName = summary.FirmName,
                status = summary.Status,
                employeeType = employeeTypeDisplay,
                documentType,
                passportNumber = data?.PassportNumber ?? summary.PassportNumber,
                visaNumber = data?.VisaNumber ?? summary.VisaNumber,
                insuranceNumber = data?.InsuranceNumber ?? summary.InsuranceNumber,
                visaAuthority = data?.VisaAuthority ?? string.Empty,
                visaStartDate = data?.VisaStartDate ?? string.Empty,
                passportExpiry = data?.PassportExpiry ?? summary.PassportExpiry,
                visaExpiry = data?.VisaExpiry ?? summary.VisaExpiry,
                insuranceExpiry = data?.InsuranceExpiry ?? summary.InsuranceExpiry,
                startDate = !string.IsNullOrWhiteSpace(historicalStartDate) ? historicalStartDate : (data?.StartDate ?? summary.StartDate),
                endDate = historicalEndDate,
                phone = data?.Phone ?? summary.Phone,
                email = data?.Email ?? summary.Email,
                bankAccountNumber = data == null || data.HasBankAccountData ? (data?.BankAccountNumber ?? summary.BankAccountNumber) : string.Empty,
                bankName = data == null || data.HasBankAccountData ? (data?.BankName ?? summary.BankName) : string.Empty,
                position = data?.PositionTag ?? summary.PositionTitle,
                positionCode = data?.PositionNumber ?? string.Empty,
                workAddress = data?.WorkAddressTag ?? string.Empty,
                addressCz = FormatAddress(data?.AddressLocal),
                addressAbroad = FormatAddress(data?.AddressAbroad),
                birthDate = data?.BirthDate ?? string.Empty,
                citizenship = data?.Citizenship ?? string.Empty,
                birthCity = data?.PassportCity ?? string.Empty,
                birthCountry = data?.PassportCountry ?? string.Empty,
                highestEducation = EducationCatalog.GetFullDisplay(data?.HighestEducationCode),
                gender = GetGenderDisplay(data?.Gender),
                passportIssuedBy = data?.PassportAuthority ?? string.Empty,
                agency = agencyName,
                isArchived = isArchived || data?.IsArchived == true || string.Equals(summary.Status, "Archived", StringComparison.OrdinalIgnoreCase)
            };
        }

        private static object MapEmployeeReport(ArchivedEmployeeSummary summary, EmployeeData? data, string agencyName)
        {
            var rawType = data?.EmployeeType ?? "visa";
            var documentType = data != null ? GetDocTypeDisplay(rawType) : "—";
            var workPermitName = data?.WorkPermitName ?? string.Empty;
            var employeeTypeDisplay = !string.IsNullOrWhiteSpace(workPermitName)
                ? workPermitName
                : documentType;

            return new
            {
                id = summary.UniqueId,
                fullName = summary.FullName,
                firmName = summary.FirmName,
                status = "Archived",
                employeeType = data != null ? employeeTypeDisplay : "—",
                documentType,
                passportNumber = data?.PassportNumber ?? string.Empty,
                visaNumber = data?.VisaNumber ?? string.Empty,
                insuranceNumber = data?.InsuranceNumber ?? string.Empty,
                visaAuthority = data?.VisaAuthority ?? string.Empty,
                visaStartDate = data?.VisaStartDate ?? string.Empty,
                passportExpiry = data?.PassportExpiry ?? string.Empty,
                visaExpiry = data?.VisaExpiry ?? string.Empty,
                insuranceExpiry = data?.InsuranceExpiry ?? string.Empty,
                startDate = !string.IsNullOrWhiteSpace(summary.StartDate) ? summary.StartDate : (data?.StartDate ?? string.Empty),
                endDate = summary.EndDate,
                phone = data?.Phone ?? string.Empty,
                email = data?.Email ?? string.Empty,
                bankAccountNumber = data?.HasBankAccountData == true ? data.BankAccountNumber : string.Empty,
                bankName = data?.HasBankAccountData == true ? data.BankName : string.Empty,
                position = data?.PositionTag ?? summary.PositionTitle,
                positionCode = data?.PositionNumber ?? string.Empty,
                workAddress = data?.WorkAddressTag ?? string.Empty,
                addressCz = FormatAddress(data?.AddressLocal),
                addressAbroad = FormatAddress(data?.AddressAbroad),
                birthDate = data?.BirthDate ?? string.Empty,
                citizenship = data?.Citizenship ?? string.Empty,
                birthCity = data?.PassportCity ?? string.Empty,
                birthCountry = data?.PassportCountry ?? string.Empty,
                highestEducation = EducationCatalog.GetFullDisplay(data?.HighestEducationCode),
                gender = GetGenderDisplay(data?.Gender),
                passportIssuedBy = data?.PassportAuthority ?? string.Empty,
                agency = agencyName,
                isArchived = true
            };
        }

        private object MapEmployeeProfile(EmployeeSummary summary, EmployeeData data)
        {
            var employeeTypeRaw = data.EmployeeType ?? string.Empty;
            var passportPage2 = data.Files?.PassportPage2 ?? string.Empty;
            var visaFile = data.Files?.Visa ?? string.Empty;
            var hasPassportPage2File = !string.IsNullOrWhiteSpace(passportPage2);
            var hasVisaFile = !string.IsNullOrWhiteSpace(visaFile);
            var isEuIdCardEmployee = string.Equals(employeeTypeRaw, "eu_citizen", StringComparison.OrdinalIgnoreCase)
                && string.Equals(data.EuDocumentType ?? string.Empty, "id_card", StringComparison.OrdinalIgnoreCase);
            var isPassportOnly = string.Equals(employeeTypeRaw, "passport_only", StringComparison.OrdinalIgnoreCase);
            var isWorkPermitType = string.Equals(employeeTypeRaw, "work_permit", StringComparison.OrdinalIgnoreCase);
            var usesPassportPage2Secondary = hasPassportPage2File && !(hasVisaFile && string.Equals(employeeTypeRaw, "visa", StringComparison.OrdinalIgnoreCase));
            var hasSecondaryDocuments = usesPassportPage2Secondary || hasVisaFile
                || (!isPassportOnly && (
                    string.Equals(employeeTypeRaw, "visa", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(employeeTypeRaw, "work_permit", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(employeeTypeRaw, "eu_citizen", StringComparison.OrdinalIgnoreCase)));

            string secondarySectionTitle =
                usesPassportPage2Secondary
                    ? (isEuIdCardEmployee ? "Дані ID-карти (стор. 2)" : "Дані з паспорту (стор. 2)")
                    : "Віза";

            var statusKey = StatusHelper.Normalize(summary.Status ?? data.Status);
            var history = _employeeService.LoadHistory(summary.EmployeeFolder)
                .OrderByDescending(item => item.Timestamp)
                .Take(80)
                .Select(item => new
                {
                    id = item.Id,
                    timestamp = item.Timestamp.ToString("dd.MM.yyyy HH:mm"),
                    eventType = item.EventType,
                    action = item.Action,
                    field = item.Field,
                    oldValue = item.OldValue,
                    newValue = item.NewValue,
                    description = item.Description,
                    actorName = item.ActorName,
                    eventIcon = item.EventIcon,
                    eventColor = item.EventColor
                })
                .ToList();

            var salaryHistory = _financeService.LoadSalaryHistory(summary.EmployeeFolder)
                .OrderByDescending(item => item.Year)
                .ThenByDescending(item => item.Month)
                .Take(60)
                .Select(item => new
                {
                    monthDisplay = item.MonthDisplay,
                    paidAt = item.PaidAt.ToString("dd.MM.yyyy HH:mm"),
                    firmName = item.FirmName,
                    hoursWorked = item.HoursWorked,
                    hourlyRate = item.HourlyRate,
                    grossSalary = item.GrossSalary,
                    advance = item.Advance,
                    netSalary = item.NetSalary,
                    note = item.Note
                })
                .ToList();

            return new
            {
                id = summary.UniqueId,
                fullName = summary.FullName,
                photoUrl = summary.HasPhoto && !string.IsNullOrWhiteSpace(summary.UniqueId)
                    ? $"/api/v1/employees/{Uri.EscapeDataString(summary.UniqueId)}/photo"
                    : string.Empty,
                basic = new
                {
                    firstName = data.FirstName,
                    lastName = data.LastName,
                    firmName = summary.FirmName,
                    position = data.PositionTag,
                    positionNumber = data.PositionNumber,
                    department = data.Department,
                    status = data.Status,
                    statusKey,
                    statusDisplay = StatusHelper.GetDisplayText(summary.Status ?? data.Status),
                    employeeType = data.EmployeeType,
                    documentProfileType = data.DocumentProfileType,
                    euDocumentType = data.EuDocumentType,
                    visaDocType = data.VisaDocType,
                    gender = data.Gender,
                    genderDisplay = string.Equals(data.Gender, "female", StringComparison.OrdinalIgnoreCase) ? "Жінка" : "Чоловік",
                    birthDate = data.BirthDate,
                    citizenship = data.Citizenship,
                    passportCity = data.PassportCity,
                    passportCountry = data.PassportCountry,
                    issuingCountry = data.IssuingCountry,
                    highestEducationCode = data.HighestEducationCode,
                    highestEducationDisplay = EducationCatalog.GetFullDisplay(data.HighestEducationCode),
                    isArchived = data.IsArchived,
                    archivedFromFirm = data.ArchivedFromFirm
                },
                contact = new
                {
                    phone = data.Phone,
                    email = data.Email,
                    addressLocalFormatted = FormatAddress(data.AddressLocal),
                    addressAbroadFormatted = FormatAddress(data.AddressAbroad),
                    addressLocal = MapAddressStructured(data.AddressLocal),
                    addressAbroad = MapAddressStructured(data.AddressAbroad)
                },
                work = new
                {
                    startDate = data.StartDate,
                    contractSignDate = data.ContractSignDate,
                    endDate = data.EndDate,
                    contractType = data.ContractType,
                    workAddressTag = data.WorkAddressTag,
                    monthlySalaryBrutto = data.MonthlySalaryBrutto,
                    hourlySalary = data.HourlySalary
                },
                documents = new
                {
                    passportNumber = data.PassportNumber,
                    passportAuthority = data.PassportAuthority,
                    passportCity = data.PassportCity,
                    passportCountry = data.PassportCountry,
                    citizenship = data.Citizenship,
                    issuingCountry = data.IssuingCountry,
                    passportExpiry = data.PassportExpiry,
                    visaNumber = data.VisaNumber,
                    visaAuthority = data.VisaAuthority,
                    visaType = data.VisaType,
                    visaStartDate = data.VisaStartDate,
                    visaExpiry = data.VisaExpiry,
                    insuranceCompanyShort = data.InsuranceCompanyShort,
                    insuranceCompanyFull = data.InsuranceCompanyFull,
                    insuranceNumber = data.InsuranceNumber,
                    insuranceExpiry = data.InsuranceExpiry,
                    workPermitName = data.WorkPermitName,
                    workPermitNumber = data.WorkPermitNumber,
                    workPermitType = data.WorkPermitType,
                    workPermitIssueDate = data.WorkPermitIssueDate,
                    workPermitExpiry = data.WorkPermitExpiry,
                    workPermitAuthority = data.WorkPermitAuthority
                },
                bank = new
                {
                    hasBankAccountData = data.HasBankAccountData,
                    bankAccountNumber = data.HasBankAccountData ? data.BankAccountNumber : string.Empty,
                    bankName = data.HasBankAccountData ? data.BankName : string.Empty
                },
                files = new
                {
                    passport = data.Files?.Passport ?? string.Empty,
                    passportPage2 = data.Files?.PassportPage2 ?? string.Empty,
                    visa = data.Files?.Visa ?? string.Empty,
                    visaPage2 = data.Files?.VisaPage2 ?? string.Empty,
                    insurance = data.Files?.Insurance ?? string.Empty,
                    workPermit = data.Files?.WorkPermit ?? string.Empty,
                    photo = data.Files?.Photo ?? string.Empty,
                    passportUrl = BuildDocumentUrl(summary.UniqueId, "passport", data.Files?.Passport),
                    passportPage2Url = BuildDocumentUrl(summary.UniqueId, "passportPage2", data.Files?.PassportPage2),
                    visaUrl = BuildDocumentUrl(summary.UniqueId, "visa", data.Files?.Visa),
                    visaPage2Url = BuildDocumentUrl(summary.UniqueId, "visaPage2", data.Files?.VisaPage2),
                    insuranceUrl = BuildDocumentUrl(summary.UniqueId, "insurance", data.Files?.Insurance),
                    workPermitUrl = BuildDocumentUrl(summary.UniqueId, "workPermit", data.Files?.WorkPermit),
                    photoUrl = BuildDocumentUrl(summary.UniqueId, "photo", data.Files?.Photo)
                },
                history,
                salary = new
                {
                    hasHistory = salaryHistory.Count > 0,
                    totalNet = salaryHistory.Sum(item => item.netSalary),
                    totalGross = salaryHistory.Sum(item => item.grossSalary),
                    totalHours = salaryHistory.Sum(item => item.hoursWorked),
                    records = salaryHistory
                },
                firmHistory = (data.FirmHistory ?? new List<FirmHistoryEntry>())
                    .Select(item => new
                    {
                        firmName = item.FirmName,
                        startDate = item.StartDate,
                        endDate = item.EndDate
                    })
                    .ToList(),
                customDocuments = (data.CustomDocuments ?? new List<CustomSignedDocument>())
                    .Where(item => !item.IsHidden)
                    .Select(item => new
                    {
                        name = item.Name,
                        signDate = item.SignDate,
                        expiryDate = item.ExpiryDate,
                        fileName = item.FileName
                    })
                    .ToList(),
                ui = new
                {
                    hasSecondaryDocuments = hasSecondaryDocuments,
                    secondarySectionTitle = secondarySectionTitle,
                    usesPassportPage2Secondary = usesPassportPage2Secondary,
                    showSecondaryVisaType = !(usesPassportPage2Secondary && isEuIdCardEmployee),
                    showVisaStartDate = string.Equals(employeeTypeRaw, "visa", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(employeeTypeRaw, "work_permit", StringComparison.OrdinalIgnoreCase),
                    showInsurance = !isPassportOnly,
                    showWorkPermitSection = isWorkPermitType
                }
            };
        }

        private static object MapAddressStructured(EmployeeAddress? address)
        {
            address ??= new EmployeeAddress();
            return new
            {
                street = address.Street,
                number = address.Number,
                city = address.City,
                zip = address.Zip
            };
        }

        private static string BuildDocumentUrl(string employeeId, string kind, string? fileName)
        {
            return string.IsNullOrWhiteSpace(employeeId) || string.IsNullOrWhiteSpace(fileName)
                ? string.Empty
                : $"/api/v1/employees/{Uri.EscapeDataString(employeeId)}/documents/{Uri.EscapeDataString(kind)}";
        }

        private static string ResolveEmployeeDocumentFileName(EmployeeData data, string kind)
        {
            var files = data.Files;
            if (files == null)
                return string.Empty;

            return (kind ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "passport" => files.Passport,
                "passportpage2" => files.PassportPage2,
                "visa" => files.Visa,
                "visapage2" => files.VisaPage2,
                "insurance" => files.Insurance,
                "workpermit" => files.WorkPermit,
                "photo" => files.Photo,
                _ => string.Empty
            };
        }

        private static string ResolveDocumentContentType(string path)
        {
            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".webp" => "image/webp",
                ".gif" => "image/gif",
                ".pdf" => "application/pdf",
                _ => "image/jpeg"
            };
        }

        private static string FormatAddress(EmployeeAddress? address)
        {
            if (address == null)
                return string.Empty;

            return string.Join(", ", new[]
            {
                $"{address.Street} {address.Number}".Trim(),
                address.City,
                address.Zip
            }.Where(part => !string.IsNullOrWhiteSpace(part)));
        }

        private static string BuildHomePageHtml()
        {
            return """
<!doctype html>
<html lang="uk">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Agency Contractor Web</title>
  <style>
    :root {
      color-scheme: light;
      --bg: #f5f7fb;
      --card: rgba(255, 255, 255, .92);
      --card-strong: #ffffff;
      --text: #111827;
      --muted: #6b7280;
      --border: #e5e7eb;
      --accent: #2563eb;
      --accent-soft: #dbeafe;
      --success: #059669;
      --warning: #d97706;
      --danger: #dc2626;
      --shadow: 0 18px 45px rgba(15, 23, 42, .10);
      font-family: "Segoe UI", system-ui, -apple-system, BlinkMacSystemFont, sans-serif;
    }

    * { box-sizing: border-box; }

    body {
      margin: 0;
      min-height: 100vh;
      background:
        radial-gradient(circle at top left, rgba(37, 99, 235, .14), transparent 32rem),
        radial-gradient(circle at top right, rgba(124, 58, 237, .10), transparent 30rem),
        var(--bg);
      color: var(--text);
    }

    .shell {
      width: min(1180px, calc(100vw - 32px));
      margin: 0 auto;
      padding: 24px 0 40px;
    }

    .topbar {
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: 16px;
      margin-bottom: 22px;
      padding: 18px 20px;
      background: var(--card);
      border: 1px solid var(--border);
      border-radius: 22px;
      box-shadow: var(--shadow);
      backdrop-filter: blur(12px);
    }

    .brand {
      display: flex;
      align-items: center;
      gap: 14px;
    }

    .brand-icon {
      width: 44px;
      height: 44px;
      border-radius: 14px;
      display: grid;
      place-items: center;
      background: linear-gradient(135deg, #2563eb, #7c3aed);
      color: white;
      font-size: 22px;
      font-weight: 700;
    }

    h1 {
      margin: 0;
      font-size: clamp(22px, 3vw, 30px);
      letter-spacing: -.03em;
    }

    .subtitle {
      margin: 3px 0 0;
      color: var(--muted);
      font-size: 13px;
    }

    .status {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 9px 12px;
      border-radius: 999px;
      background: #ecfdf5;
      color: #047857;
      font-size: 13px;
      font-weight: 600;
      white-space: nowrap;
    }

    .dot {
      width: 9px;
      height: 9px;
      border-radius: 50%;
      background: var(--success);
      box-shadow: 0 0 0 5px rgba(5, 150, 105, .12);
    }

    .grid {
      display: grid;
      grid-template-columns: repeat(4, minmax(0, 1fr));
      gap: 14px;
      margin-bottom: 18px;
    }

    .card {
      background: var(--card);
      border: 1px solid var(--border);
      border-radius: 20px;
      box-shadow: var(--shadow);
      padding: 18px;
    }

    .metric-label {
      color: var(--muted);
      font-size: 13px;
      margin-bottom: 10px;
    }

    .metric-value {
      font-size: 34px;
      line-height: 1;
      font-weight: 750;
      letter-spacing: -.04em;
    }

    .metric-note {
      margin-top: 9px;
      color: var(--muted);
      font-size: 12px;
    }

    .layout {
      display: grid;
      grid-template-columns: 360px minmax(0, 1fr);
      gap: 18px;
    }

    .section-title {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 14px;
    }

    .section-title h2 {
      margin: 0;
      font-size: 18px;
      letter-spacing: -.02em;
    }

    .pill {
      border-radius: 999px;
      padding: 5px 9px;
      background: var(--accent-soft);
      color: var(--accent);
      font-size: 12px;
      font-weight: 650;
    }

    .list {
      display: grid;
      gap: 10px;
      max-height: 620px;
      overflow: auto;
      padding-right: 4px;
    }

    .firm, .employee {
      border: 1px solid var(--border);
      background: var(--card-strong);
      border-radius: 16px;
      padding: 13px 14px;
    }

    .firm-name, .employee-name {
      font-weight: 700;
      margin-bottom: 4px;
    }

    .meta {
      color: var(--muted);
      font-size: 12px;
      line-height: 1.45;
    }

    .employee {
      display: grid;
      grid-template-columns: 1fr auto;
      gap: 12px;
      align-items: center;
    }

    .badge-row {
      display: flex;
      gap: 6px;
      flex-wrap: wrap;
      justify-content: flex-end;
    }

    .badge {
      border-radius: 999px;
      padding: 5px 8px;
      background: #f3f4f6;
      color: #374151;
      font-size: 11px;
      font-weight: 650;
    }

    .badge.ok { background: #ecfdf5; color: #047857; }
    .badge.warn { background: #fffbeb; color: #b45309; }
    .badge.expired { background: #fef2f2; color: #b91c1c; }
    .badge.missing { background: #fef2f2; color: #b91c1c; }

    .toolbar {
      display: flex;
      gap: 10px;
      margin-bottom: 14px;
    }

    input {
      width: 100%;
      border: 1px solid var(--border);
      border-radius: 14px;
      padding: 12px 14px;
      background: white;
      color: var(--text);
      font: inherit;
      outline: none;
    }

    input:focus {
      border-color: rgba(37, 99, 235, .55);
      box-shadow: 0 0 0 4px rgba(37, 99, 235, .10);
    }

    button,
    input,
    select {
      -webkit-tap-highlight-color: transparent;
    }

    button:focus-visible,
    input:focus-visible,
    select:focus-visible,
    a:focus-visible {
      outline: 2px solid rgba(45, 212, 191, .72);
      outline-offset: 2px;
    }

    .empty, .error {
      border: 1px dashed var(--border);
      border-radius: 16px;
      padding: 20px;
      text-align: center;
      color: var(--muted);
      background: linear-gradient(135deg, rgba(255, 255, 255, .58), rgba(255, 255, 255, .36));
      box-shadow: inset 0 1px 0 rgba(255, 255, 255, .30);
      line-height: 1.45;
    }

    .error { color: var(--danger); }

    @media (max-width: 920px) {
      .grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
      .layout { grid-template-columns: 1fr; }
      .topbar { align-items: flex-start; flex-direction: column; }
    }

    @media (max-width: 560px) {
      .shell { width: min(100vw - 20px, 1180px); padding-top: 10px; }
      .grid { grid-template-columns: 1fr; }
      .employee { grid-template-columns: 1fr; }
      .badge-row { justify-content: flex-start; }
    }

    body {
      background:
        radial-gradient(circle at 58% 12%, rgba(91, 33, 182, .42), transparent 30rem),
        radial-gradient(circle at 72% 96%, rgba(124, 58, 237, .22), transparent 34rem),
        linear-gradient(115deg, #06101f 0%, #090d24 52%, #11072c 100%);
      color: #f8fafc;
    }

    body.theme-light {
      --card: rgba(255, 255, 255, .86);
      --card-strong: rgba(255, 255, 255, .96);
      --text: #0f172a;
      --muted: #64748b;
      --border: rgba(148, 163, 184, .30);
      --accent: #0f766e;
      --accent-soft: #ccfbf1;
      background:
        radial-gradient(circle at 18% 12%, rgba(20, 184, 166, .18), transparent 28rem),
        radial-gradient(circle at 82% 8%, rgba(59, 130, 246, .16), transparent 30rem),
        radial-gradient(circle at 55% 95%, rgba(168, 85, 247, .10), transparent 34rem),
        linear-gradient(135deg, #f8fbff 0%, #eef7ff 46%, #f6f3ff 100%);
      color: #0f172a;
    }

    body.theme-light .chrome-button,
    body.theme-light .hamburger,
    body.theme-light .company-strip,
    body.theme-light .report-filter-panel,
    body.theme-light .report-summary-panel,
    body.theme-light .report-groups-panel,
    body.theme-light .report-group-card,
    body.theme-light .placeholder-panel {
      background: rgba(255, 255, 255, .86);
      color: #0f172a;
      border-color: rgba(15, 23, 42, .10);
      box-shadow: 0 22px 60px rgba(15, 23, 42, .12);
    }

    body.theme-light .module-tile,
    body.theme-light .card,
    body.theme-light .firm,
    body.theme-light .employee,
    body.theme-light .employee-profile-card,
    body.theme-light .employee-stat-card,
    body.theme-light .problem-summary-card,
    body.theme-light .problem-card,
    body.theme-light .firms-side,
    body.theme-light .firm-side-item,
    body.theme-light .report-columns-dialog,
    body.theme-light .report-filter-box,
    body.theme-light .web-settings-card {
      background: rgba(255, 255, 255, .82);
      color: #0f172a;
      border-color: rgba(15, 23, 42, .10);
      box-shadow: 0 16px 44px rgba(15, 23, 42, .10);
    }

    body.theme-light input,
    body.theme-light select,
    body.theme-light .report-date-input,
    body.theme-light .report-search,
    body.theme-light .employees-search,
    body.theme-light .firm-search {
      background: rgba(255, 255, 255, .92);
      color: #0f172a;
      border-color: rgba(15, 23, 42, .14);
    }

    body.theme-light input::placeholder {
      color: rgba(71, 85, 105, .72);
    }

    body.theme-light .filter-chip,
    body.theme-light .employee-action,
    body.theme-light .back-button {
      background: rgba(255, 255, 255, .84);
      color: #334155;
      border-color: rgba(15, 23, 42, .12);
      box-shadow: 0 12px 26px rgba(15, 23, 42, .08);
    }

    body.theme-light .filter-chip.is-active,
    body.theme-light .employee-action.primary,
    body.theme-light .report-mode-tab.is-active {
      background: linear-gradient(135deg, #0f766e, #0891b2);
      color: #ffffff;
      border-color: rgba(15, 118, 110, .35);
    }

    body.theme-light .report-table th,
    body.theme-light .report-firm-table th {
      background: rgba(241, 245, 249, .96);
      color: rgba(15, 23, 42, .72);
    }

    body.theme-light .report-table td,
    body.theme-light .report-firm-table td,
    body.theme-light .report-title-center,
    body.theme-light .employee-company-title,
    body.theme-light .company-name,
    body.theme-light .report-panel-title,
    body.theme-light .report-filter-title,
    body.theme-light .report-filter-box-title,
    body.theme-light .report-group-head,
    body.theme-light .report-name-cell {
      color: #0f172a;
    }

    body.theme-light .meta,
    body.theme-light .subtitle,
    body.theme-light .employee-card-position,
    body.theme-light .employee-card-meta,
    body.theme-light .problem-meta,
    body.theme-light .report-range-pill,
    body.theme-light .report-check-list,
    body.theme-light .web-settings-note,
    body.theme-light .placeholder-panel p {
      color: #475569;
    }

    body.theme-light .report-table tbody tr:nth-child(odd),
    body.theme-light .report-firm-table tbody tr:nth-child(odd) {
      background: rgba(241, 245, 249, .88);
    }

    body.theme-light .report-table tbody tr:nth-child(even),
    body.theme-light .report-firm-table tbody tr:nth-child(even) {
      background: rgba(255, 255, 255, .72);
    }

    body.theme-light .empty,
    body.theme-light .error {
      background: rgba(255, 255, 255, .66);
      border-color: rgba(15, 23, 42, .14);
      color: #64748b;
    }

    body.density-compact .report-filter-panel,
    body.density-compact .employee-profile-card,
    body.density-compact .problem-card,
    body.density-compact .report-group-card {
      padding-top: 8px;
      padding-bottom: 8px;
    }

    body.density-compact .report-table td,
    body.density-compact .report-table th,
    body.density-compact .report-firm-table td,
    body.density-compact .report-firm-table th {
      padding-top: 7px;
      padding-bottom: 7px;
    }

    .app-shell {
      width: min(1380px, calc(100vw - 40px));
      min-height: 100vh;
      margin: 0 auto;
      padding: 18px 0 46px;
    }

    .web-chrome {
      display: grid;
      grid-template-columns: 44px minmax(260px, 560px) 1fr auto;
      align-items: center;
      gap: 18px;
      margin-bottom: 22px;
    }

    .hamburger, .chrome-button {
      width: 34px;
      height: 34px;
      border: 1px solid rgba(148, 163, 184, .18);
      border-radius: 12px;
      background: rgba(15, 23, 42, .72);
      color: #e2e8f0;
      display: grid;
      place-items: center;
      box-shadow: 0 14px 35px rgba(0, 0, 0, .22);
    }

    .global-search {
      position: relative;
      justify-self: center;
      width: min(560px, 100%);
    }

    .global-search input {
      height: 36px;
      border-radius: 16px;
      border-color: rgba(129, 140, 248, .28);
      background: rgba(30, 27, 75, .64);
      color: #eef2ff;
      padding-left: 42px;
      box-shadow: inset 0 1px 0 rgba(255, 255, 255, .05), 0 16px 38px rgba(15, 23, 42, .28);
    }

    .global-search input::placeholder { color: rgba(199, 210, 254, .58); }

    .search-icon {
      position: absolute;
      left: 14px;
      top: 50%;
      transform: translateY(-50%);
      color: rgba(199, 210, 254, .72);
      font-size: 14px;
    }

    .chrome-actions {
      display: flex;
      gap: 8px;
    }

    button.chrome-button {
      cursor: pointer;
      font: inherit;
    }

    .module-stage {
      width: min(1320px, 100%);
      margin: 0 auto;
      padding-top: clamp(8px, 2vh, 28px);
    }

    .home-layout {
      display: grid;
      grid-template-columns: minmax(520px, 760px);
      justify-content: center;
      gap: clamp(18px, 2.6vw, 34px);
      align-items: start;
    }

    .menu-center {
      grid-column: 1;
    }

    .company-strip {
      display: grid;
      grid-template-columns: auto 1fr auto;
      align-items: center;
      gap: 12px;
      margin-bottom: 16px;
      padding: 12px 14px;
      border: 1px solid rgba(148, 163, 184, .18);
      border-radius: 18px;
      background: rgba(15, 23, 42, .68);
      box-shadow: 0 18px 55px rgba(0, 0, 0, .28);
      backdrop-filter: blur(14px);
    }

    .company-logo {
      width: 34px;
      height: 34px;
      border-radius: 12px;
      display: grid;
      place-items: center;
      background: linear-gradient(135deg, #ec4899, #7c3aed);
      color: white;
      font-weight: 800;
      box-shadow: 0 10px 24px rgba(124, 58, 237, .35);
    }

    .company-name {
      color: #f8fafc;
      font-size: 13px;
      font-weight: 750;
    }

    .company-meta {
      color: rgba(226, 232, 240, .58);
      font-size: 10px;
      margin-top: 2px;
    }

    .clock {
      text-align: right;
      color: #f8fafc;
      font-size: 16px;
      font-weight: 800;
    }

    .clock small {
      display: block;
      margin-top: 2px;
      color: rgba(226, 232, 240, .54);
      font-size: 10px;
      font-weight: 500;
    }

    .module-grid {
      display: grid;
      grid-template-columns: repeat(5, 1fr);
      gap: clamp(12px, 1.1vw, 18px);
    }

    .module-tile {
      position: relative;
      min-height: clamp(96px, 10.4vh, 132px);
      padding: clamp(13px, 1.3vw, 18px) 8px clamp(11px, 1.2vw, 16px);
      border: 1px solid rgba(148, 163, 184, .16);
      border-radius: 14px;
      background: linear-gradient(180deg, rgba(30, 41, 59, .68), rgba(15, 23, 42, .76));
      color: #f8fafc;
      cursor: pointer;
      box-shadow: 0 18px 40px rgba(0, 0, 0, .26), inset 0 1px 0 rgba(255, 255, 255, .04);
      transition: transform .16s ease, border-color .16s ease, background .16s ease;
    }

    .module-tile:hover {
      transform: translateY(-2px);
      border-color: rgba(129, 140, 248, .45);
      background: linear-gradient(180deg, rgba(51, 65, 85, .76), rgba(30, 27, 75, .72));
    }

    .tile-icon {
      width: clamp(34px, 3vw, 48px);
      height: clamp(34px, 3vw, 48px);
      margin: 0 auto clamp(12px, 1vw, 16px);
      border-radius: 11px;
      display: grid;
      place-items: center;
      color: white;
      font-size: clamp(17px, 1.35vw, 22px);
      font-weight: 800;
      box-shadow: 0 10px 22px rgba(0, 0, 0, .22);
    }

    .tile-title {
      text-align: center;
      font-size: clamp(11px, .85vw, 14px);
      font-weight: 750;
      line-height: 1.25;
    }

    .firms-side {
      position: fixed;
      top: 0;
      left: 0;
      z-index: 30;
      width: min(380px, calc(100vw - 34px));
      height: 100vh;
      min-height: 0;
      max-height: none;
      padding: 16px;
      border: 1px solid rgba(148, 163, 184, .18);
      border-radius: 0 24px 24px 0;
      background: rgba(10, 16, 32, .94);
      box-shadow: 0 22px 60px rgba(0, 0, 0, .30);
      backdrop-filter: blur(14px);
      overflow: hidden;
      transform: translateX(-105%);
      transition: transform .2s ease;
    }

    .firms-side.is-open {
      transform: translateX(0);
    }

    .drawer-backdrop {
      position: fixed;
      inset: 0;
      z-index: 25;
      display: none;
      background: rgba(2, 6, 23, .58);
      backdrop-filter: blur(3px);
    }

    .drawer-backdrop.is-open {
      display: block;
    }

    .firms-side-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 12px;
      color: #f8fafc;
    }

    .firms-side-title {
      font-size: 15px;
      font-weight: 800;
    }

    .firm-search {
      height: 36px;
      margin-bottom: 12px;
      border-radius: 13px;
      border-color: rgba(148, 163, 184, .18);
      background: rgba(2, 6, 23, .34);
      color: #f8fafc;
      font-size: 12px;
    }

    .firm-side-list {
      display: grid;
      gap: 9px;
      max-height: calc(100vh - 118px);
      overflow: auto;
      padding-right: 4px;
    }

    .firm-side-item {
      width: 100%;
      text-align: left;
      border: 1px solid rgba(148, 163, 184, .14);
      border-radius: 15px;
      padding: 11px 12px;
      background: rgba(30, 41, 59, .54);
      color: #e2e8f0;
      cursor: pointer;
      font: inherit;
      transition: transform .14s ease, border-color .14s ease, background .14s ease;
    }

    .firm-side-item:hover,
    .firm-side-item.is-selected {
      transform: translateY(-1px);
      border-color: rgba(96, 165, 250, .48);
      background: rgba(30, 64, 175, .32);
    }

    .firm-side-name {
      font-size: 13px;
      font-weight: 800;
      color: #f8fafc;
      margin-bottom: 4px;
    }

    .firm-side-meta {
      font-size: 11px;
      color: rgba(226, 232, 240, .62);
      line-height: 1.35;
    }

    .tile-badge {
      position: absolute;
      top: 8px;
      right: 8px;
      min-width: 18px;
      height: 18px;
      padding: 0 5px;
      border-radius: 999px;
      display: grid;
      place-items: center;
      background: #ec4899;
      color: white;
      font-size: 9px;
      font-weight: 800;
      box-shadow: 0 8px 18px rgba(236, 72, 153, .35);
    }

    .dashboard-view {
      display: none;
      width: min(1540px, 100%);
      margin: 12px auto 0;
      color: var(--text);
    }

    .dashboard-view.is-active { display: block; }

    .dash-head {
      display: grid;
      grid-template-columns: auto 1fr auto;
      gap: 12px;
      align-items: center;
      margin-bottom: 16px;
    }

    .dash-title-wrap {
      display: flex;
      align-items: center;
      gap: 12px;
      min-width: 0;
    }

    .dash-icon {
      width: 42px;
      height: 42px;
      border-radius: 13px;
      display: grid;
      place-items: center;
      background: linear-gradient(135deg, #8b5cf6, #ec4899);
      color: white;
      font-weight: 950;
      box-shadow: 0 14px 34px rgba(139, 92, 246, .26);
    }

    .dash-head h1 {
      margin: 0;
      font-size: 21px;
      letter-spacing: -.03em;
      color: #f8fafc;
    }

    .dash-head p {
      margin: 3px 0 0;
      color: rgba(226, 232, 240, .52);
      font-size: 12px;
      font-weight: 650;
    }

    .dash-ai-button {
      border: 1px solid rgba(168, 85, 247, .38);
      border-radius: 11px;
      background: rgba(88, 28, 135, .20);
      color: #d8b4fe;
      padding: 8px 12px;
      font: inherit;
      font-size: 12px;
      font-weight: 850;
    }

    .dash-stat-grid {
      display: grid;
      grid-template-columns: repeat(4, minmax(0, 1fr));
      gap: 14px;
      margin-bottom: 16px;
    }

    .dash-stat-card,
    .dash-widget {
      border: 1px solid rgba(148, 163, 184, .14);
      border-radius: 16px;
      background: linear-gradient(135deg, rgba(30, 41, 59, .56), rgba(15, 23, 42, .72));
      box-shadow: 0 20px 48px rgba(0,0,0,.18), inset 0 1px 0 rgba(255,255,255,.035);
    }

    .dash-stat-card {
      min-height: 106px;
      padding: 15px 16px;
      display: grid;
      grid-template-columns: minmax(0, 1fr) auto;
      gap: 12px;
      align-items: center;
    }

    .dash-stat-card.is-clickable {
      cursor: pointer;
      transition: transform .15s ease, border-color .15s ease, background .15s ease;
    }

    .dash-stat-card.is-clickable:hover,
    .dash-stat-card.is-clickable:focus-visible {
      transform: translateY(-2px);
      border-color: rgba(251, 146, 60, .42);
      background: linear-gradient(135deg, rgba(30, 41, 75, .66), rgba(15, 23, 42, .78));
    }

    .dash-stat-label {
      color: rgba(226, 232, 240, .55);
      font-size: 12px;
      font-weight: 850;
    }

    .dash-stat-value {
      margin-top: 5px;
      color: #f8fafc;
      font-size: 29px;
      font-weight: 950;
      line-height: 1;
      letter-spacing: -.04em;
    }

    .dash-stat-note {
      margin-top: 6px;
      color: rgba(226, 232, 240, .50);
      font-size: 11px;
      font-weight: 650;
    }

    .dash-stat-icon {
      width: 42px;
      height: 42px;
      border-radius: 12px;
      display: grid;
      place-items: center;
      color: white;
      font-weight: 950;
      font-size: 18px;
    }

    .dash-mini-bars {
      margin-top: 10px;
      display: grid;
      gap: 4px;
    }

    .dash-mini-bar {
      height: 5px;
      border-radius: 999px;
      overflow: hidden;
      background: rgba(148, 163, 184, .18);
    }

    .dash-mini-bar span {
      display: block;
      height: 100%;
      border-radius: inherit;
    }

    .dash-main-grid {
      display: grid;
      grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
      gap: 14px;
      min-height: calc(100vh - 255px);
    }

    .dash-left-stack,
    .dash-right-stack {
      display: grid;
      gap: 14px;
      min-height: 0;
    }

    .dash-left-stack { grid-template-rows: minmax(0, 1fr); }
    .dash-right-stack { grid-template-rows: minmax(180px, .72fr) 10px minmax(180px, 1fr); }

    .dash-widget-slot {
      min-height: 0;
      display: flex;
    }

    .dash-widget-slot.is-drag-over .dash-widget {
      border-color: rgba(45, 212, 191, .55);
      box-shadow: 0 22px 58px rgba(20, 184, 166, .12), inset 0 1px 0 rgba(255,255,255,.05);
    }

    .dash-splitter {
      height: 10px;
      cursor: row-resize;
      border-radius: 999px;
      background: transparent;
      position: relative;
    }

    .dash-splitter::after {
      content: "";
      position: absolute;
      left: 38%;
      right: 38%;
      top: 4px;
      height: 2px;
      border-radius: 999px;
      background: rgba(148, 163, 184, .24);
    }

    .dash-splitter:hover::after {
      background: rgba(45, 212, 191, .58);
    }

    .dash-widget {
      padding: 15px;
      min-height: 0;
      overflow: hidden;
      display: flex;
      flex-direction: column;
      width: 100%;
    }

    .dash-widget-head {
      display: flex;
      align-items: center;
      gap: 8px;
      margin-bottom: 12px;
      color: #f8fafc;
      font-size: 14px;
      font-weight: 950;
    }

    .dash-drag-handle {
      width: 18px;
      height: 18px;
      border: 0;
      border-radius: 6px;
      background: rgba(148, 163, 184, .10);
      color: rgba(226, 232, 240, .62);
      cursor: grab;
      display: grid;
      place-items: center;
      font: inherit;
      font-size: 12px;
      line-height: 1;
    }

    .dash-drag-handle:active { cursor: grabbing; }

    .dash-widget-sub {
      margin-left: auto;
      color: rgba(226, 232, 240, .48);
      font-size: 11px;
      font-weight: 750;
    }

    .dash-scroll {
      min-height: 0;
      overflow: auto;
      padding-right: 4px;
      display: grid;
      gap: 9px;
    }

    .dash-salary-card,
    .dash-expiring-card,
    .dash-eff-card {
      border: 1px solid rgba(148, 163, 184, .12);
      border-radius: 12px;
      background: rgba(15, 23, 42, .48);
      padding: 11px 12px;
      box-shadow: inset 0 1px 0 rgba(255,255,255,.025);
      transition: border-color .14s ease, background .14s ease, transform .14s ease;
    }

    .dash-salary-card:hover,
    .dash-expiring-card:hover,
    .dash-eff-card:hover {
      transform: translateY(-1px);
      border-color: rgba(96, 165, 250, .34);
      background: rgba(30, 41, 59, .62);
    }

    .dash-salary-row {
      display: grid;
      grid-template-columns: 34px minmax(0, 1fr) auto;
      gap: 10px;
      align-items: center;
    }

    .dash-status-icon {
      width: 34px;
      height: 34px;
      border-radius: 10px;
      display: grid;
      place-items: center;
      font-weight: 950;
    }

    .dash-card-title {
      color: #f8fafc;
      font-size: 13px;
      font-weight: 900;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .dash-card-meta {
      margin-top: 3px;
      color: rgba(226, 232, 240, .55);
      font-size: 11px;
      font-weight: 650;
    }

    .dash-money {
      color: #f8fafc;
      font-size: 12px;
      font-weight: 900;
      text-align: right;
      white-space: nowrap;
    }

    .dash-progress {
      height: 6px;
      border-radius: 999px;
      background: rgba(148, 163, 184, .16);
      overflow: hidden;
      margin: 8px 0 5px 44px;
    }

    .dash-progress span {
      display: block;
      height: 100%;
      border-radius: inherit;
    }

    .dash-salary-foot {
      margin-left: 44px;
      display: flex;
      justify-content: space-between;
      gap: 12px;
      color: rgba(226, 232, 240, .58);
      font-size: 11px;
    }

    .dash-eff-grid {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: 9px;
      margin-bottom: 10px;
    }

    .dash-eff-card strong {
      display: block;
      color: #f8fafc;
      font-size: 20px;
      margin-bottom: 4px;
    }

    .dash-eff-card span {
      color: rgba(226, 232, 240, .52);
      font-size: 10px;
      font-weight: 750;
    }

    .dash-expiring-card {
      display: grid;
      grid-template-columns: 34px minmax(0, 1fr) auto;
      gap: 10px;
      align-items: center;
      cursor: pointer;
    }

    .dash-severity {
      border-radius: 8px;
      padding: 5px 8px;
      font-size: 11px;
      font-weight: 900;
      white-space: nowrap;
    }

    .dash-movement-overlay {
      position: fixed;
      inset: 0;
      z-index: 80;
      display: none;
      place-items: center;
      padding: 26px;
      background: rgba(2, 6, 23, .66);
      backdrop-filter: blur(4px);
    }

    .dash-movement-overlay.is-open { display: grid; }

    .dash-movement-dialog {
      width: min(900px, calc(100vw - 34px));
      max-height: min(720px, calc(100vh - 34px));
      border: 1px solid rgba(99, 102, 241, .30);
      border-radius: 22px;
      background: linear-gradient(135deg, rgba(30, 41, 75, .97), rgba(15, 23, 42, .98));
      box-shadow: 0 32px 90px rgba(0, 0, 0, .42), inset 0 1px 0 rgba(255,255,255,.04);
      padding: 28px 30px 26px;
      color: #f8fafc;
      overflow: hidden;
      display: grid;
      grid-template-rows: auto minmax(0, 1fr) auto;
      gap: 22px;
    }

    .dash-movement-head {
      display: grid;
      grid-template-columns: auto 1fr auto;
      gap: 14px;
      align-items: center;
    }

    .dash-movement-icon {
      width: 54px;
      height: 54px;
      border-radius: 14px;
      display: grid;
      place-items: center;
      background: linear-gradient(135deg, #fb923c, #ef4444);
      color: white;
      font-size: 22px;
      font-weight: 950;
      box-shadow: 0 18px 38px rgba(239, 68, 68, .22);
    }

    .dash-movement-head h2 {
      margin: 0;
      font-size: 24px;
      font-weight: 950;
      letter-spacing: -.03em;
    }

    .dash-movement-head p {
      margin: 4px 0 0;
      color: rgba(226, 232, 240, .62);
      font-size: 13px;
      font-weight: 750;
    }

    .dash-movement-close {
      width: 44px;
      height: 44px;
      border: 1px solid rgba(148, 163, 184, .22);
      border-radius: 13px;
      background: rgba(15, 23, 42, .56);
      color: rgba(226, 232, 240, .76);
      font: inherit;
      font-size: 24px;
      cursor: pointer;
    }

    .dash-movement-grid {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 20px;
      min-height: 0;
    }

    .dash-movement-column {
      min-height: 0;
      border: 1px solid rgba(99, 102, 241, .28);
      border-radius: 15px;
      background: rgba(15, 23, 42, .42);
      padding: 16px;
      display: grid;
      grid-template-rows: auto minmax(0, 1fr);
      gap: 14px;
    }

    .dash-movement-column-title {
      display: flex;
      justify-content: space-between;
      gap: 10px;
      align-items: center;
      color: #f8fafc;
      font-size: 17px;
      font-weight: 950;
    }

    .dash-movement-count {
      min-width: 92px;
      border-radius: 9px;
      padding: 4px 9px;
      text-align: left;
      font-size: 12px;
      font-weight: 950;
    }

    .dash-movement-list {
      min-height: 0;
      overflow-y: auto;
      padding-right: 4px;
      display: grid;
      align-content: start;
      gap: 10px;
    }

    .dash-movement-item {
      border: 1px solid rgba(99, 102, 241, .22);
      border-radius: 13px;
      background: linear-gradient(135deg, rgba(30, 41, 59, .58), rgba(46, 16, 101, .24));
      padding: 12px 14px;
      display: grid;
      grid-template-columns: 42px minmax(0, 1fr);
      gap: 12px;
      align-items: center;
      cursor: pointer;
      transition: transform .14s ease, border-color .14s ease, background .14s ease;
    }

    .dash-movement-item:hover,
    .dash-movement-item:focus-visible {
      transform: translateY(-1px);
      border-color: rgba(129, 140, 248, .52);
      background: linear-gradient(135deg, rgba(30, 64, 175, .42), rgba(46, 16, 101, .34));
    }

    .dash-movement-sign {
      width: 40px;
      height: 40px;
      border-radius: 10px;
      display: grid;
      place-items: center;
      font-size: 18px;
      font-weight: 950;
    }

    .dash-movement-name {
      color: #f8fafc;
      font-size: 15px;
      font-weight: 950;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .dash-movement-meta {
      margin-top: 3px;
      color: rgba(226, 232, 240, .58);
      font-size: 12px;
      font-weight: 650;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .dash-movement-foot {
      text-align: center;
      color: rgba(226, 232, 240, .42);
      font-size: 13px;
      font-weight: 650;
    }

    @media (max-width: 1050px) {
      .dash-stat-grid,
      .dash-main-grid { grid-template-columns: 1fr; }
      .dash-right-stack { grid-template-rows: auto 10px auto; }
      .dash-movement-grid { grid-template-columns: 1fr; }
      .dash-widget { min-height: 240px; }
    }
    .employees-view {
      display: none;
      width: min(1500px, 100%);
      margin: 22px auto 0;
      color: #e2e8f0;
    }

    .employees-view.is-active { display: block; }

    .problems-view {
      display: none;
      width: min(1280px, 100%);
      margin: 22px auto 0;
      color: #e2e8f0;
    }

    .problems-view.is-active { display: block; }

    .report-view {
      display: none;
      width: min(1540px, 100%);
      margin: 22px auto 0;
      color: #e2e8f0;
    }

    .report-view.is-active { display: block; }

    .finance-view {
      display: none;
      width: min(1540px, 100%);
      margin: 22px auto 0;
      color: #e2e8f0;
    }

    .finance-view.is-active { display: block; }

    .connection-banner {
      display: none;
      margin: 0 0 10px;
      padding: 9px 12px;
      border: 1px solid rgba(251, 191, 36, .30);
      border-radius: 12px;
      background: rgba(120, 53, 15, .32);
      color: #fde68a;
      font-size: 12px;
      font-weight: 800;
    }

    .connection-banner.is-visible {
      display: block;
    }

    .finance-filter-row {
      display: flex;
      flex-wrap: wrap;
      gap: 16px;
      align-items: flex-end;
    }

    .finance-filter-row label {
      display: flex;
      flex-direction: column;
      gap: 6px;
      font-size: 11px;
      font-weight: 750;
      color: rgba(226, 232, 240, .72);
      min-width: 200px;
    }

    .finance-filter-row .report-date-input {
      min-width: 220px;
    }

    td.finance-num {
      text-align: right;
      font-variant-numeric: tabular-nums;
    }

    .finance-menu-screen {
      min-height: calc(100vh - 180px);
      display: grid;
      place-items: center;
    }

    .finance-card-grid {
      display: grid;
      grid-template-columns: repeat(2, minmax(180px, 240px));
      gap: 22px;
    }

    .finance-module-card {
      min-height: 190px;
      border: 1px solid rgba(148, 163, 184, .16);
      border-radius: 18px;
      background: linear-gradient(145deg, rgba(30, 41, 59, .70), rgba(15, 23, 42, .72));
      box-shadow: 0 24px 60px rgba(0, 0, 0, .22), inset 0 1px 0 rgba(255, 255, 255, .04);
      color: #f8fafc;
      cursor: pointer;
      display: grid;
      place-items: center;
      padding: 28px 22px;
      text-align: center;
      transition: transform .16s ease, border-color .16s ease, box-shadow .16s ease;
      font: inherit;
    }

    .finance-module-card:hover {
      transform: translateY(-3px);
      border-color: rgba(168, 85, 247, .52);
      box-shadow: 0 28px 70px rgba(88, 28, 135, .32);
    }

    .finance-module-icon {
      width: 58px;
      height: 58px;
      border-radius: 15px;
      display: grid;
      place-items: center;
      margin: 0 auto 16px;
      color: white;
      font-size: 25px;
      font-weight: 900;
      box-shadow: 0 18px 32px rgba(0, 0, 0, .24);
    }

    .finance-module-card h3 {
      margin: 0;
      font-size: 18px;
      font-weight: 900;
    }

    .finance-module-card p {
      margin: 8px 0 0;
      color: rgba(226, 232, 240, .56);
      font-size: 12px;
      line-height: 1.35;
    }

    .finance-salary-screen.is-hidden,
    .finance-menu-screen.is-hidden,
    .finance-tables-screen.is-hidden { display: none; }

    .finance-topbar {
      display: grid;
      grid-template-columns: auto 1fr auto;
      gap: 14px;
      align-items: center;
      margin-bottom: 10px;
    }

    .finance-month-nav {
      display: flex;
      justify-content: center;
      align-items: center;
      gap: 10px;
      color: #f8fafc;
      font-weight: 900;
      font-size: 18px;
    }

    .finance-action-row {
      display: flex;
      flex-wrap: wrap;
      gap: 7px;
      justify-content: flex-end;
      align-items: center;
    }

    .finance-search {
      min-width: 190px;
      border-radius: 11px;
      border: 1px solid rgba(148, 163, 184, .18);
      background: rgba(15, 23, 42, .64);
      color: #e2e8f0;
      padding: 8px 10px;
      font: inherit;
      font-size: 12px;
    }

    .finance-stat-strip {
      display: grid;
      grid-template-columns: repeat(5, minmax(120px, 1fr));
      gap: 1px;
      border: 1px solid rgba(148, 163, 184, .16);
      border-radius: 12px;
      overflow: hidden;
      background: rgba(30, 41, 59, .7);
      margin-bottom: 10px;
    }

    .finance-stat-card {
      text-align: center;
      padding: 8px 10px;
      background: rgba(15, 23, 42, .42);
    }

    .finance-stat-label {
      font-size: 11px;
      color: rgba(226, 232, 240, .58);
      margin-bottom: 3px;
    }

    .finance-stat-value {
      font-size: 19px;
      font-weight: 950;
      color: #f8fafc;
      font-variant-numeric: tabular-nums;
    }

    .finance-layout {
      display: grid;
      grid-template-columns: minmax(0, 1fr) 330px;
      gap: 12px;
      align-items: stretch;
    }

    .finance-data-panel,
    .finance-side-panel {
      border: 1px solid rgba(148, 163, 184, .16);
      border-radius: 12px;
      background: rgba(15, 23, 42, .70);
      box-shadow: 0 18px 48px rgba(0, 0, 0, .22);
      overflow: hidden;
    }

    .finance-scroll {
      max-height: calc(100vh - 260px);
      overflow: auto;
      border-radius: 12px;
      scrollbar-color: rgba(45, 212, 191, .42) rgba(15, 23, 42, .44);
    }

    .finance-table {
      width: max-content;
      min-width: 100%;
      border-collapse: collapse;
      font-size: 12px;
      table-layout: fixed;
    }

    .finance-table th {
      position: sticky;
      top: 0;
      z-index: 2;
      background: #0f8f90;
      color: white;
      padding: 8px 7px;
      border: 1px solid rgba(255, 255, 255, .18);
      font-weight: 900;
      text-align: center;
      user-select: none;
    }

    .finance-resize-handle {
      position: absolute;
      top: 0;
      right: -3px;
      width: 7px;
      height: 100%;
      cursor: col-resize;
      z-index: 5;
      touch-action: none;
    }

    .finance-resize-handle::after {
      content: "";
      position: absolute;
      top: 8px;
      bottom: 8px;
      left: 3px;
      width: 1px;
      border-radius: 999px;
      background: rgba(255, 255, 255, .36);
    }

    .finance-table td {
      padding: 7px;
      border: 1px solid rgba(148, 163, 184, .16);
      color: #dbeafe;
      background: rgba(15, 23, 42, .34);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .finance-table tbody tr:hover td {
      background: rgba(14, 165, 233, .12);
    }

    .finance-name-link {
      border: 0;
      padding: 0;
      background: transparent;
      color: #f8fafc;
      font: inherit;
      font-weight: 850;
      cursor: pointer;
      max-width: 100%;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
      display: inline-block;
      text-align: left;
    }

    .finance-name-link:hover,
    .finance-name-link:focus-visible {
      color: #38bdf8;
      text-decoration: underline;
    }

    .finance-table tr.is-paid td {
      background: rgba(34, 197, 94, .10);
    }

    .finance-table tr.is-finished td {
      background: rgba(239, 68, 68, .10);
    }

    .finance-group-row td {
      background: #22c55e !important;
      color: white;
      text-align: center;
      font-weight: 950;
      padding: 7px 10px;
    }

    .finance-side-panel {
      padding: 14px;
      display: grid;
      grid-template-rows: minmax(0, 1fr) minmax(150px, .58fr) auto;
      gap: 12px;
      min-height: calc(100vh - 260px);
      max-height: calc(100vh - 260px);
      background:
        radial-gradient(circle at 100% 0%, rgba(20, 184, 166, .08), transparent 30%),
        rgba(15, 23, 42, .76);
      overflow: hidden;
    }

    .finance-side-panel > div {
      min-height: 0;
      display: flex;
      flex-direction: column;
      overflow: hidden;
    }

    .finance-side-panel > div:last-child {
      display: block;
      flex: none;
      overflow: visible;
    }

    .finance-side-title {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 10px;
      font-size: 13px;
      font-weight: 900;
      color: #f8fafc;
      margin-bottom: 7px;
      padding-bottom: 6px;
      border-bottom: 2px solid rgba(45, 212, 191, .28);
    }

    .finance-side-scroll {
      flex: 1 1 auto;
      min-height: 0;
      overflow-y: auto;
      overflow-x: hidden;
      padding-right: 4px;
      scrollbar-width: thin;
      scrollbar-color: rgba(20, 184, 166, .58) rgba(15, 23, 42, .5);
    }

    .finance-side-scroll::-webkit-scrollbar { width: 7px; }
    .finance-side-scroll::-webkit-scrollbar-track { background: rgba(15, 23, 42, .5); border-radius: 999px; }
    .finance-side-scroll::-webkit-scrollbar-thumb { background: rgba(20, 184, 166, .58); border-radius: 999px; }

    .finance-side-plus {
      width: 28px;
      height: 28px;
      border: 0;
      border-radius: 8px;
      display: grid;
      place-items: center;
      background: linear-gradient(135deg, #14b8a6, #0f766e);
      color: white;
      font-size: 17px;
      font-weight: 950;
      box-shadow: 0 10px 22px rgba(20, 184, 166, .24);
      opacity: .92;
    }

    .finance-firm-card,
    .finance-expense-card {
      border: 1px solid rgba(45, 212, 191, .16);
      border-radius: 10px;
      background: linear-gradient(135deg, rgba(15, 23, 42, .88), rgba(30, 27, 75, .62));
      padding: 9px 10px;
      margin-bottom: 6px;
      cursor: pointer;
      box-shadow: inset 0 1px 0 rgba(255,255,255,.03), 0 8px 20px rgba(0,0,0,.16);
      transition: border-color .14s ease, transform .14s ease, background .14s ease;
    }

    .finance-firm-card:hover {
      transform: translateY(-1px);
      border-color: rgba(45, 212, 191, .45);
    }

    .finance-firm-card.is-selected {
      border-color: rgba(45, 212, 191, .7);
      background: rgba(20, 184, 166, .13);
    }

    .finance-firm-name {
      font-weight: 850;
      color: #f8fafc;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
      font-size: 12px;
      line-height: 1.25;
    }

    .finance-firm-meta,
    .finance-expense-meta {
      display: flex;
      justify-content: space-between;
      gap: 8px;
      margin-top: 4px;
      color: rgba(226, 232, 240, .62);
      font-size: 10.5px;
      line-height: 1.35;
    }

    .finance-firm-meta strong {
      flex: 0 0 auto;
      color: #14b8a6;
      font-weight: 950;
      font-variant-numeric: tabular-nums;
    }

    .finance-expense-card {
      cursor: default;
      border-color: rgba(168, 85, 247, .20);
      background: rgba(15, 23, 42, .78);
      position: relative;
      padding-right: 32px;
    }

    .finance-expense-remove {
      position: absolute;
      right: 9px;
      top: 50%;
      transform: translateY(-50%);
      color: rgba(226, 232, 240, .55);
      font-weight: 900;
    }

    .finance-expense-meta strong {
      color: #fbbf24;
      font-weight: 950;
      font-variant-numeric: tabular-nums;
    }

    .finance-summary-box {
      border-radius: 9px;
      padding: 10px 12px;
      margin-top: 6px;
      display: flex;
      justify-content: space-between;
      font-size: 12px;
      font-weight: 900;
      align-items: center;
      box-shadow: inset 0 1px 0 rgba(255,255,255,.06);
    }

    .finance-summary-box strong {
      font-size: 15px;
      font-variant-numeric: tabular-nums;
    }

    .finance-extra-stats {
      margin-top: 14px;
      padding-top: 10px;
      border-top: 1px solid rgba(148, 163, 184, .16);
      color: rgba(45, 212, 191, .78);
      font-size: 12px;
      font-weight: 850;
    }

    .finance-summary-toggle,
    .finance-summary-close {
      display: none;
    }

    .finance-side-backdrop {
      display: none;
    }

    body.detail-open,
    body.finance-summary-open,
    body.drawer-open,
    body.dashboard-movement-open,
    body.web-settings-open {
      overflow: hidden;
    }

    .report-app-head {
      display: grid;
      grid-template-columns: auto 1fr auto;
      align-items: center;
      gap: 14px;
      margin-bottom: 16px;
    }

    .report-title-center {
      text-align: center;
      color: #f8fafc;
    }

    .report-title-center h1 {
      margin: 0;
      font-size: 17px;
      font-weight: 900;
    }

    .report-title-center .report-range-pill {
      display: inline-flex;
      margin-top: 7px;
      border-radius: 999px;
      padding: 5px 10px;
      background: rgba(15, 23, 42, .72);
      color: rgba(226, 232, 240, .62);
      border: 1px solid rgba(148, 163, 184, .16);
      font-size: 11px;
      font-weight: 750;
    }

    .report-head-actions {
      display: flex;
      gap: 8px;
      justify-content: flex-end;
      flex-wrap: wrap;
    }

    .report-filter-panel {
      border: 1px solid rgba(148, 163, 184, .18);
      border-radius: 22px;
      padding: 18px;
      background: rgba(15, 23, 42, .64);
      box-shadow: 0 22px 60px rgba(0, 0, 0, .28);
      margin-bottom: 16px;
    }

    .report-filter-title {
      display: flex;
      align-items: center;
      gap: 10px;
      margin-bottom: 14px;
      color: #f8fafc;
      font-weight: 900;
    }

    .report-filter-grid {
      display: grid;
      grid-template-columns: minmax(230px, 1.1fr) minmax(210px, .9fr) minmax(150px, .55fr) minmax(150px, .55fr) auto;
      gap: 14px;
      align-items: stretch;
    }

    .report-filter-box {
      min-height: 250px;
      border: 1px solid rgba(148, 163, 184, .16);
      border-radius: 16px;
      padding: 12px;
      background: rgba(15, 23, 42, .54);
      overflow: hidden;
    }

    .report-filter-box.small {
      min-height: 92px;
    }

    .report-filter-box-title {
      margin-bottom: 8px;
      color: #f8fafc;
      font-size: 12px;
      font-weight: 850;
    }

    .report-check-list {
      display: grid;
      gap: 5px;
      max-height: 212px;
      overflow: auto;
      padding-right: 4px;
      color: rgba(226, 232, 240, .86);
      font-size: 11px;
    }

    .report-check-list label {
      display: grid;
      grid-template-columns: 16px 1fr;
      gap: 6px;
      align-items: center;
      min-width: 0;
    }

    .report-check-list input {
      width: 13px;
      height: 13px;
      margin: 0;
      accent-color: #14b8a6;
    }

    .report-date-input {
      width: 100%;
      height: 38px;
      border-radius: 12px;
      border: 1px solid rgba(148, 163, 184, .22);
      background: rgba(2, 6, 23, .36);
      color: #f8fafc;
      padding: 8px 10px;
      font: inherit;
      font-size: 12px;
    }

    .report-run-wrap {
      display: flex;
      align-items: end;
      justify-content: flex-end;
    }

    .report-mode-tabs {
      width: fit-content;
      margin: 10px auto 18px;
      display: flex;
      gap: 5px;
      padding: 6px;
      border-radius: 16px;
      background: rgba(15, 23, 42, .78);
      border: 1px solid rgba(148, 163, 184, .16);
      box-shadow: 0 14px 34px rgba(0, 0, 0, .24);
    }

    .report-mode-tab {
      border: 0;
      border-radius: 12px;
      padding: 9px 16px;
      background: transparent;
      color: rgba(226, 232, 240, .76);
      font: inherit;
      font-size: 12px;
      font-weight: 850;
      cursor: pointer;
    }

    .report-mode-tab.is-active {
      background: linear-gradient(135deg, #0f766e, #14b8a6);
      color: white;
    }

    .report-panel-title {
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 12px 14px;
      color: #f8fafc;
      font-size: 14px;
      font-weight: 900;
      border-bottom: 1px solid rgba(148, 163, 184, .16);
      background: rgba(15, 23, 42, .58);
    }

    .report-summary-panel,
    .report-groups-panel {
      border: 1px solid rgba(148, 163, 184, .16);
      border-radius: 20px;
      overflow: hidden;
      background: rgba(15, 23, 42, .62);
      box-shadow: 0 22px 60px rgba(0, 0, 0, .28);
    }

    .report-summary-panel.is-hidden,
    .report-groups-panel.is-hidden {
      display: none;
    }

    .report-firm-table {
      width: 100%;
      border-collapse: collapse;
      font-size: 12px;
    }

    .report-firm-table th,
    .report-firm-table td {
      padding: 12px 16px;
      border-bottom: 1px solid rgba(148, 163, 184, .16);
      text-align: left;
    }

    .report-firm-table th {
      color: rgba(226, 232, 240, .62);
      font-size: 11px;
      font-weight: 850;
    }

    .report-firm-table td:not(:first-child),
    .report-firm-table th:not(:first-child) {
      text-align: center;
      width: 120px;
    }

    .report-firm-table tbody tr:nth-child(even),
    .report-employee-table tbody tr:nth-child(even) {
      background: rgba(2, 6, 23, .48);
    }

    .report-group-card {
      margin: 12px;
      border: 1px solid rgba(148, 163, 184, .16);
      border-radius: 18px;
      overflow: hidden;
      background: rgba(15, 23, 42, .50);
    }

    .report-group-head {
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 12px 14px;
      color: #f8fafc;
      font-weight: 900;
      background: rgba(30, 41, 59, .44);
    }

    .report-group-count {
      color: #93c5fd;
      font-size: 11px;
      font-weight: 850;
    }

    .report-columns-overlay {
      position: fixed;
      inset: 0;
      z-index: 60;
      display: none;
      place-items: center;
      padding: 18px;
      background: rgba(2, 6, 23, .70);
      backdrop-filter: blur(5px);
    }

    .report-columns-overlay.is-open { display: grid; }

    .report-columns-dialog {
      width: min(560px, calc(100vw - 28px));
      max-height: min(720px, calc(100vh - 40px));
      display: grid;
      grid-template-rows: auto 1fr auto;
      gap: 14px;
      border: 1px solid rgba(148, 163, 184, .20);
      border-radius: 22px;
      padding: 20px;
      background: rgba(15, 23, 42, .96);
      color: #e2e8f0;
      box-shadow: 0 30px 80px rgba(0, 0, 0, .42);
    }

    .report-columns-head {
      display: flex;
      justify-content: space-between;
      gap: 14px;
      align-items: start;
    }

    .report-columns-head h3 {
      margin: 0;
      color: #f8fafc;
      font-size: 18px;
      font-weight: 900;
    }

    .report-columns-head p {
      margin: 4px 0 0;
      color: rgba(226, 232, 240, .58);
      font-size: 12px;
      line-height: 1.35;
    }

    .report-columns-list {
      display: grid;
      gap: 8px;
      overflow: auto;
      padding-right: 4px;
    }

    .report-column-item {
      display: grid;
      grid-template-columns: auto minmax(0, 1fr) auto auto;
      gap: 10px;
      align-items: center;
      border: 1px solid rgba(148, 163, 184, .14);
      border-radius: 14px;
      padding: 10px 11px;
      background: rgba(30, 41, 59, .58);
    }

    .report-column-item input {
      width: 14px;
      height: 14px;
      margin: 0;
      accent-color: #14b8a6;
    }

    .report-column-item strong {
      display: block;
      color: #f8fafc;
      font-size: 12px;
    }

    .report-column-item span {
      display: block;
      margin-top: 2px;
      color: rgba(226, 232, 240, .48);
      font-size: 10px;
    }

    .report-column-move {
      width: 28px;
      height: 28px;
      border: 1px solid rgba(148, 163, 184, .16);
      border-radius: 9px;
      background: rgba(15, 23, 42, .68);
      color: #93c5fd;
      cursor: pointer;
    }

    .report-columns-actions {
      display: flex;
      justify-content: space-between;
      gap: 10px;
      flex-wrap: wrap;
    }

    .web-settings-overlay {
      position: fixed;
      inset: 0;
      z-index: 58;
      display: none;
      place-items: center;
      padding: 18px;
      background: rgba(2, 6, 23, .68);
      backdrop-filter: blur(5px);
    }

    .web-settings-overlay.is-open { display: grid; }

    .web-settings-dialog {
      width: min(680px, calc(100vw - 28px));
      max-height: min(760px, calc(100vh - 40px));
      overflow: auto;
      border: 1px solid rgba(148, 163, 184, .20);
      border-radius: 22px;
      padding: 20px;
      background: rgba(15, 23, 42, .96);
      color: #e2e8f0;
      box-shadow: 0 30px 80px rgba(0, 0, 0, .42);
    }

    .web-settings-head {
      display: flex;
      justify-content: space-between;
      gap: 14px;
      align-items: start;
      margin-bottom: 16px;
    }

    .web-settings-head h3 {
      margin: 0;
      color: #f8fafc;
      font-size: 18px;
      font-weight: 900;
    }

    .web-settings-head p {
      margin: 4px 0 0;
      color: rgba(226, 232, 240, .58);
      font-size: 12px;
      line-height: 1.35;
    }

    .web-settings-grid {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: 12px;
    }

    .web-settings-card {
      border: 1px solid rgba(148, 163, 184, .16);
      border-radius: 16px;
      padding: 14px;
      background: rgba(30, 41, 59, .52);
    }

    .web-settings-card.full { grid-column: 1 / -1; }

    .web-settings-card label {
      display: block;
      margin-bottom: 8px;
      color: rgba(226, 232, 240, .70);
      font-size: 11px;
      font-weight: 850;
    }

    .web-settings-card select,
    .web-settings-card input[type="range"] {
      width: 100%;
    }

    .web-settings-note {
      margin-top: 8px;
      color: rgba(226, 232, 240, .52);
      font-size: 11px;
      line-height: 1.45;
    }

    .web-settings-status {
      display: grid;
      grid-template-columns: repeat(3, minmax(0, 1fr));
      gap: 8px;
    }

    .web-settings-status span {
      border-radius: 12px;
      padding: 9px 10px;
      background: rgba(15, 23, 42, .66);
      color: rgba(226, 232, 240, .78);
      font-size: 11px;
      font-weight: 750;
    }

    body.theme-light .web-settings-dialog {
      background: rgba(255, 255, 255, .96);
      color: #0f172a;
    }

    body.theme-light .web-settings-head h3,
    body.theme-light .web-settings-card label {
      color: #0f172a;
    }

    body.theme-light .web-settings-card,
    body.theme-light .web-settings-status span {
      background: rgba(241, 245, 249, .82);
      color: #0f172a;
    }

    .report-summary-grid {
      display: grid;
      grid-template-columns: repeat(4, minmax(0, 1fr));
      gap: 10px;
      margin-bottom: 12px;
    }

    .report-card {
      border: 1px solid rgba(148, 163, 184, .16);
      border-radius: 15px;
      padding: 13px 14px;
      background: linear-gradient(180deg, rgba(30, 41, 59, .66), rgba(15, 23, 42, .70));
      box-shadow: 0 16px 38px rgba(0, 0, 0, .22);
    }

    .report-card-value {
      color: #f8fafc;
      font-size: 25px;
      line-height: 1;
      font-weight: 900;
    }

    .report-card-label {
      margin-top: 6px;
      color: rgba(226, 232, 240, .58);
      font-size: 11px;
      font-weight: 750;
    }

    .report-toolbar {
      display: grid;
      grid-template-columns: minmax(240px, 1fr) auto auto auto auto;
      gap: 8px;
      align-items: center;
      margin-bottom: 12px;
      padding: 10px;
      border: 1px solid rgba(148, 163, 184, .14);
      border-radius: 16px;
      background: rgba(15, 23, 42, .58);
    }

    .report-search {
      height: 38px;
      border-radius: 13px;
      border-color: rgba(148, 163, 184, .18);
      background: rgba(2, 6, 23, .30);
      color: #f8fafc;
      font-size: 12px;
    }

    .report-table-wrap {
      max-height: calc(100vh - 290px);
      min-height: 360px;
      overflow: auto;
      border: 1px solid rgba(148, 163, 184, .16);
      border-radius: 18px;
      background: rgba(15, 23, 42, .70);
      box-shadow: 0 22px 60px rgba(0, 0, 0, .28);
    }

    .report-table {
      width: 100%;
      min-width: 1560px;
      border-collapse: separate;
      border-spacing: 0;
      font-size: 12px;
    }

    .report-employee-table {
      min-width: 1120px;
    }

    .report-table th {
      position: sticky;
      top: 0;
      z-index: 2;
      padding: 11px 10px;
      background: rgba(15, 23, 42, .96);
      color: rgba(226, 232, 240, .72);
      border-bottom: 1px solid rgba(148, 163, 184, .18);
      text-align: left;
      white-space: nowrap;
      font-weight: 850;
    }

    .report-table td {
      padding: 10px;
      border-bottom: 1px solid rgba(148, 163, 184, .10);
      color: rgba(248, 250, 252, .88);
      vertical-align: top;
      line-height: 1.35;
    }

    .report-table tr {
      cursor: pointer;
    }

    .report-table tbody tr:hover {
      background: rgba(37, 99, 235, .18);
    }

    .report-name-cell {
      color: #f8fafc;
      font-weight: 900;
      min-width: 180px;
    }

    .report-muted {
      color: rgba(226, 232, 240, .50);
      font-size: 11px;
    }

    .report-chip {
      display: inline-flex;
      align-items: center;
      border-radius: 999px;
      padding: 4px 8px;
      background: rgba(96, 165, 250, .14);
      color: #bfdbfe;
      font-size: 10px;
      font-weight: 850;
      white-space: nowrap;
    }

    .report-chip.archived {
      background: rgba(148, 163, 184, .14);
      color: #cbd5e1;
    }

    .problem-summary-grid {
      display: grid;
      grid-template-columns: repeat(4, minmax(0, 1fr));
      gap: 10px;
      margin-bottom: 14px;
    }

    .problem-summary-card {
      border: 1px solid rgba(148, 163, 184, .16);
      border-radius: 15px;
      padding: 13px 14px;
      background: linear-gradient(180deg, rgba(30, 41, 59, .66), rgba(15, 23, 42, .68));
      box-shadow: 0 16px 38px rgba(0, 0, 0, .22);
    }

    .problem-summary-value {
      font-size: 26px;
      font-weight: 900;
      color: #f8fafc;
      line-height: 1;
    }

    .problem-summary-label {
      margin-top: 6px;
      color: rgba(226, 232, 240, .58);
      font-size: 11px;
      font-weight: 750;
    }

    .problem-toolbar {
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
      align-items: center;
      margin-bottom: 16px;
      padding: 10px;
      border: 1px solid rgba(148, 163, 184, .14);
      border-radius: 16px;
      background: rgba(15, 23, 42, .58);
    }

    .problem-list {
      display: grid;
      gap: 10px;
    }

    .problem-card {
      display: grid;
      grid-template-columns: auto minmax(0, 1fr) auto;
      gap: 12px;
      align-items: center;
      border: 1px solid rgba(148, 163, 184, .16);
      border-radius: 16px;
      padding: 12px;
      background: linear-gradient(180deg, rgba(30, 41, 59, .62), rgba(15, 23, 42, .70));
      box-shadow: inset 0 1px 0 rgba(255, 255, 255, .035);
      cursor: pointer;
    }

    .problem-card:hover {
      border-color: rgba(251, 146, 60, .46);
      background: linear-gradient(180deg, rgba(67, 56, 202, .20), rgba(15, 23, 42, .76));
    }

    .problem-avatar {
      width: 46px;
      height: 46px;
      border-radius: 14px;
      display: grid;
      place-items: center;
      overflow: hidden;
      background: linear-gradient(135deg, #fed7aa, #fb7185);
      color: #0f172a;
      font-weight: 900;
      border: 1px solid rgba(255, 255, 255, .55);
    }

    .problem-avatar img {
      width: 100%;
      height: 100%;
      object-fit: cover;
    }

    .problem-name {
      color: #f8fafc;
      font-size: 14px;
      font-weight: 900;
      line-height: 1.2;
    }

    .problem-meta {
      margin-top: 4px;
      color: rgba(226, 232, 240, .56);
      font-size: 11px;
      line-height: 1.35;
    }

    .problem-reasons {
      display: flex;
      gap: 6px;
      flex-wrap: wrap;
      justify-content: flex-end;
      max-width: 430px;
    }

    .problem-reason {
      border-radius: 999px;
      padding: 5px 8px;
      font-size: 10px;
      font-weight: 850;
      white-space: nowrap;
    }

    .problem-reason.missing { background: rgba(244, 63, 94, .16); color: #fda4af; }
    .problem-reason.expired { background: rgba(239, 68, 68, .22); color: #fecaca; }
    .problem-reason.warn { background: rgba(245, 158, 11, .18); color: #fcd34d; }

    .employees-page-header {
      display: grid;
      grid-template-columns: 1fr auto;
      gap: 18px;
      align-items: start;
      margin-bottom: 14px;
    }

    .employee-company-title {
      display: flex;
      align-items: center;
      gap: 12px;
      color: #f8fafc;
    }

    .employee-company-avatar {
      width: 42px;
      height: 42px;
      border-radius: 14px;
      display: grid;
      place-items: center;
      background: linear-gradient(135deg, #14b8a6, #2563eb);
      color: white;
      font-weight: 900;
      box-shadow: 0 12px 28px rgba(20, 184, 166, .25);
    }

    .employee-company-title h1 {
      margin: 0;
      font-size: clamp(22px, 2.4vw, 32px);
      letter-spacing: -.04em;
    }

    .employee-company-title p {
      margin: 4px 0 0;
      color: rgba(226, 232, 240, .58);
      font-size: 12px;
    }

    .employee-actions {
      display: flex;
      justify-content: flex-end;
      gap: 8px;
      flex-wrap: wrap;
    }

    .employee-action {
      border: 1px solid rgba(148, 163, 184, .20);
      border-radius: 12px;
      padding: 9px 12px;
      background: rgba(15, 23, 42, .70);
      color: #e2e8f0;
      font: inherit;
      font-size: 12px;
      font-weight: 750;
      cursor: pointer;
      transition: transform .14s ease, border-color .14s ease, background .14s ease, box-shadow .14s ease;
    }

    .employee-action:hover,
    .employee-action:focus-visible {
      transform: translateY(-1px);
      border-color: rgba(96, 165, 250, .46);
      background: rgba(30, 41, 59, .86);
      box-shadow: 0 10px 24px rgba(15, 23, 42, .20);
    }

    .employee-action.primary {
      border-color: rgba(45, 212, 191, .42);
      background: linear-gradient(135deg, #0891b2, #14b8a6);
      color: white;
    }

    .employee-action.primary:hover,
    .employee-action.primary:focus-visible {
      border-color: rgba(153, 246, 228, .70);
      background: linear-gradient(135deg, #0284c7, #14b8a6);
    }

    .employee-stats {
      display: grid;
      grid-template-columns: repeat(4, minmax(0, 1fr));
      gap: 10px;
      margin-bottom: 10px;
    }

    .employee-stat-card {
      border: 1px solid rgba(148, 163, 184, .18);
      border-radius: 14px;
      padding: 12px 14px;
      background: rgba(15, 23, 42, .66);
      box-shadow: 0 16px 36px rgba(0, 0, 0, .22);
    }

    .employee-stat-card.is-active {
      border-color: rgba(45, 212, 191, .42);
      background: rgba(15, 118, 110, .24);
    }

    .employee-stat-value {
      color: #f8fafc;
      font-size: 24px;
      font-weight: 900;
      line-height: 1;
    }

    .employee-stat-label {
      margin-top: 5px;
      color: rgba(226, 232, 240, .58);
      font-size: 11px;
      font-weight: 650;
    }

    .employee-filter-bar {
      display: grid;
      grid-template-columns: minmax(260px, 1fr) auto auto auto auto;
      gap: 8px;
      align-items: center;
      margin-bottom: 18px;
      padding: 10px;
      border: 1px solid rgba(148, 163, 184, .14);
      border-radius: 16px;
      background: rgba(15, 23, 42, .58);
      box-shadow: inset 0 1px 0 rgba(255, 255, 255, .04);
    }

    .employees-search {
      height: 38px;
      border-radius: 12px;
      border-color: rgba(148, 163, 184, .18);
      background: rgba(2, 6, 23, .32);
      color: #f8fafc;
      font-size: 12px;
    }

    .filter-chip {
      border: 1px solid rgba(148, 163, 184, .18);
      border-radius: 999px;
      padding: 8px 10px;
      background: rgba(30, 41, 59, .54);
      color: rgba(226, 232, 240, .82);
      font-size: 11px;
      font-weight: 750;
      white-space: nowrap;
      cursor: pointer;
      transition: border-color .14s ease, background .14s ease, color .14s ease, transform .14s ease;
    }

    .filter-chip:hover,
    .filter-chip:focus-visible {
      transform: translateY(-1px);
      border-color: rgba(96, 165, 250, .45);
      background: rgba(30, 64, 175, .26);
      color: #e0f2fe;
    }

    .filter-chip.is-active {
      border-color: rgba(45, 212, 191, .48);
      background: rgba(20, 184, 166, .20);
      color: #99f6e4;
    }

    .employee-card-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(255px, 1fr));
      gap: 14px;
    }

    .employee-profile-card {
      position: relative;
      min-height: 132px;
      border: 1px solid rgba(148, 163, 184, .18);
      border-radius: 16px;
      padding: 13px;
      background: linear-gradient(180deg, rgba(30, 41, 59, .74), rgba(15, 23, 42, .76));
      color: #e2e8f0;
      box-shadow: 0 18px 40px rgba(0, 0, 0, .24), inset 0 1px 0 rgba(255, 255, 255, .04);
      transition: transform .15s ease, border-color .15s ease, background .15s ease, box-shadow .15s ease;
    }

    .employee-profile-card:hover {
      transform: translateY(-2px);
      border-color: rgba(96, 165, 250, .46);
      background: linear-gradient(180deg, rgba(30, 64, 175, .40), rgba(15, 23, 42, .80));
      box-shadow: 0 22px 52px rgba(15, 23, 42, .30), inset 0 1px 0 rgba(255, 255, 255, .05);
    }

    .employee-card-top {
      display: grid;
      grid-template-columns: auto 1fr;
      gap: 10px;
      align-items: start;
    }

    .employee-avatar {
      width: 42px;
      height: 42px;
      border-radius: 50%;
      display: grid;
      place-items: center;
      background: linear-gradient(135deg, #c4b5fd, #60a5fa);
      color: #0f172a;
      font-size: 13px;
      font-weight: 900;
      border: 2px solid rgba(255, 255, 255, .70);
      box-shadow: 0 10px 24px rgba(15, 23, 42, .28);
      overflow: hidden;
    }

    .employee-avatar img {
      width: 100%;
      height: 100%;
      object-fit: cover;
    }

    .employee-card-name {
      color: #f8fafc;
      font-size: 13px;
      font-weight: 900;
      line-height: 1.2;
      margin-top: 1px;
    }

    .employee-card-position {
      margin-top: 3px;
      color: rgba(226, 232, 240, .58);
      font-size: 10px;
      line-height: 1.3;
    }

    .employee-card-meta {
      display: grid;
      gap: 4px;
      margin-top: 10px;
      color: rgba(226, 232, 240, .72);
      font-size: 10px;
    }

    .employee-card-badges {
      display: flex;
      gap: 5px;
      flex-wrap: wrap;
      margin-top: 10px;
    }

    .doc-badge {
      border-radius: 999px;
      padding: 4px 7px;
      font-size: 10px;
      font-weight: 800;
    }

    .doc-badge.ok { background: rgba(16, 185, 129, .16); color: #6ee7b7; }
    .doc-badge.warn { background: rgba(245, 158, 11, .18); color: #fcd34d; }
    .doc-badge.expired { background: rgba(239, 68, 68, .20); color: #fca5a5; }
    .doc-badge.missing { background: rgba(244, 63, 94, .16); color: #fda4af; }
    .doc-badge.info { background: rgba(96, 165, 250, .16); color: #bfdbfe; }

    .employee-card-actions {
      position: absolute;
      right: 10px;
      bottom: 9px;
      display: flex;
      gap: 6px;
    }

    .mini-action {
      width: 25px;
      height: 25px;
      border: 1px solid rgba(216, 180, 254, .22);
      border-radius: 8px;
      background: rgba(88, 28, 135, .42);
      color: #e9d5ff;
      display: grid;
      place-items: center;
      font-size: 11px;
      font-weight: 800;
    }

    .employee-detail-overlay {
      position: fixed;
      inset: 0;
      z-index: 45;
      display: none;
      align-items: center;
      justify-content: center;
      padding: 10px;
      background: rgba(3, 7, 22, .78);
      backdrop-filter: blur(6px);
    }

    .employee-detail-overlay.is-open { display: flex; }

    .ep-shell {
      width: min(1160px, calc(100vw - 22px));
      height: calc(100vh - 20px);
      max-height: calc(100vh - 20px);
      display: grid;
      grid-template-columns: 278px minmax(0, 1fr);
      gap: 0;
      overflow: hidden;
      border-radius: 18px;
      border: 1px solid rgba(148, 163, 184, .18);
      box-shadow: 0 26px 80px rgba(0, 0, 0, .48);
      background: linear-gradient(165deg, #0b1226 0%, #0f172a 52%, #0c1328 100%);
      color: #e2e8f0;
    }

    .ep-side {
      display: flex;
      flex-direction: column;
      gap: 10px;
      padding: 13px;
      border-right: 1px solid rgba(148, 163, 184, .14);
      background:
        linear-gradient(180deg, rgba(17, 24, 39, .88), rgba(10, 16, 32, .92)),
        rgba(15, 23, 42, .65);
      min-height: 0;
      overflow-y: auto;
    }

    .ep-side-minihead {
      display: grid;
      grid-template-columns: 40px 40px minmax(0, 1fr);
      align-items: center;
      gap: 10px;
      margin-bottom: 4px;
      padding-bottom: 8px;
      border-bottom: 1px solid rgba(148, 163, 184, .10);
    }

    .ep-side-back,
    .ep-side-usericon {
      width: 40px;
      height: 40px;
      border-radius: 12px;
      display: grid;
      place-items: center;
      border: 1px solid rgba(148, 163, 184, .22);
      background: rgba(15, 23, 42, .60);
      color: #e2e8f0;
      font: inherit;
      font-size: 20px;
    }

    .ep-side-back {
      cursor: pointer;
    }

    .ep-side-usericon {
      background: linear-gradient(135deg, #0f766e, #14b8a6);
      color: white;
      border-color: rgba(45, 212, 191, .30);
    }

    .ep-side-mini-title {
      min-width: 0;
    }

    .ep-side-mini-title strong {
      display: block;
      color: #f8fafc;
      font-size: 13px;
      line-height: 1.2;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .ep-side-mini-title span {
      display: block;
      color: rgba(226, 232, 240, .52);
      font-size: 11px;
      margin-top: 3px;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .ep-side-photo-wrap {
      width: 100%;
      max-height: min(238px, 30vh);
      aspect-ratio: 1;
      border-radius: 15px;
      overflow: hidden;
      border: 1px solid rgba(148, 163, 184, .20);
      background: rgba(7, 12, 25, .78);
      display: grid;
      place-items: center;
      padding: 10px;
      font-size: 36px;
      font-weight: 900;
      color: #94a3b8;
      box-shadow: inset 0 1px 0 rgba(255, 255, 255, .04), 0 12px 28px rgba(0, 0, 0, .22);
    }

    .ep-side-photo-wrap img {
      width: 100%;
      height: 100%;
      object-fit: contain;
      display: block;
      border-radius: 4px;
      background: rgba(255, 255, 255, .04);
    }

    .ep-side-name {
      margin: 0;
      font-size: 17px;
      font-weight: 800;
      color: #f8fafc;
      letter-spacing: -.02em;
      line-height: 1.2;
      padding-top: 2px;
    }

    .ep-side-sub {
      margin: 3px 0 0;
      font-size: 11px;
      color: rgba(226, 232, 240, .62);
      line-height: 1.35;
      min-height: 30px;
    }

    .ep-side-dates {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: 8px;
      margin-top: 4px;
      padding-top: 10px;
      border-top: 1px solid rgba(148, 163, 184, .14);
    }

    .ep-date-pill {
      border-radius: 10px;
      padding: 8px 9px;
      border: 1px solid rgba(148, 163, 184, .16);
      background: rgba(2, 6, 23, .42);
      font-size: 11px;
    }

    .ep-date-pill span { display: block; color: rgba(226, 232, 240, .48); font-size: 9px; font-weight: 700; margin-bottom: 4px; }

    .ep-side-btn {
      width: 100%;
      border-radius: 11px;
      min-height: 40px;
      padding: 8px 10px;
      font: inherit;
      font-size: 12px;
      font-weight: 750;
      cursor: not-allowed;
      opacity: .52;
      border: 1px solid rgba(148, 163, 184, .12);
      color: rgba(226, 232, 240, .78);
      background: rgba(30, 41, 59, .45);
      box-shadow: inset 0 1px 0 rgba(255, 255, 255, .03);
    }

    .ep-side-btn-primary {
      opacity: .68;
      border-color: rgba(45, 212, 191, .35);
      background: linear-gradient(135deg, rgba(13, 148, 136, .55), rgba(20, 184, 166, .35));
      color: #ecfeff;
    }

    .ep-side-note {
      font-size: 10px;
      color: rgba(226, 232, 240, .45);
      text-align: center;
      margin-top: 4px;
      line-height: 1.35;
    }

    .ep-main {
      display: grid;
      grid-template-rows: auto auto minmax(0, 1fr);
      min-height: 0;
      height: 100%;
      overflow: hidden;
      background:
        radial-gradient(circle at 72% 8%, rgba(45, 212, 191, .06), transparent 18rem),
        rgba(8, 12, 24, .35);
    }

    .ep-topbar {
      display: flex;
      align-items: flex-start;
      justify-content: space-between;
      gap: 12px;
      padding: 16px 18px 10px;
      border-bottom: 1px solid rgba(148, 163, 184, .10);
    }

    .ep-topbar-head { min-width: 0; flex: 1; display: grid; gap: 4px; }

    .ep-topbar-head h2 {
      margin: 0;
      font-size: 21px;
      font-weight: 800;
      color: #f8fafc;
      letter-spacing: -.03em;
    }

    .ep-firm-line {
      margin: 0;
      font-size: 12px;
      color: rgba(226, 232, 240, .55);
    }

    .ep-status-pill {
      display: inline-flex;
      align-items: center;
      margin-top: 2px;
      width: fit-content;
      border-radius: 999px;
      padding: 5px 10px;
      font-size: 11px;
      font-weight: 800;
    }

    .ep-status-active { background: rgba(34, 197, 94, .16); color: #bbf7d0; border: 1px solid rgba(34, 197, 94, .32); }

    .ep-status-onleave { background: rgba(245, 158, 11, .16); color: #fde68a; border: 1px solid rgba(245, 158, 11, .32); }

    .ep-status-dismissed { background: rgba(148, 163, 184, .14); color: #e2e8f0; border: 1px solid rgba(148, 163, 184, .22); }

    .ep-status-awaiting { background: rgba(249, 115, 22, .16); color: #fed7aa; border: 1px solid rgba(249, 115, 22, .32); }

    .ep-close {
      flex-shrink: 0;
      width: 38px;
      height: 38px;
      border-radius: 12px;
      border: 1px solid rgba(148, 163, 184, .22);
      background: rgba(30, 41, 59, .55);
      color: #e2e8f0;
      font-size: 20px;
      line-height: 1;
      cursor: pointer;
      transition: background .15s ease, transform .15s ease;
    }

    .ep-close:hover { background: rgba(51, 65, 85, .72); transform: translateY(-1px); }

    .ep-tabs-bar {
      display: flex;
      flex-wrap: wrap;
      gap: 6px;
      padding: 9px 18px 12px;
      border-bottom: 1px solid rgba(148, 163, 184, .10);
      background: rgba(2, 6, 23, .12);
    }

    .ep-tab {
      border-radius: 12px;
      padding: 8px 14px;
      font: inherit;
      font-size: 12px;
      font-weight: 700;
      cursor: pointer;
      border: 1px solid transparent;
      background: transparent;
      color: rgba(226, 232, 240, .55);
    }

    .ep-tab:hover { color: #e2e8f0; background: rgba(51, 65, 85, .35); }

    .ep-tab.is-active {
      color: #0f766e;
      background: rgba(45, 212, 191, .18);
      border-color: rgba(45, 212, 191, .38);
      font-weight: 800;
    }

    .ep-tab-panels {
      min-height: 0;
      height: 100%;
      overflow: hidden;
      background: rgba(2, 6, 23, .10);
    }

    .ep-tab-panel {
      display: none;
      height: 100%;
      min-height: 0;
      overflow-y: auto;
      padding: 18px 20px 40px;
      scrollbar-gutter: stable;
    }

    .ep-tab-panel.is-active { display: block; }

    .ep-muted { color: rgba(226, 232, 240, .5); font-size: 13px; line-height: 1.45; }

    .ep-panel-title {
      margin: 0 0 14px;
      display: flex;
      align-items: baseline;
      justify-content: space-between;
      gap: 12px;
      color: #f8fafc;
    }

    .ep-panel-title h3 {
      margin: 0;
      font-size: 17px;
      font-weight: 900;
      letter-spacing: -.02em;
    }

    .ep-panel-title span {
      color: rgba(226, 232, 240, .48);
      font-size: 12px;
      font-weight: 650;
    }

    .ep-doc-grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(240px, 1fr));
      gap: 14px;
      align-items: start;
      padding-bottom: 12px;
    }

    .ep-doc-card {
      border-radius: 14px;
      border: 1px solid rgba(148, 163, 184, .16);
      background: linear-gradient(180deg, rgba(15, 23, 42, .70), rgba(10, 16, 32, .68));
      padding: 12px;
      min-height: 230px;
      display: grid;
      grid-template-rows: auto auto minmax(130px, 1fr) auto;
      gap: 7px;
      box-shadow: inset 0 1px 0 rgba(255, 255, 255, .035);
    }

    .ep-doc-preview {
      height: 150px;
      min-height: 150px;
      border-radius: 10px;
      border: 1px dashed rgba(148, 163, 184, .22);
      display: grid;
      place-items: center;
      color: rgba(226, 232, 240, .45);
      font-size: 12px;
      margin-top: 0;
      background: rgba(2, 6, 23, .28);
      padding: 12px;
      text-align: center;
      line-height: 1.35;
      overflow: hidden;
    }

    .ep-doc-preview.has-file {
      display: block;
      padding: 0;
      border-style: solid;
      background: rgba(2, 6, 23, .44);
    }

    .ep-doc-preview img,
    .ep-doc-preview iframe {
      width: 100%;
      height: 100%;
      min-height: 150px;
      display: block;
      border: 0;
      object-fit: contain;
      background: rgba(2, 6, 23, .50);
    }

    .ep-doc-actions {
      display: flex;
      gap: 6px;
      margin-top: 0;
    }

    .ep-doc-action {
      border: 1px solid rgba(148, 163, 184, .18);
      border-radius: 8px;
      padding: 6px 8px;
      background: rgba(15, 23, 42, .56);
      color: rgba(226, 232, 240, .82);
      font-size: 11px;
      font-weight: 750;
      text-decoration: none;
    }

    .ep-edit-banner {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 10px;
      margin-bottom: 16px;
      padding: 10px 12px;
      border: 1px solid rgba(148, 163, 184, .12);
      border-radius: 14px;
      background: rgba(15, 23, 42, .42);
    }

    .ep-btn-disabled {
      border-radius: 11px;
      padding: 8px 14px;
      font: inherit;
      font-size: 12px;
      font-weight: 750;
      border: none;
      cursor: not-allowed;
      opacity: .55;
      background: rgba(45, 212, 191, .22);
      color: #ccfbf1;
    }

    .ep-anketa-grid {
      display: grid;
      grid-template-columns: minmax(0, 1fr) 18px minmax(0, 1fr);
      gap: 0;
      align-items: start;
    }

    .ep-col { min-width: 0; display: grid; gap: 0; }

    .ep-section-head {
      display: flex;
      align-items: center;
      gap: 10px;
      margin: 2px 0 8px;
    }

    .ep-icon-badge {
      width: 32px;
      height: 32px;
      border-radius: 10px;
      display: grid;
      place-items: center;
      font-size: 14px;
      font-weight: 800;
    }

    .ep-icon-teal { background: rgba(20, 184, 166, .18); color: #5eead4; }

    .ep-icon-purple { background: rgba(168, 85, 247, .16); color: #d8b4fe; }

    .ep-icon-sky { background: rgba(56, 189, 248, .16); color: #bae6fd; }

    .ep-icon-amber { background: rgba(245, 158, 11, .16); color: #fcd34d; }

    .ep-icon-rose { background: rgba(251, 113, 133, .16); color: #fecdd3; }

    .ep-section-title {
      margin: 0;
      font-size: 13px;
      font-weight: 800;
      color: #f8fafc;
    }

    .ep-card {
      border-radius: 12px;
      border: 1px solid rgba(148, 163, 184, .16);
      background: linear-gradient(180deg, rgba(15, 23, 42, .62), rgba(10, 16, 32, .58));
      padding: 14px;
      margin-bottom: 16px;
      box-shadow: inset 0 1px 0 rgba(255, 255, 255, .03);
    }

    .ep-field-label {
      margin: 0 0 5px;
      font-size: 11px;
      font-weight: 650;
      color: rgba(226, 232, 240, .62);
      display: block;
    }

    .ep-field-input {
      width: 100%;
      border-radius: 10px;
      border: 1px solid rgba(148, 163, 184, .18);
      background: rgba(2, 6, 23, .5);
      color: #f1f5f9;
      font: inherit;
      font-size: 13px;
      padding: 9px 12px;
      margin-bottom: 10px;
      pointer-events: none;
      outline: none;
      min-height: 38px;
    }

    .ep-field-row2 {
      display: grid;
      grid-template-columns: minmax(0, 1fr) 112px;
      gap: 10px;
      align-items: end;
      margin-bottom: 10px;
    }

    .ep-field-row2 .ep-field-input { margin-bottom: 0; }

    .ep-empty-card {
      border-radius: 14px;
      border: 1px dashed rgba(148, 163, 184, .28);
      padding: 36px;
      text-align: center;
      background: rgba(15, 23, 42, .35);
    }

    .ep-history-list { display: grid; gap: 10px; }

    .ep-history-row {
      display: grid;
      grid-template-columns: 10px minmax(0, 1fr);
      gap: 12px;
      align-items: start;
      border-radius: 12px;
      border: 1px solid rgba(148, 163, 184, .14);
      padding: 12px 13px;
      background: linear-gradient(180deg, rgba(30, 41, 59, .45), rgba(15, 23, 42, .48));
      box-shadow: inset 0 1px 0 rgba(255, 255, 255, .03);
    }

    .ep-history-dot { width: 10px; height: 10px; margin-top: 5px; border-radius: 50%; background: #2dd4bf; }

    .ep-history-meta {
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
      align-items: center;
      margin-bottom: 6px;
      color: rgba(226, 232, 240, .55);
      font-size: 11px;
    }

    .ep-history-text {
      color: #f8fafc;
      font-size: 13px;
      font-weight: 750;
      line-height: 1.4;
    }

    .ep-history-actor {
      border-radius: 999px;
      padding: 3px 8px;
      background: rgba(236, 253, 245, .92);
      color: #047857;
      font-weight: 800;
    }

    .ep-salary-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(240px, 1fr));
      gap: 12px;
      margin-top: 12px;
    }

    .ep-pay-title {
      margin: 0 0 16px;
      color: #0f8f90;
      font-size: 18px;
      font-weight: 950;
      letter-spacing: -.02em;
    }

    .ep-pay-summary {
      border: 1px solid rgba(99, 102, 241, .30);
      border-radius: 15px;
      background: linear-gradient(135deg, rgba(30, 41, 75, .54), rgba(15, 23, 42, .58));
      box-shadow: inset 0 1px 0 rgba(255,255,255,.035);
      padding: 18px 18px;
      display: grid;
      grid-template-columns: repeat(3, minmax(0, 1fr));
      gap: 12px;
      margin-bottom: 26px;
    }

    .ep-pay-summary-item {
      text-align: center;
      min-width: 0;
    }

    .ep-pay-summary-icon {
      display: block;
      color: #0f8f90;
      font-size: 18px;
      line-height: 1;
      margin-bottom: 8px;
      opacity: .95;
    }

    .ep-pay-summary-value {
      color: #f8fafc;
      font-size: 24px;
      font-weight: 950;
      line-height: 1.1;
      letter-spacing: -.03em;
    }

    .ep-pay-summary-label {
      margin-top: 5px;
      color: rgba(226, 232, 240, .50);
      font-size: 12px;
      font-weight: 750;
    }

    .ep-pay-list {
      display: grid;
      gap: 13px;
    }

    .ep-pay-card {
      position: relative;
      overflow: hidden;
      border: 1px solid rgba(99, 102, 241, .28);
      border-left: 5px solid #22c55e;
      border-radius: 14px;
      background: linear-gradient(135deg, rgba(30, 41, 75, .50), rgba(15, 23, 42, .64));
      box-shadow: inset 0 1px 0 rgba(255,255,255,.035);
      padding: 18px 18px 17px 20px;
    }

    .ep-pay-card-head {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      gap: 12px;
      margin-bottom: 12px;
    }

    .ep-pay-month {
      display: flex;
      align-items: center;
      gap: 8px;
      color: #f8fafc;
      font-size: 19px;
      font-weight: 950;
      letter-spacing: -.02em;
    }

    .ep-pay-firm {
      border-radius: 7px;
      padding: 6px 10px;
      background: rgba(236, 253, 245, .92);
      color: #0f766e;
      font-size: 12px;
      font-weight: 900;
      white-space: nowrap;
      max-width: 260px;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .ep-pay-grid {
      display: grid;
      grid-template-columns: 1fr 1fr 1fr 1fr 1.1fr;
      gap: 14px;
      align-items: end;
    }

    .ep-pay-label {
      display: block;
      margin-bottom: 5px;
      color: rgba(226, 232, 240, .46);
      font-size: 11px;
      font-weight: 800;
    }

    .ep-pay-value {
      color: #f8fafc;
      font-size: 14px;
      font-weight: 900;
    }

    .ep-pay-value-accent {
      color: #22c55e;
      font-size: 18px;
      font-weight: 950;
    }

    .ep-pay-value-advance {
      color: #fb7185;
    }

    .ep-pay-note {
      margin-top: 12px;
      color: rgba(226, 232, 240, .72);
      font-size: 13px;
      font-style: italic;
      font-weight: 650;
    }

    @media (max-width: 920px) {
      .employee-detail-overlay {
        padding: 0;
        align-items: stretch;
        justify-content: stretch;
      }
      .ep-shell {
        width: 100vw;
        height: 100dvh;
        max-height: 100dvh;
        grid-template-columns: 1fr;
        border-radius: 0;
        border: 0;
        overflow: hidden;
      }
      .ep-side {
        display: none;
      }
      .ep-main {
        min-width: 0;
        grid-template-rows: auto auto minmax(0, 1fr);
      }
      .ep-topbar {
        padding: calc(10px + env(safe-area-inset-top)) 12px 10px;
        align-items: center;
      }
      .ep-topbar-head h2 {
        font-size: 19px;
      }
      .ep-close {
        width: 40px;
        height: 40px;
      }
      .ep-tabs-bar {
        flex-wrap: nowrap;
        overflow-x: auto;
        padding: 9px 12px 10px;
        -webkit-overflow-scrolling: touch;
      }
      .ep-tab {
        flex: 0 0 auto;
        white-space: nowrap;
        padding: 8px 12px;
      }
      .ep-tab-panel {
        padding: 14px 12px calc(82px + env(safe-area-inset-bottom));
      }
      .ep-anketa-grid { grid-template-columns: 1fr; }
      .ep-gutter-col { display: none; }
      .ep-pay-summary { grid-template-columns: 1fr; }
      .ep-pay-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
      .ep-pay-card-head { flex-direction: column; }
      .ep-pay-firm { max-width: 100%; }
    }

    @media (max-width: 520px) {
      .ep-panel-title {
        align-items: flex-start;
        flex-direction: column;
        gap: 4px;
      }
      .ep-card {
        padding: 12px;
        margin-bottom: 12px;
      }
      .ep-field-row2 {
        grid-template-columns: 1fr;
      }
      .ep-doc-grid {
        grid-template-columns: 1fr;
      }
      .ep-pay-summary {
        gap: 8px;
        margin-bottom: 14px;
      }
      .ep-pay-summary-value {
        font-size: 21px;
      }
      .ep-pay-grid {
        grid-template-columns: 1fr 1fr;
        gap: 10px;
      }
    }

    @media (min-width: 1180px) {
      .ep-doc-grid { grid-template-columns: repeat(3, minmax(0, 1fr)); }
    }
    .module-home.is-hidden { display: none; }

    .back-row {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 14px;
    }

    .back-button {
      border: 1px solid rgba(148, 163, 184, .22);
      border-radius: 13px;
      padding: 9px 12px;
      background: rgba(15, 23, 42, .75);
      color: #e2e8f0;
      cursor: pointer;
      font: inherit;
      font-weight: 700;
    }

    .placeholder-panel {
      display: none;
      width: min(640px, 100%);
      margin: 28px auto 0;
      padding: 28px;
      border: 1px solid rgba(148, 163, 184, .18);
      border-radius: 22px;
      background: rgba(15, 23, 42, .72);
      box-shadow: 0 22px 60px rgba(0, 0, 0, .32);
      text-align: center;
      color: #e2e8f0;
    }

    .placeholder-panel.is-active { display: block; }

    .placeholder-panel h2 {
      margin: 0 0 8px;
      font-size: 22px;
    }

    .placeholder-panel p {
      margin: 0;
      color: rgba(226, 232, 240, .62);
      font-size: 13px;
    }

    @media (max-width: 760px) {
      .web-chrome { grid-template-columns: 40px 1fr auto; }
      .chrome-spacer { display: none; }
      .module-grid { grid-template-columns: repeat(3, 1fr); }
      .home-layout { grid-template-columns: 1fr; }
      .menu-center { grid-column: 1; }
      .firm-side-list { max-height: calc(100vh - 118px); }
      .employees-page-header { grid-template-columns: 1fr; }
      .employee-actions { justify-content: flex-start; }
      .employee-filter-bar { grid-template-columns: 1fr 1fr; }
      .employees-search { grid-column: 1 / -1; }
      .employee-stats { grid-template-columns: repeat(2, minmax(0, 1fr)); }
      .problem-summary-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
      .problem-card { grid-template-columns: auto minmax(0, 1fr); }
      .problem-reasons { grid-column: 1 / -1; justify-content: flex-start; max-width: none; }
      .report-summary-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
      .report-toolbar { grid-template-columns: 1fr 1fr; }
      .report-search { grid-column: 1 / -1; }
      .report-app-head { grid-template-columns: 1fr; }
      .report-title-center { text-align: left; }
      .report-head-actions { justify-content: flex-start; }
      .report-filter-grid { grid-template-columns: 1fr 1fr; }
      .dash-head {
        grid-template-columns: 1fr;
        align-items: start;
      }
      .dash-title-wrap {
        align-items: flex-start;
      }
      .dash-ai-button {
        width: 100%;
        justify-content: center;
      }
      .dash-stat-grid {
        gap: 9px;
      }
      .dash-stat-card {
        min-height: 92px;
        padding: 13px;
      }
      .dash-stat-value {
        font-size: 25px;
      }
      .dash-movement-overlay {
        padding: 8px;
        align-items: end;
      }
      .dash-movement-dialog {
        width: 100%;
        max-height: 88dvh;
        border-radius: 18px;
      }
      .dash-movement-head {
        grid-template-columns: 42px minmax(0, 1fr) 40px;
        padding: 14px;
      }
      .dash-movement-icon {
        width: 42px;
        height: 42px;
      }
      .dash-movement-head h2 {
        font-size: 18px;
      }
      .dash-movement-grid {
        padding: 12px;
      }
      .finance-view {
        width: min(100%, calc(100vw - 14px));
        margin-top: 8px;
      }
      .finance-topbar {
        grid-template-columns: auto minmax(0, 1fr);
        gap: 10px;
        align-items: start;
      }
      .finance-month-nav {
        justify-content: flex-start;
        min-width: 0;
        flex-wrap: wrap;
      }
      .finance-month-nav #financeMonthTitle {
        font-size: 20px;
        line-height: 1.1;
      }
      .finance-action-row {
        grid-column: 1 / -1;
        justify-content: flex-start;
        gap: 8px;
        overflow-x: auto;
        flex-wrap: nowrap;
        padding-bottom: 4px;
        -webkit-overflow-scrolling: touch;
      }
      .finance-action-row .employee-action {
        flex: 0 0 auto;
        white-space: nowrap;
      }
      .finance-search {
        flex: 0 0 220px;
        width: 220px;
      }
      .finance-stat-strip {
        grid-template-columns: repeat(2, minmax(130px, 1fr));
        overflow: visible;
        gap: 8px;
      }
      .finance-stat-card {
        min-width: 0;
        padding: 11px 10px;
      }
      .finance-stat-value {
        font-size: 19px;
        overflow-wrap: anywhere;
      }
      .finance-layout {
        grid-template-columns: 1fr;
        gap: 10px;
      }
      .finance-summary-toggle {
        display: inline-flex;
      }
      .finance-scroll {
        max-height: 62dvh;
        border-radius: 12px;
        overflow: auto;
        -webkit-overflow-scrolling: touch;
      }
      .finance-table {
        min-width: 980px;
        font-size: 11px;
      }
      .finance-table th,
      .finance-table td {
        padding: 6px 5px;
      }
      .finance-side-panel {
        position: fixed;
        left: 8px;
        right: 8px;
        bottom: calc(8px + env(safe-area-inset-bottom));
        z-index: 75;
        min-height: 0;
        max-height: min(78dvh, 720px);
        grid-template-rows: minmax(160px, 1fr) minmax(140px, .8fr) auto;
        transform: translateY(calc(100% + 24px));
        transition: transform .2s ease;
        border-radius: 18px;
        box-shadow: 0 24px 80px rgba(0,0,0,.48);
      }
      .finance-side-panel.is-open { transform: translateY(0); }
      .finance-side-backdrop {
        position: fixed;
        inset: 0;
        z-index: 74;
        display: none;
        background: rgba(2, 6, 23, .56);
        backdrop-filter: blur(3px);
      }
      .finance-side-backdrop.is-open { display: block; }
      .finance-summary-close {
        display: inline-grid;
        width: 30px;
        height: 30px;
        place-items: center;
        border: 1px solid rgba(148, 163, 184, .18);
        border-radius: 9px;
        background: rgba(15, 23, 42, .62);
        color: #e2e8f0;
        font: inherit;
        cursor: pointer;
      }
      .finance-side-scroll {
        -webkit-overflow-scrolling: touch;
      }
    }

    @media (max-width: 520px) {
      .app-shell {
        width: 100%;
        padding: 8px 8px calc(74px + env(safe-area-inset-bottom));
      }
      .web-chrome {
        gap: 8px;
        margin-bottom: 10px;
      }
      .global-search input {
        height: 34px;
        padding-left: 34px;
        font-size: 12px;
      }
      .chrome-actions { gap: 6px; }
      .chrome-button,
      .hamburger {
        width: 34px;
        height: 34px;
      }
      .module-grid { grid-template-columns: repeat(2, 1fr); }
      .company-strip { grid-template-columns: auto 1fr; }
      .clock { display: none; }
      .employee-filter-bar { grid-template-columns: 1fr; }
      .employee-card-grid { grid-template-columns: 1fr; }
      .problem-summary-grid { grid-template-columns: 1fr; }
      .report-summary-grid { grid-template-columns: 1fr; }
      .report-toolbar { grid-template-columns: 1fr; }
      .report-filter-grid { grid-template-columns: 1fr; }
      .finance-card-grid {
        grid-template-columns: 1fr;
        width: 100%;
        gap: 12px;
      }
      .finance-module-card {
        min-height: 150px;
      }
      .dash-stat-card {
        grid-template-columns: minmax(0, 1fr);
      }
      .dash-stat-icon {
        display: none;
      }
      .dash-widget {
        border-radius: 14px;
        padding: 12px;
      }
      .finance-topbar .employee-action,
      .finance-action-row .employee-action {
        padding: 8px 11px;
        font-size: 12px;
      }
      .finance-module-icon {
        width: 38px !important;
        height: 38px !important;
        min-width: 38px;
      }
      .finance-stat-strip {
        grid-template-columns: repeat(2, minmax(0, 1fr));
      }
      .finance-scroll {
        max-height: 66dvh;
      }
      .finance-table {
        min-width: 980px;
      }
      .finance-stat-card:nth-child(5) {
        grid-column: 1 / -1;
      }
    }
  </style>
</head>
<body>
  <main class="app-shell">
    <header class="web-chrome">
      <button id="firmDrawerToggle" class="hamburger" type="button">☰</button>
      <div class="chrome-spacer"></div>
      <div class="global-search">
        <span class="search-icon">⌕</span>
        <input id="globalSearchInput" type="search" placeholder="Пошук по працівниках, шаблонах, архіву...">
      </div>
      <div class="chrome-actions">
        <div class="chrome-button">↗</div>
        <button id="webSettingsButton" class="chrome-button" type="button">⚙</button>
      </div>
    </header>
    <div id="connectionBanner" class="connection-banner">З'єднання з ПК перервалось. Пробую підключитися знову...</div>

    <div id="drawerBackdrop" class="drawer-backdrop"></div>

    <aside id="firmDrawer" class="firms-side">
      <div class="firms-side-header">
        <div class="firms-side-title">Фірми</div>
        <span class="pill" id="sideFirmCount">0</span>
      </div>
      <input id="firmSearchInput" class="firm-search" type="search" placeholder="Пошук фірми">
      <div id="sideFirmList" class="firm-side-list">
        <div class="empty">Завантаження...</div>
      </div>
    </aside>

    <section id="moduleHome" class="module-home">
      <div class="module-stage">
        <div class="home-layout">
          <div class="menu-center">
            <div class="company-strip">
              <div class="company-logo">Z</div>
              <div>
                <div class="company-name" id="activeCompanyName">Всі фірми</div>
                <div class="company-meta">Web доступ • тільки перегляд</div>
              </div>
              <div class="clock">
                <span id="clockTime">--:--</span>
                <small id="clockDate">Підключення...</small>
              </div>
            </div>

            <div class="module-grid">
              <button class="module-tile" data-target="employees"><span class="tile-icon" style="background:#8b5cf6;">👤</span><span class="tile-title">Працівники</span></button>
              <button class="module-tile" data-target="problems"><span class="tile-badge" id="problemTileBadge">0</span><span class="tile-icon" style="background:#f97316;">⚠</span><span class="tile-title">Проблеми</span></button>
              <button class="module-tile" data-target="templates"><span class="tile-icon" style="background:#22c55e;">▣</span><span class="tile-title">Шаблони</span></button>
              <button class="module-tile" data-target="finance"><span class="tile-icon" style="background:#c084fc;">⌁</span><span class="tile-title">Фінанси та Таблиці</span></button>
              <button class="module-tile" data-target="report"><span class="tile-icon" style="background:#06b6d4;">▥</span><span class="tile-title">Звіт</span></button>
              <button class="module-tile" data-target="archive"><span class="tile-icon" style="background:#38bdf8;">▤</span><span class="tile-title">Архів</span></button>
              <button class="module-tile" data-target="history"><span class="tile-icon" style="background:#fb923c;">◷</span><span class="tile-title">Історія дій</span></button>
              <button class="module-tile" data-target="candidates"><span class="tile-icon" style="background:#f59e0b;">♟</span><span class="tile-title">Кандидати</span></button>
              <button class="module-tile" data-target="ai"><span class="tile-icon" style="background:#6366f1;">✦</span><span class="tile-title">AI Помічник</span></button>
              <button class="module-tile" data-target="dashboard"><span class="tile-icon" style="background:#22d3ee;">▦</span><span class="tile-title">Панель керування</span></button>
              <button class="module-tile" data-target="invoices"><span class="tile-icon" style="background:#34d399;">▧</span><span class="tile-title">Фактури</span></button>
              <button class="module-tile" data-target="deleted"><span class="tile-icon" style="background:#fca5a5;">♲</span><span class="tile-title">Недавно видалені</span></button>
              <button class="module-tile" data-target="news"><span class="tile-icon" style="background:#38bdf8;">▣</span><span class="tile-title">Новини</span></button>
            </div>
          </div>

        </div>
      </div>
    </section>

    <section id="placeholderPanel" class="placeholder-panel">
      <h2 id="placeholderTitle">Розділ</h2>
      <p>Цей модуль буде підключений наступними етапами. Зараз готові меню і панель керування.</p>
      <button class="back-button" style="margin-top:18px" data-back>← Назад до меню</button>
    </section>

    <section id="dashboardView" class="dashboard-view">
      <header class="dash-head">
        <button class="back-button" data-back>←</button>
        <div class="dash-title-wrap">
          <div class="dash-icon">▦</div>
          <div>
            <h1>Панель керування</h1>
            <p id="dashboardSubtitle">0 працівників · 0 проблем</p>
          </div>
        </div>
        <button class="dash-ai-button" type="button">AI звіт</button>
      </header>

      <section class="dash-stat-grid">
        <article class="dash-stat-card is-clickable" id="dashMovementCard" role="button" tabindex="0">
          <div>
            <div class="dash-stat-label">Рух за місяць</div>
            <div class="dash-stat-value" id="dashMovementValue">+0 / -0</div>
            <div class="dash-stat-note"><span>Додано </span><strong id="dashAddedCount" style="color:#22c55e;">0</strong><span> · Архів </span><strong id="dashArchivedCount" style="color:#ef4444;">0</strong></div>
            <div class="dash-mini-bars"><div class="dash-mini-bar"><span id="dashAddedBar" style="width:0;background:#22c55e;"></span></div><div class="dash-mini-bar"><span id="dashArchivedBar" style="width:0;background:#ef4444;"></span></div></div>
          </div>
          <div class="dash-stat-icon" style="background:linear-gradient(135deg,#fb923c,#ef4444);">↕</div>
        </article>
        <article class="dash-stat-card">
          <div>
            <div class="dash-stat-label">Документи</div>
            <div class="dash-stat-value" id="documentProblemCount" style="color:#f87171;">0</div>
            <div class="dash-stat-note" id="dashProblemTrend">Все добре</div>
          </div>
          <div class="dash-stat-icon" style="background:linear-gradient(135deg,#fb7185,#ef4444);">⚠</div>
        </article>
        <article class="dash-stat-card">
          <div>
            <div class="dash-stat-label">Активні працівники</div>
            <div class="dash-stat-value" id="employeeCount">0</div>
            <div class="dash-stat-note">З усіх фірм</div>
          </div>
          <div class="dash-stat-icon" style="background:linear-gradient(135deg,#22c55e,#10b981);">♙</div>
        </article>
        <article class="dash-stat-card">
          <div>
            <div class="dash-stat-label">Фірми</div>
            <div class="dash-stat-value" id="firmCount">0</div>
            <div class="dash-stat-note">Активні фірми</div>
          </div>
          <div class="dash-stat-icon" style="background:linear-gradient(135deg,#8b5cf6,#ec4899);">▣</div>
        </article>
      </section>

      <section class="dash-main-grid">
        <div class="dash-left-stack" id="dashLeftStack">
          <div class="dash-widget-slot" data-dashboard-slot="0"></div>
        </div>
        <div class="dash-right-stack" id="dashRightStack">
          <div class="dash-widget-slot" data-dashboard-slot="1"></div>
          <div class="dash-splitter" id="dashRightSplitter" title="Змінити висоту"></div>
          <div class="dash-widget-slot" data-dashboard-slot="2"></div>
        </div>
      </section>
      <div style="display:none">
        <span id="firmPill">0</span>
        <span id="employeePill">0</span>
        <span id="serverStatus">Підключення...</span>
        <input id="searchInput" type="search">
        <div id="firmList"></div>
        <div id="employeeList"></div>
        <span id="missingPhotoCount">0</span>
      </div>
    </section>

    <div id="dashMovementOverlay" class="dash-movement-overlay">
      <div class="dash-movement-dialog">
        <div class="dash-movement-head">
          <div class="dash-movement-icon">↕</div>
          <div>
            <h2>Рух за місяць</h2>
            <p id="dashMovementDialogSummary">+0 / -0</p>
          </div>
          <button class="dash-movement-close" type="button" data-close-movement>×</button>
        </div>
        <div class="dash-movement-grid">
          <section class="dash-movement-column">
            <div class="dash-movement-column-title">
              <span>Наступили цього місяця</span>
              <span class="dash-movement-count" style="background:rgba(34,197,94,.18);color:#22c55e;" id="dashMovementAddedBadge">0</span>
            </div>
            <div id="dashMovementAddedList" class="dash-movement-list"><div class="empty">—</div></div>
          </section>
          <section class="dash-movement-column">
            <div class="dash-movement-column-title">
              <span>Закінчили цього місяця</span>
              <span class="dash-movement-count" style="background:rgba(239,68,68,.18);color:#ef4444;" id="dashMovementArchivedBadge">0</span>
            </div>
            <div id="dashMovementArchivedList" class="dash-movement-list"><div class="empty">—</div></div>
          </section>
        </div>
        <div class="dash-movement-foot">Натисніть на працівника, щоб відкрити його картку.</div>
      </div>
    </div>

    <section id="employeesView" class="employees-view">
      <div class="employees-page-header">
        <div class="employee-company-title">
          <div class="employee-company-avatar" id="employeesCompanyAvatar">A</div>
          <div>
            <h1 id="employeesViewTitle">Працівники</h1>
            <p id="employeesViewSubtitle">Оберіть фірму зліва або переглядайте всі фірми.</p>
          </div>
        </div>
        <div class="employee-actions">
          <button class="employee-action" data-back type="button">← Меню</button>
          <button class="employee-action" id="employeeFirmPicker" type="button">☰ Фірми</button>
          <button class="employee-action" id="employeeReportButton" type="button">Звіт</button>
          <button class="employee-action" type="button">Шаблони</button>
          <button class="employee-action primary" type="button">+ Додати працівника</button>
        </div>
      </div>

      <section class="employee-stats">
        <div class="employee-stat-card is-active">
          <div class="employee-stat-value" id="employeesViewCount">0</div>
          <div class="employee-stat-label">Всього</div>
        </div>
        <div class="employee-stat-card">
          <div class="employee-stat-value" id="employeesActiveCount">0</div>
          <div class="employee-stat-label">Активних</div>
        </div>
        <div class="employee-stat-card">
          <div class="employee-stat-value" id="employeesProblemCount">0</div>
          <div class="employee-stat-label">Проблеми з док.</div>
        </div>
        <div class="employee-stat-card">
          <div class="employee-stat-value" id="employeesNewMonthCount">0</div>
          <div class="employee-stat-label">Нових цього міс.</div>
        </div>
      </section>

      <section class="employee-filter-bar">
        <input id="employeesSearchInput" class="employees-search" type="search" placeholder="Пошук по ПІБ, паспорту, візі, страховці">
        <button class="filter-chip is-active" data-employee-filter="all" type="button">Всі</button>
        <button class="filter-chip" data-employee-filter="active" type="button">Активні</button>
        <button class="filter-chip" data-employee-filter="problems" type="button">Проблеми</button>
        <button class="filter-chip" data-employee-filter="visa" type="button">Віза</button>
        <button class="filter-chip" data-employee-filter="work_permit" type="button">Дозвіл</button>
      </section>

      <div id="employeesViewList" class="employee-card-grid"><div class="empty">Завантаження...</div></div>
    </section>

    <section id="problemsView" class="problems-view">
      <div class="employees-page-header">
        <div class="employee-company-title">
          <div class="employee-company-avatar">!</div>
          <div>
            <h1>Проблеми документів</h1>
            <p id="problemsViewSubtitle">Контроль відсутніх, прострочених і майже прострочених документів.</p>
          </div>
        </div>
        <div class="employee-actions">
          <button class="employee-action" data-back type="button">← Меню</button>
          <button class="employee-action" id="problemsFirmPicker" type="button">☰ Фірми</button>
        </div>
      </div>

      <section class="problem-summary-grid">
        <div class="problem-summary-card">
          <div class="problem-summary-value" id="problemTotalCount">0</div>
          <div class="problem-summary-label">Усього проблем</div>
        </div>
        <div class="problem-summary-card">
          <div class="problem-summary-value" id="problemMissingCount">0</div>
          <div class="problem-summary-label">Відсутні документи</div>
        </div>
        <div class="problem-summary-card">
          <div class="problem-summary-value" id="problemExpiredCount">0</div>
          <div class="problem-summary-label">Прострочено</div>
        </div>
        <div class="problem-summary-card">
          <div class="problem-summary-value" id="problemWarnCount">0</div>
          <div class="problem-summary-label">Закінчується до 30 днів</div>
        </div>
      </section>

      <section class="problem-toolbar">
        <button class="filter-chip is-active" data-problem-filter="all" type="button">Всі</button>
        <button class="filter-chip" data-problem-filter="missing" type="button">Відсутні</button>
        <button class="filter-chip" data-problem-filter="expired" type="button">Прострочені</button>
        <button class="filter-chip" data-problem-filter="warn" type="button">Скоро закінчуються</button>
        <button class="filter-chip" data-problem-filter="photo" type="button">Без фото</button>
      </section>

      <div id="problemsViewList" class="problem-list"><div class="empty">Завантаження...</div></div>
    </section>

    <section id="reportView" class="report-view">
      <div class="report-app-head">
        <button class="employee-action" data-back type="button">←</button>
        <div class="report-title-center">
          <div class="employee-company-avatar" style="margin:0 auto 8px;background:linear-gradient(135deg,#06b6d4,#2563eb);">▥</div>
          <h1>Звіт</h1>
          <span class="report-range-pill" id="reportRangePill">0 фірм, 0 працівників</span>
        </div>
        <div class="report-head-actions">
          <button class="employee-action" id="reportColumnsButton" type="button">✎ Колонки</button>
          <button class="employee-action" id="reportResetColumnsButton" type="button">↻ Скинути колонки</button>
          <button class="employee-action primary" type="button">↔ Налаштувати експорт</button>
        </div>
      </div>

      <section class="report-filter-panel">
        <div class="report-filter-title">⌃ Фільтри</div>
        <div class="report-filter-grid">
          <div class="report-filter-box">
            <div class="report-filter-box-title">⌂ Роботодавці</div>
            <div id="reportFirmChecks" class="report-check-list"><div class="empty">Завантаження...</div></div>
          </div>
          <div class="report-filter-box">
            <div class="report-filter-box-title">♙ Агенції</div>
            <div id="reportAgencyChecks" class="report-check-list"><div class="empty">Завантаження...</div></div>
          </div>
          <div class="report-filter-box small">
            <div class="report-filter-box-title">Від</div>
            <input id="reportDateFrom" class="report-date-input" type="date">
          </div>
          <div class="report-filter-box small">
            <div class="report-filter-box-title">До</div>
            <input id="reportDateTo" class="report-date-input" type="date">
          </div>
          <div class="report-run-wrap">
            <button id="reportApplyButton" class="employee-action primary" type="button">▷ Показати</button>
          </div>
        </div>
      </section>

      <div class="report-mode-tabs">
        <button class="report-mode-tab is-active" data-report-mode="summary" type="button">▣ Зведення</button>
        <button class="report-mode-tab" data-report-mode="employees" type="button">♙ Працівники</button>
      </div>

      <section id="reportSummaryPanel" class="report-summary-panel">
        <div class="report-panel-title">⌂ Деталі по фірмах</div>
        <div class="report-table-wrap" style="min-height:260px;max-height:calc(100vh - 320px);border:0;border-radius:0;box-shadow:none;">
          <table class="report-firm-table">
            <thead>
              <tr>
                <th>Фірма</th>
                <th>Всього</th>
                <th>Активні</th>
                <th>Без Дозволу</th>
                <th>Закінчили</th>
              </tr>
            </thead>
            <tbody id="reportSummaryBody">
              <tr><td colspan="5"><div class="empty">Завантаження...</div></td></tr>
            </tbody>
          </table>
        </div>
      </section>

      <section id="reportGroupsPanel" class="report-groups-panel is-hidden">
        <div class="report-toolbar">
          <input id="reportSearchInput" class="report-search" type="search" placeholder="Пошук по ПІБ, паспорту, візі, громадянству, телефону">
          <button class="filter-chip is-active" data-report-filter="all" type="button">Всі</button>
          <button class="filter-chip" data-report-filter="active" type="button">Активні</button>
          <button class="filter-chip" data-report-filter="archived" type="button">Архів</button>
          <button class="filter-chip" data-report-filter="problems" type="button">Проблеми</button>
        </div>
        <div id="reportEmployeeGroups"><div class="empty">Завантаження...</div></div>
      </section>
    </section>

    <section id="financeView" class="finance-view">
      <div id="financeMenuScreen" class="finance-menu-screen">
        <div>
          <div class="report-app-head" style="margin-bottom:54px;">
            <button class="employee-action" data-back type="button">←</button>
            <div class="report-title-center">
              <div class="employee-company-avatar" style="margin:0 auto 8px;background:linear-gradient(135deg,#c084fc,#6366f1);">⌁</div>
              <h1 data-i18n="finance">Фінанси та Таблиці</h1>
            </div>
            <div></div>
          </div>
          <div class="finance-card-grid">
            <button id="financeOpenSalary" class="finance-module-card" type="button">
              <span class="finance-module-icon" style="background:linear-gradient(135deg,#c084fc,#7c3aed);">▧</span>
              <h3 data-i18n="financeCardTitle">Фінанси</h3>
              <p data-i18n="financeCardDesc">Фінансова звітність та облік</p>
            </button>
            <button id="financeOpenTables" class="finance-module-card" type="button">
              <span class="finance-module-icon" style="background:linear-gradient(135deg,#22c55e,#10b981);">▦</span>
              <h3 data-i18n="tablesCardTitle">Таблиці</h3>
              <p data-i18n="tablesCardDesc">Генерація таблиць для друку</p>
            </button>
          </div>
        </div>
      </div>

      <div id="financeTablesScreen" class="finance-tables-screen is-hidden">
        <div class="report-app-head" style="margin-bottom:54px;">
          <button class="employee-action" id="financeTablesBack" type="button">←</button>
          <div class="report-title-center">
            <div class="employee-company-avatar" style="margin:0 auto 8px;background:linear-gradient(135deg,#22c55e,#10b981);">▦</div>
            <h1 data-i18n="tablesCardTitle">Таблиці</h1>
          </div>
          <div></div>
        </div>
        <div class="finance-card-grid" style="margin:0 auto;max-width:520px;">
          <button class="finance-module-card" type="button" data-finance-table="advance">
            <span class="finance-module-icon" style="background:linear-gradient(135deg,#fb7185,#ef4444);">⌁</span>
            <h3 data-i18n="advanceTableTitle">Таблиця авансів</h3>
            <p data-i18n="advanceTableDesc">Вибір фірм для друку таблиці авансів</p>
          </button>
          <button class="finance-module-card" type="button" data-finance-table="payment">
            <span class="finance-module-icon" style="background:linear-gradient(135deg,#38bdf8,#2563eb);">✎</span>
            <h3 data-i18n="paymentSignTitle">Підписи на виплату</h3>
            <p data-i18n="paymentSignDesc">Список працівників для підписів зарплати</p>
          </button>
        </div>
      </div>

      <div id="financeSalaryScreen" class="finance-salary-screen is-hidden">
        <div class="finance-topbar">
          <button class="employee-action" id="financeSalaryBack" type="button">←</button>
          <div class="finance-month-nav">
            <button class="employee-action" id="financePrevMonth" type="button">‹</button>
            <span class="finance-module-icon" style="width:40px;height:40px;margin:0;background:linear-gradient(135deg,#c084fc,#7c3aed);font-size:18px;">⌁</span>
            <span id="financeMonthTitle">-</span>
            <button class="employee-action" id="financeNextMonth" type="button">›</button>
            <span class="report-range-pill" id="financeCreateNextPill">+ Наступний місяць</span>
          </div>
          <div class="finance-action-row">
            <input id="financeSearchInput" class="finance-search" type="search" placeholder="Пошук у фінансах">
            <button class="employee-action" type="button" data-i18n="columns">Колонки</button>
            <button class="employee-action primary" type="button" data-i18n="financeReadOnlySave">Збереження в програмі</button>
            <button class="employee-action primary" type="button">Excel</button>
            <button class="employee-action" type="button" data-i18n="financeColAdvance">Аванс</button>
            <button class="employee-action primary" type="button" data-i18n="financeMarkAllPaid">✓ Позначити всі</button>
            <button class="employee-action primary finance-summary-toggle" id="financeSummaryToggle" type="button">Підсумки</button>
          </div>
        </div>

        <section class="finance-stat-strip">
          <div class="finance-stat-card"><div class="finance-stat-label" data-i18n="financeStatEmployees">Працівники</div><div class="finance-stat-value" id="financeTotalEmployees">0</div></div>
          <div class="finance-stat-card"><div class="finance-stat-label" data-i18n="financeStatHours">Години</div><div class="finance-stat-value" id="financeTotalHours">0</div></div>
          <div class="finance-stat-card"><div class="finance-stat-label" data-i18n="financeColGross">Брутто</div><div class="finance-stat-value" id="financeTotalGross">0 Kč</div></div>
          <div class="finance-stat-card"><div class="finance-stat-label" data-i18n="financeColNet">Нетто</div><div class="finance-stat-value" id="financeTotalNet">0 Kč</div></div>
          <div class="finance-stat-card"><div class="finance-stat-label" data-i18n="financeStatPaid">Оплачено</div><div class="finance-stat-value" id="financePaidDisplay">0/0</div></div>
        </section>

        <section class="finance-layout">
          <div class="finance-data-panel">
            <div class="finance-scroll">
              <table class="finance-table">
                <thead id="financeTableHead"></thead>
                <tbody id="financeEntriesBody">
                  <tr><td><div class="empty" data-i18n="loading">Завантаження...</div></td></tr>
                </tbody>
              </table>
            </div>
          </div>
          <div id="financeSideBackdrop" class="finance-side-backdrop"></div>
          <aside id="financeSidePanel" class="finance-side-panel">
            <div style="min-height:0;">
              <div class="finance-side-title"><span data-i18n="financeFirmBreakdown">По фірмах</span><button class="finance-summary-close" id="financeSummaryClose" type="button">×</button></div>
              <div id="financeFirmSummaryList" class="finance-side-scroll"><div class="empty">Завантаження...</div></div>
            </div>
            <div style="min-height:0;">
              <div class="finance-side-title"><span id="financeExpenseHeader">Витрати фірми</span><button class="finance-side-plus" type="button" title="Read only">+</button></div>
              <div id="financeExpensesList" class="finance-side-scroll"><div class="empty">—</div></div>
            </div>
            <div>
              <div class="finance-summary-box" style="background:rgba(120,53,15,.72);color:#fbbf24;"><span data-i18n="financeExpenseTotal">Витрати разом</span><strong id="financeExpenseTotal">0 Kč</strong></div>
              <div class="finance-summary-box" style="background:rgba(22,101,52,.65);color:#22c55e;"><span data-i18n="financeColNet">До виплати</span><strong id="financeSideNet">0 Kč</strong></div>
              <div class="finance-summary-box" style="background:linear-gradient(135deg,#fbbf24,#fb923c);color:white;"><span data-i18n="financeGrandTotal">Загальна Виплата</span><strong id="financeGrandTotal">0 Kč</strong></div>
              <div class="finance-extra-stats">◎ <span data-i18n="financeExtraStats">Додаткова статистика</span></div>
            </div>
          </aside>
        </section>
      </div>
    </section>

    <section id="reportColumnsOverlay" class="report-columns-overlay">
      <div class="report-columns-dialog">
        <div class="report-columns-head">
          <div>
            <h3>Налаштування колонок</h3>
            <p>Оберіть, які колонки показувати, та змініть їх порядок як у програмі.</p>
          </div>
          <button class="employee-action" data-report-columns-close type="button">×</button>
        </div>
        <div id="reportColumnsList" class="report-columns-list"></div>
        <div class="report-columns-actions">
          <button class="employee-action" id="reportColumnsResetInside" type="button">Скинути колонки</button>
          <div style="display:flex;gap:10px;">
            <button class="employee-action" data-report-columns-close type="button">Скасувати</button>
            <button class="employee-action primary" id="reportColumnsSaveButton" type="button">Готово</button>
          </div>
        </div>
      </div>
    </section>

    <section id="webSettingsOverlay" class="web-settings-overlay">
      <div class="web-settings-dialog">
        <div class="web-settings-head">
          <div>
            <h3 data-i18n="settingsTitle">Налаштування сайту</h3>
            <p data-i18n="settingsDesc">Це особистий вигляд тільки для цього браузера. Фірми, працівники й документи залишаються спільними з програмою.</p>
          </div>
          <button class="employee-action" data-web-settings-close type="button">×</button>
        </div>

        <div class="web-settings-grid">
          <div class="web-settings-card">
            <label for="webLanguageSelect" data-i18n="settingsLanguage">Мова сайту</label>
            <select id="webLanguageSelect" class="report-date-input">
              <option value="uk">Українська</option>
              <option value="cs">Čeština</option>
              <option value="en">English</option>
            </select>
            <div class="web-settings-note" data-i18n="settingsLanguageNote">Мова сайту окрема для кожного користувача.</div>
          </div>

          <div class="web-settings-card">
            <label for="webThemeSelect" data-i18n="settingsTheme">Тема</label>
            <select id="webThemeSelect" class="report-date-input">
              <option value="dark" data-i18n="themeDark">Темна</option>
              <option value="light" data-i18n="themeLight">Світла</option>
            </select>
          </div>

          <div class="web-settings-card">
            <label for="webScaleRange"><span data-i18n="settingsScale">Масштаб</span>: <strong id="webScaleValue">100%</strong></label>
            <input id="webScaleRange" type="range" min="85" max="120" step="5">
            <div class="web-settings-note" data-i18n="settingsScaleNote">Масштаб впливає тільки на сайт у цьому браузері.</div>
          </div>

          <div class="web-settings-card">
            <label for="webDensitySelect" data-i18n="settingsDensity">Щільність списків</label>
            <select id="webDensitySelect" class="report-date-input">
              <option value="normal" data-i18n="densityNormal">Звичайна</option>
              <option value="compact" data-i18n="densityCompact">Компактна</option>
            </select>
          </div>

          <div class="web-settings-card full">
            <label data-i18n="sharedDataTitle">Спільні дані програми</label>
            <div class="web-settings-status">
              <span id="settingsFirmStatus">Фірми: -</span>
              <span id="settingsEmployeeStatus">Працівники: -</span>
              <span id="settingsSyncStatus">Стан: локально</span>
            </div>
            <div class="web-settings-note" data-i18n="sharedDataNote">Фірми, працівники, архів і документи беруться з основної програми однаково для всіх. Особисті налаштування сайту не змінюють дані.</div>
          </div>
        </div>

        <div class="report-columns-actions" style="margin-top:16px;">
          <button class="employee-action" id="webSettingsResetButton" type="button" data-i18n="settingsReset">Скинути вигляд</button>
          <button class="employee-action primary" data-web-settings-close type="button" data-i18n="settingsDone">Готово</button>
        </div>
      </div>
    </section>

    <section id="employeeDetailOverlay" class="employee-detail-overlay">
      <div id="employeeDetailContent" class="ep-shell"></div>
    </section>
  </main>

  <script>
    var state = {
      firms: [],
      employees: [],
      reportRows: [],
      dashboard: null,
      dashboardLayout: { slots: ["salary", "efficiency", "expiring"], rightRatio: .42 },
      query: "",
      reportQuery: "",
      firmQuery: "",
      selectedFirm: "",
      employeeFilter: "all",
      problemFilter: "all",
      reportFilter: "all",
      reportMode: "summary",
      reportColumns: [],
      reportColumnsBackup: "",
      reportSelectedFirms: [],
      reportSelectedAgencies: [],
      reportDateFrom: "",
      reportDateTo: "",
      financeMonths: [],
      financeSelectedPeriod: "",
      financeYear: new Date().getFullYear(),
      financeMonth: new Date().getMonth() + 1,
      financeFirm: "",
      financeSearch: "",
      financeColumnWidths: {},
      webSettings: { language: "uk", theme: "dark", scale: 100, density: "normal" }
    };

    function byId(id) {
      return document.getElementById(id);
    }

    function setTextById(id, value) {
      var node = byId(id);
      if (node) node.textContent = value;
    }

    function setStyleById(id, prop, value) {
      var node = byId(id);
      if (node) node.style[prop] = value;
    }

    function safe(value) {
      return value == null || value === "" ? "-" : String(value);
    }

    function escapeHtml(value) {
      return safe(value)
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;")
        .replace(/'/g, "&#039;");
    }

    function setConnectionIssue(hasIssue) {
      var banner = byId("connectionBanner");
      if (banner) banner.classList.toggle("is-visible", !!hasIssue);
    }

    function delay(ms) {
      return new Promise(function(resolve) { setTimeout(resolve, ms); });
    }

    async function fetchWithRetry(url, options, retries) {
      var attempts = retries == null ? 2 : retries;
      var lastError;
      for (var attempt = 0; attempt <= attempts; attempt++) {
        try {
          var response = await fetch(url, Object.assign({ cache: "no-store" }, options || {}));
          setConnectionIssue(false);
          return response;
        } catch (error) {
          lastError = error;
          setConnectionIssue(true);
          if (attempt < attempts) await delay(450 + attempt * 850);
        }
      }
      throw lastError;
    }

    async function fetchJson(url, retries) {
      var response = await fetchWithRetry(url, null, retries);
      var data = await response.json();
      if (!response.ok) throw new Error(data && data.error ? data.error : response.statusText || "request");
      return data;
    }

    function hasDocumentProblem(employee) {
      return !employee.hasPassport
        || !employee.hasVisa
        || !employee.hasInsurance
        || expiryStatus(employee.passportExpiry) === "expired"
        || expiryStatus(employee.visaExpiry) === "expired"
        || expiryStatus(employee.insuranceExpiry) === "expired"
        || expiryStatus(employee.passportExpiry) === "warn"
        || expiryStatus(employee.visaExpiry) === "warn"
        || expiryStatus(employee.insuranceExpiry) === "warn";
    }

    function isActiveEmployee(employee) {
      return String(employee.status || "").toLowerCase() === "active";
    }

    function parseEmployeeDate(value) {
      if (!value) return null;
      var iso = String(value).match(/^(\d{4})-(\d{2})-(\d{2})$/);
      if (iso) return new Date(Number(iso[1]), Number(iso[2]) - 1, Number(iso[3]));
      var parts = String(value).match(/(\d{1,2})[.\-/](\d{1,2})[.\-/](\d{4})/);
      if (!parts) return null;
      return new Date(Number(parts[3]), Number(parts[2]) - 1, Number(parts[1]));
    }

    function formatInputDate(date) {
      var month = String(date.getMonth() + 1).padStart(2, "0");
      var day = String(date.getDate()).padStart(2, "0");
      return date.getFullYear() + "-" + month + "-" + day;
    }

    function formatDisplayDate(value) {
      var date = parseEmployeeDate(value);
      return date ? date.toLocaleDateString("uk-UA") : safe(value);
    }

    function expiryStatus(value) {
      var date = parseEmployeeDate(value);
      if (!date) return "none";
      var today = new Date();
      today.setHours(0, 0, 0, 0);
      var days = Math.ceil((date.getTime() - today.getTime()) / 86400000);
      if (days < 0) return "expired";
      if (days <= 30) return "warn";
      return "ok";
    }

    function docBadgeClass(hasDoc, expiry) {
      if (!hasDoc) return "missing";
      var status = expiryStatus(expiry);
      if (status === "expired") return "expired";
      if (status === "warn") return "warn";
      return "ok";
    }

    function docBadgeText(label, hasDoc, expiry) {
      if (!hasDoc) return label + ": " + webT("none");
      var status = expiryStatus(expiry);
      if (status === "expired") return label + ": " + webT("expiredShort");
      if (status === "warn") return label + ": " + webT("soon");
      return label + ": " + webT("ok");
    }

    function isNewThisMonth(employee) {
      var start = parseEmployeeDate(employee.startDate);
      if (!start) return false;
      var now = new Date();
      return start.getFullYear() === now.getFullYear() && start.getMonth() === now.getMonth();
    }

    function getInitials(fullName) {
      var parts = String(fullName || "").trim().split(/\s+/).filter(Boolean);
      if (parts.length === 0) return "?";
      if (parts.length === 1) return parts[0].substring(0, 2).toUpperCase();
      return (parts[0][0] + parts[1][0]).toUpperCase();
    }

    function docBadge(label, hasDoc, expiry) {
      return `<span class="doc-badge ${docBadgeClass(hasDoc, expiry)}">${escapeHtml(docBadgeText(label, hasDoc, expiry))}</span>`;
    }

    var webTranslations = {
      uk: {
        settingsTitle: "Налаштування сайту",
        settingsDesc: "Це особистий вигляд тільки для цього браузера. Фірми, працівники й документи залишаються спільними з програмою.",
        settingsLanguage: "Мова сайту",
        settingsLanguageNote: "Мова сайту окрема для кожного користувача.",
        settingsTheme: "Тема",
        themeDark: "Темна",
        themeLight: "Світла",
        settingsScale: "Масштаб",
        settingsScaleNote: "Масштаб впливає тільки на сайт у цьому браузері.",
        settingsDensity: "Щільність списків",
        densityNormal: "Звичайна",
        densityCompact: "Компактна",
        sharedDataTitle: "Спільні дані програми",
        sharedDataNote: "Фірми, працівники, архів і документи беруться з основної програми однаково для всіх. Особисті налаштування сайту не змінюють дані.",
        settingsReset: "Скинути вигляд",
        settingsDone: "Готово",
        firmsLabel: "Фірми",
        employeesLabel: "Працівники",
        stateLabel: "Стан",
        localState: "локально"
      },
      cs: {
        settingsTitle: "Nastavení webu",
        settingsDesc: "Toto je osobní vzhled jen pro tento prohlížeč. Firmy, pracovníci a dokumenty zůstávají společné s programem.",
        settingsLanguage: "Jazyk webu",
        settingsLanguageNote: "Jazyk webu je samostatný pro každého uživatele.",
        settingsTheme: "Motiv",
        themeDark: "Tmavý",
        themeLight: "Světlý",
        settingsScale: "Měřítko",
        settingsScaleNote: "Měřítko platí jen pro web v tomto prohlížeči.",
        settingsDensity: "Hustota seznamů",
        densityNormal: "Normální",
        densityCompact: "Kompaktní",
        sharedDataTitle: "Společná data programu",
        sharedDataNote: "Firmy, pracovníci, archiv a dokumenty se berou z hlavního programu stejně pro všechny. Osobní nastavení webu data nemění.",
        settingsReset: "Obnovit vzhled",
        settingsDone: "Hotovo",
        firmsLabel: "Firmy",
        employeesLabel: "Pracovníci",
        stateLabel: "Stav",
        localState: "lokálně"
      },
      en: {
        settingsTitle: "Web settings",
        settingsDesc: "This is a personal view for this browser only. Firms, employees, and documents stay shared with the desktop app.",
        settingsLanguage: "Website language",
        settingsLanguageNote: "Website language is separate for each user.",
        settingsTheme: "Theme",
        themeDark: "Dark",
        themeLight: "Light",
        settingsScale: "Scale",
        settingsScaleNote: "Scale affects only this website in this browser.",
        settingsDensity: "List density",
        densityNormal: "Normal",
        densityCompact: "Compact",
        sharedDataTitle: "Shared app data",
        sharedDataNote: "Firms, employees, archive, and documents are read from the desktop app equally for everyone. Personal web settings do not change data.",
        settingsReset: "Reset view",
        settingsDone: "Done",
        firmsLabel: "Firms",
        employeesLabel: "Employees",
        stateLabel: "State",
        localState: "local"
      }
    };

    var webExtraTranslations = {
      uk: {
        globalSearchPlaceholder: "Пошук по працівниках, шаблонах, архіву...",
        firmSearchPlaceholder: "Пошук фірми",
        dashboardSearchPlaceholder: "Пошук по імені, фірмі або посаді",
        employeesSearchPlaceholder: "Пошук по ПІБ, паспорту, візі, страховці",
        reportSearchPlaceholder: "Пошук по ПІБ, паспорту, візі, громадянству, телефону",
        allFirms: "Всі фірми", employees: "Працівники", problems: "Проблеми", templates: "Шаблони", finance: "Фінанси та Таблиці",
        report: "Звіт", archive: "Архів", history: "Історія дій", candidates: "Кандидати", ai: "AI Помічник",
        dashboard: "Панель керування", invoices: "Фактури", deleted: "Недавно видалені", news: "Новини",
        webReadonly: "Web доступ • тільки перегляд", connecting: "Підключення...", serverOk: "Працює локально", serverUnknown: "Невідомий стан", serverError: "Помилка",
        mainMenu: "Головне меню", menu: "Меню", backToMenu: "Назад до меню", firms: "Фірми", total: "Всього",
        active: "Активні", documentProblems: "Проблеми документів", docProblemsShort: "Проблеми з док.", newThisMonth: "Нових цього міс.",
        missingPhoto: "Без фото", sharedFromApp: "З основної бази програми", activeByFirms: "Активні працівники по фірмах",
        photoControl: "Для швидкого контролю документів", missingDocsNote: "Немає паспорта, візи або страховки",
        readOnlySubtitle: "Дані основної програми. Поки тільки перегляд.", chooseFirmLeft: "Оберіть фірму зліва або переглядайте всі фірми.",
        selectFirmSubtitle: "Оберіть фірму через ☰ або переглядайте всі фірми.", firmPrefix: "Фірма", positionLabel: "Позиція",
        startLabel: "Початок", statusLabel: "Статус", phoneLabel: "Телефон", employeesCount: "Працівників", addressLabel: "Адреса",
        noFirms: "Фірм поки немає.", noFirmsFound: "Фірм не знайдено.", noEmployees: "Працівників не знайдено.",
        noEmployeesFilter: "Працівників не знайдено. Виберіть іншу фірму або змініть пошук.", loading: "Завантаження...",
        all: "Всі", missing: "Відсутні", expired: "Прострочені", expiringSoon: "Скоро закінчуються",
        visa: "Віза", workPermit: "Дозвіл", passport: "Паспорт", insurance: "Страховка", photo: "Фото",
        none: "немає", expiredShort: "простр.", soon: "скоро", expiredFull: "прострочено", ok: "OK",
        documentProblemsTitle: "Проблеми документів", problemsSubtitle: "Контроль відсутніх, прострочених і майже прострочених документів.",
        allFirmsProblemsSubtitle: "Всі фірми. Контроль відсутніх, прострочених і майже прострочених документів.",
        totalProblems: "Усього проблем", missingDocuments: "Відсутні документи", expiredLabel: "Прострочено", expiresIn30: "Закінчується до 30 днів",
        noProblemsByFilter: "Проблем за цим фільтром немає.", columns: "Колонки", resetColumns: "Скинути колонки",
        configureExport: "Налаштувати експорт", filters: "Фільтри", employers: "Роботодавці", agencies: "Агенції", from: "Від", to: "До",
        show: "Показати", summary: "Зведення", firmDetails: "Деталі по фірмах", withoutPermit: "Без Дозволу", ended: "Закінчили",
        allEmployers: "Всі роботодавці", allAgencies: "Всі агенції", noEmployers: "Роботодавців немає.", noAgencies: "Агенцій немає.",
        noFirmData: "Даних по фірмах не знайдено.", noReportEmployees: "Працівників за цими фільтрами не знайдено.",
        reportRange: "{firms} фірм, {employees} працівників", columnSettingsTitle: "Налаштування колонок",
        columnSettingsDesc: "Оберіть, які колонки показувати, та змініть їх порядок як у програмі.", cancel: "Скасувати", done: "Готово",
        placeholderText: "Цей модуль буде підключений наступними етапами. Зараз готові меню і панель керування.",
        financePageTitle: "Фінанси та виплати",
        financeFiltersTitle: "⌃ Період і фірма",
        financePeriodLabel: "Період",
        financeEntriesTitle: "Виплати працівникам",
        financeColName: "ПІБ",
        financeColHours: "Години",
        financeColRate: "Ставка",
        financeColGross: "Брутто",
        financeColAdvance: "Аванс",
        financeColNet: "Нетто (збереж.)",
        financeColNote: "Примітка",
        financeExpensesTitle: "Витрати фірми",
        financeExpenseName: "Назва",
        financeExpenseAmount: "Сума",
        financeNoMonths: "Немає збережених місяців зарплати.",
        financeNoEntries: "Записів за обраний період немає.",
        financeNoExpenses: "Витрат за цим фільтром немає.",
        financeLoadError: "Не вдалося завантажити фінансові дані.",
        financePillEmpty: "немає даних",
        financeCardTitle: "Фінанси",
        financeCardDesc: "Фінансова звітність та облік",
        tablesCardTitle: "Таблиці",
        tablesCardDesc: "Генерація таблиць для друку",
        advanceTableTitle: "Таблиця авансів",
        advanceTableDesc: "Вибір фірм для друку таблиці авансів",
        paymentSignTitle: "Підписи на виплату",
        paymentSignDesc: "Список працівників для підписів зарплати",
        financeReadOnlySave: "Збереження в програмі",
        financeMarkAllPaid: "✓ Позначити всі",
        financeStatEmployees: "Працівники",
        financeStatHours: "Години",
        financeStatPaid: "Оплачено",
        financeFirmBreakdown: "По фірмах",
        financeGrandTotal: "Загальна Виплата",
        financeExpenseTotal: "Витрати разом",
        financeExtraStats: "Додаткова статистика"
      },
      cs: {
        globalSearchPlaceholder: "Hledat pracovníky, šablony, archiv...",
        firmSearchPlaceholder: "Hledat firmu",
        dashboardSearchPlaceholder: "Hledat podle jména, firmy nebo pozice",
        employeesSearchPlaceholder: "Hledat podle jména, pasu, víza, pojištění",
        reportSearchPlaceholder: "Hledat podle jména, pasu, víza, občanství, telefonu",
        allFirms: "Všechny firmy", employees: "Pracovníci", problems: "Problémy", templates: "Šablony", finance: "Finance a tabulky",
        report: "Přehled", archive: "Archiv", history: "Historie akcí", candidates: "Kandidáti", ai: "AI asistent",
        dashboard: "Ovládací panel", invoices: "Faktury", deleted: "Nedávno smazané", news: "Novinky",
        webReadonly: "Webový přístup • pouze prohlížení", connecting: "Připojování...", serverOk: "Běží lokálně", serverUnknown: "Neznámý stav", serverError: "Chyba",
        mainMenu: "Hlavní menu", menu: "Menu", backToMenu: "Zpět do menu", firms: "Firmy", total: "Celkem",
        active: "Aktivní", documentProblems: "Problémy s dokumenty", docProblemsShort: "Problémy dok.", newThisMonth: "Noví tento měsíc",
        missingPhoto: "Bez fotky", sharedFromApp: "Z hlavní databáze programu", activeByFirms: "Aktivní pracovníci podle firem",
        photoControl: "Pro rychlou kontrolu dokumentů", missingDocsNote: "Chybí pas, vízum nebo pojištění",
        readOnlySubtitle: "Data hlavního programu. Zatím pouze prohlížení.", chooseFirmLeft: "Vyberte firmu vlevo nebo zobrazte všechny firmy.",
        selectFirmSubtitle: "Vyberte firmu přes ☰ nebo zobrazte všechny firmy.", firmPrefix: "Firma", positionLabel: "Pozice",
        startLabel: "Začátek", statusLabel: "Stav", phoneLabel: "Telefon", employeesCount: "Pracovníků", addressLabel: "Adresa",
        noFirms: "Zatím nejsou žádné firmy.", noFirmsFound: "Firmy nenalezeny.", noEmployees: "Pracovníci nenalezeni.",
        noEmployeesFilter: "Pracovníci nenalezeni. Vyberte jinou firmu nebo změňte hledání.", loading: "Načítání...",
        all: "Vše", missing: "Chybí", expired: "Prošlé", expiringSoon: "Brzy končí",
        visa: "Vízum", workPermit: "Povolení", passport: "Pas", insurance: "Pojištění", photo: "Foto",
        none: "chybí", expiredShort: "prošlé", soon: "brzy", expiredFull: "prošlé", ok: "OK",
        documentProblemsTitle: "Problémy s dokumenty", problemsSubtitle: "Kontrola chybějících, prošlých a brzy končících dokumentů.",
        allFirmsProblemsSubtitle: "Všechny firmy. Kontrola chybějících, prošlých a brzy končících dokumentů.",
        totalProblems: "Celkem problémů", missingDocuments: "Chybějící dokumenty", expiredLabel: "Prošlé", expiresIn30: "Končí do 30 dnů",
        noProblemsByFilter: "Pro tento filtr nejsou žádné problémy.", columns: "Sloupce", resetColumns: "Obnovit sloupce",
        configureExport: "Nastavit export", filters: "Filtry", employers: "Zaměstnavatelé", agencies: "Agentury", from: "Od", to: "Do",
        show: "Zobrazit", summary: "Souhrn", firmDetails: "Detaily podle firem", withoutPermit: "Bez povolení", ended: "Ukončili",
        allEmployers: "Všichni zaměstnavatelé", allAgencies: "Všechny agentury", noEmployers: "Žádní zaměstnavatelé.", noAgencies: "Žádné agentury.",
        noFirmData: "Data podle firem nebyla nalezena.", noReportEmployees: "Pracovníci pro tyto filtry nebyli nalezeni.",
        reportRange: "{firms} firem, {employees} pracovníků", columnSettingsTitle: "Nastavení sloupců",
        columnSettingsDesc: "Vyberte, které sloupce se mají zobrazit, a změňte jejich pořadí jako v programu.", cancel: "Zrušit", done: "Hotovo",
        placeholderText: "Tento modul bude připojen v dalších etapách. Menu a ovládací panel jsou nyní připravené.",
        financePageTitle: "Finance a výplaty",
        financeFiltersTitle: "⌃ Období a firma",
        financePeriodLabel: "Období",
        financeEntriesTitle: "Výplaty zaměstnancům",
        financeColName: "Jméno",
        financeColHours: "Hodiny",
        financeColRate: "Sazba",
        financeColGross: "Hrubá",
        financeColAdvance: "Záloha",
        financeColNet: "Čistá (uložená)",
        financeColNote: "Poznámka",
        financeExpensesTitle: "Náklady firmy",
        financeExpenseName: "Název",
        financeExpenseAmount: "Částka",
        financeNoMonths: "Nejsou uložené měsíce mezd.",
        financeNoEntries: "Pro zvolené období nejsou žádné záznamy.",
        financeNoExpenses: "Pro tento filtr nejsou žádné náklady.",
        financeLoadError: "Nepodařilo se načíst finanční data.",
        financePillEmpty: "žádná data",
        financeCardTitle: "Finance",
        financeCardDesc: "Finanční evidence a přehledy",
        tablesCardTitle: "Tabulky",
        tablesCardDesc: "Generování tabulek pro tisk",
        advanceTableTitle: "Tabulka záloh",
        advanceTableDesc: "Výběr firem pro tisk záloh",
        paymentSignTitle: "Podpisy výplat",
        paymentSignDesc: "Seznam pracovníků pro podpisy výplat",
        financeReadOnlySave: "Ukládání v programu",
        financeMarkAllPaid: "✓ Označit vše",
        financeStatEmployees: "Pracovníci",
        financeStatHours: "Hodiny",
        financeStatPaid: "Zaplaceno",
        financeFirmBreakdown: "Podle firem",
        financeGrandTotal: "Celková výplata",
        financeExpenseTotal: "Náklady celkem",
        financeExtraStats: "Další statistika"
      },
      en: {
        globalSearchPlaceholder: "Search employees, templates, archive...",
        firmSearchPlaceholder: "Search firm",
        dashboardSearchPlaceholder: "Search by name, firm, or position",
        employeesSearchPlaceholder: "Search by name, passport, visa, insurance",
        reportSearchPlaceholder: "Search by name, passport, visa, citizenship, phone",
        allFirms: "All firms", employees: "Employees", problems: "Problems", templates: "Templates", finance: "Finance and tables",
        report: "Report", archive: "Archive", history: "Action history", candidates: "Candidates", ai: "AI Assistant",
        dashboard: "Dashboard", invoices: "Invoices", deleted: "Recently deleted", news: "News",
        webReadonly: "Web access • view only", connecting: "Connecting...", serverOk: "Running locally", serverUnknown: "Unknown state", serverError: "Error",
        mainMenu: "Main menu", menu: "Menu", backToMenu: "Back to menu", firms: "Firms", total: "Total",
        active: "Active", documentProblems: "Document problems", docProblemsShort: "Doc. problems", newThisMonth: "New this month",
        missingPhoto: "No photo", sharedFromApp: "From the main app database", activeByFirms: "Active employees by firm",
        photoControl: "For quick document control", missingDocsNote: "No passport, visa, or insurance",
        readOnlySubtitle: "Main app data. View only for now.", chooseFirmLeft: "Choose a firm on the left or view all firms.",
        selectFirmSubtitle: "Choose a firm through ☰ or view all firms.", firmPrefix: "Firm", positionLabel: "Position",
        startLabel: "Start", statusLabel: "Status", phoneLabel: "Phone", employeesCount: "Employees", addressLabel: "Address",
        noFirms: "No firms yet.", noFirmsFound: "No firms found.", noEmployees: "No employees found.",
        noEmployeesFilter: "No employees found. Choose another firm or change search.", loading: "Loading...",
        all: "All", missing: "Missing", expired: "Expired", expiringSoon: "Expiring soon",
        visa: "Visa", workPermit: "Permit", passport: "Passport", insurance: "Insurance", photo: "Photo",
        none: "missing", expiredShort: "expired", soon: "soon", expiredFull: "expired", ok: "OK",
        documentProblemsTitle: "Document problems", problemsSubtitle: "Control missing, expired, and nearly expired documents.",
        allFirmsProblemsSubtitle: "All firms. Control missing, expired, and nearly expired documents.",
        totalProblems: "Total problems", missingDocuments: "Missing documents", expiredLabel: "Expired", expiresIn30: "Expires within 30 days",
        noProblemsByFilter: "No problems for this filter.", columns: "Columns", resetColumns: "Reset columns",
        configureExport: "Configure export", filters: "Filters", employers: "Employers", agencies: "Agencies", from: "From", to: "To",
        show: "Show", summary: "Summary", firmDetails: "Firm details", withoutPermit: "No permit", ended: "Ended",
        allEmployers: "All employers", allAgencies: "All agencies", noEmployers: "No employers.", noAgencies: "No agencies.",
        noFirmData: "No firm data found.", noReportEmployees: "No employees found for these filters.",
        reportRange: "{firms} firms, {employees} employees", columnSettingsTitle: "Column settings",
        columnSettingsDesc: "Choose which columns to show and change their order like in the app.", cancel: "Cancel", done: "Done",
        placeholderText: "This module will be connected in the next stages. The menu and dashboard are ready now.",
        financePageTitle: "Finance and payroll",
        financeFiltersTitle: "⌃ Period and firm",
        financePeriodLabel: "Period",
        financeEntriesTitle: "Employee payments",
        financeColName: "Name",
        financeColHours: "Hours",
        financeColRate: "Rate",
        financeColGross: "Gross",
        financeColAdvance: "Advance",
        financeColNet: "Net (saved)",
        financeColNote: "Note",
        financeExpensesTitle: "Firm expenses",
        financeExpenseName: "Description",
        financeExpenseAmount: "Amount",
        financeNoMonths: "No salary months found in the database.",
        financeNoEntries: "No entries for the selected period.",
        financeNoExpenses: "No expenses for this filter.",
        financeLoadError: "Could not load finance data.",
        financePillEmpty: "no data",
        financeCardTitle: "Finance",
        financeCardDesc: "Financial reporting and accounting",
        tablesCardTitle: "Tables",
        tablesCardDesc: "Generate printable tables",
        advanceTableTitle: "Advance table",
        advanceTableDesc: "Choose firms for advance table printing",
        paymentSignTitle: "Payment signatures",
        paymentSignDesc: "Employee list for salary signatures",
        financeReadOnlySave: "Save in desktop app",
        financeMarkAllPaid: "✓ Mark all",
        financeStatEmployees: "Employees",
        financeStatHours: "Hours",
        financeStatPaid: "Paid",
        financeFirmBreakdown: "By firm",
        financeGrandTotal: "Grand total",
        financeExpenseTotal: "Expenses total",
        financeExtraStats: "Additional statistics"
      }
    };

    var reportColumnTranslations = {
      cs: {
        name: "Jméno", type: "Název pracovního povolení", documentType: "Typ dokumentu", passportNumber: "Číslo pasu nebo ID karty",
        visaNumber: "Číslo víza", visaAuthority: "Orgán, který vydal vízum", workAddress: "Adresa výkonu práce",
        highestEducation: "Nejvyšší dosažené vzdělání", birthDate: "Datum narození", gender: "Pohlaví",
        addressCz: "Úplná adresa pobytu", addressAbroad: "Adresa v zahraničí", passportIssuedBy: "Orgán vydání pasu",
        positionCode: "Kód pozice CZ-NACE", agency: "Agentura", passportExpiry: "Pas do", visaExpiry: "Vízum do",
        insuranceExpiry: "Pojištění do", startDate: "Nástup", endDate: "Ukonč.", phone: "Telefon",
        bankAccount: "Bankovní účet", bankName: "Banka", position: "Pozice", visaStartDate: "Vízum platné od",
        citizenship: "Občanství", birthCity: "Město narození", birthCountry: "Země narození"
      },
      en: {
        name: "Name", type: "Work permit name", documentType: "Document type", passportNumber: "Passport or ID card number",
        visaNumber: "Visa number", visaAuthority: "Visa authority", workAddress: "Work address",
        highestEducation: "Highest education", birthDate: "Birth date", gender: "Gender",
        addressCz: "Full residence address", addressAbroad: "Address abroad", passportIssuedBy: "Passport issued by",
        positionCode: "CZ-NACE position code", agency: "Agency", passportExpiry: "Passport to", visaExpiry: "Visa to",
        insuranceExpiry: "Insurance to", startDate: "Start", endDate: "End", phone: "Phone",
        bankAccount: "Bank account", bankName: "Bank", position: "Position", visaStartDate: "Visa valid from",
        citizenship: "Citizenship", birthCity: "Birth city", birthCountry: "Birth country"
      }
    };

    Object.assign(webExtraTranslations.uk, {
      docsTab: "Документи", profileTab: "Анкета", historyTab: "Історія", salaryTab: "Зарплата",
      employeeDocuments: "Документи працівника", filesPreview: "Перегляд файлів з анкети", employeeProfile: "Анкета працівника",
      readOnly: "Тільки перегляд", editProfile: "Редагувати анкету", editOnlyApp: "Редагування доступне лише в основній програмі.",
      changeHistory: "Історія змін", fromMainApp: "З основної програми", noHistory: "Записів історії ще немає.",
      salaryHistory: "Історія нарахувань", noSalary: "Записів зарплати ще немає.", loadingProfile: "Завантаження анкети…"
    });
    Object.assign(webExtraTranslations.cs, {
      docsTab: "Dokumenty", profileTab: "Dotazník", historyTab: "Historie", salaryTab: "Mzda",
      employeeDocuments: "Dokumenty pracovníka", filesPreview: "Náhled souborů z dotazníku", employeeProfile: "Dotazník pracovníka",
      readOnly: "Pouze prohlížení", editProfile: "Upravit dotazník", editOnlyApp: "Úpravy jsou dostupné pouze v hlavním programu.",
      changeHistory: "Historie změn", fromMainApp: "Z hlavního programu", noHistory: "Zatím nejsou žádné záznamy historie.",
      salaryHistory: "Historie výpočtů", noSalary: "Zatím nejsou žádné záznamy mzdy.", loadingProfile: "Načítání dotazníku…"
    });
    Object.assign(webExtraTranslations.en, {
      docsTab: "Documents", profileTab: "Profile", historyTab: "History", salaryTab: "Salary",
      employeeDocuments: "Employee documents", filesPreview: "File previews from the profile", employeeProfile: "Employee profile",
      readOnly: "View only", editProfile: "Edit profile", editOnlyApp: "Editing is available only in the main app.",
      changeHistory: "Change history", fromMainApp: "From the main app", noHistory: "No history records yet.",
      salaryHistory: "Accrual history", noSalary: "No salary records yet.", loadingProfile: "Loading profile…"
    });

    function webT(key) {
      var lang = state.webSettings.language || "uk";
      return (webTranslations[lang] && webTranslations[lang][key])
        || (webExtraTranslations[lang] && webExtraTranslations[lang][key])
        || webTranslations.uk[key]
        || webExtraTranslations.uk[key]
        || key;
    }

    function reportColumnLabel(col) {
      var lang = state.webSettings.language || "uk";
      return (reportColumnTranslations[lang] && reportColumnTranslations[lang][col.key]) || col.label;
    }

    var employeeProfileTranslations = {
      cs: {
        "Особисті дані": "Osobní údaje", "Ім'я": "Jméno", "Прізвище": "Příjmení", "Дата народження": "Datum narození",
        "Найвищий досягнутий рівень освіти": "Nejvyšší dosažené vzdělání", "Стать": "Pohlaví", "Паспортні дані": "Pasové údaje",
        "Номер паспорту": "Číslo pasu", "Орган, що видав документ": "Orgán, který vydal dokument", "Місто / місце видачі (як у програмі)": "Město / místo vydání",
        "Країна народження (паспорт)": "Země narození", "Громадянство": "Občanství", "Країна видачі": "Země vydání",
        "Термін дії паспорта до": "Platnost pasu do", "Номер (віза / документ)": "Číslo víza / dokumentu", "Тип візи": "Typ víza",
        "Віза дійсна від": "Vízum platné od", "Термін дії до": "Platnost do", "Орган, що видав": "Vydal orgán",
        "Посада для дозволу (назва)": "Pozice pro povolení", "Страховка": "Pojištění", "Страхова (код)": "Pojišťovna (kód)",
        "Страхова (повна)": "Pojišťovna (celý název)", "Номер полісу": "Číslo pojistky", "Дозвіл на роботу": "Pracovní povolení",
        "Номер дозволу": "Číslo povolení", "Тип дозволу": "Typ povolení", "Дата видачі": "Datum vydání", "Орган видачі": "Vydávající orgán",
        "Адреса (в країні)": "Adresa v zemi", "Адреса за кордоном": "Adresa v zahraničí", "Вулиця": "Ulice", "Номер будинку": "Číslo domu",
        "Дані про роботу": "Pracovní údaje", "Посада": "Pozice", "Номер посади": "Číslo pozice", "Місячна зарплата (брутто)": "Měsíční mzda (brutto)",
        "Годинна ставка": "Hodinová sazba", "Адреса роботи": "Adresa práce", "Відділ / підрозділ": "Oddělení / úsek",
        "Банківські дані": "Bankovní údaje", "IBAN / номер рахунку": "IBAN / číslo účtu", "Банк": "Banka",
        "Контакти й дати договору": "Kontakty a data smlouvy", "Телефон": "Telefon", "Тип контракту": "Typ smlouvy",
        "Початок роботи": "Začátek práce", "Дата підписання контракту": "Datum podpisu smlouvy", "Кінець роботи": "Konec práce",
        "Статус анкети": "Stav dotazníku"
      },
      en: {
        "Особисті дані": "Personal data", "Ім'я": "First name", "Прізвище": "Last name", "Дата народження": "Birth date",
        "Найвищий досягнутий рівень освіти": "Highest education", "Стать": "Gender", "Паспортні дані": "Passport data",
        "Номер паспорту": "Passport number", "Орган, що видав документ": "Issuing authority", "Місто / місце видачі (як у програмі)": "City / place of issue",
        "Країна народження (паспорт)": "Birth country", "Громадянство": "Citizenship", "Країна видачі": "Issuing country",
        "Термін дії паспорта до": "Passport valid to", "Номер (віза / документ)": "Visa / document number", "Тип візи": "Visa type",
        "Віза дійсна від": "Visa valid from", "Термін дії до": "Valid to", "Орган, що видав": "Issued by",
        "Посада для дозволу (назва)": "Permit position", "Страховка": "Insurance", "Страхова (код)": "Insurance company (code)",
        "Страхова (повна)": "Insurance company (full)", "Номер полісу": "Policy number", "Дозвіл на роботу": "Work permit",
        "Номер дозволу": "Permit number", "Тип дозволу": "Permit type", "Дата видачі": "Issue date", "Орган видачі": "Issuing authority",
        "Адреса (в країні)": "Address in country", "Адреса за кордоном": "Address abroad", "Вулиця": "Street", "Номер будинку": "House number",
        "Дані про роботу": "Work data", "Посада": "Position", "Номер посади": "Position number", "Місячна зарплата (брутто)": "Monthly salary (gross)",
        "Годинна ставка": "Hourly rate", "Адреса роботи": "Work address", "Відділ / підрозділ": "Department / unit",
        "Банківські дані": "Bank data", "IBAN / номер рахунку": "IBAN / account number", "Банк": "Bank",
        "Контакти й дати договору": "Contacts and contract dates", "Телефон": "Phone", "Тип контракту": "Contract type",
        "Початок роботи": "Work start", "Дата підписання контракту": "Contract signing date", "Кінець роботи": "Work end",
        "Статус анкети": "Profile status"
      }
    };

    function epLabelText(label) {
      var lang = state.webSettings.language || "uk";
      return (employeeProfileTranslations[lang] && employeeProfileTranslations[lang][label]) || label;
    }

    function loadWebSettings() {
      try {
        var saved = JSON.parse(localStorage.getItem("agencyContractor.webSettings") || "{}");
        state.webSettings = Object.assign({}, state.webSettings, saved || {});
      } catch (error) {
        console.warn("web settings", error);
      }
      state.webSettings.scale = Math.min(120, Math.max(85, Number(state.webSettings.scale) || 100));
      applyWebSettings();
    }

    function saveWebSettings() {
      localStorage.setItem("agencyContractor.webSettings", JSON.stringify(state.webSettings));
    }

    function applyWebSettings() {
      document.documentElement.lang = state.webSettings.language || "uk";
      document.documentElement.style.fontSize = (state.webSettings.scale || 100) + "%";
      document.body.classList.toggle("theme-light", state.webSettings.theme === "light");
      document.body.classList.toggle("density-compact", state.webSettings.density === "compact");
      document.querySelectorAll("[data-i18n]").forEach(function(node) {
        node.textContent = webT(node.getAttribute("data-i18n"));
      });
      applySiteLanguage();
      updateWebSettingsControls();
      updateWebSettingsStatus();
      if (state.firms.length || state.employees.length || state.reportRows.length) {
        renderMetrics();
        renderFirms();
        renderEmployees();
        renderProblems();
        renderReport();
      }
    }

    function setText(selector, value) {
      var node = document.querySelector(selector);
      if (node) node.textContent = value;
    }

    function setPlaceholder(id, value) {
      var node = byId(id);
      if (node) node.placeholder = value;
    }

    function applySiteLanguage() {
      setPlaceholder("globalSearchInput", webT("globalSearchPlaceholder"));
      setPlaceholder("firmSearchInput", webT("firmSearchPlaceholder"));
      setPlaceholder("searchInput", webT("dashboardSearchPlaceholder"));
      setPlaceholder("employeesSearchInput", webT("employeesSearchPlaceholder"));
      setPlaceholder("reportSearchInput", webT("reportSearchPlaceholder"));
      setText(".firms-side-title", webT("firms"));
      setText(".company-meta", webT("webReadonly"));
      if (!state.selectedFirm) setText("#activeCompanyName", webT("allFirms"));
      var placeholderText = document.querySelector("#placeholderPanel p");
      if (placeholderText) placeholderText.textContent = webT("placeholderText");
      document.querySelectorAll("[data-target]").forEach(function(button) {
        var target = button.getAttribute("data-target");
        var title = button.querySelector(".tile-title");
        if (title && webT(target) !== target) title.textContent = webT(target);
      });
      document.querySelectorAll("[data-back]").forEach(function(button) {
        button.textContent = button.textContent.trim() === "←" ? "←" : "← " + webT("menu");
      });
      setText(".brand h1", webT("dashboard"));
      setText(".brand .subtitle", webT("readOnlySubtitle"));
      var metricLabels = document.querySelectorAll(".metric-label");
      if (metricLabels[0]) metricLabels[0].textContent = webT("firms");
      if (metricLabels[1]) metricLabels[1].textContent = webT("employees");
      if (metricLabels[2]) metricLabels[2].textContent = webT("missingPhoto");
      if (metricLabels[3]) metricLabels[3].textContent = webT("documentProblems");
      var metricNotes = document.querySelectorAll(".metric-note");
      if (metricNotes[0]) metricNotes[0].textContent = webT("sharedFromApp");
      if (metricNotes[1]) metricNotes[1].textContent = webT("activeByFirms");
      if (metricNotes[2]) metricNotes[2].textContent = webT("photoControl");
      if (metricNotes[3]) metricNotes[3].textContent = webT("missingDocsNote");
      var statLabels = document.querySelectorAll(".employee-stat-label");
      if (statLabels[0]) statLabels[0].textContent = webT("total");
      if (statLabels[1]) statLabels[1].textContent = webT("active");
      if (statLabels[2]) statLabels[2].textContent = webT("docProblemsShort");
      if (statLabels[3]) statLabels[3].textContent = webT("newThisMonth");
      var problemLabels = document.querySelectorAll(".problem-summary-label");
      if (problemLabels[0]) problemLabels[0].textContent = webT("totalProblems");
      if (problemLabels[1]) problemLabels[1].textContent = webT("missingDocuments");
      if (problemLabels[2]) problemLabels[2].textContent = webT("expiredLabel");
      if (problemLabels[3]) problemLabels[3].textContent = webT("expiresIn30");
      setText("#problemsView h1", webT("documentProblemsTitle"));
      setText("#employeeFirmPicker", "☰ " + webT("firms"));
      setText("#problemsFirmPicker", "☰ " + webT("firms"));
      setText("#employeeReportButton", webT("report"));
      var employeeActionButtons = document.querySelectorAll("#employeesView .employee-actions .employee-action");
      if (employeeActionButtons[3]) employeeActionButtons[3].textContent = webT("templates");
      if (employeeActionButtons[4]) employeeActionButtons[4].textContent = "+ " + webT("employees");
      setText("#reportView h1", webT("report"));
      setText("#reportColumnsButton", "✎ " + webT("columns"));
      setText("#reportResetColumnsButton", "↻ " + webT("resetColumns"));
      setText(".report-head-actions .primary", "↔ " + webT("configureExport"));
      setText(".report-filter-title", "⌃ " + webT("filters"));
      var reportBoxTitles = document.querySelectorAll(".report-filter-box-title");
      if (reportBoxTitles[0]) reportBoxTitles[0].textContent = "⌂ " + webT("employers");
      if (reportBoxTitles[1]) reportBoxTitles[1].textContent = "♙ " + webT("agencies");
      if (reportBoxTitles[2]) reportBoxTitles[2].textContent = webT("from");
      if (reportBoxTitles[3]) reportBoxTitles[3].textContent = webT("to");
      setText("#reportApplyButton", "▷ " + webT("show"));
      var modeTabs = document.querySelectorAll(".report-mode-tab");
      if (modeTabs[0]) modeTabs[0].textContent = "▣ " + webT("summary");
      if (modeTabs[1]) modeTabs[1].textContent = "♙ " + webT("employees");
      setText(".report-panel-title", "⌂ " + webT("firmDetails"));
      var summaryHeaders = document.querySelectorAll(".report-firm-table th");
      if (summaryHeaders[0]) summaryHeaders[0].textContent = webT("firms");
      if (summaryHeaders[1]) summaryHeaders[1].textContent = webT("total");
      if (summaryHeaders[2]) summaryHeaders[2].textContent = webT("active");
      if (summaryHeaders[3]) summaryHeaders[3].textContent = webT("withoutPermit");
      if (summaryHeaders[4]) summaryHeaders[4].textContent = webT("ended");
      setText(".report-columns-head h3", webT("columnSettingsTitle"));
      setText(".report-columns-head p", webT("columnSettingsDesc"));
      setText("#reportColumnsResetInside", webT("resetColumns"));
      var reportCloseButtons = document.querySelectorAll("[data-report-columns-close]");
      if (reportCloseButtons[1]) reportCloseButtons[1].textContent = webT("cancel");
      setText("#reportColumnsSaveButton", webT("done"));
      translateFilterButtons();
    }

    function translateFilterButtons() {
      document.querySelectorAll("[data-employee-filter='all'], [data-problem-filter='all'], [data-report-filter='all']").forEach(function(node) { node.textContent = webT("all"); });
      document.querySelectorAll("[data-employee-filter='active'], [data-report-filter='active']").forEach(function(node) { node.textContent = webT("active"); });
      document.querySelectorAll("[data-employee-filter='problems'], [data-report-filter='problems']").forEach(function(node) { node.textContent = webT("problems"); });
      setText("[data-employee-filter='visa']", webT("visa"));
      setText("[data-employee-filter='work_permit']", webT("workPermit"));
      setText("[data-problem-filter='missing']", webT("missing"));
      setText("[data-problem-filter='expired']", webT("expired"));
      setText("[data-problem-filter='warn']", webT("expiringSoon"));
      setText("[data-problem-filter='photo']", webT("missingPhoto"));
      setText("[data-report-filter='archived']", webT("archive"));
    }

    function updateWebSettingsControls() {
      if (!byId("webLanguageSelect")) return;
      byId("webLanguageSelect").value = state.webSettings.language || "uk";
      byId("webThemeSelect").value = state.webSettings.theme || "dark";
      byId("webDensitySelect").value = state.webSettings.density || "normal";
      byId("webScaleRange").value = state.webSettings.scale || 100;
      byId("webScaleValue").textContent = (state.webSettings.scale || 100) + "%";
    }

    function updateWebSettingsStatus() {
      if (!byId("settingsFirmStatus")) return;
      var visibleEmployees = state.employees.filter(function(employee) { return isFirmNameVisibleNow(employee.firmName); });
      byId("settingsFirmStatus").textContent = webT("firmsLabel") + ": " + getVisibleFirmsNow().length;
      byId("settingsEmployeeStatus").textContent = webT("employeesLabel") + ": " + visibleEmployees.length;
      byId("settingsSyncStatus").textContent = webT("stateLabel") + ": " + webT("localState");
    }

    function openWebSettings() {
      updateWebSettingsControls();
      updateWebSettingsStatus();
      byId("webSettingsOverlay").classList.add("is-open");
      document.body.classList.add("web-settings-open");
    }

    function closeWebSettings() {
      byId("webSettingsOverlay").classList.remove("is-open");
      document.body.classList.remove("web-settings-open");
    }

    function resetWebSettings() {
      state.webSettings = { language: "uk", theme: "dark", scale: 100, density: "normal" };
      saveWebSettings();
      applyWebSettings();
    }

    var reportDefaultColumns = [
      { key: "name", label: "Ім'я", field: "fullName", visible: true, required: true },
      { key: "type", label: "Назва дозволу на роботу", field: "employeeType", visible: true },
      { key: "documentType", label: "Тип документа", field: "documentType", visible: false },
      { key: "passportNumber", label: "Номер паспорта або ID-карти", field: "passportNumber", visible: false },
      { key: "visaNumber", label: "Номер візи", field: "visaNumber", visible: false },
      { key: "visaAuthority", label: "Орган, що видав візу", field: "visaAuthority", visible: false },
      { key: "workAddress", label: "Адреса виконання роботи", field: "workAddress", visible: false },
      { key: "highestEducation", label: "Найвищий досягнутий рівень освіти", field: "highestEducation", visible: false },
      { key: "birthDate", label: "Дата народження", field: "birthDate", visible: false },
      { key: "gender", label: "Стать", field: "gender", visible: false },
      { key: "addressCz", label: "Повна адреса проживання", field: "addressCz", visible: false },
      { key: "addressAbroad", label: "Адреса за кордоном", field: "addressAbroad", visible: false },
      { key: "passportIssuedBy", label: "Орган видачі паспорта", field: "passportIssuedBy", visible: false },
      { key: "positionCode", label: "Код позиції CZ-NACE", field: "positionCode", visible: false },
      { key: "agency", label: "Агенція", field: "agency", visible: false },
      { key: "passportExpiry", label: "Паспорт до", field: "passportExpiry", visible: true, date: true },
      { key: "visaExpiry", label: "Віза до", field: "visaExpiry", visible: true, date: true },
      { key: "insuranceExpiry", label: "Страховка до", field: "insuranceExpiry", visible: true, date: true },
      { key: "startDate", label: "Наступ", field: "startDate", visible: true },
      { key: "endDate", label: "Закінч.", field: "endDate", visible: true },
      { key: "phone", label: "Телефон", field: "phone", visible: true },
      { key: "bankAccount", label: "Банківський рахунок", field: "bankAccountNumber", visible: false },
      { key: "bankName", label: "Банк", field: "bankName", visible: false },
      { key: "position", label: "Позиція", field: "position", visible: true },
      { key: "visaStartDate", label: "Віза дійсна від", field: "visaStartDate", visible: false },
      { key: "citizenship", label: "Громадянство", field: "citizenship", visible: false },
      { key: "birthCity", label: "Місто народження", field: "birthCity", visible: false },
      { key: "birthCountry", label: "Країна народження", field: "birthCountry", visible: false }
    ];

    function cloneReportColumns(source) {
      return source.map(function(col, index) {
        return {
          key: col.key,
          label: col.label,
          field: col.field,
          visible: col.required ? true : !!col.visible,
          required: !!col.required,
          date: !!col.date,
          order: index
        };
      });
    }

    function loadReportColumns() {
      var columns = cloneReportColumns(reportDefaultColumns);
      try {
        var saved = JSON.parse(localStorage.getItem("agencyContractor.reportColumns") || "[]");
        if (Array.isArray(saved) && saved.length) {
          var savedByKey = new Map(saved.map(function(col) { return [col.key, col]; }));
          columns.forEach(function(col) {
            var savedCol = savedByKey.get(col.key);
            if (!savedCol) return;
            col.visible = col.required ? true : !!savedCol.visible;
            col.order = Number.isFinite(savedCol.order) ? savedCol.order : col.order;
          });
        }
      } catch (error) {
        console.warn("report columns", error);
      }
      state.reportColumns = columns.sort(function(a, b) { return a.order - b.order; });
    }

    function saveReportColumns() {
      state.reportColumns.forEach(function(col, index) { col.order = index; });
      localStorage.setItem("agencyContractor.reportColumns", JSON.stringify(state.reportColumns.map(function(col) {
        return { key: col.key, visible: col.visible, order: col.order };
      })));
    }

    function resetReportColumns() {
      state.reportColumns = cloneReportColumns(reportDefaultColumns);
      saveReportColumns();
      state.reportColumnsBackup = JSON.stringify(state.reportColumns);
      renderReportColumnsList();
      renderReport();
    }

    function visibleReportColumns() {
      return state.reportColumns.filter(function(col) { return col.visible || col.required; });
    }

    function reportCellHtml(row, col) {
      if (col.key === "name") return `<td class="report-name-cell">${escapeHtml(row.fullName)}</td>`;
      if (col.date) return `<td>${renderDateCell(row[col.field])}</td>`;
      return `<td>${escapeHtml(row[col.field])}</td>`;
    }

    function matchesEmployeeFilter(employee) {
      if (state.employeeFilter === "active") return isActiveEmployee(employee);
      if (state.employeeFilter === "problems") return hasDocumentProblem(employee);
      if (state.employeeFilter === "visa") return String(employee.employeeType || "").toLowerCase().indexOf("visa") >= 0 || employee.hasVisa;
      if (state.employeeFilter === "work_permit") return String(employee.employeeType || "").toLowerCase().indexOf("work") >= 0 || !!employee.workPermitName;
      return true;
    }

    function getFilteredEmployees() {
      var query = state.query.trim().toLowerCase();
      return state.employees.filter(function(employee) {
        if (!isFirmNameVisibleNow(employee.firmName)) return false;
        if (state.selectedFirm && employee.firmName !== state.selectedFirm) return false;
        if (!matchesEmployeeFilter(employee)) return false;
        if (!query) return true;
        return [
          employee.fullName,
          employee.firmName,
          employee.positionTitle,
          employee.status,
          employee.phone,
          employee.email,
          employee.passportNumber,
          employee.visaNumber,
          employee.insuranceNumber
        ].some(function(value) { return String(value || "").toLowerCase().indexOf(query) >= 0; });
      });
    }

    function renderAvatar(employee, className) {
      var initials = escapeHtml(getInitials(employee.fullName));
      if (employee.photoUrl) {
        return `<div class="${className}"><img src="${escapeHtml(employee.photoUrl)}" alt="${escapeHtml(employee.fullName)}" onerror="this.parentNode.textContent='${initials}'"></div>`;
      }
      return `<div class="${className}">${initials}</div>`;
    }

    function renderMetrics() {
      var visibleFirms = getVisibleFirmsNow();
      var visibleEmployees = state.employees.filter(function(employee) { return isFirmNameVisibleNow(employee.firmName); });
      var missingPhoto = visibleEmployees.filter(function(employee) { return !employee.hasPhoto; }).length;
      var documentProblems = visibleEmployees.filter(hasDocumentProblem).length;
      byId("firmCount").textContent = visibleFirms.length;
      byId("employeeCount").textContent = visibleEmployees.length;
      byId("missingPhotoCount").textContent = missingPhoto;
      byId("documentProblemCount").textContent = documentProblems;
      byId("firmPill").textContent = visibleFirms.length;
      byId("employeePill").textContent = visibleEmployees.length;
      byId("problemTileBadge").textContent = documentProblems;
      byId("sideFirmCount").textContent = visibleFirms.length;
      updateWebSettingsStatus();
    }

    function pct(value, max) {
      var n = Number(value || 0);
      var m = Math.max(1, Number(max || 1));
      return Math.max(0, Math.min(100, Math.round(n / m * 100))) + "%";
    }

    function loadDashboardLayout() {
      try {
        var saved = JSON.parse(localStorage.getItem("agencyContractor.dashboardLayout") || "{}");
        if (saved && Array.isArray(saved.slots) && saved.slots.length === 3) {
          var allowed = ["salary", "efficiency", "expiring"];
          var unique = saved.slots.filter(function(item, index) {
            return allowed.indexOf(item) >= 0 && saved.slots.indexOf(item) === index;
          });
          if (unique.length === 3) state.dashboardLayout.slots = unique;
        }
        if (saved && Number(saved.rightRatio) > .22 && Number(saved.rightRatio) < .78) {
          state.dashboardLayout.rightRatio = Number(saved.rightRatio);
        }
      } catch (error) {
        console.warn("dashboard layout", error);
      }
      applyDashboardRightRatio();
      renderDashboardWidgets();
    }

    function saveDashboardLayout() {
      localStorage.setItem("agencyContractor.dashboardLayout", JSON.stringify(state.dashboardLayout));
    }

    function applyDashboardRightRatio() {
      var ratio = Math.max(.22, Math.min(.78, Number(state.dashboardLayout.rightRatio || .42)));
      state.dashboardLayout.rightRatio = ratio;
      byId("dashRightStack").style.gridTemplateRows = "minmax(180px, " + ratio.toFixed(3) + "fr) 10px minmax(180px, " + (1 - ratio).toFixed(3) + "fr)";
    }

    function dashboardWidgetHtml(type) {
      if (type === "efficiency") {
        return `<article class="dash-widget" data-dashboard-widget="efficiency" draggable="true">
          <div class="dash-widget-head"><button class="dash-drag-handle" type="button" title="Перетягнути">☰</button>Ефективність</div>
          <div class="dash-eff-grid">
            <div class="dash-eff-card"><strong id="dashAllTimeEmployees">0</strong><span>Усього працівників за весь час</span></div>
            <div class="dash-eff-card"><strong id="dashGeneratedDocs">0</strong><span>Згенерованих документів</span></div>
            <div class="dash-eff-card"><strong id="dashProgramTime">0 хв</strong><span>Час у програмі</span></div>
            <div class="dash-eff-card"><strong id="dashSavedTime" style="color:#22c55e;">0 хв</strong><span>Зекономлено часу</span></div>
          </div>
          <div class="dash-card-meta" style="margin-bottom:8px;">15 хв за працівника + 8 хв за документ</div>
          <div class="dash-card-meta" style="display:flex;justify-content:space-between;"><span>Зекономлено часу</span><strong id="dashSavedTimeSmall" style="color:#22c55e;">0 хв</strong></div>
          <div class="dash-progress" style="margin-left:0;"><span id="dashSavedBar" style="width:0;background:#22c55e;"></span></div>
          <div class="dash-card-meta" style="display:flex;justify-content:space-between;"><span>Час у програмі</span><strong id="dashProgramTimeSmall" style="color:#38bdf8;">0 хв</strong></div>
          <div class="dash-progress" style="margin-left:0;"><span id="dashProgramBar" style="width:0;background:#38bdf8;"></span></div>
        </article>`;
      }
      if (type === "expiring") {
        return `<article class="dash-widget" data-dashboard-widget="expiring" draggable="true">
          <div class="dash-widget-head"><button class="dash-drag-handle" type="button" title="Перетягнути">☰</button>⚠ Закінчуються найближчим часом</div>
          <div id="dashExpiringList" class="dash-scroll"><div class="empty">Завантаження...</div></div>
        </article>`;
      }
      return `<article class="dash-widget" data-dashboard-widget="salary" draggable="true">
        <div class="dash-widget-head"><button class="dash-drag-handle" type="button" title="Перетягнути">☰</button>▤ Зарплати <span class="dash-widget-sub" id="dashSalaryTotal"></span></div>
        <div id="dashSalaryList" class="dash-scroll"><div class="empty">Завантаження...</div></div>
      </article>`;
    }

    function renderDashboardWidgets() {
      document.querySelectorAll("[data-dashboard-slot]").forEach(function(slot) {
        var index = Number(slot.getAttribute("data-dashboard-slot") || 0);
        slot.innerHTML = dashboardWidgetHtml(state.dashboardLayout.slots[index] || "salary");
      });
    }

    function dashboardEmployeeFromMovement(item) {
      return state.employees.find(function(employee) { return employee.id === item.uniqueId; })
        || state.employees.find(function(employee) { return employee.fullName === item.fullName && employee.firmName === item.firmName; })
        || (item.uniqueId ? { id: item.uniqueId, fullName: item.fullName || "-", firmName: item.firmName || "", positionTitle: "", photoUrl: "" } : null);
    }

    function renderDashboardMovementList(items, kind) {
      if (!items.length) return `<div class="empty">Немає записів.</div>`;
      var isAdded = kind === "added";
      return items.map(function(item) {
        return `<article class="dash-movement-item" data-dashboard-movement-id="${escapeHtml(item.uniqueId || "")}" data-dashboard-movement-name="${escapeHtml(item.fullName || "")}" data-dashboard-movement-firm="${escapeHtml(item.firmName || "")}">
          <div class="dash-movement-sign" style="background:${isAdded ? "rgba(34,197,94,.14);color:#22c55e" : "rgba(239,68,68,.14);color:#ef4444"};">${isAdded ? "+" : "-"}</div>
          <div>
            <div class="dash-movement-name">${escapeHtml(item.fullName || "-")}</div>
            <div class="dash-movement-meta">${escapeHtml(item.firmName || "-")} · ${escapeHtml(item.dateText || "")}</div>
          </div>
        </article>`;
      }).join("");
    }

    function openDashboardMovement() {
      var data = state.dashboard || {};
      var totals = data.totals || {};
      var added = Array.isArray(data.monthlyAdded) ? data.monthlyAdded : [];
      var archived = Array.isArray(data.monthlyArchived) ? data.monthlyArchived : [];
      byId("dashMovementDialogSummary").textContent = totals.monthlyMovementText || "+0 / -0";
      byId("dashMovementAddedBadge").textContent = String(totals.monthlyAdded || added.length || 0);
      byId("dashMovementArchivedBadge").textContent = String(totals.monthlyArchived || archived.length || 0);
      byId("dashMovementAddedList").innerHTML = renderDashboardMovementList(added, "added");
      byId("dashMovementArchivedList").innerHTML = renderDashboardMovementList(archived, "archived");
      byId("dashMovementOverlay").classList.add("is-open");
      document.body.classList.add("dashboard-movement-open");
    }

    function closeDashboardMovement() {
      byId("dashMovementOverlay").classList.remove("is-open");
      document.body.classList.remove("dashboard-movement-open");
    }

    function setupDashboardInteractions() {
      var draggedType = "";
      byId("dashboardView").addEventListener("dragstart", function(event) {
        var widget = event.target.closest("[data-dashboard-widget]");
        if (!widget) return;
        draggedType = widget.getAttribute("data-dashboard-widget") || "";
        event.dataTransfer.effectAllowed = "move";
        event.dataTransfer.setData("text/plain", draggedType);
      });
      byId("dashboardView").addEventListener("dragover", function(event) {
        var slot = event.target.closest("[data-dashboard-slot]");
        if (!slot || !draggedType) return;
        event.preventDefault();
        slot.classList.add("is-drag-over");
        event.dataTransfer.dropEffect = "move";
      });
      byId("dashboardView").addEventListener("dragleave", function(event) {
        var slot = event.target.closest("[data-dashboard-slot]");
        if (slot) slot.classList.remove("is-drag-over");
      });
      byId("dashboardView").addEventListener("drop", function(event) {
        var slot = event.target.closest("[data-dashboard-slot]");
        document.querySelectorAll("[data-dashboard-slot]").forEach(function(item) { item.classList.remove("is-drag-over"); });
        if (!slot || !draggedType) return;
        event.preventDefault();
        var targetIndex = Number(slot.getAttribute("data-dashboard-slot") || 0);
        var sourceIndex = state.dashboardLayout.slots.indexOf(draggedType);
        if (sourceIndex >= 0 && targetIndex >= 0 && targetIndex < state.dashboardLayout.slots.length && sourceIndex !== targetIndex) {
          var moved = state.dashboardLayout.slots[sourceIndex];
          state.dashboardLayout.slots[sourceIndex] = state.dashboardLayout.slots[targetIndex];
          state.dashboardLayout.slots[targetIndex] = moved;
          saveDashboardLayout();
          renderDashboard();
        }
        draggedType = "";
      });
      byId("dashboardView").addEventListener("dragend", function() {
        draggedType = "";
        document.querySelectorAll("[data-dashboard-slot]").forEach(function(item) { item.classList.remove("is-drag-over"); });
      });

      var resizing = null;
      byId("dashRightSplitter").addEventListener("pointerdown", function(event) {
        event.preventDefault();
        var stack = byId("dashRightStack");
        resizing = {
          startY: event.clientY,
          startRatio: state.dashboardLayout.rightRatio,
          height: Math.max(1, stack.getBoundingClientRect().height - 10)
        };
        event.currentTarget.setPointerCapture(event.pointerId);
      });
      byId("dashRightSplitter").addEventListener("pointermove", function(event) {
        if (!resizing) return;
        var next = resizing.startRatio + ((event.clientY - resizing.startY) / resizing.height);
        state.dashboardLayout.rightRatio = Math.max(.22, Math.min(.78, next));
        applyDashboardRightRatio();
      });
      function stopResize() {
        if (!resizing) return;
        resizing = null;
        saveDashboardLayout();
      }
      byId("dashRightSplitter").addEventListener("pointerup", stopResize);
      byId("dashRightSplitter").addEventListener("pointercancel", stopResize);
    }

    function renderDashboard() {
      var data = state.dashboard || {};
      var totals = data.totals || {};
      renderDashboardWidgets();
      setTextById("dashboardSubtitle", (totals.employees || 0) + " працівників · " + (totals.problems || 0) + " проблем");
      setTextById("firmCount", totals.companies || 0);
      setTextById("employeeCount", totals.employees || 0);
      setTextById("documentProblemCount", totals.problems || 0);
      setTextById("dashMovementValue", totals.monthlyMovementText || "+0 / -0");
      setTextById("dashAddedCount", totals.monthlyAdded || 0);
      setTextById("dashArchivedCount", totals.monthlyArchived || 0);
      setTextById("dashProblemTrend", totals.problemTrend || "Все добре");
      setStyleById("dashAddedBar", "width", pct(totals.monthlyAdded, totals.movementMax));
      setStyleById("dashArchivedBar", "width", pct(totals.monthlyArchived, totals.movementMax));
      setTextById("dashAllTimeEmployees", totals.totalEmployeesAllTime || 0);
      setTextById("dashGeneratedDocs", totals.generatedDocuments || 0);
      setTextById("dashProgramTime", totals.programTimeText || "0 хв");
      setTextById("dashSavedTime", totals.savedTimeText || "0 хв");
      setTextById("dashProgramTimeSmall", totals.programTimeText || "0 хв");
      setTextById("dashSavedTimeSmall", totals.savedTimeText || "0 хв");
      setStyleById("dashProgramBar", "width", pct(totals.programMinutes, totals.efficiencyMaxMinutes));
      setStyleById("dashSavedBar", "width", pct(totals.savedMinutes, totals.efficiencyMaxMinutes));
      setTextById("dashSalaryTotal", data.salaryTotalText || "");

      var salaryMonths = Array.isArray(data.salaryMonths) ? data.salaryMonths : [];
      var salaryList = byId("dashSalaryList");
      if (salaryList) salaryList.innerHTML = salaryMonths.length
        ? salaryMonths.map(function(item) {
          var color = item.statusColor || "#22c55e";
          return `<article class="dash-salary-card">
            <div class="dash-salary-row">
              <div class="dash-status-icon" style="background:${escapeHtml(color)}22;color:${escapeHtml(color)};">${escapeHtml(item.statusIcon || "✓")}</div>
              <div>
                <div class="dash-card-title">${escapeHtml(item.monthLabel)}</div>
                <div class="dash-card-meta">${escapeHtml(item.countText || "")}</div>
              </div>
              <div class="dash-money">${escapeHtml(fmtKc(item.totalGross || 0))}</div>
            </div>
            <div class="dash-progress"><span style="width:${pct(Number(item.paidRatio || 0) * 100, 100)};background:${escapeHtml(color)};"></span></div>
            <div class="dash-salary-foot">
              <span style="color:${escapeHtml(color)};">${escapeHtml(fmtKc(item.totalPaid || 0))} / ${escapeHtml(fmtKc(item.totalNet || 0))}</span>
              <span>До виплати: ${escapeHtml(fmtKc(item.totalNet || 0))}</span>
            </div>
            ${Number(item.totalExpenses || 0) ? `<div class="dash-card-meta" style="margin-left:44px;color:#f59e0b;">Витрати: ${escapeHtml(fmtKc(item.totalExpenses))}</div>` : ""}
            <div class="dash-card-meta" style="margin-left:44px;color:#38bdf8;font-weight:850;">Загальна виплата: ${escapeHtml(fmtKc(item.grandTotal || 0))}</div>
          </article>`;
        }).join("")
        : `<div class="empty">Місяців зарплати ще немає.</div>`;

      var expiring = Array.isArray(data.expiringDocs) ? data.expiringDocs : [];
      var expiringList = byId("dashExpiringList");
      if (expiringList) expiringList.innerHTML = expiring.length
        ? expiring.map(function(item) {
          var color = item.severityColor || "#ef4444";
          return `<article class="dash-expiring-card" data-dashboard-employee-id="${escapeHtml(item.uniqueId || "")}">
            <div class="dash-status-icon" style="background:${escapeHtml(color)}22;color:${escapeHtml(color)};">!</div>
            <div>
              <div class="dash-card-title">${escapeHtml(item.title)}</div>
              <div class="dash-card-meta">${escapeHtml(item.subtitle)} · ${escapeHtml(item.companyName)}</div>
            </div>
            <div class="dash-severity" style="background:${escapeHtml(color)}22;color:${escapeHtml(color)};">${escapeHtml(item.severityLabel)}</div>
          </article>`;
        }).join("")
        : `<div class="empty">Документи в порядку.</div>`;
    }

    function showHome() {
      byId("moduleHome").classList.remove("is-hidden");
      byId("dashboardView").classList.remove("is-active");
      byId("employeesView").classList.remove("is-active");
      byId("problemsView").classList.remove("is-active");
      byId("reportView").classList.remove("is-active");
      byId("financeView").classList.remove("is-active");
      byId("placeholderPanel").classList.remove("is-active");
    }

    function showDashboard() {
      byId("moduleHome").classList.add("is-hidden");
      byId("employeesView").classList.remove("is-active");
      byId("problemsView").classList.remove("is-active");
      byId("reportView").classList.remove("is-active");
      byId("financeView").classList.remove("is-active");
      byId("placeholderPanel").classList.remove("is-active");
      byId("dashboardView").classList.add("is-active");
      renderDashboard();
    }

    function showEmployees() {
      byId("moduleHome").classList.add("is-hidden");
      byId("dashboardView").classList.remove("is-active");
      byId("placeholderPanel").classList.remove("is-active");
      byId("problemsView").classList.remove("is-active");
      byId("reportView").classList.remove("is-active");
      byId("financeView").classList.remove("is-active");
      byId("employeesView").classList.add("is-active");
      renderEmployees();
    }

    function showProblems() {
      byId("moduleHome").classList.add("is-hidden");
      byId("dashboardView").classList.remove("is-active");
      byId("employeesView").classList.remove("is-active");
      byId("reportView").classList.remove("is-active");
      byId("financeView").classList.remove("is-active");
      byId("placeholderPanel").classList.remove("is-active");
      byId("problemsView").classList.add("is-active");
      renderProblems();
    }

    function showReport() {
      byId("moduleHome").classList.add("is-hidden");
      byId("dashboardView").classList.remove("is-active");
      byId("employeesView").classList.remove("is-active");
      byId("problemsView").classList.remove("is-active");
      byId("financeView").classList.remove("is-active");
      byId("placeholderPanel").classList.remove("is-active");
      byId("reportView").classList.add("is-active");
      renderReport();
    }

    function fmtMoney(value, digits) {
      if (value == null || value === "") return "-";
      var n = Number(value);
      if (!isFinite(n)) return "-";
      var d = digits == null ? 0 : digits;
      return n.toLocaleString(undefined, { minimumFractionDigits: d, maximumFractionDigits: d });
    }

    function fmtKc(value) {
      return fmtMoney(value, 0) + " Kč";
    }

    function formatFinancePeriodTitle(year, month) {
      var lang = state.webSettings.language || "uk";
      var loc = lang === "cs" ? "cs-CZ" : lang === "en" ? "en-GB" : "uk-UA";
      try {
        return new Date(year, month - 1, 1).toLocaleString(loc, { month: "long", year: "numeric" });
      } catch (e) {
        return year + "-" + String(month).padStart(2, "0");
      }
    }

    function showFinanceMenu() {
      byId("financeMenuScreen").classList.remove("is-hidden");
      byId("financeSalaryScreen").classList.add("is-hidden");
      byId("financeTablesScreen").classList.add("is-hidden");
    }

    function showFinanceTablesMenu() {
      byId("financeMenuScreen").classList.add("is-hidden");
      byId("financeSalaryScreen").classList.add("is-hidden");
      byId("financeTablesScreen").classList.remove("is-hidden");
    }

    function showFinanceSalary() {
      byId("financeMenuScreen").classList.add("is-hidden");
      byId("financeTablesScreen").classList.add("is-hidden");
      byId("financeSalaryScreen").classList.remove("is-hidden");
      loadFinanceScreen();
    }

    function openFinanceSummaryPanel() {
      byId("financeSidePanel").classList.add("is-open");
      byId("financeSideBackdrop").classList.add("is-open");
      document.body.classList.add("finance-summary-open");
    }

    function closeFinanceSummaryPanel() {
      byId("financeSidePanel").classList.remove("is-open");
      byId("financeSideBackdrop").classList.remove("is-open");
      document.body.classList.remove("finance-summary-open");
    }

    async function loadFinanceScreen() {
      byId("financeMonthTitle").textContent = formatFinancePeriodTitle(state.financeYear, state.financeMonth);
      byId("financeEntriesBody").innerHTML = `<tr><td><div class="empty">${escapeHtml(webT("loading"))}</div></td></tr>`;
      byId("financeFirmSummaryList").innerHTML = `<div class="empty">${escapeHtml(webT("loading"))}</div>`;
      byId("financeExpensesList").innerHTML = `<div class="empty">${escapeHtml(webT("loading"))}</div>`;
      try {
        var url = "/api/v1/finance/screen?year=" + state.financeYear + "&month=" + state.financeMonth;
        if (state.financeFirm) url += "&firm=" + encodeURIComponent(state.financeFirm);
        if (state.financeSearch) url += "&search=" + encodeURIComponent(state.financeSearch);
        var data = await fetchJson(url, 3);
        renderFinanceScreen(data);
      } catch (error) {
        var fallbackLoaded = await loadFinanceLegacyFallback();
        if (!fallbackLoaded) {
          renderFinanceLocalFirmFallback();
        }
        console.error(error);
      }
    }

    async function loadFinanceLegacyFallback() {
      try {
        var url = "/api/v1/finance/payments?year=" + state.financeYear + "&month=" + state.financeMonth;
        if (state.financeFirm) url += "&firm=" + encodeURIComponent(state.financeFirm);
        var response = await fetchWithRetry(url, null, 2);
        if (!response.ok) return false;
        var data = await response.json();
        var rows = (data.entries || []).map(function(row) {
          var net = row.netSalary != null ? row.netSalary : (row.savedNetSalary != null && row.savedNetSalary !== 0 ? row.savedNetSalary : (Number(row.grossSalary || 0) - Number(row.advance || 0)));
          return Object.assign({}, row, {
            netSalary: net,
            isPaid: row.status === "paid",
            isFinished: !!row.isFinished,
            customValues: row.customValues || {}
          });
        });
        var expenses = data.expenses || [];
        var visibleRows = state.financeSearch
          ? rows.filter(function(row) {
            var q = state.financeSearch.toLowerCase();
            return String(row.fullName || "").toLowerCase().indexOf(q) >= 0
              || String(row.firmName || "").toLowerCase().indexOf(q) >= 0;
          })
          : rows;
        var byFirm = new Map();
        rows.forEach(function(row) {
          var firm = row.firmName || "";
          if (!byFirm.has(firm)) byFirm.set(firm, []);
          byFirm.get(firm).push(row);
        });
        var totalNet = visibleRows.reduce(function(sum, row) { return sum + Number(row.netSalary || 0); }, 0);
        var totalGross = visibleRows.reduce(function(sum, row) { return sum + Number(row.grossSalary || 0); }, 0);
        var totalHours = visibleRows.reduce(function(sum, row) { return sum + Number(row.hoursWorked || 0); }, 0);
        var totalExpenses = expenses.reduce(function(sum, item) { return sum + Number(item.amount || 0); }, 0);
        var paidCount = visibleRows.filter(function(row) { return row.isPaid; }).length;
        renderFinanceScreen({
          year: state.financeYear,
          month: state.financeMonth,
          selectedFirm: state.financeFirm,
          nextMonthExists: true,
          columns: [],
          totals: {
            totalEmployees: visibleRows.length,
            totalHours: totalHours,
            totalGross: totalGross,
            totalNet: totalNet,
            totalExpenses: totalExpenses,
            grandTotal: totalNet + totalExpenses,
            paidCount: paidCount,
            paidDisplay: paidCount + "/" + visibleRows.length,
            allPaid: visibleRows.length > 0 && paidCount === visibleRows.length
          },
          firmSummaries: Array.from(byFirm.entries()).map(function(pair) {
            var firmRows = pair[1];
            return {
              firmName: pair[0],
              totalGross: firmRows.reduce(function(sum, row) { return sum + Number(row.grossSalary || 0); }, 0),
              totalNet: firmRows.reduce(function(sum, row) { return sum + Number(row.netSalary || 0); }, 0),
              totalHours: firmRows.reduce(function(sum, row) { return sum + Number(row.hoursWorked || 0); }, 0),
              employeeCount: firmRows.length,
              paidCount: firmRows.filter(function(row) { return row.isPaid; }).length,
              isSelected: state.financeFirm === pair[0]
            };
          }).sort(function(a, b) { return b.totalGross - a.totalGross; }),
          entries: visibleRows.sort(function(a, b) {
            return String(a.firmName || "").localeCompare(String(b.firmName || "")) || String(a.fullName || "").localeCompare(String(b.fullName || ""));
          }),
          expenses: expenses
        });
        return true;
      } catch (error) {
        console.error("finance fallback", error);
        return false;
      }
    }

    function renderFinanceLocalFirmFallback() {
      var visibleFirms = getVisibleFirmsNow();
      var employeesByFirm = new Map();
      state.employees.forEach(function(employee) {
        if (!isFirmNameVisibleNow(employee.firmName)) return;
        employeesByFirm.set(employee.firmName, (employeesByFirm.get(employee.firmName) || 0) + 1);
      });
      var summaries = visibleFirms.map(function(firm) {
        return {
          firmName: firm.name,
          totalGross: 0,
          totalNet: 0,
          totalHours: 0,
          employeeCount: employeesByFirm.get(firm.name) || 0,
          paidCount: 0,
          isSelected: state.financeFirm === firm.name
        };
      });
      var cols = financeColumns([]);
      renderFinanceHeader(cols);
      byId("financeEntriesBody").innerHTML = `<tr><td colspan="${cols.length}"><div class="empty">${escapeHtml(webT("financeLoadError"))}</div></td></tr>`;
      byId("financeTotalEmployees").textContent = "0";
      byId("financeTotalHours").textContent = "0";
      byId("financeTotalGross").textContent = "0 Kč";
      byId("financeTotalNet").textContent = "0 Kč";
      byId("financePaidDisplay").textContent = "0/0";
      byId("financeSideNet").textContent = "0 Kč";
      byId("financeGrandTotal").textContent = "0 Kč";
      byId("financeExpenseTotal").textContent = "0 Kč";
      byId("financeExpenseHeader").textContent = webT("financeExpensesTitle");
      renderFinanceFirmSummaries(summaries);
      byId("financeExpensesList").innerHTML = `<div class="empty">${escapeHtml(webT("financeLoadError"))}</div>`;
    }

    function financeColSpan(columns) {
      return (columns || []).length;
    }

    function loadFinanceColumnWidths() {
      try {
        state.financeColumnWidths = JSON.parse(localStorage.getItem("agencyContractor.financeColumnWidths") || "{}") || {};
      } catch (error) {
        state.financeColumnWidths = {};
      }
    }

    function saveFinanceColumnWidths() {
      localStorage.setItem("agencyContractor.financeColumnWidths", JSON.stringify(state.financeColumnWidths || {}));
    }

    function financeColumns(columns) {
      var result = [
        { key: "name", label: webT("financeColName"), width: 190 },
        { key: "hours", label: webT("financeColHours"), width: 72 },
        { key: "rate", label: webT("financeColRate"), width: 70 },
        { key: "gross", label: webT("financeColGross"), width: 86 },
        { key: "advance", label: webT("financeColAdvance"), width: 82 }
      ];
      (columns || []).forEach(function(col) {
        result.push({ key: "custom:" + col.id, label: col.name, width: 88, customId: col.id });
      });
      result.push(
        { key: "net", label: webT("financeColNet"), width: 98 },
        { key: "paid", label: "✓", width: 46 },
        { key: "note", label: webT("financeColNote"), width: 260 }
      );
      return result.map(function(col) {
        var saved = Number(state.financeColumnWidths[col.key]);
        col.width = isFinite(saved) && saved >= 42 ? saved : col.width;
        return col;
      });
    }

    function financeCellStyle(col) {
      return `style="width:${col.width}px;min-width:${col.width}px;max-width:${col.width}px;"`;
    }

    function renderFinanceHeader(cols) {
      byId("financeTableHead").innerHTML = `
        <tr>
          ${cols.map(function(col) {
            return `<th data-finance-col="${escapeHtml(col.key)}" ${financeCellStyle(col)}>${escapeHtml(col.label)}<span class="finance-resize-handle" data-finance-resize="${escapeHtml(col.key)}"></span></th>`;
          }).join("")}
        </tr>`;
    }

    function setupFinanceColumnResizing() {
      var active = null;
      byId("financeTableHead").addEventListener("pointerdown", function(event) {
        var handle = event.target.closest("[data-finance-resize]");
        if (!handle) return;
        event.preventDefault();
        var key = handle.getAttribute("data-finance-resize");
        var th = handle.closest("th");
        active = {
          key: key,
          startX: event.clientX,
          startWidth: th ? th.getBoundingClientRect().width : Number(state.financeColumnWidths[key]) || 80
        };
        handle.setPointerCapture(event.pointerId);
      });
      byId("financeTableHead").addEventListener("pointermove", function(event) {
        if (!active) return;
        var next = Math.max(42, Math.round(active.startWidth + event.clientX - active.startX));
        state.financeColumnWidths[active.key] = next;
        applyFinanceColumnWidth(active.key, next);
      });
      byId("financeTableHead").addEventListener("pointerup", function() {
        if (!active) return;
        saveFinanceColumnWidths();
        active = null;
      });
      byId("financeTableHead").addEventListener("pointercancel", function() {
        if (!active) return;
        saveFinanceColumnWidths();
        active = null;
      });
    }

    function applyFinanceColumnWidth(key, width) {
      document.querySelectorAll("[data-finance-col]").forEach(function(cell) {
        if (cell.getAttribute("data-finance-col") !== key) return;
        cell.style.width = width + "px";
        cell.style.minWidth = width + "px";
        cell.style.maxWidth = width + "px";
      });
    }

    function renderFinanceScreen(data) {
      data = data || {};
      var totals = data.totals || {};
      var entries = Array.isArray(data.entries) ? data.entries : [];
      var columns = Array.isArray(data.columns) ? data.columns : [];
      var expenses = Array.isArray(data.expenses) ? data.expenses : [];
      var firmSummaries = Array.isArray(data.firmSummaries) ? data.firmSummaries : [];
      state.financeYear = data.year || state.financeYear;
      state.financeMonth = data.month || state.financeMonth;
      state.financeFirm = data.selectedFirm || "";
      byId("financeMonthTitle").textContent = formatFinancePeriodTitle(state.financeYear, state.financeMonth);
      byId("financeCreateNextPill").style.display = data.nextMonthExists ? "none" : "inline-flex";
      byId("financeTotalEmployees").textContent = totals.totalEmployees || 0;
      byId("financeTotalHours").textContent = fmtMoney(totals.totalHours || 0, 1);
      byId("financeTotalGross").textContent = fmtKc(totals.totalGross || 0);
      byId("financeTotalNet").textContent = fmtKc(totals.totalNet || 0);
      byId("financePaidDisplay").textContent = totals.paidDisplay || "0/0";
      byId("financeSideNet").textContent = fmtKc(totals.totalNet || 0);
      byId("financeGrandTotal").textContent = fmtKc(totals.grandTotal || 0);
      byId("financeExpenseTotal").textContent = fmtKc(totals.totalExpenses || 0);
      byId("financeExpenseHeader").textContent = webT("financeExpensesTitle") + (expenses.length ? " (" + expenses.length + ")" : "");

      var renderedColumns = financeColumns(columns);
      renderFinanceHeader(renderedColumns);

      renderFinanceEntries(entries, renderedColumns);
      renderFinanceFirmSummaries(firmSummaries);
      renderFinanceExpenses(expenses);
    }

    function renderFinanceEntries(entries, columns) {
      if (!entries.length) {
        byId("financeEntriesBody").innerHTML = `<tr><td colspan="${financeColSpan(columns)}"><div class="empty">${escapeHtml(webT("financeNoEntries"))}</div></td></tr>`;
        return;
      }
      var html = "";
      var currentFirm = null;
      entries.forEach(function(row) {
        if (row.firmName !== currentFirm) {
          currentFirm = row.firmName;
          html += `<tr class="finance-group-row"><td colspan="${financeColSpan(columns)}">▣ ${escapeHtml(currentFirm)}</td></tr>`;
        }
        var byKey = {};
        columns.forEach(function(col) { byKey[col.key] = col; });
        html += `
          <tr class="${row.isPaid ? "is-paid" : ""} ${row.isFinished ? "is-finished" : ""}">
            <td data-finance-col="name" ${financeCellStyle(byKey.name)}><button class="finance-name-link" type="button" data-finance-employee-id="${escapeHtml(row.employeeId || "")}" data-finance-employee-name="${escapeHtml(row.fullName || "")}">${escapeHtml(row.fullName)}</button></td>
            <td data-finance-col="hours" class="finance-num" ${financeCellStyle(byKey.hours)}>${escapeHtml(fmtMoney(row.hoursWorked, 1))}</td>
            <td data-finance-col="rate" class="finance-num" ${financeCellStyle(byKey.rate)}>${escapeHtml(fmtMoney(row.hourlyRate, 0))}</td>
            <td data-finance-col="gross" class="finance-num" style="color:#22c55e;font-weight:900;width:${byKey.gross.width}px;min-width:${byKey.gross.width}px;max-width:${byKey.gross.width}px;">${escapeHtml(fmtMoney(row.grossSalary, 0))}</td>
            <td data-finance-col="advance" class="finance-num" style="color:#f97316;width:${byKey.advance.width}px;min-width:${byKey.advance.width}px;max-width:${byKey.advance.width}px;">${escapeHtml(fmtMoney(row.advance, 0))}</td>
            ${columns.filter(function(col) { return col.customId; }).map(function(col) {
              var value = row.customValues && row.customValues[col.customId] != null ? row.customValues[col.customId] : 0;
              return `<td data-finance-col="${escapeHtml(col.key)}" class="finance-num" ${financeCellStyle(col)}>${escapeHtml(fmtMoney(value, 0))}</td>`;
            }).join("")}
            <td data-finance-col="net" class="finance-num" style="color:#38bdf8;font-weight:900;width:${byKey.net.width}px;min-width:${byKey.net.width}px;max-width:${byKey.net.width}px;">${escapeHtml(fmtMoney(row.netSalary, 0))}</td>
            <td data-finance-col="paid" style="text-align:center;width:${byKey.paid.width}px;min-width:${byKey.paid.width}px;max-width:${byKey.paid.width}px;">${row.isPaid ? "☑" : "☐"}</td>
            <td data-finance-col="note" style="color:rgba(226,232,240,.7);font-style:italic;width:${byKey.note.width}px;min-width:${byKey.note.width}px;max-width:${byKey.note.width}px;">${escapeHtml(safe(row.note))}</td>
          </tr>`;
      });
      byId("financeEntriesBody").innerHTML = html;
    }

    function renderFinanceFirmSummaries(items) {
      byId("financeFirmSummaryList").innerHTML = items.length
        ? items.map(function(item) {
          return `<div class="finance-firm-card ${item.isSelected ? "is-selected" : ""}" data-finance-firm="${escapeHtml(item.firmName)}">
            <div class="finance-firm-name">${escapeHtml(item.firmName)}</div>
            <div class="finance-firm-meta"><span>${item.employeeCount} | ${escapeHtml(fmtMoney(item.totalHours, 1))} h | ✓ ${item.paidCount}/${item.employeeCount}</span><strong>${escapeHtml(fmtKc(item.totalNet))}</strong></div>
          </div>`;
        }).join("")
        : `<div class="empty">${escapeHtml(webT("noFirmData"))}</div>`;
    }

    function renderFinanceExpenses(items) {
      byId("financeExpensesList").innerHTML = items.length
        ? items.map(function(item) {
          return `<div class="finance-expense-card">
            <div class="finance-firm-name">${escapeHtml(item.name)}</div>
            <div class="finance-expense-meta"><span>${escapeHtml(item.firmName)}</span><strong>${escapeHtml(fmtKc(item.amount))}</strong></div>
            <span class="finance-expense-remove">×</span>
          </div>`;
        }).join("")
        : `<div class="empty">${escapeHtml(webT("financeNoExpenses"))}</div>`;
    }

    function changeFinanceMonth(delta) {
      var d = new Date(state.financeYear, state.financeMonth - 1, 1);
      d.setMonth(d.getMonth() + delta);
      state.financeYear = d.getFullYear();
      state.financeMonth = d.getMonth() + 1;
      loadFinanceScreen();
    }

    function showFinance() {
      byId("moduleHome").classList.add("is-hidden");
      byId("dashboardView").classList.remove("is-active");
      byId("employeesView").classList.remove("is-active");
      byId("problemsView").classList.remove("is-active");
      byId("reportView").classList.remove("is-active");
      byId("placeholderPanel").classList.remove("is-active");
      byId("financeView").classList.add("is-active");
      showFinanceMenu();
    }

    function showPlaceholder(title) {
      byId("moduleHome").classList.add("is-hidden");
      byId("dashboardView").classList.remove("is-active");
      byId("employeesView").classList.remove("is-active");
      byId("problemsView").classList.remove("is-active");
      byId("reportView").classList.remove("is-active");
      byId("financeView").classList.remove("is-active");
      byId("placeholderTitle").textContent = title;
      byId("placeholderPanel").classList.add("is-active");
    }

    function openFirmDrawer() {
      byId("firmDrawer").classList.add("is-open");
      byId("drawerBackdrop").classList.add("is-open");
      document.body.classList.add("drawer-open");
    }

    function closeFirmDrawer() {
      byId("firmDrawer").classList.remove("is-open");
      byId("drawerBackdrop").classList.remove("is-open");
      document.body.classList.remove("drawer-open");
    }

    function toggleFirmDrawer() {
      if (byId("firmDrawer").classList.contains("is-open")) closeFirmDrawer();
      else openFirmDrawer();
    }

    function updateClock() {
      var now = new Date();
      byId("clockTime").textContent = now.toLocaleTimeString("uk-UA", { hour: "2-digit", minute: "2-digit" });
      byId("clockDate").textContent = now.toLocaleDateString("uk-UA", { weekday: "short", day: "2-digit", month: "2-digit", year: "numeric" });
    }

    function renderFirms() {
      var visibleFirms = getVisibleFirmsNow();
      var employeesByFirm = new Map();
      for (var i = 0; i < state.employees.length; i++) {
        var employee = state.employees[i];
        if (!isFirmNameVisibleNow(employee.firmName)) continue;
        var key = employee.firmName || "";
        employeesByFirm.set(key, (employeesByFirm.get(key) || 0) + 1);
      }

      var sideFirms = visibleFirms.filter(function(firm) {
        if (!state.firmQuery) return true;
        return String(firm.name || "").toLowerCase().indexOf(state.firmQuery) >= 0;
      });

      byId("firmList").innerHTML = visibleFirms.length
        ? visibleFirms.map(firm => `
          <div class="firm" data-firm-name="${escapeHtml(firm.name)}">
            <div class="firm-name">${escapeHtml(firm.name)}</div>
            <div class="meta">
              ${escapeHtml(webT("employeesCount"))}: ${employeesByFirm.get(firm.name) || 0}<br>
              ICO: ${escapeHtml(firm.ico)}<br>
              ${escapeHtml(webT("addressLabel"))}: ${escapeHtml(firm.legalAddress)}
            </div>
          </div>`).join("")
        : `<div class="empty">${escapeHtml(webT("noFirms"))}</div>`;

      byId("sideFirmList").innerHTML = `
        <button class="firm-side-item ${state.selectedFirm === "" ? "is-selected" : ""}" data-firm-name="">
          <div class="firm-side-name">${escapeHtml(webT("allFirms"))}</div>
          <div class="firm-side-meta">${escapeHtml(webT("employeesCount"))}: ${state.employees.filter(function(employee) { return isFirmNameVisibleNow(employee.firmName); }).length}</div>
        </button>`
        + (sideFirms.length
          ? sideFirms.map(firm => `
            <button class="firm-side-item ${state.selectedFirm === firm.name ? "is-selected" : ""}" data-firm-name="${escapeHtml(firm.name)}">
              <div class="firm-side-name">${escapeHtml(firm.name)}</div>
              <div class="firm-side-meta">${escapeHtml(webT("employeesCount"))}: ${employeesByFirm.get(firm.name) || 0}<br>ICO: ${escapeHtml(firm.ico)}</div>
            </button>`).join("")
          : `<div class="empty">${escapeHtml(webT("noFirmsFound"))}</div>`);
    }

    function renderEmployees() {
      var employees = getFilteredEmployees();

      byId("employeePill").textContent = employees.length;
      byId("employeesViewCount").textContent = employees.length;
      byId("employeesActiveCount").textContent = employees.filter(isActiveEmployee).length;
      byId("employeesProblemCount").textContent = employees.filter(hasDocumentProblem).length;
      byId("employeesNewMonthCount").textContent = employees.filter(isNewThisMonth).length;
      byId("employeesViewTitle").textContent = state.selectedFirm || webT("employees");
      byId("employeesCompanyAvatar").textContent = getInitials(state.selectedFirm || "Agency");
      byId("employeesViewSubtitle").textContent = state.selectedFirm
        ? webT("firmPrefix") + ": " + state.selectedFirm
        : webT("selectFirmSubtitle");

      var dashboardEmployeesHtml = employees.length
        ? employees.map(employee => `
          <div class="employee">
            <div>
              <div class="employee-name">${escapeHtml(employee.fullName)}</div>
              <div class="meta">
                ${escapeHtml(employee.firmName)} · ${escapeHtml(employee.positionTitle)}<br>
                ${escapeHtml(webT("startLabel"))}: ${escapeHtml(employee.startDate)} · ${escapeHtml(webT("statusLabel"))}: ${escapeHtml(employee.status)}
              </div>
            </div>
            <div class="badge-row">
              <span class="badge ${employee.hasPhoto ? "ok" : "missing"}">${escapeHtml(webT("photo"))}</span>
              <span class="badge ${docBadgeClass(employee.hasPassport, employee.passportExpiry)}">${escapeHtml(webT("passport"))}</span>
              <span class="badge ${docBadgeClass(employee.hasVisa, employee.visaExpiry)}">${escapeHtml(webT("visa"))}</span>
              <span class="badge ${docBadgeClass(employee.hasInsurance, employee.insuranceExpiry)}">${escapeHtml(webT("insurance"))}</span>
            </div>
          </div>`).join("")
        : `<div class="empty">${escapeHtml(webT("noEmployees"))}</div>`;

      var employeeCardsHtml = employees.length
        ? employees.map(employee => `
          <article class="employee-profile-card" data-employee-id="${escapeHtml(employee.id)}">
            <div class="employee-card-top">
              ${renderAvatar(employee, "employee-avatar")}
              <div>
                <div class="employee-card-name">${escapeHtml(employee.fullName)}</div>
                <div class="employee-card-position">${escapeHtml(employee.positionTitle)}<br>${escapeHtml(employee.firmName)}</div>
              </div>
            </div>
            <div class="employee-card-meta">
              <div>${escapeHtml(webT("startLabel"))}: ${escapeHtml(employee.startDate)}</div>
              <div>${escapeHtml(webT("statusLabel"))}: ${escapeHtml(employee.status)}</div>
              <div>${escapeHtml(webT("phoneLabel"))}: ${escapeHtml(employee.phone)}</div>
            </div>
            <div class="employee-card-badges">
              ${docBadge(webT("passport"), employee.hasPassport, employee.passportExpiry)}
              ${docBadge(webT("visa"), employee.hasVisa, employee.visaExpiry)}
              ${docBadge(webT("insurance"), employee.hasInsurance, employee.insuranceExpiry)}
              ${docBadge(webT("photo"), employee.hasPhoto, "")}
            </div>
            <div class="employee-card-actions">
              <span class="mini-action" title="Перегляд">👁</span>
              <span class="mini-action">▣</span>
              <span class="mini-action">⋯</span>
            </div>
          </article>`).join("")
        : `<div class="empty">${escapeHtml(webT("noEmployeesFilter"))}</div>`;

      byId("employeeList").innerHTML = dashboardEmployeesHtml;
      byId("employeesViewList").innerHTML = employeeCardsHtml;
    }

    function getEmployeeProblemReasons(employee) {
      var reasons = [];
      function addDoc(label, hasDoc, expiry) {
        var status = expiryStatus(expiry);
        if (!hasDoc) reasons.push({ type: "missing", label: label + ": " + webT("none") });
        else if (status === "expired") reasons.push({ type: "expired", label: label + ": " + webT("expiredFull") });
        else if (status === "warn") reasons.push({ type: "warn", label: label + ": " + webT("soon") });
      }

      if (!employee.hasPhoto) reasons.push({ type: "photo", label: webT("photo") + ": " + webT("none") });
      addDoc(webT("passport"), employee.hasPassport, employee.passportExpiry);
      addDoc(webT("visa"), employee.hasVisa, employee.visaExpiry);
      addDoc(webT("insurance"), employee.hasInsurance, employee.insuranceExpiry);
      if (employee.workPermitName || String(employee.employeeType || "").toLowerCase().indexOf("work") >= 0) {
        addDoc(webT("workPermit"), !!employee.workPermitName, employee.workPermitExpiry);
      }
      return reasons;
    }

    function problemMatchesFilter(reasons) {
      if (state.problemFilter === "all") return true;
      return reasons.some(function(reason) {
        if (state.problemFilter === "photo") return reason.type === "photo";
        return reason.type === state.problemFilter;
      });
    }

    function renderProblems() {
      var allProblemRows = state.employees
        .filter(function(employee) {
          if (!isFirmNameVisibleNow(employee.firmName)) return false;
          return !state.selectedFirm || employee.firmName === state.selectedFirm;
        })
        .map(function(employee) { return { employee: employee, reasons: getEmployeeProblemReasons(employee) }; })
        .filter(function(row) { return row.reasons.length > 0; });

      var filteredRows = allProblemRows.filter(function(row) { return problemMatchesFilter(row.reasons); });
      var missingCount = allProblemRows.filter(function(row) { return row.reasons.some(function(r) { return r.type === "missing" || r.type === "photo"; }); }).length;
      var expiredCount = allProblemRows.filter(function(row) { return row.reasons.some(function(r) { return r.type === "expired"; }); }).length;
      var warnCount = allProblemRows.filter(function(row) { return row.reasons.some(function(r) { return r.type === "warn"; }); }).length;

      byId("problemTotalCount").textContent = allProblemRows.length;
      byId("problemMissingCount").textContent = missingCount;
      byId("problemExpiredCount").textContent = expiredCount;
      byId("problemWarnCount").textContent = warnCount;
      byId("problemsViewSubtitle").textContent = state.selectedFirm
        ? webT("firmPrefix") + ": " + state.selectedFirm
        : webT("allFirmsProblemsSubtitle");

      byId("problemsViewList").innerHTML = filteredRows.length
        ? filteredRows.map(function(row) {
          var employee = row.employee;
          return `
            <article class="problem-card" data-employee-id="${escapeHtml(employee.id)}">
              ${renderAvatar(employee, "problem-avatar")}
              <div>
                <div class="problem-name">${escapeHtml(employee.fullName)}</div>
                <div class="problem-meta">${escapeHtml(employee.firmName)} · ${escapeHtml(employee.positionTitle)}<br>${escapeHtml(webT("phoneLabel"))}: ${escapeHtml(employee.phone)}</div>
              </div>
              <div class="problem-reasons">
                ${row.reasons.map(function(reason) {
                  var cls = reason.type === "photo" ? "missing" : reason.type;
                  return `<span class="problem-reason ${cls}">${escapeHtml(reason.label)}</span>`;
                }).join("")}
              </div>
            </article>`;
        }).join("")
        : `<div class="empty">${escapeHtml(webT("noProblemsByFilter"))}</div>`;
    }

    function isArchivedReportRow(row) {
      var status = String(row.status || "").toLowerCase();
      return !!row.isArchived || status === "archived" || status === "dismissed" || status.indexOf("арх") >= 0 || status.indexOf("зв") >= 0;
    }

    function isActiveReportRow(row) {
      var status = String(row.status || "").toLowerCase();
      return !isArchivedReportRow(row) && (status === "" || status === "active");
    }

    function hasReportProblem(row) {
      return !row.passportNumber
        || !row.visaNumber
        || !row.insuranceNumber
        || expiryStatus(row.passportExpiry) === "expired"
        || expiryStatus(row.visaExpiry) === "expired"
        || expiryStatus(row.insuranceExpiry) === "expired"
        || expiryStatus(row.passportExpiry) === "warn"
        || expiryStatus(row.visaExpiry) === "warn"
        || expiryStatus(row.insuranceExpiry) === "warn";
    }

    function matchesReportFilter(row) {
      if (state.reportFilter === "active") return isActiveReportRow(row);
      if (state.reportFilter === "archived") return isArchivedReportRow(row);
      if (state.reportFilter === "problems") return hasReportProblem(row);
      return true;
    }

    function normalizeReportAgencyName(value) {
      return String(value || "")
        .replace(/[\u200B-\u200D\uFEFF]/g, "")
        .replace(/\s+/g, " ")
        .trim();
    }

    function reportAgencyKey(value) {
      return normalizeReportAgencyName(value).toLocaleLowerCase();
    }

    function reportAgencyNames() {
      var names = new Map();
      getReportVisibleFirms().forEach(function(firm) {
        var name = normalizeReportAgencyName(firm.agencyName);
        var key = reportAgencyKey(name);
        if (key && !names.has(key)) names.set(key, name);
      });
      return Array.from(names.values()).sort(function(a, b) { return a.localeCompare(b); });
    }

    function compareYearMonth(yearA, monthA, yearB, monthB) {
      if (yearA !== yearB) return yearA - yearB;
      return monthA - monthB;
    }

    function isFirmVisibleAt(firm, date) {
      var hiddenYear = Number(firm.hiddenFromYear || 0);
      var hiddenMonth = Number(firm.hiddenFromMonth || 0);
      if (!hiddenYear || hiddenMonth < 1 || hiddenMonth > 12) return true;
      if (!date) return true;
      return compareYearMonth(date.getFullYear(), date.getMonth() + 1, hiddenYear, hiddenMonth) < 0;
    }

    function isFirmVisibleNow(firm) {
      return firm.isVisibleNow !== false;
    }

    function isFirmVisibleForReport(firm) {
      var from = parseEmployeeDate(state.reportDateFrom) || new Date();
      var to = parseEmployeeDate(state.reportDateTo) || from;
      var start = from <= to ? from : to;
      return isFirmVisibleAt(firm, start);
    }

    function getVisibleFirmsNow() {
      return state.firms.filter(isFirmVisibleNow);
    }

    function getReportVisibleFirms() {
      return state.firms.filter(isFirmVisibleForReport);
    }

    function isFirmNameVisibleNow(firmName) {
      var firm = state.firms.find(function(item) { return item.name === firmName; });
      return !firm || isFirmVisibleNow(firm);
    }

    function isFirmNameVisibleForReport(firmName) {
      var firm = state.firms.find(function(item) { return item.name === firmName; });
      return !firm || isFirmVisibleForReport(firm);
    }

    function initializeReportFilters() {
      if (!state.reportSelectedFirms.length) {
        state.reportSelectedFirms = getReportVisibleFirms().map(function(firm) { return firm.name; });
      }
      var agencies = reportAgencyNames();
      if (!state.reportSelectedAgencies.length) {
        state.reportSelectedAgencies = agencies.slice();
      }
      if (!state.reportDateFrom || !state.reportDateTo) {
        var today = new Date();
        var from = new Date(today.getFullYear(), today.getMonth(), 1);
        state.reportDateFrom = state.reportDateFrom || formatInputDate(from);
        state.reportDateTo = state.reportDateTo || formatInputDate(today);
      }
      byId("reportDateFrom").value = state.reportDateFrom;
      byId("reportDateTo").value = state.reportDateTo;
    }

    function firmAgencyName(firmName) {
      var firm = state.firms.find(function(item) { return item.name === firmName; });
      return firm ? (firm.agencyName || "") : "";
    }

    function isReportFirmSelected(firmName) {
      return state.reportSelectedFirms.indexOf(firmName) >= 0;
    }

    function isReportAgencySelected(agencyName) {
      if (!agencyName) return true;
      var key = reportAgencyKey(agencyName);
      return state.reportSelectedAgencies.some(function(name) { return reportAgencyKey(name) === key; });
    }

    function rowOverlapsReportRange(row) {
      var from = parseEmployeeDate(state.reportDateFrom);
      var to = parseEmployeeDate(state.reportDateTo);
      if (!from || !to) return true;
      var start = parseEmployeeDate(row.startDate);
      var end = parseEmployeeDate(row.endDate);
      if (!start && !end) return true;
      if (start && start > to) return false;
      if (end && end < from) return false;
      return true;
    }

    function getFilteredReportRows() {
      var query = state.reportQuery.trim().toLowerCase();
      return state.reportRows.filter(function(row) {
        if (!isFirmNameVisibleForReport(row.firmName)) return false;
        if (!isReportFirmSelected(row.firmName)) return false;
        if (!isReportAgencySelected(row.agency)) return false;
        if (!rowOverlapsReportRange(row)) return false;
        if (!matchesReportFilter(row)) return false;
        if (!query) return true;
        return [
          row.fullName,
          row.firmName,
          row.status,
          row.employeeType,
          row.passportNumber,
          row.visaNumber,
          row.insuranceNumber,
          row.visaAuthority,
          row.citizenship,
          row.birthCity,
          row.birthCountry,
          row.phone,
          row.position,
          row.bankAccountNumber,
          row.bankName
        ].some(function(value) { return String(value || "").toLowerCase().indexOf(query) >= 0; });
      });
    }

    function renderDateCell(value) {
      var status = expiryStatus(value);
      if (status === "expired") return `<span class="problem-reason expired">${escapeHtml(value)}</span>`;
      if (status === "warn") return `<span class="problem-reason warn">${escapeHtml(value)}</span>`;
      return escapeHtml(value);
    }

    function displayGender(value) {
      var raw = String(value || "").toLowerCase();
      if (raw === "female" || raw === "жінка" || raw === "zena" || raw === "žena") return "Žena";
      if (raw === "male" || raw === "чоловік" || raw === "muz" || raw === "muž") return "Muž";
      return safe(value);
    }

    function renderReportFilters() {
      var reportFirms = getReportVisibleFirms();
      state.reportSelectedFirms = state.reportSelectedFirms.filter(function(name) { return isFirmNameVisibleForReport(name); });
      var allFirmsChecked = reportFirms.length > 0 && reportFirms.every(function(firm) { return isReportFirmSelected(firm.name); });
      var agencies = reportAgencyNames();
      state.reportSelectedAgencies = state.reportSelectedAgencies.filter(function(name) {
        var key = reportAgencyKey(name);
        return agencies.some(function(agency) { return reportAgencyKey(agency) === key; });
      });
      var allAgenciesChecked = agencies.length > 0 && agencies.every(function(name) { return isReportAgencySelected(name); });

      byId("reportFirmChecks").innerHTML = `
        <label><input type="checkbox" data-report-all-firms ${allFirmsChecked ? "checked" : ""}><span>${escapeHtml(webT("allEmployers"))}</span></label>`
        + (reportFirms.length
          ? reportFirms.map(function(firm) {
            return `<label><input type="checkbox" data-report-firm="${escapeHtml(firm.name)}" ${isReportFirmSelected(firm.name) ? "checked" : ""}><span>${escapeHtml(firm.name)}</span></label>`;
          }).join("")
          : `<div class="empty">${escapeHtml(webT("noEmployers"))}</div>`);

      byId("reportAgencyChecks").innerHTML = `
        <label><input type="checkbox" data-report-all-agencies ${allAgenciesChecked ? "checked" : ""}><span>${escapeHtml(webT("allAgencies"))}</span></label>`
        + (agencies.length
          ? agencies.map(function(name) {
            return `<label><input type="checkbox" data-report-agency="${escapeHtml(name)}" ${isReportAgencySelected(name) ? "checked" : ""}><span>${escapeHtml(name)}</span></label>`;
          }).join("")
          : `<div class="empty">${escapeHtml(webT("noAgencies"))}</div>`);
    }

    function groupRowsByFirm(rows) {
      var groups = new Map();
      rows.forEach(function(row) {
        var firmName = row.firmName || "Без фірми";
        if (!groups.has(firmName)) groups.set(firmName, []);
        groups.get(firmName).push(row);
      });
      return Array.from(groups.entries()).sort(function(a, b) { return a[0].localeCompare(b[0]); });
    }

    function reportRowsForSummary() {
      return state.reportRows.filter(function(row) {
        return isFirmNameVisibleForReport(row.firmName)
          && isReportFirmSelected(row.firmName)
          && isReportAgencySelected(row.agency)
          && rowOverlapsReportRange(row);
      });
    }

    function renderReportSummary(rows) {
      var groups = groupRowsByFirm(rows);
      byId("reportSummaryBody").innerHTML = groups.length
        ? groups.map(function(group) {
          var firmName = group[0];
          var firmRows = group[1];
          var active = firmRows.filter(isActiveReportRow).length;
          var passportOnly = firmRows.filter(function(row) {
            return String(row.employeeType || "").toLowerCase().indexOf("passport") >= 0
              || String(row.documentType || "").toLowerCase().indexOf("pas") >= 0;
          }).length;
          var ended = firmRows.filter(function(row) {
            var end = parseEmployeeDate(row.endDate);
            var from = parseEmployeeDate(state.reportDateFrom);
            var to = parseEmployeeDate(state.reportDateTo);
            return end && from && to && end >= from && end <= to;
          }).length;
          return `<tr><td>${escapeHtml(firmName)}</td><td>${firmRows.length}</td><td>${active}</td><td>${passportOnly}</td><td>${ended}</td></tr>`;
        }).join("")
        : `<tr><td colspan="5"><div class="empty">${escapeHtml(webT("noFirmData"))}</div></td></tr>`;
    }

    function renderReportColumnsList() {
      byId("reportColumnsList").innerHTML = state.reportColumns.map(function(col, index) {
        return `<div class="report-column-item">
          <input type="checkbox" data-report-column-check="${escapeHtml(col.key)}" ${col.visible || col.required ? "checked" : ""} ${col.required ? "disabled" : ""}>
          <div><strong>${escapeHtml(reportColumnLabel(col))}</strong><span>${escapeHtml(col.key)}</span></div>
          <button class="report-column-move" data-report-column-up="${escapeHtml(col.key)}" type="button" ${index === 0 ? "disabled" : ""}>↑</button>
          <button class="report-column-move" data-report-column-down="${escapeHtml(col.key)}" type="button" ${index === state.reportColumns.length - 1 ? "disabled" : ""}>↓</button>
        </div>`;
      }).join("");
    }

    function openReportColumnsDialog() {
      state.reportColumnsBackup = JSON.stringify(state.reportColumns);
      renderReportColumnsList();
      byId("reportColumnsOverlay").classList.add("is-open");
    }

    function closeReportColumnsDialog(restore) {
      if (restore !== false && state.reportColumnsBackup) {
        try {
          state.reportColumns = JSON.parse(state.reportColumnsBackup);
        } catch (error) {
          loadReportColumns();
        }
      }
      state.reportColumnsBackup = "";
      byId("reportColumnsOverlay").classList.remove("is-open");
    }

    function moveReportColumn(key, delta) {
      var index = state.reportColumns.findIndex(function(col) { return col.key === key; });
      var target = index + delta;
      if (index < 0 || target < 0 || target >= state.reportColumns.length) return;
      var item = state.reportColumns.splice(index, 1)[0];
      state.reportColumns.splice(target, 0, item);
      renderReportColumnsList();
    }

    function renderReportEmployeeGroups(rows) {
      var groups = groupRowsByFirm(rows);
      var columns = visibleReportColumns();
      byId("reportEmployeeGroups").innerHTML = groups.length
        ? groups.map(function(group) {
          var firmName = group[0];
          var firmRows = group[1];
          return `<article class="report-group-card">
            <div class="report-group-head"><span>⌃</span><span>⌂</span><span>${escapeHtml(firmName)}</span><span class="report-group-count">${firmRows.length} ${escapeHtml(webT("employeesCount").toLowerCase())}</span></div>
            <div class="report-table-wrap" style="min-height:0;max-height:none;border:0;border-radius:0;box-shadow:none;">
              <table class="report-table report-employee-table">
                <thead><tr>
                  ${columns.map(function(col) { return `<th>${escapeHtml(reportColumnLabel(col))}</th>`; }).join("")}
                </tr></thead>
                <tbody>
                  ${firmRows.map(function(row) {
                    return `<tr data-report-employee-id="${escapeHtml(row.id)}">
                      ${columns.map(function(col) { return reportCellHtml(row, col); }).join("")}
                    </tr>`;
                  }).join("")}
                </tbody>
              </table>
            </div>
          </article>`;
        }).join("")
        : `<div class="empty">${escapeHtml(webT("noReportEmployees"))}</div>`;
    }

    function renderReport() {
      var rows = getFilteredReportRows();
      var summaryRows = reportRowsForSummary();
      var firmCount = groupRowsByFirm(summaryRows).length;
      byId("reportRangePill").textContent = webT("reportRange").replace("{firms}", firmCount).replace("{employees}", summaryRows.length) + ", "
        + formatDisplayDate(state.reportDateFrom) + " - " + formatDisplayDate(state.reportDateTo);
      byId("reportSummaryPanel").classList.toggle("is-hidden", state.reportMode !== "summary");
      byId("reportGroupsPanel").classList.toggle("is-hidden", state.reportMode !== "employees");
      renderReportFilters();
      renderReportSummary(summaryRows);
      renderReportEmployeeGroups(rows);
    }

    function epStr(value) {
      return value != null && value !== "" ? String(value) : "—";
    }

    function epField(label, value) {
      var v = epStr(value);
      return `<div style="margin-bottom:10px"><label class="ep-field-label">${escapeHtml(epLabelText(label))}</label><input class="ep-field-input" type="text" readonly value="${escapeHtml(v)}"/></div>`;
    }

    function epSectionHead(icon, badgeClass, title) {
      return `<div class="ep-section-head"><span class="ep-icon-badge ${badgeClass}">${escapeHtml(icon)}</span><h4 class="ep-section-title">${escapeHtml(epLabelText(title))}</h4></div>`;
    }

    function epStatusPillClass(key) {
      var k = String(key || "Active");
      if (k === "OnLeave") return "ep-status-onleave";
      if (k === "Dismissed") return "ep-status-dismissed";
      if (k === "AwaitingDocs") return "ep-status-awaiting";
      return "ep-status-active";
    }

    function epSidePhotoHtml(profile, employee) {
      var initials = escapeHtml(getInitials(profile.fullName || employee.fullName));
      if (profile.photoUrl) {
        return `<div class="ep-side-photo-wrap"><img src="${escapeHtml(profile.photoUrl)}" alt=""></div>`;
      }
      return `<div class="ep-side-photo-wrap">${initials}</div>`;
    }

    function epDocPreviewCard(title, fileName, fileUrl, extraClass) {
      var hasFile = !!fileName && !!fileUrl;
      var isPdf = /\.pdf$/i.test(String(fileName || ""));
      var previewHtml = hasFile
        ? (isPdf
          ? `<iframe src="${escapeHtml(fileUrl)}" title="${escapeHtml(title)}"></iframe>`
          : `<img src="${escapeHtml(fileUrl)}" alt="${escapeHtml(title)}">`)
        : "Немає прикріпленого файлу.";
      return `
        <article class="ep-doc-card ${extraClass || ""}">
          <div style="font-weight:800;font-size:13px;color:#f8fafc;line-height:1.25;">${escapeHtml(title)}</div>
          ${fileName && hasFile ? `<div style="font-size:11px;color:rgba(226,232,240,.5);line-height:1.25;">${escapeHtml(fileName)}</div>` : "<div></div>"}
          <div class="ep-doc-preview ${hasFile ? "has-file" : ""}">${previewHtml}</div>
          <div class="ep-doc-actions">
            ${hasFile ? `<a class="ep-doc-action" href="${escapeHtml(fileUrl)}" target="_blank" rel="noopener">Відкрити</a>` : ""}
            <span class="ep-doc-action" style="opacity:.55;">Заміна лише у програмі</span>
          </div>
        </article>`;
    }

    function setEmployeeProfileTab(tabId) {
      var tabs = document.querySelectorAll("[data-ep-tab]");
      var panels = document.querySelectorAll("[data-ep-panel]");
      for (var i = 0; i < tabs.length; i++)
        tabs[i].classList.toggle("is-active", tabs[i].getAttribute("data-ep-tab") === tabId);
      for (var j = 0; j < panels.length; j++)
        panels[j].classList.toggle("is-active", panels[j].getAttribute("data-ep-panel") === tabId);
    }

    function buildEmployeeProfileHtml(profile, employee) {
      var basic = profile.basic || {};
      var contact = profile.contact || {};
      var work = profile.work || {};
      var documents = profile.documents || {};
      var bank = profile.bank || {};
      var files = profile.files || {};
      var ui = profile.ui || {};
      var loc = contact.addressLocal || {};
      var abr = contact.addressAbroad || {};
      var stKey = basic.statusKey || "Active";

      var hasSecondary = !!ui.hasSecondaryDocuments;
      var secondaryDocTitle = (ui.secondarySectionTitle && String(ui.secondarySectionTitle).trim()) || "Віза / додатковий документ";
      var showInsurance = !!ui.showInsurance;
      var showWpSection = !!ui.showWorkPermitSection;
      var showVisaType = !!ui.showSecondaryVisaType;
      var showVisaStart = !!ui.showVisaStartDate;

      var sidebarPhoto = epSidePhotoHtml(profile, employee);
      var docsHtml = `
        <div class="ep-doc-grid">
          ${epDocPreviewCard("Паспорт", files.passport, files.passportUrl, "")}
          ${hasSecondary ? epDocPreviewCard(secondaryDocTitle, files.visa || files.passportPage2, files.visaUrl || files.passportPage2Url, "") : ""}
          ${showInsurance ? epDocPreviewCard("Страховка", files.insurance, files.insuranceUrl, "") : ""}
          ${(showWpSection || files.workPermit) ? epDocPreviewCard("Дозвіл на роботу", files.workPermit, files.workPermitUrl, "") : ""}
          ${epDocPreviewCard("Фото", files.photo, files.photoUrl || profile.photoUrl, "")}
          ${(profile.customDocuments || []).slice(0, 12).map(function(d) {
            return epDocPreviewCard(d.name || "Документ", d.fileName, "", "");
          }).join("")}
        </div>`;

      var anketaLeft = `
        <div class="ep-col">
          ${epSectionHead("◇", "ep-icon-teal", "Особисті дані")}
          <div class="ep-card">
            ${epField("Ім'я", basic.firstName)}
            ${epField("Прізвище", basic.lastName)}
            ${epField("Дата народження", basic.birthDate)}
            ${epField("Найвищий досягнутий рівень освіти", basic.highestEducationDisplay || basic.highestEducationCode)}
            ${epField("Стать", basic.genderDisplay || basic.gender)}
          </div>
          ${epSectionHead("▣", "ep-icon-teal", "Паспортні дані")}
          <div class="ep-card">
            ${epField("Номер паспорту", documents.passportNumber)}
            ${epField("Орган, що видав документ", documents.passportAuthority)}
            ${epField("Місто / місце видачі (як у програмі)", documents.passportCity)}
            ${epField("Країна народження (паспорт)", documents.passportCountry)}
            ${epField("Громадянство", documents.citizenship)}
            ${epField("Країна видачі", documents.issuingCountry)}
            ${epField("Термін дії паспорта до", documents.passportExpiry)}
          </div>
          ${hasSecondary ? epSectionHead("▤", "ep-icon-purple", secondaryDocTitle) +
            `<div class="ep-card">
              ${epField("Номер (віза / документ)", documents.visaNumber)}
              ${showVisaType ? epField("Тип візи", documents.visaType) : ""}
              ${showVisaStart ? epField("Віза дійсна від", documents.visaStartDate) : ""}
              ${epField("Термін дії до", documents.visaExpiry)}
              ${epField("Орган, що видав", documents.visaAuthority)}
              ${epField("Посада для дозволу (назва)", documents.workPermitName)}
            </div>` : ""}
          ${showInsurance ? epSectionHead("♥", "ep-icon-amber", "Страховка") +
            `<div class="ep-card">
              ${epField("Страхова (код)", documents.insuranceCompanyShort)}
              ${epField("Страхова (повна)", documents.insuranceCompanyFull)}
              ${epField("Номер полісу", documents.insuranceNumber)}
              ${epField("Термін дії до", documents.insuranceExpiry)}
            </div>` : ""}
          ${showWpSection ? epSectionHead("◎", "ep-icon-rose", "Дозвіл на роботу") +
            `<div class="ep-card">
              ${epField("Номер дозволу", documents.workPermitNumber)}
              ${epField("Тип дозволу", documents.workPermitType)}
              ${epField("Дата видачі", documents.workPermitIssueDate)}
              ${epField("Термін дії до", documents.workPermitExpiry)}
              ${epField("Орган видачі", documents.workPermitAuthority)}
            </div>` : ""}
        </div>`;

      var anketaRight = `
        <div class="ep-col">
          ${epSectionHead("⌂", "ep-icon-purple", "Адреса (в країні)")}
          <div class="ep-card">
            ${epField("Вулиця", loc.street)}
            ${epField("Номер будинку", loc.number)}
            <div class="ep-field-row2">
              <div>
                <label class="ep-field-label">Місто</label>
                <input class="ep-field-input" type="text" readonly value="${escapeHtml(epStr(loc.city))}"/>
              </div>
              <div>
                <label class="ep-field-label">Індекс</label>
                <input class="ep-field-input" type="text" readonly value="${escapeHtml(epStr(loc.zip))}"/>
              </div>
            </div>
          </div>
          ${epSectionHead("🌐", "ep-icon-sky", "Адреса за кордоном")}
          <div class="ep-card">
            ${epField("Вулиця", abr.street)}
            ${epField("Номер будинку", abr.number)}
            <div class="ep-field-row2">
              <div>
                <label class="ep-field-label">Місто</label>
                <input class="ep-field-input" type="text" readonly value="${escapeHtml(epStr(abr.city))}"/>
              </div>
              <div>
                <label class="ep-field-label">Індекс</label>
                <input class="ep-field-input" type="text" readonly value="${escapeHtml(epStr(abr.zip))}"/>
              </div>
            </div>
          </div>
          ${epSectionHead("⚙", "ep-icon-teal", "Дані про роботу")}
          <div class="ep-card">
            ${epField("Посада", basic.position)}
            ${epField("Номер посади", basic.positionNumber)}
            ${epField("Місячна зарплата (брутто)", work.monthlySalaryBrutto)}
            ${epField("Годинна ставка", work.hourlySalary)}
            ${epField("Адреса роботи", work.workAddressTag)}
            ${epField("Відділ / підрозділ", basic.department)}
          </div>
          ${bank && bank.hasBankAccountData ? epSectionHead("₿", "ep-icon-teal", "Банківські дані") +
            `<div class="ep-card">
              ${epField("IBAN / номер рахунку", bank.bankAccountNumber)}
              ${epField("Банк", bank.bankName)}
            </div>` : ""}
          ${epSectionHead("✉", "ep-icon-purple", "Контакти й дати договору")}
          <div class="ep-card">
            ${epField("Телефон", contact.phone)}
            ${epField("Email", contact.email)}
            ${epField("Тип контракту", work.contractType)}
            ${epField("Початок роботи", work.startDate)}
            ${epField("Дата підписання контракту", work.contractSignDate)}
            ${epField("Кінець роботи", work.endDate)}
            ${epField("Статус анкети", basic.statusDisplay || basic.status)}
          </div>
        </div>`;

      var historyRows = Array.isArray(profile.history) ? profile.history.map(function(row) {
        var color = row.eventColor || "#2dd4bf";
        var text = row.description || ([row.field, row.oldValue, row.newValue].filter(Boolean).join(" → "));
        return `<div class="ep-history-row"><div class="ep-history-dot" style="background:${escapeHtml(color)}"></div><div>
          <div class="ep-history-meta"><span>${escapeHtml(row.timestamp)}</span>${row.actorName ? `<span class="ep-history-actor">${escapeHtml(row.actorName)}</span>` : ""}<span>${escapeHtml(row.action || row.eventType)}</span></div>
          <div class="ep-history-text">${escapeHtml(text || "Зміна анкети")}</div>
        </div></div>`;
      }).join("") : "";

      var salaryRecords = profile.salary && Array.isArray(profile.salary.records) ? profile.salary.records : [];
      var salaryHtml = `
        <h3 class="ep-pay-title">Історія виплат</h3>
        <div class="ep-pay-summary">
          <div class="ep-pay-summary-item">
            <span class="ep-pay-summary-icon">▰</span>
            <div class="ep-pay-summary-value">${escapeHtml(fmtMoney(profile.salary && profile.salary.totalNet, 0))}</div>
            <div class="ep-pay-summary-label">Загальна виплата</div>
          </div>
          <div class="ep-pay-summary-item">
            <span class="ep-pay-summary-icon">◷</span>
            <div class="ep-pay-summary-value">${escapeHtml(fmtMoney(profile.salary && profile.salary.totalHours, 1))}</div>
            <div class="ep-pay-summary-label">Загальні години</div>
          </div>
          <div class="ep-pay-summary-item">
            <span class="ep-pay-summary-icon">▣</span>
            <div class="ep-pay-summary-value">${escapeHtml(String(salaryRecords.length))}</div>
            <div class="ep-pay-summary-label">Місяців</div>
          </div>
        </div>`;

      var salaryRows = salaryRecords.map(function(row) {
        return `<article class="ep-pay-card">
          <div class="ep-pay-card-head">
            <div class="ep-pay-month">▣ ${escapeHtml(row.monthDisplay)}</div>
            <div class="ep-pay-firm">▤ ${escapeHtml(epStr(row.firmName))}</div>
          </div>
          <div class="ep-pay-grid">
            <div>
              <span class="ep-pay-label">Години</span>
              <span class="ep-pay-value">${escapeHtml(fmtMoney(row.hoursWorked, 1))}</span>
            </div>
            <div>
              <span class="ep-pay-label">Ставка</span>
              <span class="ep-pay-value">${escapeHtml(fmtMoney(row.hourlyRate, 0))}</span>
            </div>
            <div>
              <span class="ep-pay-label">Нараховано</span>
              <span class="ep-pay-value">${escapeHtml(fmtMoney(row.grossSalary, 0))}</span>
            </div>
            <div>
              <span class="ep-pay-label">Аванс</span>
              <span class="ep-pay-value ep-pay-value-advance">${escapeHtml(fmtMoney(row.advance, 0))}</span>
            </div>
            <div>
              <span class="ep-pay-label">До виплати</span>
              <span class="ep-pay-value-accent">${escapeHtml(fmtMoney(row.netSalary, 0))}</span>
            </div>
          </div>
          ${row.note ? `<div class="ep-pay-note">${escapeHtml(row.note)}</div>` : ""}
        </article>`;
      }).join("");

      var endDate = String(work.endDate || employee.endDate || "").trim();
      var showEndDate = endDate && endDate !== "—" && (basic.isArchived || stKey === "Dismissed");
      var sideDatesHtml = `
        <div class="ep-date-pill"><span>Початок</span>${escapeHtml(epStr(work.startDate))}</div>
        <div class="ep-date-pill"><span>Підпис</span>${escapeHtml(epStr(work.contractSignDate))}</div>
        ${showEndDate ? `<div class="ep-date-pill"><span>Завершення</span>${escapeHtml(endDate)}</div>` : ""}`;

      return `
        <aside class="ep-side">
          <div class="ep-side-minihead">
            <button type="button" class="ep-side-back" data-close-detail aria-label="Назад">←</button>
            <div class="ep-side-usericon">♙</div>
            <div class="ep-side-mini-title">
              <strong>${escapeHtml(profile.fullName || employee.fullName)}</strong>
              <span>${escapeHtml(basic.firmName || employee.firmName)}</span>
            </div>
          </div>
          ${sidebarPhoto}
          <div>
            <h3 class="ep-side-name">${escapeHtml(profile.fullName || employee.fullName)}</h3>
            <p class="ep-side-sub">${escapeHtml(employee.positionTitle || basic.position || "")}<br>${escapeHtml(epStr(work.contractType))}</p>
          </div>
          <div class="ep-side-dates">
            ${sideDatesHtml}
          </div>
          <button type="button" class="ep-side-btn ep-side-btn-primary">Згенерувати документ</button>
          <button type="button" class="ep-side-btn">Відкрити папку</button>
          <button type="button" class="ep-side-btn">AI перевірка</button>
          <button type="button" class="ep-side-btn">Експорт анкети PDF</button>
          <button type="button" class="ep-side-btn" style="border-color:rgba(248,113,113,.38);background:rgba(127,29,29,.32);color:#fecaca;">В архів</button>
          <p class="ep-side-note">Дії недоступні у веб-перегляді — лише у програмі Agency Contractor.</p>
        </aside>
        <div class="ep-main">
          <div class="ep-topbar">
            <div class="ep-topbar-head">
              <h2>${escapeHtml(profile.fullName || employee.fullName)}</h2>
              <div class="ep-firm-line">${escapeHtml(basic.firmName || employee.firmName)}</div>
              <span class="ep-status-pill ${epStatusPillClass(stKey)}">${escapeHtml(basic.statusDisplay || employee.status)}</span>
            </div>
            <button type="button" class="ep-close" data-close-detail aria-label="Закрити">×</button>
          </div>
          <div class="ep-tabs-bar">
            <button type="button" class="ep-tab" data-ep-tab="docs">📄 ${escapeHtml(webT("docsTab"))}</button>
            <button type="button" class="ep-tab is-active" data-ep-tab="profile">📋 ${escapeHtml(webT("profileTab"))}</button>
            <button type="button" class="ep-tab" data-ep-tab="history">🕘 ${escapeHtml(webT("historyTab"))}</button>
            <button type="button" class="ep-tab" data-ep-tab="salary">💷 ${escapeHtml(webT("salaryTab"))}</button>
          </div>
          <div class="ep-tab-panels">
            <section class="ep-tab-panel" data-ep-panel="docs">
              <div class="ep-panel-title">
                <h3>${escapeHtml(webT("employeeDocuments"))}</h3>
                <span>${escapeHtml(webT("filesPreview"))}</span>
              </div>
              ${docsHtml}
            </section>
            <section class="ep-tab-panel is-active" data-ep-panel="profile">
              <div class="ep-panel-title">
                <h3>${escapeHtml(webT("employeeProfile"))}</h3>
                <span>${escapeHtml(webT("readOnly"))}</span>
              </div>
              <div class="ep-edit-banner">
                <button type="button" class="ep-btn-disabled" disabled>✏ ${escapeHtml(webT("editProfile"))}</button>
                <span class="ep-muted" style="margin:0;font-size:12px;">${escapeHtml(webT("editOnlyApp"))}</span>
              </div>
              <div class="ep-anketa-grid">
                ${anketaLeft}
                <div class="ep-gutter-col"></div>
                ${anketaRight}
              </div>
            </section>
            <section class="ep-tab-panel" data-ep-panel="history">
              <div class="ep-panel-title">
                <h3>${escapeHtml(webT("changeHistory"))}</h3>
                <span>${escapeHtml(webT("fromMainApp"))}</span>
              </div>
              <div class="ep-history-list">${historyRows}</div>
              ${historyRows === "" ? "<p class=\"ep-muted\" style=\"margin-top:12px\">" + escapeHtml(webT("noHistory")) + "</p>" : ""}
            </section>
            <section class="ep-tab-panel" data-ep-panel="salary">
              ${salaryHtml}
              <div class="ep-pay-list">${salaryRows}</div>
              ${salaryRows === "" ? "<p class=\"ep-muted\" style=\"margin-top:12px\">" + escapeHtml(webT("noSalary")) + "</p>" : ""}
            </section>
          </div>
        </div>`;
    }

    function epCardBlock(title, inner, withHead) {
      return `<div class="ep-card"><div style="font-weight:750;margin-bottom:6px">${escapeHtml(title)}</div>${inner}</div>`;
    }

    async function renderEmployeeDetails(employee) {
      if (!employee) return;
      byId("employeeDetailContent").innerHTML = `
        <aside class="ep-side"><div class="ep-side-photo-wrap">${escapeHtml(getInitials(employee.fullName))}</div>
          <h3 class="ep-side-name">${escapeHtml(employee.fullName)}</h3><p class="ep-side-sub">${escapeHtml(employee.positionTitle || "")}</p>
          <p class="ep-muted" style="margin-top:12px;text-align:center">${escapeHtml(webT("loadingProfile"))}</p></aside>
        <div class="ep-main"><div class="ep-topbar"><div class="ep-topbar-head"><h2>${escapeHtml(employee.fullName)}</h2></div>
          <button type="button" class="ep-close" data-close-detail>×</button></div></div>`;
      byId("employeeDetailOverlay").classList.add("is-open");
      document.body.classList.add("detail-open");

      try {
        var profile = await fetchJson("/api/v1/employees/" + encodeURIComponent(employee.id), 2);
        byId("employeeDetailContent").innerHTML = buildEmployeeProfileHtml(profile, employee);
      } catch (error) {
        byId("employeeDetailContent").innerHTML = `
          <aside class="ep-side"><div class="ep-side-photo-wrap">${escapeHtml(getInitials(employee.fullName))}</div>
            <h3 class="ep-side-name">${escapeHtml(employee.fullName)}</h3></aside>
          <div class="ep-main"><div class="ep-topbar"><button type="button" class="ep-close" data-close-detail>×</button></div>
            <div style="padding:24px;"><div class="error">Не вдалося завантажити профіль працівника.</div></div></div>`;
      }
    }

    function closeEmployeeDetails() {
      byId("employeeDetailOverlay").classList.remove("is-open");
      document.body.classList.remove("detail-open");
    }

    function selectFirm(firmName, openDashboard) {
      state.selectedFirm = firmName || "";
      byId("activeCompanyName").textContent = state.selectedFirm || webT("allFirms");
      renderFirms();
      renderEmployees();
      renderProblems();
      renderReport();
      closeFirmDrawer();
      if (openDashboard) showDashboard();
      if (byId("financeView").classList.contains("is-active")) {
        state.financeFirm = state.selectedFirm || "";
        if (!byId("financeSalaryScreen").classList.contains("is-hidden")) loadFinanceScreen();
      }
    }

    async function loadData() {
      try {
        var results = await Promise.all([
          fetchJson("/healthz", 3),
          fetchJson("/api/v1/firms", 3),
          fetchJson("/api/v1/employees", 3),
          fetchJson("/api/v1/report/employees", 3),
          fetchJson("/api/v1/dashboard", 3)
        ]);
        var health = results[0];
        var firms = results[1];
        var employees = results[2];
        var reportRows = results[3];
        var dashboard = results[4];

        state.firms = Array.isArray(firms) ? firms : [];
        state.employees = Array.isArray(employees) ? employees : [];
        state.reportRows = Array.isArray(reportRows) ? reportRows : [];
        state.dashboard = dashboard && !dashboard.error ? dashboard : null;
        initializeReportFilters();
        byId("serverStatus").textContent = health.status === "ok" ? webT("serverOk") : webT("serverUnknown");
        renderMetrics();
        renderDashboard();
        renderFirms();
        renderEmployees();
        renderProblems();
        renderReport();
      } catch (error) {
        byId("serverStatus").textContent = webT("serverError");
        byId("firmList").innerHTML = `<div class="error">${escapeHtml(webT("serverError"))}</div>`;
        byId("employeeList").innerHTML = `<div class="error">${escapeHtml(webT("serverError"))}</div>`;
        byId("problemsViewList").innerHTML = `<div class="error">${escapeHtml(webT("serverError"))}</div>`;
        byId("reportSummaryBody").innerHTML = `<tr><td colspan="5"><div class="error">${escapeHtml(webT("serverError"))}</div></td></tr>`;
        byId("reportEmployeeGroups").innerHTML = `<div class="error">${escapeHtml(webT("serverError"))}</div>`;
        console.error(error);
      }
    }

    byId("searchInput").addEventListener("input", function(event) {
      state.query = event.target.value || "";
      byId("employeesSearchInput").value = state.query;
      renderEmployees();
    });

    byId("employeesSearchInput").addEventListener("input", function(event) {
      state.query = event.target.value || "";
      byId("searchInput").value = state.query;
      renderEmployees();
    });

    byId("reportSearchInput").addEventListener("input", function(event) {
      state.reportQuery = event.target.value || "";
      renderReport();
    });

    byId("financeOpenSalary").addEventListener("click", showFinanceSalary);
    byId("financeOpenTables").addEventListener("click", showFinanceTablesMenu);
    byId("financeSalaryBack").addEventListener("click", showFinanceMenu);
    byId("financeTablesBack").addEventListener("click", showFinanceMenu);
    byId("financePrevMonth").addEventListener("click", function() { changeFinanceMonth(-1); });
    byId("financeNextMonth").addEventListener("click", function() { changeFinanceMonth(1); });
    byId("financeSummaryToggle").addEventListener("click", openFinanceSummaryPanel);
    byId("financeSummaryClose").addEventListener("click", closeFinanceSummaryPanel);
    byId("financeSideBackdrop").addEventListener("click", closeFinanceSummaryPanel);
    byId("financeSearchInput").addEventListener("input", function(event) {
      state.financeSearch = event.target.value || "";
      loadFinanceScreen();
    });
    byId("financeFirmSummaryList").addEventListener("click", function(event) {
      var card = event.target.closest("[data-finance-firm]");
      if (!card) return;
      var firm = card.getAttribute("data-finance-firm") || "";
      state.financeFirm = state.financeFirm === firm ? "" : firm;
      loadFinanceScreen();
    });
    document.querySelectorAll("[data-finance-table]").forEach(function(button) {
      button.addEventListener("click", function() {
        showPlaceholder(button.querySelector("h3") ? button.querySelector("h3").textContent : webT("tablesCardTitle"));
      });
    });

    byId("financeEntriesBody").addEventListener("click", function(event) {
      var link = event.target.closest("[data-finance-employee-id]");
      if (!link) return;
      var id = link.getAttribute("data-finance-employee-id") || "";
      var name = link.getAttribute("data-finance-employee-name") || "";
      var employee = state.employees.find(function(item) { return item.id === id; })
        || state.employees.find(function(item) { return item.fullName === name; })
        || (id ? { id: id, fullName: name || "-", positionTitle: "", photoUrl: "" } : null);
      renderEmployeeDetails(employee);
    });

    byId("dashboardView").addEventListener("click", function(event) {
      var expiringCard = event.target.closest("[data-dashboard-employee-id]");
      if (expiringCard) {
        var id = expiringCard.getAttribute("data-dashboard-employee-id") || "";
        var employee = state.employees.find(function(item) { return item.id === id; });
        if (employee) renderEmployeeDetails(employee);
        return;
      }
      if (event.target.closest("#dashMovementCard")) {
        openDashboardMovement();
      }
    });

    byId("dashboardView").addEventListener("keydown", function(event) {
      if ((event.key === "Enter" || event.key === " ") && event.target.closest("#dashMovementCard")) {
        event.preventDefault();
        openDashboardMovement();
      }
    });

    byId("dashMovementOverlay").addEventListener("click", function(event) {
      if (event.target === byId("dashMovementOverlay") || event.target.closest("[data-close-movement]")) {
        closeDashboardMovement();
        return;
      }
      var itemNode = event.target.closest("[data-dashboard-movement-id]");
      if (!itemNode) return;
      var item = {
        uniqueId: itemNode.getAttribute("data-dashboard-movement-id") || "",
        fullName: itemNode.getAttribute("data-dashboard-movement-name") || "",
        firmName: itemNode.getAttribute("data-dashboard-movement-firm") || ""
      };
      var employee = dashboardEmployeeFromMovement(item);
      if (employee) {
        closeDashboardMovement();
        renderEmployeeDetails(employee);
      }
    });

    byId("employeesViewList").addEventListener("click", function(event) {
      var card = event.target.closest("[data-employee-id]");
      if (!card) return;
      var id = card.getAttribute("data-employee-id");
      var employee = state.employees.find(function(item) { return item.id === id; });
      renderEmployeeDetails(employee);
    });

    byId("reportEmployeeGroups").addEventListener("click", function(event) {
      var row = event.target.closest("[data-report-employee-id]");
      if (!row) return;
      var id = row.getAttribute("data-report-employee-id");
      var employee = state.employees.find(function(item) { return item.id === id; });
      renderEmployeeDetails(employee);
    });

    byId("problemsViewList").addEventListener("click", function(event) {
      var card = event.target.closest("[data-employee-id]");
      if (!card) return;
      var id = card.getAttribute("data-employee-id");
      var employee = state.employees.find(function(item) { return item.id === id; });
      renderEmployeeDetails(employee);
    });

    byId("employeeDetailOverlay").addEventListener("click", function(event) {
      if (event.target === byId("employeeDetailOverlay")) {
        closeEmployeeDetails();
        return;
      }
      if (event.target.closest("[data-close-detail]")) {
        closeEmployeeDetails();
        return;
      }
      var tabBtn = event.target.closest("[data-ep-tab]");
      if (tabBtn) {
        setEmployeeProfileTab(tabBtn.getAttribute("data-ep-tab") || "profile");
      }
    });

    var filterButtons = document.querySelectorAll("[data-employee-filter]");
    for (var filterIndex = 0; filterIndex < filterButtons.length; filterIndex++) {
      filterButtons[filterIndex].addEventListener("click", function(event) {
        state.employeeFilter = event.currentTarget.getAttribute("data-employee-filter") || "all";
        for (var i = 0; i < filterButtons.length; i++) {
          filterButtons[i].classList.toggle("is-active", filterButtons[i] === event.currentTarget);
        }
        renderEmployees();
      });
    }

    var problemFilterButtons = document.querySelectorAll("[data-problem-filter]");
    for (var pfIndex = 0; pfIndex < problemFilterButtons.length; pfIndex++) {
      problemFilterButtons[pfIndex].addEventListener("click", function(event) {
        state.problemFilter = event.currentTarget.getAttribute("data-problem-filter") || "all";
        for (var i = 0; i < problemFilterButtons.length; i++) {
          problemFilterButtons[i].classList.toggle("is-active", problemFilterButtons[i] === event.currentTarget);
        }
        renderProblems();
      });
    }

    var reportFilterButtons = document.querySelectorAll("[data-report-filter]");
    for (var rfIndex = 0; rfIndex < reportFilterButtons.length; rfIndex++) {
      reportFilterButtons[rfIndex].addEventListener("click", function(event) {
        state.reportFilter = event.currentTarget.getAttribute("data-report-filter") || "all";
        for (var i = 0; i < reportFilterButtons.length; i++) {
          reportFilterButtons[i].classList.toggle("is-active", reportFilterButtons[i] === event.currentTarget);
        }
        renderReport();
      });
    }

    var reportModeButtons = document.querySelectorAll("[data-report-mode]");
    for (var rmIndex = 0; rmIndex < reportModeButtons.length; rmIndex++) {
      reportModeButtons[rmIndex].addEventListener("click", function(event) {
        state.reportMode = event.currentTarget.getAttribute("data-report-mode") || "summary";
        for (var i = 0; i < reportModeButtons.length; i++) {
          reportModeButtons[i].classList.toggle("is-active", reportModeButtons[i] === event.currentTarget);
        }
        renderReport();
      });
    }

    byId("reportFirmChecks").addEventListener("change", function(event) {
      if (event.target.matches("[data-report-all-firms]")) {
        state.reportSelectedFirms = event.target.checked ? getReportVisibleFirms().map(function(firm) { return firm.name; }) : [];
        renderReport();
        return;
      }
      var firmName = event.target.getAttribute("data-report-firm");
      if (!firmName) return;
      if (event.target.checked && state.reportSelectedFirms.indexOf(firmName) < 0) state.reportSelectedFirms.push(firmName);
      if (!event.target.checked) state.reportSelectedFirms = state.reportSelectedFirms.filter(function(name) { return name !== firmName; });
      renderReport();
    });

    byId("reportAgencyChecks").addEventListener("change", function(event) {
      var agencies = reportAgencyNames();
      if (event.target.matches("[data-report-all-agencies]")) {
        state.reportSelectedAgencies = event.target.checked ? agencies.slice() : [];
        renderReport();
        return;
      }
      var agencyName = event.target.getAttribute("data-report-agency");
      if (!agencyName) return;
      var agencyKey = reportAgencyKey(agencyName);
      if (event.target.checked && !state.reportSelectedAgencies.some(function(name) { return reportAgencyKey(name) === agencyKey; })) state.reportSelectedAgencies.push(agencyName);
      if (!event.target.checked) state.reportSelectedAgencies = state.reportSelectedAgencies.filter(function(name) { return reportAgencyKey(name) !== agencyKey; });
      renderReport();
    });

    byId("reportApplyButton").addEventListener("click", function() {
      state.reportDateFrom = byId("reportDateFrom").value || state.reportDateFrom;
      state.reportDateTo = byId("reportDateTo").value || state.reportDateTo;
      state.reportSelectedFirms = state.reportSelectedFirms.filter(function(name) { return isFirmNameVisibleForReport(name); });
      var agencies = reportAgencyNames();
      state.reportSelectedAgencies = state.reportSelectedAgencies.filter(function(name) {
        var key = reportAgencyKey(name);
        return agencies.some(function(agency) { return reportAgencyKey(agency) === key; });
      });
      renderReport();
    });

    byId("reportColumnsButton").addEventListener("click", openReportColumnsDialog);
    byId("reportResetColumnsButton").addEventListener("click", resetReportColumns);
    byId("reportColumnsResetInside").addEventListener("click", resetReportColumns);
    byId("reportColumnsSaveButton").addEventListener("click", function() {
      saveReportColumns();
      closeReportColumnsDialog(false);
      renderReport();
    });

    byId("reportColumnsOverlay").addEventListener("click", function(event) {
      if (event.target === byId("reportColumnsOverlay") || event.target.closest("[data-report-columns-close]")) {
        closeReportColumnsDialog(true);
        return;
      }
      var check = event.target.closest("[data-report-column-check]");
      if (check) {
        var key = check.getAttribute("data-report-column-check");
        var col = state.reportColumns.find(function(item) { return item.key === key; });
        if (col && !col.required) col.visible = check.checked;
        return;
      }
      var up = event.target.closest("[data-report-column-up]");
      if (up) {
        moveReportColumn(up.getAttribute("data-report-column-up"), -1);
        return;
      }
      var down = event.target.closest("[data-report-column-down]");
      if (down) {
        moveReportColumn(down.getAttribute("data-report-column-down"), 1);
      }
    });

    byId("webSettingsButton").addEventListener("click", openWebSettings);
    byId("webSettingsOverlay").addEventListener("click", function(event) {
      if (event.target === byId("webSettingsOverlay") || event.target.closest("[data-web-settings-close]")) {
        closeWebSettings();
      }
    });
    document.addEventListener("keydown", function(event) {
      if (event.key !== "Escape") return;
      if (byId("employeeDetailOverlay").classList.contains("is-open")) closeEmployeeDetails();
      if (byId("dashMovementOverlay").classList.contains("is-open")) closeDashboardMovement();
      if (byId("webSettingsOverlay").classList.contains("is-open")) closeWebSettings();
      if (byId("financeSidePanel").classList.contains("is-open")) closeFinanceSummaryPanel();
      if (byId("firmDrawer").classList.contains("is-open")) closeFirmDrawer();
    });
    byId("webLanguageSelect").addEventListener("change", function(event) {
      state.webSettings.language = event.target.value || "uk";
      saveWebSettings();
      applyWebSettings();
    });
    byId("webThemeSelect").addEventListener("change", function(event) {
      state.webSettings.theme = event.target.value || "dark";
      saveWebSettings();
      applyWebSettings();
    });
    byId("webDensitySelect").addEventListener("change", function(event) {
      state.webSettings.density = event.target.value || "normal";
      saveWebSettings();
      applyWebSettings();
    });
    byId("webScaleRange").addEventListener("input", function(event) {
      state.webSettings.scale = Number(event.target.value) || 100;
      saveWebSettings();
      applyWebSettings();
    });
    byId("webSettingsResetButton").addEventListener("click", resetWebSettings);

    byId("firmSearchInput").addEventListener("input", function(event) {
      state.firmQuery = (event.target.value || "").trim().toLowerCase();
      renderFirms();
    });

    byId("firmDrawerToggle").addEventListener("click", toggleFirmDrawer);
    byId("employeeFirmPicker").addEventListener("click", openFirmDrawer);
    byId("employeeReportButton").addEventListener("click", showReport);
    byId("problemsFirmPicker").addEventListener("click", openFirmDrawer);
    byId("drawerBackdrop").addEventListener("click", closeFirmDrawer);

    byId("sideFirmList").addEventListener("click", function(event) {
      var item = event.target.closest("[data-firm-name]");
      if (!item) return;
      selectFirm(item.getAttribute("data-firm-name") || "", false);
    });

    byId("firmList").addEventListener("click", function(event) {
      var item = event.target.closest("[data-firm-name]");
      if (!item) return;
      selectFirm(item.getAttribute("data-firm-name") || "", true);
    });

    byId("globalSearchInput").addEventListener("input", function(event) {
      state.query = event.target.value || "";
      byId("searchInput").value = state.query;
      showDashboard();
      renderEmployees();
    });

    var moduleButtons = document.querySelectorAll("[data-target]");
    for (var i = 0; i < moduleButtons.length; i++) {
      moduleButtons[i].addEventListener("click", function(event) {
        var target = event.currentTarget.getAttribute("data-target");
        if (target === "dashboard") {
          showDashboard();
          return;
        }

        if (target === "employees") {
          state.query = "";
          byId("searchInput").value = "";
          byId("employeesSearchInput").value = "";
          showEmployees();
          return;
        }

        if (target === "problems") {
          state.query = "";
          byId("searchInput").value = "";
          byId("employeesSearchInput").value = "";
          showProblems();
          return;
        }

        if (target === "report") {
          state.reportQuery = "";
          byId("reportSearchInput").value = "";
          showReport();
          return;
        }

        if (target === "finance") {
          showFinance();
          return;
        }

        showPlaceholder(webT(target) === target ? webT("report") : webT(target));
      });
    }

    var backButtons = document.querySelectorAll("[data-back]");
    for (var j = 0; j < backButtons.length; j++) {
      backButtons[j].addEventListener("click", showHome);
    }

    loadWebSettings();
    loadReportColumns();
    loadFinanceColumnWidths();
    loadDashboardLayout();
    setupDashboardInteractions();
    setupFinanceColumnResizing();
    updateClock();
    setInterval(updateClock, 30000);
    loadData();
  </script>
</body>
</html>
""";
        }

        private static string ResolveLocalBindAddress(string value)
        {
            var normalized = (value ?? string.Empty).Trim();
            if (normalized.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || normalized == "127.0.0.1"
                || normalized == "::1")
            {
                return normalized;
            }

            if (normalized == "0.0.0.0" || normalized == "*" || normalized == "+")
                return "0.0.0.0";

            return "127.0.0.1";
        }

        private static int NormalizePort(int port)
        {
            return port is >= 1024 and <= 65535 ? port : 47831;
        }
    }
}
