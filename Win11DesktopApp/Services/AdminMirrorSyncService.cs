using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    public sealed class AdminMirrorSyncService
    {
        private const string SchemaVersion = "1";
        private const string OperationFullResync = "full_resync";
        private const string OperationFullResyncStart = "full_resync_start";
        private const string OperationFullResyncEmployersBatch = "full_resync_employers_batch";
        private const string OperationFullResyncEmployeesBatch = "full_resync_employees_batch";
        private const string OperationFullResyncFinish = "full_resync_finish";
        private const string OperationEmployerUpsert = "employer_upsert";
        private const string OperationEmployerDelete = "employer_delete";
        private const string OperationEmployeeUpsert = "employee_upsert";
        private const string OperationEmployeeDelete = "employee_delete";
        private const int FullResyncEmployerBatchSize = 10;
        private const int FullResyncEmployeeBatchSize = 50;
        private static readonly TimeSpan MirrorSyncTimeout = TimeSpan.FromSeconds(90);

        private readonly string _storageFolder;
        private readonly string _outboxPath;
        private readonly string _statePath;
        private readonly HttpClient _mirrorHttpClient = new() { Timeout = MirrorSyncTimeout };
        private readonly SemaphoreSlim _outboxLock = new(1, 1);
        private readonly SemaphoreSlim _processLock = new(1, 1);
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };

        private bool _workerStarted;

        public AdminMirrorSyncService()
        {
            _storageFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AgencyContractor",
                "AdminMirror");
            _outboxPath = Path.Combine(_storageFolder, "outbox.json");
            _statePath = Path.Combine(_storageFolder, "state.json");
        }

        public void Start(string? startupClientId = null)
        {
            if (_workerStarted)
                return;

            Directory.CreateDirectory(_storageFolder);
            _workerStarted = true;

            _ = Task.Run(() => BackgroundLoopAsync());
            _ = Task.Run(async () =>
            {
                try
                {
                    var state = await LoadStateAsync().ConfigureAwait(false);
                    if (!state.HasCompletedInitialFullSync)
                        await EnqueueFullResyncAsync().ConfigureAwait(false);

                    if (!string.IsNullOrWhiteSpace(startupClientId))
                        await ProcessOutboxOnceAsync(startupClientId).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LoggingService.LogWarning("AdminMirrorSyncService.Start", ex.Message);
                }
            });
        }

        public void EnqueueEmployerUpsert(EmployerCompany? company)
        {
            if (company == null)
                return;

            var payload = BuildEmployerPayload(company);
            if (payload == null)
                return;

            var entry = new AdminMirrorOutboxEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Operation = OperationEmployerUpsert,
                ScopeKey = $"employer:{company.Id}",
                PayloadJson = JsonSerializer.Serialize(payload, _jsonOptions),
                UpdatedAtUtc = DateTime.UtcNow
            };

            _ = EnqueueAsync(entry);
        }

        public void EnqueueEmployerDelete(EmployerCompany? company)
        {
            if (company == null)
                return;

            var payload = new AdminMirrorEmployerDeletePayload
            {
                EmployerId = company.Id,
                AgencyId = BuildAgencyId(company.Agency, company.Id)
            };

            var entry = new AdminMirrorOutboxEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Operation = OperationEmployerDelete,
                ScopeKey = $"employer:{company.Id}",
                PayloadJson = JsonSerializer.Serialize(payload, _jsonOptions),
                UpdatedAtUtc = DateTime.UtcNow
            };

            _ = EnqueueAsync(entry);
        }

        public void EnqueueEmployeeUpsert(string? firmName, string employeeFolder, EmployeeData? data)
        {
            if (data == null)
                return;

            EnsureEmployeeUniqueId(data);
            var payload = BuildEmployeePayload(firmName, employeeFolder, data);
            if (payload == null)
                return;

            var entry = new AdminMirrorOutboxEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Operation = OperationEmployeeUpsert,
                ScopeKey = $"employee:{payload.Employee.EmployeeId}",
                PayloadJson = JsonSerializer.Serialize(payload, _jsonOptions),
                UpdatedAtUtc = DateTime.UtcNow
            };

            _ = EnqueueAsync(entry);
        }

        public void EnqueueEmployeeDelete(string? firmName, EmployeeData? data)
        {
            if (data == null)
                return;

            EnsureEmployeeUniqueId(data);
            if (string.IsNullOrWhiteSpace(data.UniqueId))
                return;

            var payload = new AdminMirrorEmployeeDeletePayload
            {
                EmployeeId = data.UniqueId,
                EmployerId = TryResolveEmployerId(firmName, data.ArchivedFromFirm)
            };

            var entry = new AdminMirrorOutboxEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Operation = OperationEmployeeDelete,
                ScopeKey = $"employee:{data.UniqueId}",
                PayloadJson = JsonSerializer.Serialize(payload, _jsonOptions),
                UpdatedAtUtc = DateTime.UtcNow
            };

            _ = EnqueueAsync(entry);
        }

        public async Task EnqueueFullResyncAsync()
        {
            var entry = new AdminMirrorOutboxEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Operation = OperationFullResync,
                ScopeKey = OperationFullResync,
                UpdatedAtUtc = DateTime.UtcNow
            };

            await EnqueueAsync(entry).ConfigureAwait(false);
        }

        private async Task EnqueueAsync(AdminMirrorOutboxEntry entry)
        {
            try
            {
                await _outboxLock.WaitAsync().ConfigureAwait(false);
                var outbox = LoadOutboxUnsafe();

                if (string.Equals(entry.Operation, OperationFullResync, StringComparison.Ordinal))
                {
                    outbox.Clear();
                    outbox.Add(entry);
                }
                else if (outbox.Any(item => string.Equals(item.Operation, OperationFullResync, StringComparison.Ordinal)))
                {
                    return;
                }
                else
                {
                    outbox.RemoveAll(item => string.Equals(item.ScopeKey, entry.ScopeKey, StringComparison.OrdinalIgnoreCase));
                    outbox.Add(entry);
                }

                SaveOutboxUnsafe(outbox);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("AdminMirrorSyncService.Enqueue", ex.Message);
            }
            finally
            {
                _outboxLock.Release();
            }

            _ = Task.Run(() => ProcessOutboxOnceAsync());
        }

        private async Task BackgroundLoopAsync()
        {
            while (true)
            {
                try
                {
                    await ProcessOutboxOnceAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LoggingService.LogWarning("AdminMirrorSyncService.BackgroundLoop", ex.Message);
                }

                await Task.Delay(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
            }
        }

        private async Task ProcessOutboxOnceAsync(string? preferredClientId = null)
        {
            if (!await _processLock.WaitAsync(0).ConfigureAwait(false))
                return;

            try
            {
                var clientId = await EnsureClientIdAsync(preferredClientId).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(clientId))
                    return;

                while (true)
                {
                    AdminMirrorOutboxEntry? nextEntry;

                    await _outboxLock.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        nextEntry = LoadOutboxUnsafe().OrderBy(item => item.UpdatedAtUtc).FirstOrDefault();
                    }
                    finally
                    {
                        _outboxLock.Release();
                    }

                    if (nextEntry == null)
                        break;

                    try
                    {
                        await ExecuteEntryAsync(clientId, nextEntry).ConfigureAwait(false);
                        await RemoveProcessedEntryAsync(nextEntry.Id).ConfigureAwait(false);
                        await UpdateStateAfterSuccessAsync(nextEntry.Operation).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        await UpdateStateAfterFailureAsync(ex.Message).ConfigureAwait(false);
                        LoggingService.LogWarning("AdminMirrorSyncService.ProcessOutbox", ex.Message);
                        break;
                    }
                }
            }
            finally
            {
                _processLock.Release();
            }
        }

        private async Task ExecuteEntryAsync(string clientId, AdminMirrorOutboxEntry entry)
        {
            switch (entry.Operation)
            {
                case OperationFullResync:
                    await PerformFullResyncAsync(clientId).ConfigureAwait(false);
                    return;

                case OperationEmployerUpsert:
                    {
                        var payload = JsonSerializer.Deserialize<AdminMirrorEmployerPayload>(entry.PayloadJson, _jsonOptions);
                        if (payload == null)
                            throw new InvalidOperationException($"Mirror outbox payload is invalid for operation '{entry.Operation}' (entry {entry.Id}).");

                        await SyncEmployerUpsertAsync(clientId, payload).ConfigureAwait(false);
                        return;
                    }

                case OperationEmployerDelete:
                    {
                        var payload = JsonSerializer.Deserialize<AdminMirrorEmployerDeletePayload>(entry.PayloadJson, _jsonOptions);
                        if (payload == null)
                            throw new InvalidOperationException($"Mirror outbox payload is invalid for operation '{entry.Operation}' (entry {entry.Id}).");

                        await SyncEmployerDeleteAsync(clientId, payload).ConfigureAwait(false);
                        return;
                    }

                case OperationEmployeeUpsert:
                    {
                        var payload = JsonSerializer.Deserialize<AdminMirrorEmployeePayload>(entry.PayloadJson, _jsonOptions);
                        if (payload == null)
                            throw new InvalidOperationException($"Mirror outbox payload is invalid for operation '{entry.Operation}' (entry {entry.Id}).");

                        await SyncEmployeeUpsertAsync(clientId, payload).ConfigureAwait(false);
                        return;
                    }

                case OperationEmployeeDelete:
                    {
                        var payload = JsonSerializer.Deserialize<AdminMirrorEmployeeDeletePayload>(entry.PayloadJson, _jsonOptions);
                        if (payload == null)
                            throw new InvalidOperationException($"Mirror outbox payload is invalid for operation '{entry.Operation}' (entry {entry.Id}).");

                        await SyncEmployeeDeleteAsync(clientId, payload).ConfigureAwait(false);
                        return;
                    }
            }
        }

        private async Task PerformFullResyncAsync(string clientId)
        {
            var employerPayloads = new List<AdminMirrorEmployerPayload>();
            var employeePayloads = new List<AdminMirrorEmployeePayload>();
            var companies = App.CompanyService?.Companies?.ToList() ?? new List<EmployerCompany>();
            foreach (var company in companies)
            {
                var employerPayload = BuildEmployerPayload(company);
                if (employerPayload != null)
                    employerPayloads.Add(employerPayload);

                var summaries = App.EmployeeService?.GetEmployeesForFirm(company.Name) ?? new List<EmployeeSummary>();
                foreach (var summary in summaries)
                {
                    var data = App.EmployeeService?.LoadEmployeeData(summary.EmployeeFolder);
                    var employeePayload = BuildEmployeePayload(company.Name, summary.EmployeeFolder, data);
                    if (employeePayload != null)
                        employeePayloads.Add(employeePayload);
                }
            }

            var archivedEmployees = App.EmployeeService?.GetArchivedEmployees() ?? new List<ArchivedEmployeeSummary>();
            foreach (var archived in archivedEmployees)
            {
                var data = App.EmployeeService?.LoadEmployeeData(archived.EmployeeFolder);
                var employeePayload = BuildEmployeePayload(archived.FirmName, archived.EmployeeFolder, data);
                if (employeePayload != null)
                    employeePayloads.Add(employeePayload);
            }

            await CallMirrorSyncAsync(
                OperationFullResyncStart,
                new
                {
                    employers_total = employerPayloads.Count,
                    employees_total = employeePayloads.Count
                },
                $"full_resync_start (employers={employerPayloads.Count}, employees={employeePayloads.Count})").ConfigureAwait(false);

            var employerBatches = employerPayloads
                .Select(ToEmployerSyncPayload)
                .Chunk(FullResyncEmployerBatchSize)
                .ToList();
            for (var i = 0; i < employerBatches.Count; i++)
            {
                var batch = employerBatches[i];
                await CallMirrorSyncAsync(
                    OperationFullResyncEmployersBatch,
                    new { employers = batch },
                    $"full_resync_employers_batch {i + 1}/{employerBatches.Count} (count={batch.Length})").ConfigureAwait(false);
            }

            var employeeBatches = employeePayloads
                .Select(ToEmployeeSyncPayload)
                .Chunk(FullResyncEmployeeBatchSize)
                .ToList();
            for (var i = 0; i < employeeBatches.Count; i++)
            {
                var batch = employeeBatches[i];
                await CallMirrorSyncAsync(
                    OperationFullResyncEmployeesBatch,
                    new { employees = batch },
                    $"full_resync_employees_batch {i + 1}/{employeeBatches.Count} (count={batch.Length})").ConfigureAwait(false);
            }

            await CallMirrorSyncAsync(
                OperationFullResyncFinish,
                new
                {
                    employers_total = employerPayloads.Count,
                    employees_total = employeePayloads.Count
                },
                $"full_resync_finish (employers={employerPayloads.Count}, employees={employeePayloads.Count})").ConfigureAwait(false);
        }

        private async Task SyncEmployerUpsertAsync(string clientId, AdminMirrorEmployerPayload payload)
        {
            await CallMirrorSyncAsync(OperationEmployerUpsert, ToEmployerSyncPayload(payload)).ConfigureAwait(false);
        }

        private async Task SyncEmployerDeleteAsync(string clientId, AdminMirrorEmployerDeletePayload payload)
        {
            await CallMirrorSyncAsync(OperationEmployerDelete, new
            {
                employer_id = payload.EmployerId,
                agency_id = payload.AgencyId,
                agency_still_referenced = AgencyStillReferenced(payload.AgencyId)
            }).ConfigureAwait(false);
        }

        private async Task SyncEmployeeUpsertAsync(string clientId, AdminMirrorEmployeePayload payload)
        {
            await CallMirrorSyncAsync(OperationEmployeeUpsert, ToEmployeeSyncPayload(payload)).ConfigureAwait(false);
        }

        private async Task SyncEmployeeDeleteAsync(string clientId, AdminMirrorEmployeeDeletePayload payload)
        {
            await CallMirrorSyncAsync(OperationEmployeeDelete, new
            {
                employee_id = payload.EmployeeId,
                employer_id = payload.EmployerId
            }).ConfigureAwait(false);
        }

        private async Task CallMirrorSyncAsync(string operation, object payload, string? operationLabel = null)
        {
            var label = string.IsNullOrWhiteSpace(operationLabel) ? operation : operationLabel;
            var request = new HttpRequestMessage(HttpMethod.Post, $"{TelemetryService.BaseUrl}/functions/v1/mirror-sync")
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    machine_id = LicenseService.GetMachineId(),
                    app_version = AppSettingsService.CurrentAppVersion,
                    schema_version = SchemaVersion,
                    operation,
                    payload
                }, _jsonOptions), Encoding.UTF8, "application/json")
            };
            TelemetryService.ApplyHeaders(request);

            try
            {
                using var response = await _mirrorHttpClient.SendAsync(request).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException($"Mirror sync failed for {label}: {(int)response.StatusCode} {body}");
            }
            catch (TaskCanceledException ex) when (!ex.CancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"Mirror sync timed out for {label} after {_mirrorHttpClient.Timeout.TotalSeconds:0} seconds.",
                    ex);
            }
        }

        private static object ToEmployerSyncPayload(AdminMirrorEmployerPayload payload) => new
        {
            agency = new
            {
                agency_id = payload.Agency.AgencyId,
                name = payload.Agency.Name,
                ico = payload.Agency.Ico,
                full_address = payload.Agency.FullAddress,
                source_updated_at = payload.Agency.SourceUpdatedAt
            },
            employer = new
            {
                employer_id = payload.Employer.EmployerId,
                agency_id = payload.Employer.AgencyId,
                name = payload.Employer.Name,
                ico = payload.Employer.Ico,
                legal_address = payload.Employer.LegalAddress,
                weekly_work_hours = payload.Employer.WeeklyWorkHours,
                daily_work_hours = payload.Employer.DailyWorkHours,
                shift_count = payload.Employer.ShiftCount,
                hidden_from_year = payload.Employer.HiddenFromYear,
                hidden_from_month = payload.Employer.HiddenFromMonth,
                created_at = payload.Employer.CreatedAt,
                source_updated_at = payload.Employer.SourceUpdatedAt,
                addresses = payload.Employer.Addresses.Select(item => new
                {
                    street = item.Street,
                    number = item.Number,
                    city = item.City,
                    zip_code = item.ZipCode
                }).ToList(),
                positions = payload.Employer.Positions.Select(item => new
                {
                    title = item.Title,
                    position_number = item.PositionNumber,
                    monthly_salary_brutto = item.MonthlySalaryBrutto,
                    hourly_salary = item.HourlySalary
                }).ToList()
            }
        };

        private static object ToEmployeeSyncPayload(AdminMirrorEmployeePayload payload) => new
        {
            employee = new
            {
                employee_id = payload.Employee.EmployeeId,
                employer_id = payload.Employee.EmployerId,
                full_name = payload.Employee.FullName,
                first_name = payload.Employee.FirstName,
                last_name = payload.Employee.LastName,
                birth_date = payload.Employee.BirthDate,
                employee_type = payload.Employee.EmployeeType,
                eu_document_type = payload.Employee.EuDocumentType,
                visa_doc_type = payload.Employee.VisaDocType,
                gender = payload.Employee.Gender,
                passport_number = payload.Employee.PassportNumber,
                passport_city = payload.Employee.PassportCity,
                passport_country = payload.Employee.PassportCountry,
                citizenship = payload.Employee.Citizenship,
                issuing_country = payload.Employee.IssuingCountry,
                passport_expiry = payload.Employee.PassportExpiry,
                visa_number = payload.Employee.VisaNumber,
                visa_type = payload.Employee.VisaType,
                visa_expiry = payload.Employee.VisaExpiry,
                insurance_company_short = payload.Employee.InsuranceCompanyShort,
                insurance_company_full = payload.Employee.InsuranceCompanyFull,
                insurance_number = payload.Employee.InsuranceNumber,
                insurance_expiry = payload.Employee.InsuranceExpiry,
                work_permit_name = payload.Employee.WorkPermitName,
                work_permit_number = payload.Employee.WorkPermitNumber,
                work_permit_type = payload.Employee.WorkPermitType,
                work_permit_issue_date = payload.Employee.WorkPermitIssueDate,
                work_permit_expiry = payload.Employee.WorkPermitExpiry,
                work_permit_authority = payload.Employee.WorkPermitAuthority,
                address_local_street = payload.Employee.AddressLocalStreet,
                address_local_number = payload.Employee.AddressLocalNumber,
                address_local_city = payload.Employee.AddressLocalCity,
                address_local_zip = payload.Employee.AddressLocalZip,
                address_abroad_street = payload.Employee.AddressAbroadStreet,
                address_abroad_number = payload.Employee.AddressAbroadNumber,
                address_abroad_city = payload.Employee.AddressAbroadCity,
                address_abroad_zip = payload.Employee.AddressAbroadZip,
                work_address_tag = payload.Employee.WorkAddressTag,
                position_tag = payload.Employee.PositionTag,
                position_number = payload.Employee.PositionNumber,
                monthly_salary_brutto = payload.Employee.MonthlySalaryBrutto,
                hourly_salary = payload.Employee.HourlySalary,
                contract_type = payload.Employee.ContractType,
                phone = payload.Employee.Phone,
                email = payload.Employee.Email,
                department = payload.Employee.Department,
                status = payload.Employee.Status,
                start_date = payload.Employee.StartDate,
                contract_sign_date = payload.Employee.ContractSignDate,
                end_date = payload.Employee.EndDate,
                is_archived = payload.Employee.IsArchived,
                archived_from_firm = payload.Employee.ArchivedFromFirm,
                source_updated_at = payload.Employee.SourceUpdatedAt,
                firm_history = payload.Employee.FirmHistory.Select(item => new
                {
                    firm_name = item.FirmName,
                    start_date = item.StartDate,
                    end_date = item.EndDate
                }).ToList()
            }
        };

        private async Task<string?> EnsureClientIdAsync(string? preferredClientId)
        {
            if (!string.IsNullOrWhiteSpace(preferredClientId))
                return preferredClientId;

            var current = TelemetryService.GetCurrentClientId();
            if (!string.IsNullOrWhiteSpace(current))
                return current;

            return await TelemetryService.EnsureStartupClientIdAsync().ConfigureAwait(false);
        }

        private AdminMirrorEmployerPayload? BuildEmployerPayload(EmployerCompany company)
        {
            if (company == null)
                return null;

            var agencyId = BuildAgencyId(company.Agency, company.Id);
            var now = DateTime.UtcNow;

            return new AdminMirrorEmployerPayload
            {
                Agency = new AdminMirrorAgencyDto
                {
                    AgencyId = agencyId,
                    Name = company.Agency?.Name ?? string.Empty,
                    Ico = company.Agency?.ICO ?? string.Empty,
                    FullAddress = company.Agency?.FullAddress ?? string.Empty,
                    SourceUpdatedAt = company.LastModified == default ? now : company.LastModified.ToUniversalTime()
                },
                Employer = new AdminMirrorEmployerDto
                {
                    EmployerId = company.Id,
                    AgencyId = agencyId,
                    Name = company.Name ?? string.Empty,
                    Ico = company.ICO ?? string.Empty,
                    LegalAddress = company.LegalAddress ?? string.Empty,
                    WeeklyWorkHours = company.WeeklyWorkHours,
                    DailyWorkHours = company.DailyWorkHours,
                    ShiftCount = company.ShiftCount,
                    HiddenFromYear = company.HiddenFromYear,
                    HiddenFromMonth = company.HiddenFromMonth,
                    CreatedAt = company.CreatedAt == default ? null : company.CreatedAt.ToUniversalTime(),
                    SourceUpdatedAt = company.LastModified == default ? now : company.LastModified.ToUniversalTime(),
                    Addresses = company.Addresses.Select((address, index) => new AdminMirrorEmployerAddressDto
                    {
                        Street = address.Street ?? string.Empty,
                        Number = address.Number ?? string.Empty,
                        City = address.City ?? string.Empty,
                        ZipCode = address.ZipCode ?? string.Empty,
                        SortOrder = index
                    }).ToList(),
                    Positions = company.Positions.Select((position, index) => new AdminMirrorEmployerPositionDto
                    {
                        Title = position.Title ?? string.Empty,
                        PositionNumber = position.PositionNumber ?? string.Empty,
                        MonthlySalaryBrutto = position.MonthlySalaryBrutto,
                        HourlySalary = position.HourlySalary,
                        SortOrder = index
                    }).ToList()
                }
            };
        }

        private AdminMirrorEmployeePayload? BuildEmployeePayload(string? firmName, string employeeFolder, EmployeeData? data)
        {
            if (data == null)
                return null;

            EnsureEmployeeUniqueId(data);
            if (string.IsNullOrWhiteSpace(data.UniqueId))
                return null;

            var employerId = TryResolveEmployerId(firmName, data.ArchivedFromFirm);
            var now = DateTime.UtcNow;

            return new AdminMirrorEmployeePayload
            {
                Employee = new AdminMirrorEmployeeDto
                {
                    EmployeeId = data.UniqueId,
                    EmployerId = employerId,
                    FullName = $"{data.FirstName} {data.LastName}".Trim(),
                    FirstName = data.FirstName ?? string.Empty,
                    LastName = data.LastName ?? string.Empty,
                    BirthDate = data.BirthDate ?? string.Empty,
                    EmployeeType = data.EmployeeType ?? string.Empty,
                    EuDocumentType = data.EuDocumentType ?? string.Empty,
                    VisaDocType = data.VisaDocType ?? string.Empty,
                    Gender = data.Gender ?? string.Empty,
                    PassportNumber = data.PassportNumber ?? string.Empty,
                    PassportCity = data.PassportCity ?? string.Empty,
                    PassportCountry = data.PassportCountry ?? string.Empty,
                    Citizenship = data.Citizenship ?? string.Empty,
                    IssuingCountry = data.IssuingCountry ?? string.Empty,
                    PassportExpiry = data.PassportExpiry ?? string.Empty,
                    VisaNumber = data.VisaNumber ?? string.Empty,
                    VisaType = data.VisaType ?? string.Empty,
                    VisaExpiry = data.VisaExpiry ?? string.Empty,
                    InsuranceCompanyShort = data.InsuranceCompanyShort ?? string.Empty,
                    InsuranceCompanyFull = data.InsuranceCompanyFull ?? string.Empty,
                    InsuranceNumber = data.InsuranceNumber ?? string.Empty,
                    InsuranceExpiry = data.InsuranceExpiry ?? string.Empty,
                    WorkPermitName = data.WorkPermitName ?? string.Empty,
                    WorkPermitNumber = data.WorkPermitNumber ?? string.Empty,
                    WorkPermitType = data.WorkPermitType ?? string.Empty,
                    WorkPermitIssueDate = data.WorkPermitIssueDate ?? string.Empty,
                    WorkPermitExpiry = data.WorkPermitExpiry ?? string.Empty,
                    WorkPermitAuthority = data.WorkPermitAuthority ?? string.Empty,
                    AddressLocalStreet = data.AddressLocal?.Street ?? string.Empty,
                    AddressLocalNumber = data.AddressLocal?.Number ?? string.Empty,
                    AddressLocalCity = data.AddressLocal?.City ?? string.Empty,
                    AddressLocalZip = data.AddressLocal?.Zip ?? string.Empty,
                    AddressAbroadStreet = data.AddressAbroad?.Street ?? string.Empty,
                    AddressAbroadNumber = data.AddressAbroad?.Number ?? string.Empty,
                    AddressAbroadCity = data.AddressAbroad?.City ?? string.Empty,
                    AddressAbroadZip = data.AddressAbroad?.Zip ?? string.Empty,
                    WorkAddressTag = data.WorkAddressTag ?? string.Empty,
                    PositionTag = data.PositionTag ?? string.Empty,
                    PositionNumber = data.PositionNumber ?? string.Empty,
                    MonthlySalaryBrutto = data.MonthlySalaryBrutto,
                    HourlySalary = data.HourlySalary,
                    ContractType = data.ContractType ?? string.Empty,
                    Phone = data.Phone ?? string.Empty,
                    Email = data.Email ?? string.Empty,
                    Department = data.Department ?? string.Empty,
                    Status = data.Status ?? string.Empty,
                    StartDate = data.StartDate ?? string.Empty,
                    ContractSignDate = data.ContractSignDate ?? string.Empty,
                    EndDate = data.EndDate ?? string.Empty,
                    IsArchived = data.IsArchived,
                    ArchivedFromFirm = data.ArchivedFromFirm ?? string.Empty,
                    SourceUpdatedAt = now,
                    FirmHistory = data.FirmHistory.Select((history, index) => new AdminMirrorEmployeeFirmHistoryDto
                    {
                        FirmName = history.FirmName ?? string.Empty,
                        StartDate = history.StartDate ?? string.Empty,
                        EndDate = history.EndDate ?? string.Empty,
                        SortOrder = index
                    }).ToList()
                }
            };
        }

        private Guid? TryResolveEmployerId(params string?[] firmNames)
        {
            var companies = App.CompanyService?.Companies?.ToList();
            if (companies == null)
                return null;

            foreach (var candidate in firmNames)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;

                var match = companies.FirstOrDefault(company =>
                    string.Equals(company.Name, candidate, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    return match.Id;
            }

            return null;
        }

        public string? InferFirmNameFromEmployeeFolder(string employeeFolder)
        {
            if (string.IsNullOrWhiteSpace(employeeFolder) || !Directory.Exists(employeeFolder))
                return null;

            var employeesFolder = Directory.GetParent(employeeFolder)?.Parent;
            if (employeesFolder == null)
                return null;

            var companyFolderName = employeesFolder.Name;
            var companies = App.CompanyService?.Companies?.ToList();
            if (companies == null)
                return companyFolderName;

            var match = companies.FirstOrDefault(company =>
                string.Equals(FolderService.NormalizeFolderName(company.Name), companyFolderName, StringComparison.OrdinalIgnoreCase));

            return match?.Name ?? companyFolderName;
        }

        private bool AgencyStillReferenced(string agencyId)
        {
            var companies = App.CompanyService?.Companies?.ToList();
            if (companies == null)
                return false;

            return companies.Any(company =>
                string.Equals(BuildAgencyId(company.Agency, company.Id), agencyId, StringComparison.OrdinalIgnoreCase));
        }

        private static string BuildAgencyId(AgencyCompany? agency, Guid employerId)
        {
            if (!string.IsNullOrWhiteSpace(agency?.ICO))
                return $"ico:{agency.ICO.Trim()}";
            if (!string.IsNullOrWhiteSpace(agency?.Name))
                return $"name:{agency.Name.Trim().ToLowerInvariant()}";
            return $"employer:{employerId}";
        }

        private static void EnsureEmployeeUniqueId(EmployeeData data)
        {
            if (string.IsNullOrWhiteSpace(data.UniqueId))
                data.UniqueId = Guid.NewGuid().ToString();
        }

        private List<AdminMirrorOutboxEntry> LoadOutboxUnsafe()
        {
            return SafeFileService.ReadJsonOrDefault(_outboxPath, new List<AdminMirrorOutboxEntry>(), _jsonOptions);
        }

        private void SaveOutboxUnsafe(List<AdminMirrorOutboxEntry> outbox)
        {
            SafeFileService.WriteJsonAtomic(_outboxPath, outbox, _jsonOptions);
        }

        private async Task RemoveProcessedEntryAsync(string entryId)
        {
            await _outboxLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var outbox = LoadOutboxUnsafe();
                outbox.RemoveAll(item => string.Equals(item.Id, entryId, StringComparison.OrdinalIgnoreCase));
                SaveOutboxUnsafe(outbox);
            }
            finally
            {
                _outboxLock.Release();
            }
        }

        private async Task<AdminMirrorLocalState> LoadStateAsync()
        {
            await Task.Yield();
            return SafeFileService.ReadJsonOrDefault(_statePath, new AdminMirrorLocalState(), _jsonOptions);
        }

        private async Task UpdateStateAfterSuccessAsync(string operation)
        {
            var state = await LoadStateAsync().ConfigureAwait(false);
            state.LastErrorText = string.Empty;
            state.LastSuccessfulSyncAtUtc = DateTime.UtcNow;
            if (string.Equals(operation, OperationFullResync, StringComparison.Ordinal))
            {
                state.HasCompletedInitialFullSync = true;
                state.LastFullSyncAtUtc = DateTime.UtcNow;
            }

            SafeFileService.WriteJsonAtomic(_statePath, state, _jsonOptions);
        }

        private async Task UpdateStateAfterFailureAsync(string errorText)
        {
            var state = await LoadStateAsync().ConfigureAwait(false);
            state.LastErrorText = errorText;
            SafeFileService.WriteJsonAtomic(_statePath, state, _jsonOptions);
        }

        private sealed class AdminMirrorOutboxEntry
        {
            public string Id { get; set; } = string.Empty;
            public string Operation { get; set; } = string.Empty;
            public string ScopeKey { get; set; } = string.Empty;
            public string PayloadJson { get; set; } = string.Empty;
            public DateTime UpdatedAtUtc { get; set; }
        }

        private sealed class AdminMirrorLocalState
        {
            public string SchemaVersion { get; set; } = AdminMirrorSyncService.SchemaVersion;
            public bool HasCompletedInitialFullSync { get; set; }
            public DateTime? LastFullSyncAtUtc { get; set; }
            public DateTime? LastSuccessfulSyncAtUtc { get; set; }
            public string LastErrorText { get; set; } = string.Empty;
        }

        private sealed class AdminMirrorEmployerPayload
        {
            public AdminMirrorAgencyDto Agency { get; set; } = new();
            public AdminMirrorEmployerDto Employer { get; set; } = new();
        }

        private sealed class AdminMirrorEmployerDeletePayload
        {
            public Guid EmployerId { get; set; }
            public string AgencyId { get; set; } = string.Empty;
        }

        private sealed class AdminMirrorEmployeePayload
        {
            public AdminMirrorEmployeeDto Employee { get; set; } = new();
        }

        private sealed class AdminMirrorEmployeeDeletePayload
        {
            public string EmployeeId { get; set; } = string.Empty;
            public Guid? EmployerId { get; set; }
        }
    }
}
