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
        private readonly SalaryMonthDisplayService _salaryMonthDisplayService;
        private readonly AppStatisticsService _appStatisticsService;
        private readonly KeepAwakeService _keepAwakeService;
        private WebApplication? _app;

        public WebPanelHostService(
            AppSettingsService settingsService,
            CompanyService companyService,
            EmployeeService employeeService,
            TemplateService templateService,
            FinanceService financeService,
            SalaryMonthDisplayService salaryMonthDisplayService,
            AppStatisticsService appStatisticsService,
            KeepAwakeService keepAwakeService)
        {
            _settingsService = settingsService;
            _companyService = companyService;
            _employeeService = employeeService;
            _templateService = templateService;
            _financeService = financeService;
            _salaryMonthDisplayService = salaryMonthDisplayService;
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

            app.MapGet("/api/v1/dashboard", (HttpRequest request) =>
            {
                try
                {
                    var movementMonths = ParseDashboardMovementMonthCount(request.Query["movementMonths"]);
                    return Results.Ok(BuildDashboardModel(movementMonths));
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

        private static int ParseDashboardMovementMonthCount(string? raw)
        {
            if (!int.TryParse(raw, out var count))
                return 1;
            return count < 1 ? 1 : count > 3 ? 3 : count;
        }

        private object BuildDashboardModel(int movementMonthCount = 1)
        {
            movementMonthCount = ParseDashboardMovementMonthCount(movementMonthCount.ToString());
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

                    if (IsDashboardDateInMovementPeriod(employee.StartDate, now, movementMonthCount)
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

                    if (IsDashboardDateInMovementPeriod(archived.StartDate, now, movementMonthCount)
                        && AddDashboardEmployeeIdentity(addedIds, addedFallbacks, archived.UniqueId, archived.FirmName, archived.FullName, archived.StartDate))
                    {
                        monthlyAdded.Add(MapDashboardMovement(archived.FullName, archived.FirmName, archived.StartDate, archived.UniqueId, "Додано", "#22c55e"));
                    }

                    if (IsDashboardDateInMovementPeriod(archived.EndDate, now, movementMonthCount))
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
                    movementMonthCount,
                    movementPeriodHint = BuildDashboardMovementPeriodHint(movementMonthCount, now),
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
                    var entries = _salaryMonthDisplayService.BuildDisplayedEntries(year, month);
                    if (entries.Count == 0)
                        continue;

                    var (_, expenses) = _financeService.LoadAllFirmPayments(year, month);

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

                    var totalEntries = entries.Count;
                    var totalExpenses = expenses.Sum(expense => expense.Amount);
                    var isFullyPaid = paidEntries > 0 && paidEntries == totalEntries;
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
                        totalEntries,
                        paidEntries,
                        paidRatio,
                        statusColor,
                        statusIcon = isFullyPaid ? "✓" : paidEntries > 0 ? "!" : "×",
                        countText = $"{paidEntries}/{totalEntries} працівників"
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

        private static bool IsDashboardDateInMovementPeriod(string dateText, DateTime now, int monthCount)
        {
            var date = DateParsingHelper.TryParseDate(dateText);
            if (date == null)
                return false;

            monthCount = ParseDashboardMovementMonthCount(monthCount.ToString());
            var periodStart = new DateTime(now.Year, now.Month, 1).AddMonths(-(monthCount - 1));
            var periodEnd = new DateTime(now.Year, now.Month, 1).AddMonths(1).AddDays(-1);
            var value = date.Value.Date;
            return value >= periodStart && value <= periodEnd;
        }

        private static string BuildDashboardMovementPeriodHint(int monthCount, DateTime now)
        {
            monthCount = ParseDashboardMovementMonthCount(monthCount.ToString());
            var end = new DateTime(now.Year, now.Month, 1);
            if (monthCount <= 1)
                return FormatDashboardMonthLabel(end.Year, end.Month);

            var start = end.AddMonths(-(monthCount - 1));
            return $"{FormatDashboardMonthLabel(start.Year, start.Month)} – {FormatDashboardMonthLabel(end.Year, end.Month)}";
        }

        private static string FormatDashboardMonthLabel(int year, int month)
        {
            try
            {
                var dt = new DateTime(year, month, 1);
                var name = dt.ToString("MMMM", System.Globalization.CultureInfo.CurrentUICulture);
                return char.ToUpper(name[0]) + name[1..] + " " + year;
            }
            catch
            {
                return $"{month:D2}.{year}";
            }
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

            var (archivedFirstName, archivedLastName) = EmployeeSummary.SplitFullName(archived.FullName);
            return new EmployeeSummary
            {
                UniqueId = archived.UniqueId,
                FullName = archived.FullName,
                FirstName = archivedFirstName,
                LastName = archivedLastName,
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

        private static string? _homePageHtml;

        private static string BuildHomePageHtml()
        {
            if (_homePageHtml != null)
                return _homePageHtml;

            var path = Path.Combine(AppContext.BaseDirectory, "WebPanel", "home.html");
            _homePageHtml = File.ReadAllText(path);
            return _homePageHtml;
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
