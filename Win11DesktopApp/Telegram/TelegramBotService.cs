using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.Telegram
{
    public sealed class TelegramBotService
    {
        private const string TelegramAiModel = "gemini-2.5-flash";
        private const int MaxConversationTurns = 20;
        private const int ConversationTurnsAfterCompression = 12;
        private const int ConversationTurnsSentToAi = 16;
        private const int ConversationContextTtlHours = 24;
        private const int MaxConversationSummaryLength = 1000;
        private const int MaxConversationTurnTextLength = 700;
        private const int MaxTelegramMessageLength = 3900;
        private const int DailyDigestLookAheadDays = 7;
        private const string TelegramRoleAdmin = "Admin";
        private readonly AppSettingsService _appSettingsService;
        private readonly TelegramPairingService _pairingService;
        private readonly CompanyService _companyService;
        private readonly EmployeeService _employeeService;
        private readonly FinanceService _financeService;
        private readonly GeminiApiService _geminiApiService;
        private readonly ActivityLogService _activityLogService;
        private readonly NewsService _newsService;
        private readonly RecentlyDeletedService _recentlyDeletedService;

        private TelegramBotClient? _client;
        private CancellationTokenSource? _cts;
        private Task? _dailyDigestTask;
        private string _lastDailyDigestLocalDate = string.Empty;
        private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
        private readonly ConcurrentDictionary<string, CallbackPayload> _callbackPayloads = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<long, ConversationContext> _conversationContexts = new();

        public event EventHandler? StateChanged;

        public bool IsRunning { get; private set; }
        public string LastStatus { get; private set; } = string.Empty;

        private sealed class CallbackPayload
        {
            public string Action { get; init; } = string.Empty;
            public string Value { get; init; } = string.Empty;
            public string Metadata { get; init; } = string.Empty;
            public long OwnerUserId { get; init; }
            public DateTime ExpiresAtUtc { get; init; }
        }

        private sealed class ConversationContext
        {
            public string LastEmployeeId { get; set; } = string.Empty;
            public string LastEmployeeName { get; set; } = string.Empty;
            public string LastFirmName { get; set; } = string.Empty;
            public string LastMonthKey { get; set; } = string.Empty;
            public string LastSecondaryMonthKey { get; set; } = string.Empty;
            public string LastTopic { get; set; } = string.Empty;
            public string LastAction { get; set; } = string.Empty;
            public string LastAiTool { get; set; } = string.Empty;
            public string HistorySummary { get; set; } = string.Empty;
            public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
            public List<(string role, string text)> History { get; } = new();
        }

        private sealed class SalaryInfo
        {
            public EmployeeSummary Employee { get; init; } = null!;
            public SalaryHistoryRecord Record { get; init; } = null!;
            public List<AdvancePayment> Advances { get; init; } = new();
            public string MonthKey { get; init; } = string.Empty;
            public bool IsPaid { get; init; }
            public DateTime? PaidAt { get; init; }
            public decimal TotalAdvances => Advances.Sum(a => a.Amount);
        }

        private sealed class FirmSalaryInfo
        {
            public string FirmName { get; init; } = string.Empty;
            public int Year { get; init; }
            public int Month { get; init; }
            public List<SalaryEntry> Entries { get; init; } = new();
            public List<FirmExpense> Expenses { get; init; } = new();
            public string MonthDisplay => $"{Month:D2}.{Year}";
            public decimal TotalGross => Entries.Sum(e => e.GrossSalary);
            public decimal TotalNet => Entries.Sum(GetEntryNetSalary);
            public decimal TotalAdvances => Entries.Sum(e => e.Advance);
            public decimal TotalExpenses => Expenses.Sum(e => e.Amount);
            public int PaidCount => Entries.Count(e => string.Equals(e.Status, "paid", StringComparison.OrdinalIgnoreCase));
            public int PendingCount => Math.Max(0, Entries.Count - PaidCount);
        }

        private sealed class EmployeeFlowPeriodMonthInfo
        {
            public string MonthKey { get; init; } = string.Empty;
            public string MonthDisplay { get; init; } = string.Empty;
            public int StartedCount { get; init; }
            public int EndedCount { get; init; }
            public int NetChange => StartedCount - EndedCount;
        }

        private sealed class EmployeeFlowPeriodFirmInfo
        {
            public string FirmName { get; init; } = string.Empty;
            public int StartedCount { get; init; }
            public int EndedCount { get; init; }
            public int NetChange => StartedCount - EndedCount;
        }

        private sealed class AiToolExecutionResult
        {
            public object Payload { get; init; } = new { ok = true };
            public EmployeeSummary? Employee { get; init; }
            public string FirmName { get; init; } = string.Empty;
            public string MonthKey { get; init; } = string.Empty;
            public List<PendingFile> FilesToSend { get; init; } = new();
            public List<EmployeeLookupResult> CandidateEmployees { get; init; } = new();
        }

        private sealed class PendingFile
        {
            public string FilePath { get; init; } = string.Empty;
            public byte[] ContentBytes { get; init; } = Array.Empty<byte>();
            public string FileName { get; init; } = string.Empty;
            public string Caption { get; init; } = string.Empty;
            public bool IsPhoto { get; init; }
        }

        private sealed class FirmMatchResult
        {
            public EmployerCompany Company { get; init; } = null!;
            public int Score { get; init; }
        }

        private sealed class AllFirmsSalaryInfo
        {
            public int Year { get; init; }
            public int Month { get; init; }
            public List<FirmSalaryInfo> Firms { get; init; } = new();
            public string MonthDisplay => $"{Month:D2}.{Year}";
            public decimal TotalGross => Firms.Sum(f => f.TotalGross);
            public decimal TotalNet => Firms.Sum(f => f.TotalNet);
            public decimal TotalAdvances => Firms.Sum(f => f.TotalAdvances);
            public decimal TotalExpenses => Firms.Sum(f => f.TotalExpenses);
            public int TotalPaidCount => Firms.Sum(f => f.PaidCount);
            public int TotalPendingCount => Firms.Sum(f => f.PendingCount);
        }

        private sealed class FirmEmployeeSalaryDetail
        {
            public string EmployeeName { get; init; } = string.Empty;
            public string EmployeeFolder { get; init; } = string.Empty;
            public decimal HoursWorked { get; init; }
            public decimal HourlyRate { get; init; }
            public decimal SalaryAdvance { get; init; }
            public decimal NetSalary { get; init; }
            public string Note { get; init; } = string.Empty;
            public List<(string name, string operation, decimal value)> OrderedColumns { get; init; } = new();
            public string OrderedRowText { get; init; } = string.Empty;
        }

        private sealed class EmployeeLookupResult
        {
            public string UniqueId { get; init; } = string.Empty;
            public string FullName { get; init; } = string.Empty;
            public string FirmName { get; init; } = string.Empty;
            public string PositionTitle { get; init; } = string.Empty;
            public string StartDate { get; init; } = string.Empty;
            public string EndDate { get; init; } = string.Empty;
            public string EmployeeFolder { get; init; } = string.Empty;
            public string Source { get; init; } = string.Empty;
            public string StatusLabel { get; init; } = string.Empty;
            public DateTime? DeletedAtUtc { get; init; }
            public EmployeeSummary? ActiveEmployee { get; init; }
            public ArchivedEmployeeSummary? ArchivedEmployee { get; init; }
            public RecentlyDeletedItem? RecentlyDeletedItem { get; init; }
            public bool IsArchived => string.Equals(Source, "archived", StringComparison.Ordinal);
            public bool IsRecentlyDeleted => string.Equals(Source, "recently_deleted", StringComparison.Ordinal);
        }

        private static readonly string[] SalaryKeywords =
        {
            "зарплат", "зарпл", "зароб", "аванс", "нетто", "брутто", "виплат", "оплат", "зп"
        };

        private static readonly string[] SalaryBreakdownKeywords =
        {
            "розпиши", "розписати", "поясни", "пояснити", "з чого", "що входить", "складається", "детально"
        };

        private static readonly string[] HourlyRateKeywords =
        {
            "на годину", "за годину", "ставка", "погодин", "годинна"
        };

        private static readonly string[] HoursWorkedKeywords =
        {
            "скільки годин", "годин", "відпрацю", "відпрацював", "відпрацювала"
        };

        private static readonly string[] DocumentKeywords =
        {
            "віз", "паспорт", "страх", "документ", "дозв", "закінч", "термін"
        };

        private static readonly string[] DocumentFileRequestKeywords =
        {
            "надіш", "скинь", "скин", "відправ", "покажи", "покажи", "дай", "дайте", "прикріп", "прикріпи", "attach", "show", "send"
        };

        private static readonly string[] EmployeeListKeywords =
        {
            "праців", "людей", "люди", "список", "покажи", "хто"
        };

        private static readonly string[] EmploymentKeywords =
        {
            "працю", "робот", "звіль", "закінч", "почав", "почала", "початок", "кінець", "статус", "фірм"
        };

        private static readonly string[] AllFirmsMarkers =
        {
            "всіх фірм", "усіх фірм", "всі фірми", "усі фірми", "по фірмах", "по всіх", "по усіх"
        };

        private static readonly HashSet<string> FirmQueryNoiseWords = new(StringComparer.Ordinal)
        {
            "яка", "який", "яке", "які", "скільки", "зарплата", "зарплта", "зарплат", "зарпл",
            "по", "на", "у", "в", "за", "місяць", "місяця", "цей", "поточний", "фірма", "фірмі",
            "фірму", "фірмах", "фірм", "всіх", "усіх", "всі", "усі", "цілу", "всю", "компанія", "компанії",
            "дай", "дайте", "покажи", "покажи", "скажи", "мені", "будь", "ласка", "будьласка", "треба", "потрібно"
        };

        private static readonly HashSet<string> FirmSuffixNoiseWords = new(StringComparer.Ordinal)
        {
            "a", "s", "as", "a s", "s r o", "sro", "spol", "spolecnost", "společnost", "akc", "akciova", "akciová",
            "bohemia", "group", "holding", "company", "firma"
        };

        private static readonly string[] PronounMarkers =
        {
            "в нього", "в неї", "його", "її", "нього", "неї", "цього працівника", "цьому працівнику", "цей працівник", "ця працівниця"
        };

        private static readonly string[] FollowUpMarkers =
        {
            "а ", "а в", "а за", "а скільки", "в нього", "в неї", "його", "її", "нього", "неї",
            "цього", "цьому", "цю фірм", "цілу фірм", "всю фірм", "ще", "тоді", "за квіт",
            "за берез", "за минулий", "цей місяц"
        };

        private static readonly string[] EmployeeQueryNoiseWords =
        {
            "скільки", "заробив", "заробила", "заробили", "покажи", "показати", "працівника", "працівник",
            "працівниці", "працівниця", "людей", "люди", "хто", "коли", "яка", "який", "яке", "які",
            "де", "в", "у", "на", "по", "за", "із", "з", "до", "від", "чи", "і", "й", "та", "що",
            "мені", "мене", "його", "її", "нього", "неї", "цього", "цьому", "такий", "така", "такого",
            "телефон", "номер", "email", "пошта", "адреса", "адресу", "банк", "банківські", "реквізити",
            "фірма", "фірмі", "фірму", "документи", "документ", "віза", "віза", "паспорт", "страховка",
            "страхування", "дозвіл", "роботу", "працює", "працював", "працювала", "оплачено", "виплачено",
            "аванс", "нетто", "брутто", "місяць", "місяця", "рік", "року", "потрібно", "треба", "ще",
            "будь", "ласка", "про", "зараз", "минулий", "попередній", "цей", "цього", "цьому", "цьої", "а"
        };

        private static readonly string[] DocumentEmployeeQueryNoiseWords =
        {
            "віз", "віза", "візу", "візи", "паспорт", "паспорта", "паспорту",
            "страх", "страховка", "страхування", "поліс", "полис", "дозвіл", "дозвол",
            "фото", "photo", "скан", "scan", "файл", "файлом", "копія", "копию",
            "надіш", "скин", "відправ", "прикріп", "attach", "show", "send"
        };

        private static readonly Dictionary<int, string[]> MonthAliases = new()
        {
            [1] = new[] { "січень", "січня", "січ" },
            [2] = new[] { "лютий", "лютого", "лют" },
            [3] = new[] { "березень", "березня", "берез" },
            [4] = new[] { "квітень", "квітня", "квіт" },
            [5] = new[] { "травень", "травня", "трав" },
            [6] = new[] { "червень", "червня", "черв" },
            [7] = new[] { "липень", "липня", "лип" },
            [8] = new[] { "серпень", "серпня", "серп" },
            [9] = new[] { "вересень", "вересня", "верес" },
            [10] = new[] { "жовтень", "жовтня", "жовт" },
            [11] = new[] { "листопад", "листопада", "листоп" },
            [12] = new[] { "грудень", "грудня", "груд" }
        };

        public TelegramBotService(
            AppSettingsService appSettingsService,
            TelegramPairingService pairingService,
            CompanyService companyService,
            EmployeeService employeeService,
            FinanceService financeService,
            GeminiApiService geminiApiService,
            ActivityLogService activityLogService,
            NewsService newsService,
            RecentlyDeletedService recentlyDeletedService)
        {
            _appSettingsService = appSettingsService;
            _pairingService = pairingService;
            _companyService = companyService;
            _employeeService = employeeService;
            _financeService = financeService;
            _geminiApiService = geminiApiService;
            _activityLogService = activityLogService;
            _newsService = newsService;
            _recentlyDeletedService = recentlyDeletedService;
        }

        public async Task<(bool ok, string message, string botUsername)> TestTokenAsync(string token, CancellationToken cancellationToken = default)
        {
            try
            {
                var client = new TelegramBotClient(token.Trim());
                var me = await client.GetMe(cancellationToken).ConfigureAwait(false);
                return (true, $"Підключено до @{me.Username}", me.Username ?? string.Empty);
            }
            catch (Exception ex)
            {
                return (false, ex.Message, string.Empty);
            }
        }

        public async Task<(bool ok, string message)> ConnectAsync(string token, CancellationToken cancellationToken = default)
        {
            var test = await TestTokenAsync(token, cancellationToken).ConfigureAwait(false);
            if (!test.ok)
                return (false, test.message);

            var settings = _appSettingsService.Settings.Telegram;
            settings.EncryptedBotToken = TelegramTokenProtection.Protect(token.Trim());
            settings.BotUsername = test.botUsername;
            settings.Enabled = true;
            settings.AuthorizedUsers ??= new List<TelegramAuthorizedUser>();
            _appSettingsService.SaveSettings();

            await RestartAsync(cancellationToken).ConfigureAwait(false);
            return (true, test.message);
        }

        public async Task RestartAsync(CancellationToken cancellationToken = default)
        {
            await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                StopInternal();

                var token = TelegramTokenProtection.Unprotect(_appSettingsService.Settings.Telegram.EncryptedBotToken);
                if (string.IsNullOrWhiteSpace(token) || !_appSettingsService.Settings.Telegram.Enabled)
                {
                    SetStatus("Telegram-бот вимкнений.");
                    return;
                }

                _client = new TelegramBotClient(token);
                var me = await _client.GetMe(cancellationToken).ConfigureAwait(false);
                _appSettingsService.Settings.Telegram.BotUsername = me.Username ?? string.Empty;
                _appSettingsService.SaveSettings();

                _cts = new CancellationTokenSource();
                _client.StartReceiving(
                    updateHandler: HandleUpdateAsync,
                    errorHandler: HandlePollingErrorAsync,
                    receiverOptions: new ReceiverOptions
                    {
                        AllowedUpdates = Array.Empty<UpdateType>()
                    },
                    cancellationToken: _cts.Token);

                _dailyDigestTask = Task.Run(() => RunDailyDigestLoopAsync(_cts.Token), _cts.Token);

                IsRunning = true;
                SetStatus($"Telegram-бот активний як @{me.Username}");
                LoggingService.LogInfo("TelegramBot.Start", $"Started as @{me.Username}");
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        public async Task<string> AskProgramAssistantAsync(
            string question,
            List<(string role, string text)>? history,
            long conversationUserId,
            CancellationToken cancellationToken = default)
        {
            if (!_appSettingsService.Settings.Telegram.AllowAiQuestions)
                return "AI-запитання вимкнені в налаштуваннях.";

            if (!_geminiApiService.IsConfigured)
                return "Gemini API не налаштований у програмі.";

            if (string.IsNullOrWhiteSpace(question))
                return "Напишіть питання для AI-помічника.";

            EmployeeSummary? resolvedEmployee = null;
            string resolvedFirm = string.Empty;
            string resolvedMonthKey = string.Empty;
            string lastAiTool = string.Empty;
            var pendingFiles = new List<PendingFile>();
            string clarificationMessage = string.Empty;

            async Task<GeminiToolChatResult> RunAiAttemptAsync(List<(string role, string text)>? attemptHistory, string prompt)
            {
                resolvedEmployee = null;
                resolvedFirm = string.Empty;
                resolvedMonthKey = string.Empty;
                lastAiTool = string.Empty;
                pendingFiles.Clear();
                clarificationMessage = string.Empty;

                return await _geminiApiService.ChatWithToolsAsync(
                    attemptHistory,
                    prompt,
                    GetAiTools(),
                    async (toolCall, ct) =>
                    {
                        lastAiTool = toolCall.Name;
                        var execution = await ExecuteAiToolAsync(toolCall, conversationUserId, ct).ConfigureAwait(false);
                        if (execution.Employee != null)
                            resolvedEmployee = execution.Employee;
                        if (!string.IsNullOrWhiteSpace(execution.FirmName))
                            resolvedFirm = execution.FirmName;
                        if (!string.IsNullOrWhiteSpace(execution.MonthKey))
                            resolvedMonthKey = execution.MonthKey;
                        if (execution.FilesToSend.Count > 0)
                            pendingFiles.AddRange(execution.FilesToSend);
                        if (execution.CandidateEmployees.Count > 0)
                            clarificationMessage = BuildEmployeeSelectionText(ExtractClarificationMessage(execution.Payload), execution.CandidateEmployees);
                        return execution.Payload;
                    },
                    systemPrompt: BuildAiSystemPrompt(),
                    modelOverride: TelegramAiModel,
                    ct: cancellationToken).ConfigureAwait(false);
            }

            var prompt = BuildAiQuestionPrompt(question, conversationUserId);
            var result = await RunAiAttemptAsync(history, prompt).ConfigureAwait(false);
            if (ShouldRetryAiAnswer(result.Text))
            {
                result = await RunAiAttemptAsync(
                    null,
                    prompt + "\nRetry mode: answer briefly using only exact tool facts. If data is still ambiguous, ask one short clarification question.")
                    .ConfigureAwait(false);
            }

            var answer = string.IsNullOrWhiteSpace(result.Text)
                ? "Не вдалося сформувати відповідь. Спробуйте уточнити запит."
                : result.Text;

            if (!string.IsNullOrWhiteSpace(clarificationMessage))
                answer = clarificationMessage;
            else if (pendingFiles.Count > 0)
                answer += "\n\nЗнайдено пов'язані файли працівника, але AI чат у програмі поки що не відправляє вкладення автоматично.";

            TouchConversationContext(
                conversationUserId,
                resolvedEmployee,
                resolvedFirm,
                resolvedMonthKey,
                topic: InferTopicFromTool(lastAiTool, question),
                action: lastAiTool,
                aiTool: lastAiTool);

            AppendConversationTurn(conversationUserId, "user", question);
            AppendConversationTurn(conversationUserId, "model", BuildConversationModelHistoryEntry(answer, lastAiTool, resolvedEmployee, resolvedFirm, resolvedMonthKey));

            return answer;
        }

        public void Stop()
        {
            try
            {
                StopInternal();
                SetStatus("Telegram-бот зупинено.");
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("TelegramBot.Stop", ex.Message);
            }
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                StopInternal();
                var settings = _appSettingsService.Settings.Telegram;
                settings.Enabled = false;
                settings.EncryptedBotToken = string.Empty;
                settings.BotUsername = string.Empty;
                settings.AuthorizedUsers.Clear();
                _appSettingsService.SaveSettings();
                _callbackPayloads.Clear();
                SetStatus("Telegram-бот відключено.");
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        public void RemoveAuthorizedUser(long telegramUserId)
        {
            var settings = _appSettingsService.Settings.Telegram;
            var target = settings.AuthorizedUsers.FirstOrDefault(u => u.TelegramUserId == telegramUserId);
            if (target == null)
                return;

            settings.AuthorizedUsers.Remove(target);
            _appSettingsService.SaveSettings();
            RaiseStateChanged();
        }

        private void StopInternal()
        {
            var cts = _cts;
            var dailyDigestTask = _dailyDigestTask;

            try { cts?.Cancel(); } catch { }
            if (dailyDigestTask != null)
            {
                try { dailyDigestTask.Wait(TimeSpan.FromSeconds(2)); }
                catch (AggregateException ex) when (ex.InnerExceptions.All(inner => inner is OperationCanceledException)) { }
                catch (OperationCanceledException) { }
                catch (Exception ex) { LoggingService.LogWarning("TelegramBot.Stop", ex.Message); }
            }

            cts?.Dispose();
            _cts = null;
            _dailyDigestTask = null;
            _lastDailyDigestLocalDate = string.Empty;
            _client = null;
            IsRunning = false;
        }

        private Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, HandleErrorSource source, CancellationToken cancellationToken)
        {
            LoggingService.LogWarning("TelegramBot.Polling", exception.Message);
            SetStatus($"Telegram polling warning: {exception.Message}");
            return Task.CompletedTask;
        }

        private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            try
            {
                if (update.Message != null)
                {
                    await HandleMessageAsync(botClient, update.Message, cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (update.CallbackQuery != null)
                {
                    await HandleCallbackQueryAsync(botClient, update.CallbackQuery, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogError("TelegramBot.HandleUpdate", ex);
            }
        }

        private async Task HandleMessageAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            var userId = message.From?.Id ?? 0;
            var text = (message.Text ?? string.Empty).Trim();
            var hasVoiceInput = message.Voice != null || message.Audio != null;
            if (string.IsNullOrWhiteSpace(text) && !hasVoiceInput)
                return;

            if (!IsPrivateChat(message.Chat))
            {
                await SendMessageAsync(
                    botClient,
                    message.Chat.Id,
                    "Для безпечного доступу бот працює тільки в приватному чаті. Відкрийте бота напряму і повторіть запит там.",
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            var (command, args) = string.IsNullOrWhiteSpace(text)
                ? (string.Empty, string.Empty)
                : SplitCommand(text);
            if (!IsAuthorized(userId) && !string.Equals(command, "/start", StringComparison.OrdinalIgnoreCase))
            {
                await SendMessageAsync(botClient, message.Chat.Id,
                "Доступ заборонено. Відскануйте QR-код у налаштуваннях програми, щоб прив'язати цей Telegram-акаунт.",
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            UpdateLastSeen(userId);

            if (hasVoiceInput)
            {
                if (!HasAdminAccess(userId))
                {
                    await SendMessageAsync(botClient, message.Chat.Id, "Голосові запитання доступні тільки Telegram-користувачам з роллю Admin.", cancellationToken).ConfigureAwait(false);
                    return;
                }

                await HandleVoiceMessageAsync(botClient, message, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (!text.StartsWith("/", StringComparison.Ordinal))
            {
                var normalizedText = NormalizeForSearch(text);
                if (HasDocumentFileRequestIntent(normalizedText) && !HasAdminAccess(userId))
                {
                    await SendMessageAsync(botClient, message.Chat.Id, "Надсилання файлів документів доступне тільки Telegram-користувачам з роллю Admin.", cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (!HasAdminAccess(userId))
                {
                    await SendMessageAsync(botClient, message.Chat.Id, "Текстові AI-запити доступні тільки Telegram-користувачам з роллю Admin. Для інших ролей залишаються кнопки та базові команди.", cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (await TryHandleDirectDocumentRequestAsync(botClient, message.Chat.Id, text, cancellationToken, userId).ConfigureAwait(false))
                    return;

                await SendAiAsync(botClient, message.Chat.Id, text, cancellationToken, userId).ConfigureAwait(false);
                _activityLogService.Log("TelegramBotAi", "Telegram", "", "",
                    "Telegram natural language query", string.Empty, text);
                return;
            }

            switch (command.ToLowerInvariant())
            {
                case "/start":
                    await HandleStartAsync(botClient, message, args, cancellationToken).ConfigureAwait(false);
                    break;
                case "/help":
                    await SendHelpAsync(botClient, message.Chat.Id, cancellationToken, userId).ConfigureAwait(false);
                    break;
                case "/menu":
                    await SendMenuAsync(botClient, message.Chat.Id, "Головне меню:", cancellationToken, userId).ConfigureAwait(false);
                    break;
                case "/firms":
                    await SendFirmsAsync(botClient, message.Chat.Id, cancellationToken, userId).ConfigureAwait(false);
                    break;
                case "/employees":
                    await SendEmployeesAsync(botClient, message.Chat.Id, args, cancellationToken, userId).ConfigureAwait(false);
                    break;
                case "/employee":
                    await SendEmployeeDetailsAsync(botClient, message.Chat.Id, args, cancellationToken, userId).ConfigureAwait(false);
                    break;
                case "/expiring":
                    await SendExpiringAsync(botClient, message.Chat.Id, args, cancellationToken, userId).ConfigureAwait(false);
                    break;
                case "/salary":
                    if (!HasAdminAccess(userId))
                    {
                        await SendMessageAsync(botClient, message.Chat.Id, "Перегляд зарплати в Telegram доступний тільки користувачам з роллю Admin.", cancellationToken).ConfigureAwait(false);
                        break;
                    }

                    await SendSalaryAsync(botClient, message.Chat.Id, args, cancellationToken, userId).ConfigureAwait(false);
                    break;
                case "/ai":
                    if (!HasAdminAccess(userId))
                    {
                        await SendMessageAsync(botClient, message.Chat.Id, "AI-запити в Telegram доступні тільки користувачам з роллю Admin.", cancellationToken).ConfigureAwait(false);
                        break;
                    }

                    await SendAiAsync(botClient, message.Chat.Id, args, cancellationToken, userId).ConfigureAwait(false);
                    break;
                default:
                    if (!HasAdminAccess(userId))
                    {
                        await SendMessageAsync(botClient, message.Chat.Id, "Ця команда доступна тільки користувачам з роллю Admin.", cancellationToken).ConfigureAwait(false);
                        break;
                    }

                    await SendAiAsync(botClient, message.Chat.Id, text, cancellationToken, userId).ConfigureAwait(false);
                    break;
            }

            _activityLogService.Log("TelegramBot", "Telegram", "", "",
                $"Telegram command {command}", string.Empty, text);
        }

        private async Task HandleStartAsync(ITelegramBotClient botClient, Message message, string args, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(args) && args.StartsWith("PAIR_", StringComparison.OrdinalIgnoreCase))
            {
                var displayName = BuildDisplayName(message.From);
                var username = message.From?.Username ?? string.Empty;
                if (_pairingService.TryConsume(args, message.From?.Id ?? 0, displayName, username))
                {
                    SetStatus($"Telegram-акаунт прив'язано: {displayName}");
                    await SendMenuAsync(
                        botClient,
                        message.Chat.Id,
                        "Підключення успішне. Тепер можна користуватись кнопками, командами або просто писати питання людською мовою.",
                        cancellationToken,
                        message.From?.Id).ConfigureAwait(false);
                    RaiseStateChanged();
                    return;
                }

                await SendMessageAsync(botClient, message.Chat.Id,
                    "Код прив'язки недійсний або вже прострочений. Згенеруйте новий QR-код у налаштуваннях.",
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            await SendHelpAsync(botClient, message.Chat.Id, cancellationToken, message.From?.Id).ConfigureAwait(false);
        }

        private Task SendHelpAsync(ITelegramBotClient botClient, ChatId chatId, CancellationToken cancellationToken, long? ownerUserId = null)
        {
            const string help =
                "Команди Telegram-бота:\n" +
                "Бот працює тільки в приватному чаті. AI-запитання і надсилання файлів документів доступні лише користувачам з роллю Admin.\n\n" +
                "/help - показати довідку\n" +
                "/menu - відкрити головне меню\n" +
                "/firms - список фірм\n" +
                "/employees [firm] - працівники вибраної фірми або всіх фірм\n" +
                "/employee <ім'я або id> - деталі працівника\n" +
                "/expiring [days] - документи, що скоро закінчуються\n" +
                "/salary <ім'я> [yyyy-mm] - остання зарплата або зарплата за місяць\n" +
                "/ai <питання> - опціонально примусово відправити запит прямо в Gemini\n\n" +
                "Приклади без команд:\n" +
                "• яка зарплата фірми DCK за 03.2026\n" +
                "• а за наступний місяць\n" +
                "• коли працівник закінчив роботу\n" +
                "• що вміє модуль Фактури";

            return SendMessageAsync(botClient, chatId, help, cancellationToken, BuildMainMenuKeyboard(ownerUserId));
        }

        private Task SendFirmsAsync(ITelegramBotClient botClient, ChatId chatId, CancellationToken cancellationToken, long? ownerUserId = null)
        {
            var firmsData = _companyService.VisibleCompanies
                .OrderBy(c => c.Name)
                .ToList();

            var firms = firmsData
                .Select(c => $"• {c.Name} ({_employeeService.GetEmployeesForFirm(c.Name).Count})")
                .ToList();

            var message = firms.Count == 0
                ? "Не знайдено жодної видимої фірми."
                : "Фірми:\n" + string.Join("\n", firms);

            return SendMessageAsync(botClient, chatId, message, cancellationToken, BuildFirmsKeyboard(firmsData, ownerUserId));
        }

        private Task SendEmployeesAsync(ITelegramBotClient botClient, ChatId chatId, string args, CancellationToken cancellationToken, long? conversationUserId = null)
        {
            List<EmployeeSummary> employees;
            if (!string.IsNullOrWhiteSpace(args))
            {
                employees = _employeeService.GetEmployeesForFirm(args.Trim());
            }
            else
            {
                employees = GetAllEmployees();
            }

            var lines = employees
                .OrderBy(e => e.FirmName)
                .ThenBy(e => e.FullName)
                .Take(40)
                .Select(e => $"• {e.FullName} | {e.FirmName} | віза {FormatExpiry(e.VisaExpiry)}")
                .ToList();

            if (lines.Count == 0)
                return SendMessageAsync(botClient, chatId, "Працівників не знайдено.", cancellationToken);

            var header = string.IsNullOrWhiteSpace(args)
                ? "Працівники:"
                : $"Працівники фірми {args.Trim()}:";

            if (employees.Count > 40)
                lines.Add($"… і ще {employees.Count - 40}");

            if (conversationUserId.HasValue && !string.IsNullOrWhiteSpace(args))
                TouchConversationContext(conversationUserId.Value, firmName: args.Trim());

            return SendMessageAsync(
                botClient,
                chatId,
                header + "\n" + string.Join("\n", lines),
                cancellationToken,
                BuildEmployeesKeyboard(employees, args, conversationUserId));
        }

        private async Task SendEmployeeDetailsAsync(ITelegramBotClient botClient, ChatId chatId, string args, CancellationToken cancellationToken, long? conversationUserId = null)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                await SendMessageAsync(botClient, chatId,
                    "Використайте /employee <ім'я або id>.",
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            var candidates = FindEmployeeRecords(args, 5, conversationUserId, allowContextFallback: true, includeArchived: false, includeRecentlyDeleted: false);
            if (!TrySelectSingleEmployeeRecord(args, candidates, out var employeeRecord))
            {
                if (candidates.Count > 0)
                {
                    var selectionText = BuildEmployeeSelectionText("Знайшов кількох працівників. Натисніть потрібне ім'я:", candidates);
                    var keyboard = BuildEmployeeSelectionKeyboard(candidates, BuildEmployeePickMetadata("employee_details"), conversationUserId);
                    await SendMessageAsync(botClient, chatId, selectionText, cancellationToken, keyboard).ConfigureAwait(false);
                    return;
                }

                await SendMessageAsync(botClient, chatId, "Працівника не знайдено.", cancellationToken).ConfigureAwait(false);
                return;
            }

            var employee = employeeRecord?.ActiveEmployee;
            if (employee == null)
            {
                await SendMessageAsync(botClient, chatId, "Працівника не знайдено.", cancellationToken).ConfigureAwait(false);
                return;
            }

            var data = _employeeService.LoadEmployeeData(employee.EmployeeFolder);
            if (data == null)
            {
                await SendMessageAsync(botClient, chatId, "Дані працівника недоступні.", cancellationToken).ConfigureAwait(false);
                return;
            }

            var text = new StringBuilder()
                .AppendLine($"Працівник: {employee.FullName}")
                .AppendLine($"Фірма: {employee.FirmName}")
                .AppendLine($"Позиція: {employee.PositionTitle}")
                .AppendLine($"Телефон: {employee.Phone}")
                .AppendLine($"Email: {employee.Email}")
                .AppendLine($"Паспорт: {data.PassportNumber} | до {FormatExpiry(data.PassportExpiry)}")
                .AppendLine($"Віза: {data.VisaNumber} | до {FormatExpiry(data.VisaExpiry)}")
                .AppendLine($"Страхування: {data.InsuranceCompanyShort} | до {FormatExpiry(data.InsuranceExpiry)}")
                .AppendLine($"Дозвіл на роботу: {data.WorkPermitNumber} | до {FormatExpiry(data.WorkPermitExpiry)}")
                .AppendLine($"Початок роботи: {data.StartDate}")
                .AppendLine($"Кінець роботи: {data.EndDate}")
                .AppendLine($"Банк: {data.BankAccountNumber}")
                .AppendLine($"Адреса: {FormatAddress(data.AddressLocal)}")
                .ToString();

            await SendMessageAsync(
                botClient,
                chatId,
                text,
                cancellationToken,
                BuildEmployeeDetailsKeyboard(employee, conversationUserId)).ConfigureAwait(false);

            if (conversationUserId.HasValue)
                TouchConversationContext(conversationUserId.Value, employee, employee.FirmName);
        }

        private Task SendExpiringAsync(ITelegramBotClient botClient, ChatId chatId, string args, CancellationToken cancellationToken, long? ownerUserId = null)
        {
            var days = 30;
            if (!string.IsNullOrWhiteSpace(args) && int.TryParse(args.Trim(), out var parsedDays))
                days = Math.Max(1, Math.Min(365, parsedDays));

            var expiring = new List<(string employee, string firm, string type, string date, int daysLeft)>();
            foreach (var employee in GetAllEmployees())
            {
                AddIfExpiring(employee, "паспорт", employee.PassportExpiry, days, expiring);
                AddIfExpiring(employee, "віза", employee.VisaExpiry, days, expiring);
                AddIfExpiring(employee, "страхування", employee.InsuranceExpiry, days, expiring);
                AddIfExpiring(employee, "дозвіл на роботу", employee.WorkPermitExpiry, days, expiring);
            }

            if (expiring.Count == 0)
                return SendMessageAsync(botClient, chatId, $"У найближчі {days} днів документи не закінчуються.", cancellationToken);

            var lines = expiring
                .OrderBy(x => x.daysLeft)
                .ThenBy(x => x.employee)
                .Take(40)
                .Select(x => $"• {x.employee} | {x.firm} | {x.type}: {x.date} ({FormatDaysLeft(x.daysLeft)})")
                .ToList();

            if (expiring.Count > 40)
                lines.Add($"… і ще {expiring.Count - 40}");

            return SendMessageAsync(
                botClient,
                chatId,
                $"Закінчуються у найближчі {days} днів:\n" + string.Join("\n", lines),
                cancellationToken,
                BuildExpiringKeyboard(ownerUserId));
        }

        private async Task SendSalaryAsync(ITelegramBotClient botClient, ChatId chatId, string args, CancellationToken cancellationToken, long? conversationUserId = null)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                await SendMessageAsync(botClient, chatId, "Використайте /salary <ім'я> [yyyy-mm].", cancellationToken).ConfigureAwait(false);
                return;
            }

            var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string monthKey = string.Empty;
            if (parts.Length > 1 && TryParseMonthKey(parts[^1], out var parsedMonth))
            {
                monthKey = $"{parsedMonth.Year:D4}-{parsedMonth.Month:D2}";
                args = string.Join(' ', parts.Take(parts.Length - 1));
            }

            var candidates = FindEmployeeRecords(args, 5, conversationUserId, allowContextFallback: true, includeArchived: false, includeRecentlyDeleted: false);
            if (!TrySelectSingleEmployeeRecord(args, candidates, out var employeeRecord))
            {
                if (candidates.Count > 0)
                {
                    var selectionText = BuildEmployeeSelectionText("Знайшов кількох працівників для зарплати. Натисніть потрібне ім'я:", candidates);
                    var keyboard = BuildEmployeeSelectionKeyboard(candidates, BuildEmployeePickMetadata("salary", monthKey: monthKey), conversationUserId);
                    await SendMessageAsync(botClient, chatId, selectionText, cancellationToken, keyboard).ConfigureAwait(false);
                    return;
                }

                await SendMessageAsync(botClient, chatId, "Працівника не знайдено.", cancellationToken).ConfigureAwait(false);
                return;
            }

            var employee = employeeRecord?.ActiveEmployee;
            if (employee == null)
            {
                await SendMessageAsync(botClient, chatId, "Працівника не знайдено.", cancellationToken).ConfigureAwait(false);
                return;
            }

            var salaryInfo = ResolveSalaryInfo(employee, monthKey);
            if (salaryInfo == null)
            {
                await SendMessageAsync(botClient, chatId, "Для цього працівника не знайдено історію зарплат.", cancellationToken).ConfigureAwait(false);
                return;
            }

            var text = BuildSalarySummaryText(salaryInfo);

            await SendMessageAsync(
                botClient,
                chatId,
                text,
                cancellationToken,
                BuildSalaryKeyboard(employee, conversationUserId)).ConfigureAwait(false);

            if (conversationUserId.HasValue)
                TouchConversationContext(conversationUserId.Value, employee, employee.FirmName, salaryInfo.MonthKey);
        }

        private async Task SendAiAsync(ITelegramBotClient botClient, ChatId chatId, string args, CancellationToken cancellationToken, long? conversationUserId = null)
        {
            if (!_appSettingsService.Settings.Telegram.AllowAiQuestions)
            {
                await SendMessageAsync(botClient, chatId, "AI-запитання вимкнені в налаштуваннях.", cancellationToken).ConfigureAwait(false);
                return;
            }

            if (!_geminiApiService.IsConfigured)
            {
                await SendMessageAsync(botClient, chatId, "Gemini API не налаштований у програмі.", cancellationToken).ConfigureAwait(false);
                return;
            }

            if (string.IsNullOrWhiteSpace(args))
            {
                await SendMessageAsync(botClient, chatId, "Використайте /ai <питання>.", cancellationToken).ConfigureAwait(false);
                return;
            }

            EmployeeSummary? resolvedEmployee = null;
            string resolvedFirm = string.Empty;
            string resolvedMonthKey = string.Empty;
            string lastAiTool = string.Empty;
            var pendingFiles = new List<PendingFile>();
            string clarificationMessage = string.Empty;
            InlineKeyboardMarkup? clarificationKeyboard = null;

            async Task<GeminiToolChatResult> RunAiAttemptAsync(List<(string role, string text)>? history, string prompt)
            {
                resolvedEmployee = null;
                resolvedFirm = string.Empty;
                resolvedMonthKey = string.Empty;
                lastAiTool = string.Empty;
                pendingFiles.Clear();
                clarificationMessage = string.Empty;
                clarificationKeyboard = null;

                return await _geminiApiService.ChatWithToolsAsync(
                    history,
                    prompt,
                    GetAiTools(),
                    async (toolCall, ct) =>
                    {
                        lastAiTool = toolCall.Name;
                        var execution = await ExecuteAiToolAsync(toolCall, conversationUserId, ct).ConfigureAwait(false);
                        if (execution.Employee != null)
                            resolvedEmployee = execution.Employee;
                        if (!string.IsNullOrWhiteSpace(execution.FirmName))
                            resolvedFirm = execution.FirmName;
                        if (!string.IsNullOrWhiteSpace(execution.MonthKey))
                            resolvedMonthKey = execution.MonthKey;
                        if (execution.FilesToSend.Count > 0)
                            pendingFiles.AddRange(execution.FilesToSend);
                        if (execution.CandidateEmployees.Count > 0 && conversationUserId.HasValue)
                        {
                            clarificationMessage = BuildEmployeeSelectionText(ExtractClarificationMessage(execution.Payload), execution.CandidateEmployees);
                            clarificationKeyboard = BuildEmployeeSelectionKeyboard(
                                execution.CandidateEmployees,
                                BuildEmployeePickMetadata("ai", originalQuery: args, toolName: toolCall.Name),
                                conversationUserId);
                        }
                        return execution.Payload;
                    },
                    systemPrompt: BuildAiSystemPrompt(),
                    modelOverride: TelegramAiModel,
                    ct: cancellationToken).ConfigureAwait(false);
            }

            await botClient.SendChatAction(chatId, ChatAction.Typing, cancellationToken: cancellationToken).ConfigureAwait(false);
            var prompt = BuildAiQuestionPrompt(args, conversationUserId);
            var result = await RunAiAttemptAsync(
                conversationUserId.HasValue ? GetConversationHistory(conversationUserId.Value) : null,
                prompt).ConfigureAwait(false);
            if (ShouldRetryAiAnswer(result.Text))
            {
                result = await RunAiAttemptAsync(
                    null,
                    prompt + "\nRetry mode: answer briefly using only exact tool facts. If data is still ambiguous, ask one short clarification question.")
                    .ConfigureAwait(false);
            }
            var answer = string.IsNullOrWhiteSpace(result.Text)
                ? "Не вдалося сформувати відповідь. Спробуйте уточнити запит."
                : result.Text;

            if (clarificationKeyboard != null && !string.IsNullOrWhiteSpace(clarificationMessage))
            {
                await SendMessageAsync(botClient, chatId, clarificationMessage, cancellationToken, clarificationKeyboard).ConfigureAwait(false);
            }
            else
            {
                await SendMessageAsync(botClient, chatId, answer, cancellationToken).ConfigureAwait(false);
                if (pendingFiles.Count > 0)
                    await SendPendingFilesAsync(botClient, chatId, pendingFiles, cancellationToken).ConfigureAwait(false);
            }

            if (conversationUserId.HasValue)
            {
                TouchConversationContext(
                    conversationUserId.Value,
                    resolvedEmployee,
                    resolvedFirm,
                    resolvedMonthKey,
                    topic: InferTopicFromTool(lastAiTool, args),
                    action: lastAiTool,
                    aiTool: lastAiTool);
                AppendConversationTurn(conversationUserId.Value, "user", args);
                var finalHistoryAnswer = clarificationKeyboard != null && !string.IsNullOrWhiteSpace(clarificationMessage)
                    ? clarificationMessage
                    : answer;
                AppendConversationTurn(conversationUserId.Value, "model", BuildConversationModelHistoryEntry(finalHistoryAnswer, lastAiTool, resolvedEmployee, resolvedFirm, resolvedMonthKey));
            }
        }

        private async Task<bool> TryHandleDirectDocumentRequestAsync(
            ITelegramBotClient botClient,
            ChatId chatId,
            string text,
            CancellationToken cancellationToken,
            long userId)
        {
            var normalized = NormalizeForSearch(text);
            if (!HasDocumentFileRequestIntent(normalized) || !TryDetectRequestedDocumentType(normalized, out var documentType))
                return false;

            var contextEmployee = ResolveEmployeeRecordFromContext(userId);
            var employeeQuery = ExtractEmployeeQuery(text, contextEmployee?.FirmName, DocumentEmployeeQueryNoiseWords);
            if (string.IsNullOrWhiteSpace(employeeQuery) && contextEmployee != null)
                employeeQuery = contextEmployee.UniqueId;

            if (string.IsNullOrWhiteSpace(employeeQuery) && contextEmployee == null)
            {
                var prompt = $"Напишіть ім'я працівника, кому треба надіслати {GetEmployeeDocumentLabel(documentType)}.";
                await SendMessageAsync(botClient, chatId, prompt, cancellationToken).ConfigureAwait(false);
                AppendConversationTurn(userId, "user", text);
                AppendConversationTurn(userId, "model", BuildConversationModelHistoryEntry(prompt, "send_employee_document_clarify", null, null, null));
                return true;
            }

            var execution = BuildSendEmployeeDocumentResult(employeeQuery, documentType, userId);
            using var payloadDocument = JsonDocument.Parse(JsonSerializer.Serialize(execution.Payload));
            var root = payloadDocument.RootElement;
            var ok = root.TryGetProperty("ok", out var okElement) && okElement.ValueKind == JsonValueKind.True;

            string responseText;
            if (ok && execution.FilesToSend.Count > 0)
            {
                responseText = root.TryGetProperty("formatted_result", out var formattedResultElement) && formattedResultElement.ValueKind == JsonValueKind.String
                    ? formattedResultElement.GetString() ?? $"Надсилаю документ {GetEmployeeDocumentLabel(documentType)}."
                    : $"Надсилаю документ {GetEmployeeDocumentLabel(documentType)}.";

                await SendMessageAsync(botClient, chatId, responseText, cancellationToken).ConfigureAwait(false);
                await SendPendingFilesAsync(botClient, chatId, execution.FilesToSend, cancellationToken).ConfigureAwait(false);

                TouchConversationContext(userId, execution.Employee, execution.FirmName, topic: "documents", action: "send_document", aiTool: "send_employee_document");
                AppendConversationTurn(userId, "user", text);
                AppendConversationTurn(userId, "model", BuildConversationModelHistoryEntry(responseText, "send_employee_document", execution.Employee, execution.FirmName, string.Empty));
                return true;
            }

            var message = ExtractClarificationMessage(execution.Payload);
            if (string.IsNullOrWhiteSpace(message))
                message = $"Не вдалося підготувати документ {GetEmployeeDocumentLabel(documentType)}.";

            if (execution.CandidateEmployees.Count > 0)
            {
                var textWithChoices = BuildEmployeeSelectionText(message, execution.CandidateEmployees);
                var keyboard = BuildEmployeeSelectionKeyboard(
                    execution.CandidateEmployees,
                    BuildEmployeePickMetadata("document", documentType: documentType),
                    userId);
                await SendMessageAsync(botClient, chatId, textWithChoices, cancellationToken, keyboard).ConfigureAwait(false);
                AppendConversationTurn(userId, "user", text);
                AppendConversationTurn(userId, "model", BuildConversationModelHistoryEntry(textWithChoices, "send_employee_document_clarify", null, null, null));
                return true;
            }

            await SendMessageAsync(botClient, chatId, message, cancellationToken).ConfigureAwait(false);
            AppendConversationTurn(userId, "user", text);
            AppendConversationTurn(userId, "model", BuildConversationModelHistoryEntry(message, "send_employee_document_failed", null, null, null));
            return true;
        }

        private async Task SendSelectedEmployeeDocumentAsync(
            ITelegramBotClient botClient,
            ChatId chatId,
            string employeeId,
            string documentType,
            CancellationToken cancellationToken,
            long userId)
        {
            var execution = BuildSendEmployeeDocumentResult(employeeId, documentType, userId);
            using var payloadDocument = JsonDocument.Parse(JsonSerializer.Serialize(execution.Payload));
            var root = payloadDocument.RootElement;
            var ok = root.TryGetProperty("ok", out var okElement) && okElement.ValueKind == JsonValueKind.True;

            if (ok && execution.FilesToSend.Count > 0)
            {
                var responseText = root.TryGetProperty("formatted_result", out var formattedResultElement) && formattedResultElement.ValueKind == JsonValueKind.String
                    ? formattedResultElement.GetString() ?? $"Надсилаю документ {GetEmployeeDocumentLabel(documentType)}."
                    : $"Надсилаю документ {GetEmployeeDocumentLabel(documentType)}.";
                await SendMessageAsync(botClient, chatId, responseText, cancellationToken).ConfigureAwait(false);
                await SendPendingFilesAsync(botClient, chatId, execution.FilesToSend, cancellationToken).ConfigureAwait(false);
                return;
            }

            var message = ExtractClarificationMessage(execution.Payload);
            if (string.IsNullOrWhiteSpace(message))
                message = $"Не вдалося знайти документ {GetEmployeeDocumentLabel(documentType)}.";

            if (execution.CandidateEmployees.Count > 0)
            {
                var text = BuildEmployeeSelectionText(message, execution.CandidateEmployees);
                var keyboard = BuildEmployeeSelectionKeyboard(
                    execution.CandidateEmployees,
                    BuildEmployeePickMetadata("document", documentType: documentType),
                    userId);
                await SendMessageAsync(botClient, chatId, text, cancellationToken, keyboard).ConfigureAwait(false);
                return;
            }

            await SendMessageAsync(botClient, chatId, message, cancellationToken).ConfigureAwait(false);
        }

        private static string ExtractClarificationMessage(object payload)
        {
            using var payloadDocument = JsonDocument.Parse(JsonSerializer.Serialize(payload));
            var root = payloadDocument.RootElement;
            return root.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String
                ? messageElement.GetString() ?? string.Empty
                : string.Empty;
        }

        private async Task HandleVoiceMessageAsync(ITelegramBotClient botClient, Message message, CancellationToken cancellationToken)
        {
            if (!_appSettingsService.Settings.Telegram.AllowAiQuestions)
            {
                await SendMessageAsync(botClient, message.Chat.Id, "Голосові запитання працюють тільки коли AI увімкнений у налаштуваннях.", cancellationToken).ConfigureAwait(false);
                return;
            }

            if (!_geminiApiService.IsConfigured)
            {
                await SendMessageAsync(botClient, message.Chat.Id, "Gemini API не налаштований у програмі, тому голосові ще не працюють.", cancellationToken).ConfigureAwait(false);
                return;
            }

            var fileId = message.Voice?.FileId ?? message.Audio?.FileId;
            if (string.IsNullOrWhiteSpace(fileId))
            {
                await SendMessageAsync(botClient, message.Chat.Id, "Не вдалося прочитати це голосове повідомлення.", cancellationToken).ConfigureAwait(false);
                return;
            }

            await botClient.SendChatAction(message.Chat.Id, ChatAction.Typing, cancellationToken: cancellationToken).ConfigureAwait(false);
            var file = await botClient.GetFile(fileId, cancellationToken).ConfigureAwait(false);
            if (file == null || string.IsNullOrWhiteSpace(file.FilePath))
            {
                await SendMessageAsync(botClient, message.Chat.Id, "Telegram не повернув файл голосового повідомлення.", cancellationToken).ConfigureAwait(false);
                return;
            }

            await using var stream = new MemoryStream();
            await botClient.DownloadFile(file.FilePath, stream, cancellationToken).ConfigureAwait(false);
            var mimeType = message.Voice?.MimeType
                ?? message.Audio?.MimeType
                ?? "audio/ogg";

            var transcript = (await _geminiApiService.TranscribeAudioAsync(stream.ToArray(), mimeType, TelegramAiModel, cancellationToken).ConfigureAwait(false)).Trim();
            if (GeminiApiService.IsFailureResponse(transcript) || string.IsNullOrWhiteSpace(transcript))
            {
                await SendMessageAsync(botClient, message.Chat.Id, "Не вдалося розпізнати голосове повідомлення. Спробуйте коротше або надішліть текстом.", cancellationToken).ConfigureAwait(false);
                return;
            }

            await SendMessageAsync(botClient, message.Chat.Id, $"🎤 {transcript}", cancellationToken).ConfigureAwait(false);
            await SendAiAsync(botClient, message.Chat.Id, transcript, cancellationToken, message.From?.Id ?? 0).ConfigureAwait(false);

            _activityLogService.Log("TelegramBotVoice", "Telegram", "", "",
                "Telegram voice query", string.Empty, transcript);
        }

        private static string BuildAiSystemPrompt()
        {
            return new StringBuilder()
                .AppendLine("## Role")
                .AppendLine("You are the Agency Contractor Telegram HR assistant for a desktop app. Reply only in Ukrainian.")
                .AppendLine()
                .AppendLine("## Hard Rules")
                .AppendLine("1. Never invent facts. If data is missing, say that directly.")
                .AppendLine("2. Use read-only tools whenever local program data is needed. Do not answer from memory when a tool can verify the fact.")
                .AppendLine("3. Never claim that you changed data, updated records or performed actions in the program.")
                .AppendLine("4. If several employees or firms match, ask one short clarifying question and do not guess.")
                .AppendLine("5. Keep the default answer short and factual. Usually 3-5 sentences or a short bullet list.")
                .AppendLine("6. Do not repeat the user question. Do not use filler openings like 'Звичайно', 'Добре', 'Чудове питання'.")
                .AppendLine("7. Ніколи не використовуй Markdown форматування. Не використовуй ** для жирного тексту, * для курсиву чи # для заголовків. Відповідай лише звичайним чистим текстом (plain text).")
                .AppendLine()
                .AppendLine("## Conversation Context")
                .AppendLine("You receive conversation history and structured follow-up context. Use it to resolve pronouns, implicit references, the same employee, the same firm, previous month and next month.")
                .AppendLine("If a tool result contains formatted_* text that directly matches the question, use that formatted text as the answer base and only shorten it if needed.")
                .AppendLine()
                .AppendLine("## Tool Priorities")
                .AppendLine("- Use product-help tools for questions about how the program works or how to use a module.")
                .AppendLine("- Use archive and analytics tools for archived employees, who finished work, termination dates, old firms, recently deleted employees and timelines.")
                .AppendLine("- Use the full employee summary tool when the user asks to tell everything about one employee.")
                .AppendLine("- Use salary tools for salary, payout, comparison, ranking, advances, deductions and firm salary questions.")
                .AppendLine("- Use file sending tools when the user asks to send, show, attach or export a document or Excel file.")
                .AppendLine("- Use external updates only for official public news or legislation, and keep them separate from local database facts.")
                .AppendLine()
                .AppendLine("## Output Format")
                .AppendLine("- Prefer bullet points for lists.")
                .AppendLine("- Dates should be shown as dd.MM.yyyy when possible.")
                .AppendLine("- Numbers must keep units like CZK, год, днів.")
                .AppendLine("- For salary by firm, describe each employee in this strict order: hours worked, hourly rate, salary advance if any, table columns in saved order, final payout, note.")
                .AppendLine("- Treat final payout as the main salary sum, not gross salary.")
                .AppendLine("- Keep answers usually under 1500 characters unless the user explicitly asks for full detail.")
                .AppendLine()
                .AppendLine("## Negative Example")
                .AppendLine("Wrong: 'Добре, зараз подивлюся і ось що мені вдалося знайти...'")
                .AppendLine("Right: reply directly with exact facts, names, dates and amounts from tool results.")
                .ToString()
                .TrimEnd();
        }

        private string BuildAiQuestionPrompt(string question, long? conversationUserId)
        {
            var today = DateTime.Now;
            var builder = new StringBuilder()
                .AppendLine($"User question: {question}")
                .AppendLine($"Today: {today:dd.MM.yyyy}")
                .AppendLine($"Current month: {today:yyyy-MM}");

            if (conversationUserId.HasValue)
            {
                var context = GetConversationContext(conversationUserId.Value);
                if (context != null)
                {
                    builder.AppendLine("Conversation context:")
                        .AppendLine($"- last_employee={context.LastEmployeeName}")
                        .AppendLine($"- last_firm={context.LastFirmName}")
                        .AppendLine($"- last_month={context.LastMonthKey}")
                        .AppendLine($"- last_secondary_month={context.LastSecondaryMonthKey}")
                        .AppendLine($"- last_topic={context.LastTopic}")
                        .AppendLine($"- last_action={context.LastAction}")
                        .AppendLine($"- last_ai_tool={context.LastAiTool}");

                    if (!string.IsNullOrWhiteSpace(context.HistorySummary))
                    {
                        builder.AppendLine("Earlier conversation summary:")
                            .AppendLine(context.HistorySummary);
                    }
                }
            }

            builder.AppendLine("Answer directly if enough data is available. If not, ask a short clarifying question.");
            builder.AppendLine("IMPORTANT: your answer must contain only facts from tool results or explicit prompt context.");
            builder.AppendLine("IMPORTANT: if a tool returned exact names, dates, document numbers or amounts, quote those exact values.");
            builder.AppendLine("IMPORTANT: if a tool returned formatted_* text that already answers the question, reuse it as the answer base.");
            builder.AppendLine("IMPORTANT: do not add general explanations unless the user explicitly asks 'чому', 'поясни' or 'як'.");
            builder.AppendLine("IMPORTANT: keep the answer under 1500 characters unless the user explicitly asks for full detail.");
            builder.AppendLine("IMPORTANT: if multiple employees or firms match, ask one short clarification and do not guess.");
            return builder.ToString();
        }

        private static IReadOnlyList<GeminiFunctionTool> GetAiTools()
        {
            return new List<GeminiFunctionTool>
            {
                new()
                {
                    Name = "list_firms",
                    Description = "List visible firms with basic metadata and employee counts.",
                    Parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            limit = new { type = "integer", description = "Maximum number of firms to return. Default 20." }
                        }
                    }
                },
                new()
                {
                    Name = "list_employees",
                    Description = "List active employees, optionally filtered by firm name or search query.",
                    Parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            firm = new { type = "string", description = "Firm name or partial firm name." },
                            query = new { type = "string", description = "Employee name, ID, passport number, phone, bank account or other identifying text." },
                            limit = new { type = "integer", description = "Maximum number of employees to return. Default 15." }
                        }
                    }
                },
                new()
                {
                    Name = "get_employee",
                    Description = "Get one employee card with exact profile facts: documents, contacts, addresses, bank details and firm history. Use when the user asks who the employee is or needs exact card data.",
                    Parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            employee_query = new { type = "string", description = "Employee full name, surname, ID or follow-up reference." }
                        }
                    }
                },
                new()
                {
                    Name = "resolve_employee",
                    Description = "Resolve an employee query into best employee matches before using other employee tools.",
                    Parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            employee_query = new { type = "string", description = "Employee full name, surname, ID, phone or other identifying text." },
                            limit = new { type = "integer", description = "Maximum number of candidate matches. Default 5." }
                        },
                        required = new[] { "employee_query" }
                    }
                },
                new()
                {
                    Name = "resolve_firm",
                    Description = "Resolve a firm query into best matching visible firms.",
                    Parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            firm = new { type = "string", description = "Firm name or partial firm name." },
                            limit = new { type = "integer", description = "Maximum number of candidate matches. Default 5." }
                        },
                        required = new[] { "firm" }
                    }
                },
                new()
                {
                    Name = "get_company_profile",
                    Description = "Get company profile, requisites, addresses, agency info, positions and active employee count for a firm.",
                    Parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            firm = new { type = "string", description = "Firm name or partial firm name." }
                        },
                        required = new[] { "firm" }
                    }
                },
                new()
                {
                    Name = "get_employee_employment",
                    Description = "Get employment facts for one employee: status, start date, end date, archive state and firm history. Use for questions about when someone started, finished or where they worked.",
                    Parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            employee_query = new { type = "string", description = "Employee full name, surname, ID or follow-up reference." }
                        },
                        required = new[] { "employee_query" }
                    }
                },
                new()
                {
                    Name = "get_employee_full_summary",
                    Description = "Get a full employee summary in one tool call: profile card, documents, employment status, latest salary, recent history and timeline. Prefer this when the user asks to tell everything about one employee.",
                    Parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            employee_query = new { type = "string", description = "Employee full name, surname, ID or follow-up reference." }
                        },
                        required = new[] { "employee_query" }
                    }
                },
                new()
                {
                    Name = "send_employee_document",
                    Description = "Send an employee document file to Telegram chat: passport, visa, insurance, work permit, photo or second document pages. Use when the user asks to send, show or attach a document scan.",
                    Parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            employee_query = new { type = "string", description = "Employee full name, surname, ID or follow-up reference." },
                            document_type = new
                            {
                                type = "string",
                                description = "Document type to send.",
                                @enum = new[] { "passport", "visa", "insurance", "work_permit", "photo", "passport_page2", "visa_page2" }
                            }
                        },
                        required = new[] { "employee_query", "document_type" }
                    }
                },
                new()
                {
                    Name = "get_salary",
                    Description = "Get one employee salary for a specific month or the latest month. Returns exact hours, rate, gross, advance, deductions, custom fields, extra advances and final payout. Use this for salary, payout and earnings questions instead of guessing.",
                    Parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            employee_query = new { type = "string", description = "Employee full name, surname, ID or follow-up reference." },
                            month = new { type = "string", description = "Month in yyyy-MM, MM.yyyy, MM-yyyy or month name format." }
                        },
                        required = new[] { "employee_query" }
                    }
                },
                new()
                {
                    Name = "get_firm_salary",
                    Description = "Get total salary information for one firm for a specific month, including detailed per-employee payout rows. Use for questions like salary by firm, payout by firm or all employees in one firm for a month.",
                    Parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            firm = new { type = "string", description = "Firm name or partial firm name." },
                            month = new { type = "string", description = "Month in yyyy-MM, MM.yyyy, MM-yyyy or month name format." }
                        }
                    }
                },
                new()
                {
                    Name = "get_all_firms_salary",
                    Description = "Get total salary information for all visible firms for one month. Use when the user asks about salary across all firms, not one specific firm.",
                    Parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            month = new { type = "string", description = "Month in yyyy-MM, MM.yyyy, MM-yyyy, month name format or phrase like 'за місяць'." }
                        }
                    }
                },
                new()
                {
                    Name = "get_firm_period_summary",
                    Description = "Get salary summary for one firm across a month range, with per-month totals and aggregate totals.",
                    Parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            firm = new { type = "string", description = "Firm name or partial firm name." },
                            start_month = new { type = "string", description = "Range start month in yyyy-MM, MM.yyyy, MM-yyyy or month name format." },
                            end_month = new { type = "string", description = "Range end month in yyyy-MM, MM.yyyy, MM-yyyy or month name format." }
                        },
                        required = new[] { "firm", "start_month", "end_month" }
                    }
                },
                new()
                {
                    Name = "compare_salary_months",
                    Description = "Compare two months either for one firm or for all firms, including totals and deltas. Use for comparison, difference and 'which month is bigger' questions.",
                    Parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            firm = new { type = "string", description = "Optional firm name. If omitted, compare all visible firms." },
                            month_a = new { type = "string", description = "First month to compare." },
                            month_b = new { type = "string", description = "Second month to compare." }
                        },
                        required = new[] { "month_a", "month_b" }
                    }
                },
                new()
                {
                    Name = "export_firm_salary_excel",
                    Description = "Generate and send an Excel file with the salary table for one firm and one month. Use when the user asks for salary export, Excel file or spreadsheet.",
                    Parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            firm = new { type = "string", description = "Firm name or partial firm name." },
                            month = new { type = "string", description = "Month in yyyy-MM, MM.yyyy, MM-yyyy or month name format." }
                        },
                        required = new[] { "firm", "month" }
                    }
                },
                new()
                {
                    Name = "get_advances",
                    Description = "Get employee advances for a month or all recent months.",
                    Parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            employee_query = new { type = "string", description = "Employee full name, surname, ID or follow-up reference." },
                            month = new { type = "string", description = "Optional month filter." }
                        },
                        required = new[] { "employee_query" }
                    }
                },
                new()
                {
                    Name = "get_expiring_documents",
                    Description = "List employees whose passport, visa, insurance or work permit expires within given days.",
                    Parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            days = new { type = "integer", description = "Number of days ahead, for example 7 or 30. Default 30." }
                        }
                    }
                },
                new()
                {
                    Name = "get_employee_history",
                    Description = "Get recent employee history events and profile changes.",
                    Parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            employee_query = new { type = "string", description = "Employee full name, surname, ID or follow-up reference." },
                            limit = new { type = "integer", description = "Maximum number of history records to return. Default 10." }
                        },
                        required = new[] { "employee_query" }
                    }
                },
                new()
                {
                    Name = "get_employee_status_overview",
                    Description = "Get one employee status overview across active, archived and recently deleted states. Use when the user asks whether the employee is active, archived, deleted or when they finished work.",
                    Parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            employee_query = new { type = "string", description = "Employee full name, surname, ID or follow-up reference." }
                        },
                        required = new[] { "employee_query" }
                    }
                },
                new()
                {
                    Name = "get_employee_timeline",
                    Description = "Build a timeline of employment, archive, salary and history events for one employee. Use for chronology questions, timelines and 'what happened before or after' requests.",
                    Parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            employee_query = new { type = "string", description = "Employee full name, surname, ID or follow-up reference." },
                            limit = new { type = "integer", description = "Maximum recent history and salary events to include. Default 12." }
                        },
                        required = new[] { "employee_query" }
                    }
                },
                new()
                {
                    Name = "list_archived_employees",
                    Description = "List archived employees, optionally filtered by firm or by end month range.",
                    Parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            query = new { type = "string", description = "Optional employee name or identifying text." },
                            firm = new { type = "string", description = "Optional firm name or partial firm name." },
                            start_month = new { type = "string", description = "Optional archived-from month range start." },
                            end_month = new { type = "string", description = "Optional archived-to month range end." },
                            limit = new { type = "integer", description = "Maximum results. Default 15." }
                        }
                    }
                },
                new()
                {
                    Name = "get_top_payouts_for_month",
                    Description = "Rank employee payouts for a month, optionally filtered by firm, sorted by net or gross salary.",
                    Parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            month = new { type = "string", description = "Month in yyyy-MM, MM.yyyy, MM-yyyy or month name format." },
                            firm = new { type = "string", description = "Optional firm name." },
                            sort_by = new { type = "string", description = "Sort metric: net or gross. Default net." },
                            limit = new { type = "integer", description = "Maximum ranked employees. Default 10." }
                        },
                        required = new[] { "month" }
                    }
                },
                new()
                {
                    Name = "get_hiring_summary",
                    Description = "Count new employees for a month, optionally by firm, and identify who started last in that month.",
                    Parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            month = new { type = "string", description = "Month in yyyy-MM, MM.yyyy, MM-yyyy or month name format." },
                            firm = new { type = "string", description = "Optional firm name." },
                            limit = new { type = "integer", description = "Maximum employees to include in the list. Default 10." }
                        },
                        required = new[] { "month" }
                    }
                },
                new()
                {
                    Name = "get_termination_summary",
                    Description = "Count employees who finished work in a month, optionally by firm, and identify who ended last in that month.",
                    Parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            month = new { type = "string", description = "Month in yyyy-MM, MM.yyyy, MM-yyyy or month name format." },
                            firm = new { type = "string", description = "Optional firm name." },
                            limit = new { type = "integer", description = "Maximum employees to include in the list. Default 10." }
                        },
                        required = new[] { "month" }
                    }
                },
                new()
                {
                    Name = "get_employee_flow_summary",
                    Description = "Summarize employee flow for a month: how many started, how many finished, net change, and latest start/end events.",
                    Parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            month = new { type = "string", description = "Month in yyyy-MM, MM.yyyy, MM-yyyy or month name format." },
                            firm = new { type = "string", description = "Optional firm name." }
                        },
                        required = new[] { "month" }
                    }
                },
                new()
                {
                    Name = "get_employee_flow_period",
                    Description = "Summarize employee flow across a month range: starts, terminations, net change, monthly breakdown and per-firm totals. Use this for quarter, year or custom period questions.",
                    Parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            start_month = new { type = "string", description = "Range start month in yyyy-MM, MM.yyyy, MM-yyyy or month name format." },
                            end_month = new { type = "string", description = "Range end month in yyyy-MM, MM.yyyy, MM-yyyy or month name format." },
                            firm = new { type = "string", description = "Optional firm name." }
                        },
                        required = new[] { "start_month", "end_month" }
                    }
                },
                new()
                {
                    Name = "get_program_help",
                    Description = "Get step-by-step help about what this desktop program can do, which module to open and how to perform a task.",
                    Parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            topic = new { type = "string", description = "Requested feature, module, task or question about using the program." },
                            limit = new { type = "integer", description = "Maximum number of matching help topics. Default 3." }
                        }
                    }
                },
                new()
                {
                    Name = "list_program_capabilities",
                    Description = "List the main capabilities of the program and quick start tips for users who do not know how to use it.",
                    Parameters = new
                    {
                        type = "object",
                        properties = new { }
                    }
                },
                new()
                {
                    Name = "get_external_updates",
                    Description = "Get recent official public updates related to employment, migration, documents or legislation from configured news sources.",
                    Parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            query = new { type = "string", description = "Optional keyword like visa, work permit, migration, law, insurance or employment." },
                            limit = new { type = "integer", description = "Maximum number of updates. Default 5." }
                        }
                    }
                },
                new()
                {
                    Name = "search_everything",
                    Description = "Global search across firms and employees, including active, archived and recently deleted people, plus document numbers, contacts, bank details and addresses. Use when the user asks to find something by partial name, number, phone, passport, bank account or other identifying text.",
                    Parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            query = new { type = "string", description = "Text to find, such as a partial name, phone, passport number, bank account, address or firm-related keyword." },
                            limit = new { type = "integer", description = "Maximum results to return. Default 12." }
                        },
                        required = new[] { "query" }
                    }
                }
            };
        }

        private async Task<AiToolExecutionResult> ExecuteAiToolAsync(GeminiFunctionCall call, long? conversationUserId, CancellationToken cancellationToken)
        {
            using var argsDoc = ParseToolArguments(call.ArgumentsJson);
            var args = argsDoc.RootElement;

            var result = call.Name switch
            {
                "list_firms" => ExecuteListFirmsTool(args),
                "list_employees" => ExecuteListEmployeesTool(args, conversationUserId),
                "get_employee" => ExecuteGetEmployeeTool(args, conversationUserId),
                "resolve_employee" => ExecuteResolveEmployeeTool(args, conversationUserId),
                "resolve_firm" => ExecuteResolveFirmTool(args),
                "get_company_profile" => ExecuteGetCompanyProfileTool(args, conversationUserId),
                "get_employee_employment" => ExecuteGetEmployeeEmploymentTool(args, conversationUserId),
                "get_employee_full_summary" => ExecuteGetEmployeeFullSummaryTool(args, conversationUserId),
                "send_employee_document" => ExecuteSendEmployeeDocumentTool(args, conversationUserId),
                "get_salary" => ExecuteGetSalaryTool(args, conversationUserId),
                "get_firm_salary" => ExecuteGetFirmSalaryTool(args, conversationUserId),
                "get_all_firms_salary" => ExecuteGetAllFirmsSalaryTool(args, conversationUserId),
                "get_firm_period_summary" => ExecuteGetFirmPeriodSummaryTool(args, conversationUserId),
                "compare_salary_months" => ExecuteCompareSalaryMonthsTool(args, conversationUserId),
                "export_firm_salary_excel" => ExecuteExportFirmSalaryExcelTool(args, conversationUserId),
                "get_advances" => ExecuteGetAdvancesTool(args, conversationUserId),
                "get_expiring_documents" => ExecuteGetExpiringDocumentsTool(args),
                "get_employee_history" => ExecuteGetEmployeeHistoryTool(args, conversationUserId),
                "get_employee_status_overview" => ExecuteGetEmployeeStatusOverviewTool(args, conversationUserId),
                "get_employee_timeline" => ExecuteGetEmployeeTimelineTool(args, conversationUserId),
                "list_archived_employees" => ExecuteListArchivedEmployeesTool(args, conversationUserId),
                "get_top_payouts_for_month" => ExecuteGetTopPayoutsForMonthTool(args, conversationUserId),
                "get_hiring_summary" => ExecuteGetHiringSummaryTool(args, conversationUserId),
                "get_termination_summary" => ExecuteGetTerminationSummaryTool(args, conversationUserId),
                "get_employee_flow_summary" => ExecuteGetEmployeeFlowSummaryTool(args, conversationUserId),
                "get_employee_flow_period" => ExecuteGetEmployeeFlowPeriodTool(args, conversationUserId),
                "get_program_help" => ExecuteGetProgramHelpTool(args),
                "list_program_capabilities" => ExecuteListProgramCapabilitiesTool(),
                "get_external_updates" => await ExecuteGetExternalUpdatesToolAsync(args, cancellationToken).ConfigureAwait(false),
                "search_everything" => ExecuteSearchEverythingTool(args, conversationUserId),
                _ => new AiToolExecutionResult
                {
                    Payload = new
                    {
                        ok = false,
                        message = $"Unknown tool: {call.Name}"
                    }
                }
            };

            _activityLogService.Log(
                "TelegramBotTool",
                "Telegram",
                result.FirmName,
                result.Employee?.FullName ?? string.Empty,
                $"Telegram AI tool {call.Name}",
                string.Empty,
                call.ArgumentsJson);

            await Task.CompletedTask;
            return result;
        }

        private AiToolExecutionResult ExecuteListFirmsTool(JsonElement args)
        {
            var limit = Math.Max(1, Math.Min(50, GetIntArg(args, "limit", 20)));
            var firms = _companyService.VisibleCompanies
                .OrderBy(company => company.Name)
                .Take(limit)
                .Select(company => new
                {
                    name = company.Name,
                    ico = company.ICO,
                    legal_address = company.LegalAddress,
                    employee_count = _employeeService.GetEmployeesForFirm(company.Name).Count,
                    weekly_work_hours = company.WeeklyWorkHours,
                    daily_work_hours = company.DailyWorkHours
                })
                .ToList();

            return new AiToolExecutionResult
            {
                Payload = new
                {
                    ok = true,
                    firms
                }
            };
        }

        private AiToolExecutionResult ExecuteListEmployeesTool(JsonElement args, long? conversationUserId)
        {
            var limit = Math.Max(1, Math.Min(50, GetIntArg(args, "limit", 15)));
            var query = GetStringArg(args, "query");
            var firm = ResolveFirmNameForAi(GetStringArg(args, "firm"), conversationUserId);

            List<EmployeeSummary> employees;
            if (!string.IsNullOrWhiteSpace(query))
            {
                employees = FindEmployees(query, limit, conversationUserId, allowContextFallback: false);
                if (!string.IsNullOrWhiteSpace(firm))
                    employees = employees.Where(e => string.Equals(e.FirmName, firm, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            else if (!string.IsNullOrWhiteSpace(firm))
            {
                employees = _employeeService.GetEmployeesForFirm(firm)
                    .OrderBy(e => e.FullName)
                    .Take(limit)
                    .ToList();
            }
            else
            {
                employees = GetAllEmployees()
                    .OrderBy(e => e.FirmName)
                    .ThenBy(e => e.FullName)
                    .Take(limit)
                    .ToList();
            }

            return new AiToolExecutionResult
            {
                FirmName = firm,
                Employee = employees.FirstOrDefault(),
                Payload = new
                {
                    ok = true,
                    firm,
                    employees = employees.Select(e => new
                    {
                        full_name = e.FullName,
                        firm = e.FirmName,
                        position = e.PositionTitle,
                        phone = e.Phone,
                        visa_expiry = e.VisaExpiry,
                        insurance_expiry = e.InsuranceExpiry,
                        status = e.Status
                    }).ToList()
                }
            };
        }

        private AiToolExecutionResult ExecuteGetEmployeeTool(JsonElement args, long? conversationUserId)
        {
            var query = GetStringArg(args, "employee_query");
            var candidates = FindEmployeeRecords(query, 5, conversationUserId, allowContextFallback: true);
            if (!TrySelectSingleEmployeeRecord(query, candidates, out var employeeRecord))
            {
                return new AiToolExecutionResult
                {
                    CandidateEmployees = candidates.ToList(),
                    Payload = new
                    {
                        ok = false,
                        needs_clarification = true,
                        message = candidates.Count == 0 ? "Employee not found." : "Multiple employees match this query.",
                        candidates = candidates.Select(e => new
                        {
                            full_name = e.FullName,
                            firm = e.FirmName,
                            source = e.Source,
                            status = e.StatusLabel,
                            end_date = e.EndDate
                        }).ToList()
                    }
                };
            }

            var selectedEmployee = employeeRecord!;
            var data = _employeeService.LoadEmployeeData(selectedEmployee.EmployeeFolder);
            if (data == null)
            {
                return new AiToolExecutionResult
                {
                    Employee = BuildConversationEmployeeShadow(selectedEmployee),
                    FirmName = selectedEmployee.FirmName,
                    Payload = new
                    {
                        ok = false,
                        message = "Employee data is unavailable."
                    }
                };
            }

            return new AiToolExecutionResult
            {
                Employee = BuildConversationEmployeeShadow(selectedEmployee),
                FirmName = selectedEmployee.FirmName,
                Payload = new
                {
                    ok = true,
                    employee = BuildEmployeeToolPayload(selectedEmployee, data)
                }
            };
        }

        private AiToolExecutionResult ExecuteResolveEmployeeTool(JsonElement args, long? conversationUserId)
        {
            var query = GetStringArg(args, "employee_query");
            var limit = Math.Max(1, Math.Min(10, GetIntArg(args, "limit", 5)));
            var candidates = FindEmployeeRecords(query, limit, conversationUserId, allowContextFallback: true);

            return new AiToolExecutionResult
            {
                Employee = BuildConversationEmployeeShadow(candidates.FirstOrDefault()),
                FirmName = candidates.FirstOrDefault()?.FirmName ?? string.Empty,
                Payload = new
                {
                    ok = candidates.Count > 0,
                    query,
                    candidates = candidates.Select(e => new
                    {
                        full_name = e.FullName,
                        firm = e.FirmName,
                        position = e.PositionTitle,
                        status = e.StatusLabel,
                        source = e.Source,
                        start_date = e.StartDate,
                        end_date = e.EndDate,
                        recently_deleted_at = e.DeletedAtUtc?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                    }).ToList(),
                    message = candidates.Count == 0 ? "Employee not found." : string.Empty
                }
            };
        }

        private AiToolExecutionResult ExecuteResolveFirmTool(JsonElement args)
        {
            var query = GetStringArg(args, "firm");
            var limit = Math.Max(1, Math.Min(10, GetIntArg(args, "limit", 5)));
            var matches = FindFirmMatches(query, limit);

            return new AiToolExecutionResult
            {
                FirmName = matches.FirstOrDefault()?.Company.Name ?? string.Empty,
                Payload = new
                {
                    ok = matches.Count > 0,
                    query,
                    firms = matches.Select(match => new
                    {
                        name = match.Company.Name,
                        ico = match.Company.ICO,
                        legal_address = match.Company.LegalAddress,
                        employee_count = _employeeService.GetEmployeesForFirm(match.Company.Name).Count,
                        match_score = match.Score
                    }).ToList(),
                    message = matches.Count == 0 ? "Firm not found." : string.Empty
                }
            };
        }

        private AiToolExecutionResult ExecuteGetCompanyProfileTool(JsonElement args, long? conversationUserId)
        {
            var firmQuery = GetStringArg(args, "firm");
            var firmName = ResolveFirmNameForAi(firmQuery, conversationUserId);
            if (string.IsNullOrWhiteSpace(firmName))
            {
                return new AiToolExecutionResult
                {
                    Payload = new
                    {
                        ok = false,
                        needs_clarification = true,
                        message = "Company profile requires a firm name.",
                        suggested_firms = FindFirmMatches(firmQuery, 3).Select(match => match.Company.Name).ToList()
                    }
                };
            }

            var company = FindVisibleCompanyByName(firmName);
            if (company == null)
            {
                return new AiToolExecutionResult
                {
                    FirmName = firmName,
                    Payload = new
                    {
                        ok = false,
                        message = "Firm profile not found."
                    }
                };
            }

            var employeeCount = _employeeService.GetEmployeesForFirm(company.Name).Count;
            var workAddresses = company.Addresses
                .Select(FormatWorkAddress)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();
            var positions = company.Positions
                .Select(position => new
                {
                    title = position.Title,
                    position_number = position.PositionNumber,
                    hourly_salary = position.HourlySalary,
                    monthly_salary_brutto = position.MonthlySalaryBrutto
                })
                .ToList();
            var hiddenFrom = company.HiddenFromYear > 0 && company.HiddenFromMonth > 0
                ? $"{company.HiddenFromYear:D4}-{company.HiddenFromMonth:D2}"
                : string.Empty;

            return new AiToolExecutionResult
            {
                FirmName = company.Name,
                Payload = new
                {
                    ok = true,
                    company = new
                    {
                        name = company.Name,
                        ico = company.ICO,
                        legal_address = company.LegalAddress,
                        active_employee_count = employeeCount,
                        weekly_work_hours = company.WeeklyWorkHours,
                        daily_work_hours = company.DailyWorkHours,
                        shift_count = company.ShiftCount,
                        hidden_from = hiddenFrom,
                        work_addresses = workAddresses,
                        agency = new
                        {
                            name = company.Agency?.Name ?? string.Empty,
                            ico = company.Agency?.ICO ?? string.Empty,
                            full_address = company.Agency?.FullAddress ?? string.Empty
                        },
                        positions,
                        formatted_profile = BuildCompanyProfileText(company, employeeCount, workAddresses, positions.Count, hiddenFrom)
                    }
                }
            };
        }

        private AiToolExecutionResult ExecuteGetEmployeeEmploymentTool(JsonElement args, long? conversationUserId)
        {
            var query = GetStringArg(args, "employee_query");
            var candidates = FindEmployeeRecords(query, 5, conversationUserId, allowContextFallback: true);
            if (!TrySelectSingleEmployeeRecord(query, candidates, out var employee))
            {
                return new AiToolExecutionResult
                {
                    CandidateEmployees = candidates.ToList(),
                    Payload = new
                    {
                        ok = false,
                        needs_clarification = true,
                        message = candidates.Count == 0 ? "Employee not found." : "Multiple employees match this employment request.",
                        candidates = candidates.Select(e => new
                        {
                            full_name = e.FullName,
                            firm = e.FirmName,
                            source = e.Source,
                            status = e.StatusLabel
                        }).ToList()
                    }
                };
            }

            var selectedEmployee = employee!;
            var data = _employeeService.LoadEmployeeData(selectedEmployee.EmployeeFolder);
            if (data == null)
            {
                return new AiToolExecutionResult
                {
                    Employee = BuildConversationEmployeeShadow(selectedEmployee),
                    FirmName = selectedEmployee.FirmName,
                    Payload = new
                    {
                        ok = false,
                        message = "Employee data is unavailable."
                    }
                };
            }

            var firmHistory = data.FirmHistory
                .OrderByDescending(item => item.StartDate)
                .Select(item => new
                {
                    firm = item.FirmName,
                    start_date = item.StartDate,
                    end_date = item.EndDate
                })
                .ToList();

            return new AiToolExecutionResult
            {
                Employee = BuildConversationEmployeeShadow(selectedEmployee),
                FirmName = selectedEmployee.FirmName,
                Payload = new
                {
                    ok = true,
                    employment = new
                    {
                        employee = selectedEmployee.FullName,
                        current_firm = selectedEmployee.FirmName,
                        source = selectedEmployee.Source,
                        status = data.Status,
                        start_date = data.StartDate,
                        end_date = data.EndDate,
                        is_archived = data.IsArchived,
                        archived_from_firm = data.ArchivedFromFirm,
                        firm_history = firmHistory,
                        currently_active = string.IsNullOrWhiteSpace(data.EndDate) && !data.IsArchived && !selectedEmployee.IsRecentlyDeleted,
                        recently_deleted_at = selectedEmployee.DeletedAtUtc?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                    }
                }
            };
        }

        private AiToolExecutionResult ExecuteGetEmployeeFullSummaryTool(JsonElement args, long? conversationUserId)
        {
            var query = GetStringArg(args, "employee_query");
            var candidates = FindEmployeeRecords(query, 5, conversationUserId, allowContextFallback: true);
            if (!TrySelectSingleEmployeeRecord(query, candidates, out var employee))
            {
                return new AiToolExecutionResult
                {
                    CandidateEmployees = candidates.ToList(),
                    Payload = new
                    {
                        ok = false,
                        needs_clarification = true,
                        message = candidates.Count == 0 ? "Employee not found." : "Multiple employees match this full summary request.",
                        candidates = candidates.Select(e => new
                        {
                            full_name = e.FullName,
                            firm = e.FirmName,
                            source = e.Source,
                            status = e.StatusLabel,
                            end_date = e.EndDate
                        }).ToList()
                    }
                };
            }

            var selectedEmployee = employee!;
            var data = _employeeService.LoadEmployeeData(selectedEmployee.EmployeeFolder);
            if (data == null)
            {
                return new AiToolExecutionResult
                {
                    Employee = BuildConversationEmployeeShadow(selectedEmployee),
                    FirmName = selectedEmployee.FirmName,
                    Payload = new
                    {
                        ok = false,
                        message = "Employee data is unavailable."
                    }
                };
            }

            var historyEvents = _employeeService.LoadHistory(selectedEmployee.EmployeeFolder)
                .OrderByDescending(item => item.Timestamp)
                .Take(6)
                .Select(item => new
                {
                    timestamp = item.Timestamp.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    event_type = item.EventType,
                    action = item.Action,
                    description = string.IsNullOrWhiteSpace(item.Description) ? item.Action : item.Description,
                    actor = item.ActorName
                })
                .ToList();

            var salaryInfo = ResolveSalaryInfo(selectedEmployee, null);
            var latestPaidSalary = ResolveLatestPaidSalaryInfo(selectedEmployee, salaryInfo);
            var employmentEvents = BuildEmploymentTimelineItems(selectedEmployee, data);

            return new AiToolExecutionResult
            {
                Employee = BuildConversationEmployeeShadow(selectedEmployee),
                FirmName = selectedEmployee.FirmName,
                MonthKey = salaryInfo?.MonthKey ?? string.Empty,
                Payload = new
                {
                    ok = true,
                    employee = BuildEmployeeToolPayload(selectedEmployee, data),
                    source = selectedEmployee.Source,
                    status_overview = new
                    {
                        source = selectedEmployee.Source,
                        status = data.Status,
                        is_archived = data.IsArchived || selectedEmployee.IsArchived,
                        archived_from_firm = data.ArchivedFromFirm,
                        recently_deleted_at = selectedEmployee.DeletedAtUtc?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                        formatted_status = BuildEmployeeStatusOverviewText(selectedEmployee, data, selectedEmployee.RecentlyDeletedItem, salaryInfo == null ? 0 : 1)
                    },
                    latest_salary = salaryInfo == null
                        ? null
                        : new
                        {
                            month = salaryInfo.MonthKey,
                            month_display = salaryInfo.Record.MonthDisplay,
                            firm = salaryInfo.Record.FirmName,
                            hours_worked = salaryInfo.Record.HoursWorked,
                            hourly_rate = salaryInfo.Record.HourlyRate,
                            gross_salary = salaryInfo.Record.GrossSalary,
                            net_salary = salaryInfo.Record.NetSalary,
                            advance = salaryInfo.Record.Advance,
                            extra_advances = salaryInfo.TotalAdvances,
                            payment_status = salaryInfo.IsPaid ? "paid" : "pending",
                            paid_at = salaryInfo.PaidAt?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? string.Empty,
                            note = salaryInfo.Record.Note,
                            formatted_salary = BuildSalaryBreakdownText(salaryInfo)
                        },
                    latest_paid_salary = latestPaidSalary == null
                        ? null
                        : new
                        {
                            month = latestPaidSalary.MonthKey,
                            month_display = latestPaidSalary.Record.MonthDisplay,
                            firm = latestPaidSalary.Record.FirmName,
                            gross_salary = latestPaidSalary.Record.GrossSalary,
                            net_salary = latestPaidSalary.Record.NetSalary,
                            paid_at = latestPaidSalary.PaidAt?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? string.Empty
                        },
                    employment_timeline = employmentEvents,
                    recent_history = historyEvents,
                    formatted_full_summary = BuildEmployeeFullSummaryText(selectedEmployee, data, salaryInfo, latestPaidSalary, historyEvents.Count, employmentEvents.Count)
                }
            };
        }

        private AiToolExecutionResult ExecuteSendEmployeeDocumentTool(JsonElement args, long? conversationUserId)
        {
            var query = GetStringArg(args, "employee_query");
            var documentType = NormalizeForSearch(GetStringArg(args, "document_type")).Replace(" ", "_", StringComparison.Ordinal);
            return BuildSendEmployeeDocumentResult(query, documentType, conversationUserId);
        }

        private AiToolExecutionResult BuildSendEmployeeDocumentResult(string query, string documentType, long? conversationUserId)
        {
            var candidates = FindEmployeeRecords(query, 5, conversationUserId, allowContextFallback: true);
            if (!TrySelectSingleEmployeeRecord(query, candidates, out var employee))
            {
                return new AiToolExecutionResult
                {
                    CandidateEmployees = candidates.ToList(),
                    Payload = new
                    {
                        ok = false,
                        needs_clarification = true,
                        message = candidates.Count == 0 ? "Employee not found." : "Multiple employees match this document request.",
                        candidates = candidates.Select(e => new
                        {
                            full_name = e.FullName,
                            firm = e.FirmName,
                            source = e.Source,
                            status = e.StatusLabel
                        }).ToList()
                    }
                };
            }

            var selectedEmployee = employee!;
            var data = _employeeService.LoadEmployeeData(selectedEmployee.EmployeeFolder);
            if (data == null)
            {
                return new AiToolExecutionResult
                {
                    Employee = BuildConversationEmployeeShadow(selectedEmployee),
                    FirmName = selectedEmployee.FirmName,
                    Payload = new
                    {
                        ok = false,
                        message = "Employee data is unavailable."
                    }
                };
            }

            if (!TryBuildEmployeeDocumentPendingFile(selectedEmployee, data, documentType, out var pendingFile, out var documentLabel, out var errorMessage))
            {
                return new AiToolExecutionResult
                {
                    Employee = BuildConversationEmployeeShadow(selectedEmployee),
                    FirmName = selectedEmployee.FirmName,
                    Payload = new
                    {
                        ok = false,
                        message = errorMessage
                    }
                };
            }

            return new AiToolExecutionResult
            {
                Employee = BuildConversationEmployeeShadow(selectedEmployee),
                FirmName = selectedEmployee.FirmName,
                Payload = new
                {
                    ok = true,
                    employee = selectedEmployee.FullName,
                    firm = selectedEmployee.FirmName,
                    document_type = documentType,
                    document_label = documentLabel,
                    file_name = pendingFile!.FileName,
                    formatted_result = $"Надсилаю документ {documentLabel} працівника {selectedEmployee.FullName}."
                },
                FilesToSend = new List<PendingFile> { pendingFile! }
            };
        }

        private AiToolExecutionResult ExecuteExportFirmSalaryExcelTool(JsonElement args, long? conversationUserId)
        {
            var firm = ResolveFirmNameForAi(GetStringArg(args, "firm"), conversationUserId);
            var monthKey = ResolveMonthKeyForAi(GetStringArg(args, "month"), conversationUserId);
            if (string.IsNullOrWhiteSpace(firm))
            {
                return new AiToolExecutionResult
                {
                    Payload = new
                    {
                        ok = false,
                        needs_clarification = true,
                        message = "Salary Excel export requires a firm name."
                    }
                };
            }

            if (string.IsNullOrWhiteSpace(monthKey) || !TryParseMonthKey(monthKey, out var month))
            {
                return new AiToolExecutionResult
                {
                    FirmName = firm,
                    Payload = new
                    {
                        ok = false,
                        needs_clarification = true,
                        message = "Salary Excel export requires a month."
                    }
                };
            }

            var payments = _financeService.TryLoadAllFirmPayments(month.Year, month.Month, forceReload: true);
            if (!payments.success)
            {
                return new AiToolExecutionResult
                {
                    FirmName = firm,
                    MonthKey = monthKey,
                    Payload = new
                    {
                        ok = false,
                        message = "Could not load salary data for this month."
                    }
                };
            }

            var exportEntries = payments.entries
                .Where(entry => string.Equals(NormalizeForSearch(entry.FirmName), NormalizeForSearch(firm), StringComparison.Ordinal))
                .OrderBy(entry => entry.FullName)
                .ToList();
            if (exportEntries.Count == 0)
            {
                return new AiToolExecutionResult
                {
                    FirmName = firm,
                    MonthKey = monthKey,
                    Payload = new
                    {
                        ok = false,
                        message = "No salary entries found for this firm and month."
                    }
                };
            }

            var fields = _financeService.GetFieldsForFirm(firm)
                .OrderBy(field => field.Order)
                .ThenBy(field => field.Name)
                .ToList();
            foreach (var entry in exportEntries)
                entry.FieldDefinitions = fields;

            var exportExpenses = payments.expenses
                .Where(expense => string.Equals(NormalizeForSearch(expense.FirmName), NormalizeForSearch(firm), StringComparison.Ordinal))
                .OrderBy(expense => expense.Name)
                .ToList();
            var fileName = $"Salary_{SanitizeFileNamePart(firm)}_{month.Year:D4}-{month.Month:D2}.xlsx";
            var content = SalaryExcelExportService.GenerateFirmSalaryExcel(
                firm,
                month.Year,
                month.Month,
                exportEntries,
                fields,
                exportExpenses,
                $"{month.Month:D2}.{month.Year:D4}");

            var pendingFile = new PendingFile
            {
                ContentBytes = content,
                FileName = fileName,
                Caption = $"Зарплата по фірмі {firm} за {month.Month:D2}.{month.Year:D4}",
                IsPhoto = false
            };

            return new AiToolExecutionResult
            {
                FirmName = firm,
                MonthKey = monthKey,
                Payload = new
                {
                    ok = true,
                    firm,
                    month = monthKey,
                    entry_count = exportEntries.Count,
                    expense_count = exportExpenses.Count,
                    file_name = fileName,
                    formatted_result = $"Надсилаю Excel зарплати по фірмі {firm} за {month.Month:D2}.{month.Year:D4}."
                },
                FilesToSend = new List<PendingFile> { pendingFile }
            };
        }

        private AiToolExecutionResult ExecuteGetSalaryTool(JsonElement args, long? conversationUserId)
        {
            var query = GetStringArg(args, "employee_query");
            var candidates = FindEmployeeRecords(query, 5, conversationUserId, allowContextFallback: true);
            if (!TrySelectSingleEmployeeRecord(query, candidates, out var employee))
            {
                return new AiToolExecutionResult
                {
                    CandidateEmployees = candidates.ToList(),
                    Payload = new
                    {
                        ok = false,
                        needs_clarification = true,
                        message = candidates.Count == 0 ? "Employee not found." : "Multiple employees match this salary request.",
                        candidates = candidates.Select(e => new
                        {
                            unique_id = e.UniqueId,
                            full_name = e.FullName,
                            firm = e.FirmName,
                            source = e.Source,
                            status = e.StatusLabel
                        }).ToList()
                    }
                };
            }

            var selectedEmployee = employee!;
            var monthKey = ResolveMonthKeyForAi(GetStringArg(args, "month"), conversationUserId);
            var salaryInfo = ResolveSalaryInfo(selectedEmployee, monthKey);
            if (salaryInfo == null)
            {
                return new AiToolExecutionResult
                {
                    Employee = BuildConversationEmployeeShadow(selectedEmployee),
                    FirmName = selectedEmployee.FirmName,
                    MonthKey = monthKey,
                    Payload = new
                    {
                        ok = false,
                        message = "Salary data not found for this employee and month."
                    }
                };
            }

            var accommodation = _financeService.GetAccommodationForEmployee(selectedEmployee.EmployeeFolder, salaryInfo.Record.Year, salaryInfo.Record.Month);
            var (totalDebt, debtDetails) = _financeService.CalculateCarriedDebtForFirm(selectedEmployee.EmployeeFolder, salaryInfo.Record.FirmName, salaryInfo.Record.Year, salaryInfo.Record.Month);

            return new AiToolExecutionResult
            {
                Employee = BuildConversationEmployeeShadow(selectedEmployee),
                FirmName = salaryInfo.Record.FirmName,
                MonthKey = salaryInfo.MonthKey,
                Payload = new
                {
                    ok = true,
                    salary = new
                    {
                        employee = selectedEmployee.FullName,
                        source = selectedEmployee.Source,
                        firm = salaryInfo.Record.FirmName,
                        month = salaryInfo.MonthKey,
                        month_display = salaryInfo.Record.MonthDisplay,
                        hours_worked = salaryInfo.Record.HoursWorked,
                        hourly_rate = salaryInfo.Record.HourlyRate,
                        gross_salary = salaryInfo.Record.GrossSalary,
                        salary_advance = salaryInfo.Record.Advance,
                        extra_advances_total = salaryInfo.TotalAdvances,
                        accommodation,
                        carried_debt = totalDebt,
                        debt_details = debtDetails.Select(d => new
                        {
                            month_key = d.FromMonthKey,
                            amount = d.Amount
                        }).ToList(),
                        custom_fields = salaryInfo.Record.CustomFields.Select(field => new
                        {
                            name = field.Name,
                            operation = field.Operation,
                            value = field.Value
                        }).ToList(),
                        net_salary = salaryInfo.Record.NetSalary,
                        payment_status = salaryInfo.IsPaid ? "paid" : "pending",
                        paid_at = salaryInfo.PaidAt?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? string.Empty,
                        note = salaryInfo.Record.Note,
                        advances = salaryInfo.Advances.Select(a => new
                        {
                            date = a.Date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
                            amount = a.Amount,
                            note = a.Note
                        }).ToList()
                    }
                }
            };
        }

        private AiToolExecutionResult ExecuteGetFirmSalaryTool(JsonElement args, long? conversationUserId)
        {
            var firmQuery = GetStringArg(args, "firm");
            var firm = ResolveFirmNameForAi(firmQuery, conversationUserId);
            var monthKey = ResolveMonthKeyForAi(GetStringArg(args, "month"), conversationUserId);
            if (string.IsNullOrWhiteSpace(firm) || string.IsNullOrWhiteSpace(monthKey))
            {
                var suggestions = string.IsNullOrWhiteSpace(firm)
                    ? FindFirmMatches(firmQuery, 3).Select(match => match.Company.Name).ToList()
                    : new List<string>();
                return new AiToolExecutionResult
                {
                    FirmName = firm,
                    MonthKey = monthKey,
                    Payload = new
                    {
                        ok = false,
                        needs_clarification = true,
                        message = "Firm salary requires both firm name and month.",
                        suggested_firms = suggestions
                    }
                };
            }

            var firmSalary = ResolveFirmSalaryInfo(firm, monthKey);
            if (firmSalary == null)
            {
                return new AiToolExecutionResult
                {
                    FirmName = firm,
                    MonthKey = monthKey,
                    Payload = new
                    {
                        ok = false,
                        message = "Firm salary data not found for this month."
                    }
                };
            }

            var employeeDetails = BuildFirmEmployeeSalaryDetails(firmSalary);

            return new AiToolExecutionResult
            {
                FirmName = firm,
                MonthKey = $"{firmSalary.Year:D4}-{firmSalary.Month:D2}",
                Payload = new
                {
                    ok = true,
                    firm_salary = new
                    {
                        firm = firmSalary.FirmName,
                        month = $"{firmSalary.Year:D4}-{firmSalary.Month:D2}",
                        month_display = firmSalary.MonthDisplay,
                        employee_count = firmSalary.Entries.Count,
                        total_gross = firmSalary.TotalGross,
                        total_net = firmSalary.TotalNet,
                        total_advances = firmSalary.TotalAdvances,
                        total_expenses = firmSalary.TotalExpenses,
                        paid_count = firmSalary.PaidCount,
                        pending_count = firmSalary.PendingCount,
                        employees = employeeDetails
                            .Select(detail => new
                            {
                                employee = detail.EmployeeName,
                                hours_worked = detail.HoursWorked,
                                hourly_rate = detail.HourlyRate,
                                salary_advance = detail.SalaryAdvance,
                                net_salary = detail.NetSalary,
                                ordered_columns = detail.OrderedColumns.Select(field => new
                                {
                                    name = field.name,
                                    operation = field.operation,
                                    value = field.value
                                }).ToList(),
                                ordered_row_text = detail.OrderedRowText,
                                note = detail.Note,
                                final_payout = detail.NetSalary
                            }).ToList(),
                        expenses = firmSalary.Expenses.Select(expense => new
                        {
                            name = expense.Name,
                            amount = expense.Amount
                        }).ToList()
                    }
                }
            };
        }

        private AiToolExecutionResult ExecuteGetAllFirmsSalaryTool(JsonElement args, long? conversationUserId)
        {
            var monthKey = ResolveMonthKeyForAi(GetStringArg(args, "month"), conversationUserId);
            if (string.IsNullOrWhiteSpace(monthKey))
            {
                return new AiToolExecutionResult
                {
                    MonthKey = monthKey,
                    Payload = new
                    {
                        ok = false,
                        needs_clarification = true,
                        message = "All firms salary requires a month."
                    }
                };
            }

            var allFirmsSalary = ResolveAllFirmsSalaryInfo(monthKey);
            if (allFirmsSalary == null)
            {
                return new AiToolExecutionResult
                {
                    MonthKey = monthKey,
                    Payload = new
                    {
                        ok = false,
                        message = "No salary data found for all firms in this month."
                    }
                };
            }

            return new AiToolExecutionResult
            {
                MonthKey = $"{allFirmsSalary.Year:D4}-{allFirmsSalary.Month:D2}",
                Payload = new
                {
                    ok = true,
                    all_firms_salary = new
                    {
                        month = $"{allFirmsSalary.Year:D4}-{allFirmsSalary.Month:D2}",
                        month_display = allFirmsSalary.MonthDisplay,
                        firm_count = allFirmsSalary.Firms.Count,
                        total_gross = allFirmsSalary.TotalGross,
                        total_net = allFirmsSalary.TotalNet,
                        total_advances = allFirmsSalary.TotalAdvances,
                        total_expenses = allFirmsSalary.TotalExpenses,
                        paid_count = allFirmsSalary.TotalPaidCount,
                        pending_count = allFirmsSalary.TotalPendingCount,
                        firms = allFirmsSalary.Firms
                            .OrderByDescending(f => f.TotalNet)
                            .Select(firm => new
                            {
                                firm = firm.FirmName,
                                employee_count = firm.Entries.Count,
                                total_gross = firm.TotalGross,
                                total_net = firm.TotalNet,
                                total_advances = firm.TotalAdvances,
                                total_expenses = firm.TotalExpenses,
                                paid_count = firm.PaidCount,
                                pending_count = firm.PendingCount
                            }).ToList()
                    }
                }
            };
        }

        private AiToolExecutionResult ExecuteGetFirmPeriodSummaryTool(JsonElement args, long? conversationUserId)
        {
            var firmQuery = GetStringArg(args, "firm");
            var firm = ResolveFirmNameForAi(firmQuery, conversationUserId);
            var startMonthKey = ResolveMonthKeyForAi(GetStringArg(args, "start_month"), conversationUserId);
            var endMonthKey = ResolveMonthKeyForAi(GetStringArg(args, "end_month"), conversationUserId);
            if (string.IsNullOrWhiteSpace(firm) || string.IsNullOrWhiteSpace(startMonthKey) || string.IsNullOrWhiteSpace(endMonthKey))
            {
                return new AiToolExecutionResult
                {
                    FirmName = firm,
                    MonthKey = startMonthKey,
                    Payload = new
                    {
                        ok = false,
                        needs_clarification = true,
                        message = "Firm period summary requires firm name, start month and end month.",
                        suggested_firms = string.IsNullOrWhiteSpace(firm) ? FindFirmMatches(firmQuery, 3).Select(match => match.Company.Name).ToList() : new List<string>()
                    }
                };
            }

            if (!TryParseMonthKey(startMonthKey, out var startMonth) || !TryParseMonthKey(endMonthKey, out var endMonth))
            {
                return new AiToolExecutionResult
                {
                    FirmName = firm,
                    Payload = new
                    {
                        ok = false,
                        message = "Could not parse one of the requested months."
                    }
                };
            }

            if (startMonth > endMonth)
                (startMonth, endMonth) = (endMonth, startMonth);

            var monthItems = EnumerateMonths(startMonth, endMonth)
                .Select(month => new
                {
                    Key = $"{month.Year:D4}-{month.Month:D2}",
                    Info = ResolveFirmSalaryInfo(firm, $"{month.Year:D4}-{month.Month:D2}")
                })
                .Where(item => item.Info != null)
                .Select(item => new { item.Key, Info = item.Info! })
                .ToList();

            if (monthItems.Count == 0)
            {
                return new AiToolExecutionResult
                {
                    FirmName = firm,
                    Payload = new
                    {
                        ok = false,
                        message = "No salary data found for this firm in the selected period."
                    }
                };
            }

            return new AiToolExecutionResult
            {
                FirmName = firm,
                MonthKey = $"{startMonth:yyyy-MM}..{endMonth:yyyy-MM}",
                Payload = new
                {
                    ok = true,
                    firm_period_summary = new
                    {
                        firm,
                        start_month = $"{startMonth:yyyy-MM}",
                        end_month = $"{endMonth:yyyy-MM}",
                        months_with_data = monthItems.Count,
                        total_gross = monthItems.Sum(item => item.Info.TotalGross),
                        total_net = monthItems.Sum(item => item.Info.TotalNet),
                        total_advances = monthItems.Sum(item => item.Info.TotalAdvances),
                        total_expenses = monthItems.Sum(item => item.Info.TotalExpenses),
                        total_paid_count = monthItems.Sum(item => item.Info.PaidCount),
                        total_pending_count = monthItems.Sum(item => item.Info.PendingCount),
                        monthly_breakdown = monthItems.Select(item => new
                        {
                            month = item.Key,
                            month_display = item.Info.MonthDisplay,
                            employee_count = item.Info.Entries.Count,
                            total_gross = item.Info.TotalGross,
                            total_net = item.Info.TotalNet,
                            total_advances = item.Info.TotalAdvances,
                            total_expenses = item.Info.TotalExpenses,
                            paid_count = item.Info.PaidCount,
                            pending_count = item.Info.PendingCount
                        }).ToList(),
                        formatted_summary = BuildFirmPeriodSummaryText(firm, startMonth, endMonth, monthItems.Select(item => item.Info).ToList())
                    }
                }
            };
        }

        private AiToolExecutionResult ExecuteCompareSalaryMonthsTool(JsonElement args, long? conversationUserId)
        {
            var firmQuery = GetStringArg(args, "firm");
            var firm = ResolveFirmNameForAi(firmQuery, conversationUserId);
            var monthAKey = ResolveMonthKeyForAi(GetStringArg(args, "month_a"), conversationUserId);
            var monthBKey = ResolveMonthKeyForAi(GetStringArg(args, "month_b"), conversationUserId);
            if (string.IsNullOrWhiteSpace(monthAKey) || string.IsNullOrWhiteSpace(monthBKey))
            {
                return new AiToolExecutionResult
                {
                    FirmName = firm,
                    Payload = new
                    {
                        ok = false,
                        needs_clarification = true,
                        message = "Month comparison requires two months."
                    }
                };
            }

            if (string.IsNullOrWhiteSpace(firm))
            {
                var infoA = ResolveAllFirmsSalaryInfo(monthAKey);
                var infoB = ResolveAllFirmsSalaryInfo(monthBKey);
                if (infoA == null || infoB == null)
                {
                    return new AiToolExecutionResult
                    {
                        Payload = new
                        {
                            ok = false,
                            message = "Could not load all-firms salary data for one of the requested months."
                        }
                    };
                }

                return new AiToolExecutionResult
                {
                    MonthKey = $"{monthAKey}|{monthBKey}",
                    Payload = new
                    {
                        ok = true,
                        comparison = BuildSalaryComparisonPayload(
                            "all_firms",
                            "Всі фірми",
                            monthAKey,
                            infoA.TotalGross,
                            infoA.TotalNet,
                            infoA.TotalAdvances,
                            infoA.TotalExpenses,
                            infoA.TotalPaidCount,
                            infoA.TotalPendingCount,
                            infoB.MonthDisplay,
                            monthBKey,
                            infoB.TotalGross,
                            infoB.TotalNet,
                            infoB.TotalAdvances,
                            infoB.TotalExpenses,
                            infoB.TotalPaidCount,
                            infoB.TotalPendingCount,
                            infoA.MonthDisplay,
                            BuildSalaryComparisonText("Всі фірми", infoA.MonthDisplay, infoA.TotalNet, infoA.TotalGross, infoA.TotalExpenses, infoB.MonthDisplay, infoB.TotalNet, infoB.TotalGross, infoB.TotalExpenses))
                    }
                };
            }

            var firmA = ResolveFirmSalaryInfo(firm, monthAKey);
            var firmB = ResolveFirmSalaryInfo(firm, monthBKey);
            if (firmA == null || firmB == null)
            {
                return new AiToolExecutionResult
                {
                    FirmName = firm,
                    Payload = new
                    {
                        ok = false,
                        message = "Could not load firm salary data for one of the requested months."
                    }
                };
            }

            return new AiToolExecutionResult
            {
                FirmName = firm,
                MonthKey = $"{monthAKey}|{monthBKey}",
                Payload = new
                {
                    ok = true,
                    comparison = BuildSalaryComparisonPayload(
                        "firm",
                        firm,
                        monthAKey,
                        firmA.TotalGross,
                        firmA.TotalNet,
                        firmA.TotalAdvances,
                        firmA.TotalExpenses,
                        firmA.PaidCount,
                        firmA.PendingCount,
                        firmB.MonthDisplay,
                        monthBKey,
                        firmB.TotalGross,
                        firmB.TotalNet,
                        firmB.TotalAdvances,
                        firmB.TotalExpenses,
                        firmB.PaidCount,
                        firmB.PendingCount,
                        firmA.MonthDisplay,
                        BuildSalaryComparisonText(firm, firmA.MonthDisplay, firmA.TotalNet, firmA.TotalGross, firmA.TotalExpenses, firmB.MonthDisplay, firmB.TotalNet, firmB.TotalGross, firmB.TotalExpenses))
                }
            };
        }

        private AiToolExecutionResult ExecuteGetAdvancesTool(JsonElement args, long? conversationUserId)
        {
            var query = GetStringArg(args, "employee_query");
            var candidates = FindEmployeeRecords(query, 5, conversationUserId, allowContextFallback: true);
            if (!TrySelectSingleEmployeeRecord(query, candidates, out var employee))
            {
                return new AiToolExecutionResult
                {
                    CandidateEmployees = candidates.ToList(),
                    Payload = new
                    {
                        ok = false,
                        needs_clarification = true,
                        message = candidates.Count == 0 ? "Employee not found." : "Multiple employees match this advances request.",
                        candidates = candidates.Select(e => new
                        {
                            unique_id = e.UniqueId,
                            full_name = e.FullName,
                            firm = e.FirmName,
                            source = e.Source
                        }).ToList()
                    }
                };
            }

            var selectedEmployee = employee!;
            var monthKey = ResolveMonthKeyForAi(GetStringArg(args, "month"), conversationUserId);
            var advances = string.IsNullOrWhiteSpace(monthKey)
                ? _financeService.GetAllAdvancesForEmployee(selectedEmployee.EmployeeFolder)
                : _financeService.GetAdvancesForEmployeeMonth(selectedEmployee.EmployeeFolder, monthKey);

            return new AiToolExecutionResult
            {
                Employee = BuildConversationEmployeeShadow(selectedEmployee),
                FirmName = selectedEmployee.FirmName,
                MonthKey = monthKey,
                Payload = new
                {
                    ok = true,
                    employee = selectedEmployee.FullName,
                    source = selectedEmployee.Source,
                    month = monthKey,
                    advances = advances
                        .OrderByDescending(a => a.Date)
                        .Take(20)
                        .Select(a => new
                        {
                            date = a.Date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
                            amount = a.Amount,
                            month = a.Month,
                            note = a.Note
                        }).ToList()
                }
            };
        }

        private AiToolExecutionResult ExecuteGetExpiringDocumentsTool(JsonElement args)
        {
            var days = Math.Max(1, Math.Min(365, GetIntArg(args, "days", 30)));
            var expiring = BuildExpiringItems(days);

            return new AiToolExecutionResult
            {
                Payload = new
                {
                    ok = true,
                    days,
                    documents = expiring
                        .Take(50)
                        .Select(item => new
                        {
                            employee = item.employee,
                            firm = item.firm,
                            type = item.type,
                            date = item.date,
                            days_left = item.daysLeft
                        }).ToList()
                }
            };
        }

        private AiToolExecutionResult ExecuteGetEmployeeHistoryTool(JsonElement args, long? conversationUserId)
        {
            var query = GetStringArg(args, "employee_query");
            var limit = Math.Max(1, Math.Min(30, GetIntArg(args, "limit", 10)));
            var candidates = FindEmployeeRecords(query, 5, conversationUserId, allowContextFallback: true);
            if (!TrySelectSingleEmployeeRecord(query, candidates, out var employee))
            {
                return new AiToolExecutionResult
                {
                    CandidateEmployees = candidates.ToList(),
                    Payload = new
                    {
                        ok = false,
                        needs_clarification = true,
                        message = candidates.Count == 0 ? "Employee not found." : "Multiple employees match this history request.",
                        candidates = candidates.Select(e => new
                        {
                            unique_id = e.UniqueId,
                            full_name = e.FullName,
                            firm = e.FirmName,
                            source = e.Source
                        }).ToList()
                    }
                };
            }

            var selectedEmployee = employee!;
            var history = _employeeService.LoadHistory(selectedEmployee.EmployeeFolder)
                .OrderByDescending(item => item.Timestamp)
                .Take(limit)
                .Select(item => new
                {
                    timestamp = item.Timestamp.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    event_type = item.EventType,
                    action = item.Action,
                    field = item.Field,
                    old_value = item.OldValue,
                    new_value = item.NewValue,
                    description = item.Description,
                    actor = item.ActorName
                })
                .ToList();

            return new AiToolExecutionResult
            {
                Employee = BuildConversationEmployeeShadow(selectedEmployee),
                FirmName = selectedEmployee.FirmName,
                Payload = new
                {
                    ok = true,
                    employee = selectedEmployee.FullName,
                    source = selectedEmployee.Source,
                    history
                }
            };
        }

        private AiToolExecutionResult ExecuteGetEmployeeStatusOverviewTool(JsonElement args, long? conversationUserId)
        {
            var query = GetStringArg(args, "employee_query");
            var candidates = FindEmployeeRecords(query, 5, conversationUserId, allowContextFallback: true);
            if (!TrySelectSingleEmployeeRecord(query, candidates, out var employee))
            {
                return new AiToolExecutionResult
                {
                    CandidateEmployees = candidates.ToList(),
                    Payload = new
                    {
                        ok = false,
                        needs_clarification = true,
                        message = candidates.Count == 0 ? "Employee not found." : "Multiple employees match this status request.",
                        candidates = candidates.Select(e => new
                        {
                            unique_id = e.UniqueId,
                            full_name = e.FullName,
                            firm = e.FirmName,
                            source = e.Source
                        }).ToList()
                    }
                };
            }

            var selectedEmployee = employee!;
            var data = _employeeService.LoadEmployeeData(selectedEmployee.EmployeeFolder);
            var recentDeleted = selectedEmployee.RecentlyDeletedItem;
            var salaryHistory = _financeService.LoadSalaryHistory(selectedEmployee.EmployeeFolder)
                .OrderByDescending(item => $"{item.Year:D4}-{item.Month:D2}")
                .Take(3)
                .Select(item => new
                {
                    month = $"{item.Year:D4}-{item.Month:D2}",
                    firm = item.FirmName,
                    net_salary = item.NetSalary
                })
                .ToList();

            return new AiToolExecutionResult
            {
                Employee = BuildConversationEmployeeShadow(selectedEmployee),
                FirmName = selectedEmployee.FirmName,
                Payload = new
                {
                    ok = true,
                    overview = new
                    {
                        employee = selectedEmployee.FullName,
                        unique_id = selectedEmployee.UniqueId,
                        source = selectedEmployee.Source,
                        current_or_last_firm = selectedEmployee.FirmName,
                        start_date = data?.StartDate ?? selectedEmployee.StartDate,
                        end_date = data?.EndDate ?? selectedEmployee.EndDate,
                        is_archived = data?.IsArchived ?? selectedEmployee.IsArchived,
                        archived_from_firm = data?.ArchivedFromFirm ?? string.Empty,
                        status = data?.Status ?? selectedEmployee.StatusLabel,
                        recently_deleted_at = recentDeleted?.DeletedAtUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                        salary_history_preview = salaryHistory,
                        formatted_status = BuildEmployeeStatusOverviewText(selectedEmployee, data, recentDeleted, salaryHistory.Count)
                    }
                }
            };
        }

        private AiToolExecutionResult ExecuteGetEmployeeTimelineTool(JsonElement args, long? conversationUserId)
        {
            var query = GetStringArg(args, "employee_query");
            var limit = Math.Max(3, Math.Min(30, GetIntArg(args, "limit", 12)));
            var candidates = FindEmployeeRecords(query, 5, conversationUserId, allowContextFallback: true);
            if (!TrySelectSingleEmployeeRecord(query, candidates, out var employee))
            {
                return new AiToolExecutionResult
                {
                    Payload = new
                    {
                        ok = false,
                        needs_clarification = true,
                        message = candidates.Count == 0 ? "Employee not found." : "Multiple employees match this timeline request.",
                        candidates = candidates.Select(e => new
                        {
                            unique_id = e.UniqueId,
                            full_name = e.FullName,
                            firm = e.FirmName,
                            source = e.Source
                        }).ToList()
                    }
                };
            }

            var selectedEmployee = employee!;
            var data = _employeeService.LoadEmployeeData(selectedEmployee.EmployeeFolder);
            var salaryHistory = _financeService.LoadSalaryHistory(selectedEmployee.EmployeeFolder)
                .OrderByDescending(item => item.Year)
                .ThenByDescending(item => item.Month)
                .Take(limit)
                .Select(item => new
                {
                    type = "salary",
                    sort_key = $"{item.Year:D4}-{item.Month:D2}",
                    title = $"Зарплата {item.MonthDisplay}",
                    details = $"{item.FirmName}: нетто {item.NetSalary:N2} CZK, брутто {item.GrossSalary:N2} CZK"
                })
                .ToList();
            var historyEvents = _employeeService.LoadHistory(selectedEmployee.EmployeeFolder)
                .OrderByDescending(item => item.Timestamp)
                .Take(limit)
                .Select(item => new
                {
                    type = "history",
                    sort_key = item.Timestamp.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    title = item.EventType,
                    details = string.IsNullOrWhiteSpace(item.Description) ? item.Action : item.Description
                })
                .ToList();
            var employmentEvents = BuildEmploymentTimelineItems(selectedEmployee, data);

            return new AiToolExecutionResult
            {
                Employee = BuildConversationEmployeeShadow(selectedEmployee),
                FirmName = selectedEmployee.FirmName,
                Payload = new
                {
                    ok = true,
                    timeline = new
                    {
                        employee = selectedEmployee.FullName,
                        unique_id = selectedEmployee.UniqueId,
                        source = selectedEmployee.Source,
                        employment = employmentEvents,
                        history_events = historyEvents,
                        salary_events = salaryHistory,
                        formatted_timeline = BuildEmployeeTimelineText(selectedEmployee, data, historyEvents.Count, salaryHistory.Count)
                    }
                }
            };
        }

        private AiToolExecutionResult ExecuteListArchivedEmployeesTool(JsonElement args, long? conversationUserId)
        {
            var query = GetStringArg(args, "query");
            var firm = ResolveFirmNameForAi(GetStringArg(args, "firm"), conversationUserId);
            var startMonthKey = ResolveMonthKeyForAi(GetStringArg(args, "start_month"), conversationUserId);
            var endMonthKey = ResolveMonthKeyForAi(GetStringArg(args, "end_month"), conversationUserId);
            var limit = Math.Max(1, Math.Min(50, GetIntArg(args, "limit", 15)));

            var archived = _employeeService.GetArchivedEmployees()
                .Where(item => string.IsNullOrWhiteSpace(query) || CalculateArchivedEmployeeScore(item, query) > 0)
                .Where(item => string.IsNullOrWhiteSpace(firm) || string.Equals(NormalizeForSearch(item.FirmName), NormalizeForSearch(firm), StringComparison.Ordinal))
                .Where(item => MatchesMonthRange(item.EndDate, startMonthKey, endMonthKey))
                .OrderByDescending(item => item.ParsedEndDate ?? DateTime.MinValue)
                .ThenBy(item => item.FullName)
                .Take(limit)
                .ToList();

            return new AiToolExecutionResult
            {
                FirmName = firm,
                Payload = new
                {
                    ok = archived.Count > 0,
                    firm,
                    start_month = startMonthKey,
                    end_month = endMonthKey,
                    archived_employees = archived.Select(item => new
                    {
                        unique_id = item.UniqueId,
                        full_name = item.FullName,
                        firm = item.FirmName,
                        position = item.PositionTitle,
                        start_date = item.StartDate,
                        end_date = item.EndDate
                    }).ToList(),
                    message = archived.Count == 0 ? "No archived employees found for this filter." : string.Empty
                }
            };
        }

        private AiToolExecutionResult ExecuteGetTopPayoutsForMonthTool(JsonElement args, long? conversationUserId)
        {
            var monthKey = ResolveMonthKeyForAi(GetStringArg(args, "month"), conversationUserId);
            var firm = ResolveFirmNameForAi(GetStringArg(args, "firm"), conversationUserId);
            var sortBy = NormalizeForSearch(GetStringArg(args, "sort_by"));
            var limit = Math.Max(1, Math.Min(30, GetIntArg(args, "limit", 10)));
            if (string.IsNullOrWhiteSpace(monthKey) || !TryParseMonthKey(monthKey, out var month))
            {
                return new AiToolExecutionResult
                {
                    Payload = new
                    {
                        ok = false,
                        needs_clarification = true,
                        message = "Top payouts ranking requires a month."
                    }
                };
            }

            var payments = _financeService.TryLoadAllFirmPayments(month.Year, month.Month, forceReload: true);
            if (!payments.success)
            {
                return new AiToolExecutionResult
                {
                    Payload = new
                    {
                        ok = false,
                        message = "Could not load salary data for this month."
                    }
                };
            }

            var filteredEntries = payments.entries
                .Where(entry => string.IsNullOrWhiteSpace(firm) || string.Equals(NormalizeForSearch(entry.FirmName), NormalizeForSearch(firm), StringComparison.Ordinal))
                .ToList();

            var ranking = (sortBy == "gross" ? filteredEntries.OrderByDescending(entry => entry.GrossSalary) : filteredEntries.OrderByDescending(GetEntryNetSalary))
                .ThenBy(entry => entry.FullName)
                .Take(limit)
                .Select((entry, index) => new
                {
                    rank = index + 1,
                    employee = entry.FullName,
                    firm = entry.FirmName,
                    hours_worked = entry.HoursWorked,
                    hourly_rate = entry.HourlyRate,
                    gross_salary = entry.GrossSalary,
                    net_salary = GetEntryNetSalary(entry),
                    advance = entry.Advance,
                    note = entry.Note
                })
                .ToList();

            return new AiToolExecutionResult
            {
                FirmName = firm,
                MonthKey = monthKey,
                Payload = new
                {
                    ok = ranking.Count > 0,
                    month = monthKey,
                    firm,
                    sort_by = sortBy == "gross" ? "gross" : "net",
                    ranking,
                    formatted_ranking = BuildTopPayoutsText(monthKey, firm, sortBy == "gross" ? "gross" : "net", ranking.Select(item => (item.rank, item.employee, item.firm, item.gross_salary, item.net_salary)).ToList()),
                    message = ranking.Count == 0 ? "No payouts found for this month and filter." : string.Empty
                }
            };
        }

        private AiToolExecutionResult ExecuteGetHiringSummaryTool(JsonElement args, long? conversationUserId)
        {
            var monthKey = ResolveMonthKeyForAi(GetStringArg(args, "month"), conversationUserId);
            var firm = ResolveFirmNameForAi(GetStringArg(args, "firm"), conversationUserId);
            var limit = Math.Max(1, Math.Min(30, GetIntArg(args, "limit", 10)));
            if (string.IsNullOrWhiteSpace(monthKey) || !TryParseMonthKey(monthKey, out var month))
            {
                return new AiToolExecutionResult
                {
                    FirmName = firm,
                    Payload = new
                    {
                        ok = false,
                        needs_clarification = true,
                        message = "Hiring summary requires a month."
                    }
                };
            }

            var startedEmployees = GetAllEmployeeRecords(includeArchived: true, includeRecentlyDeleted: false)
                .Where(record => !string.IsNullOrWhiteSpace(record.StartDate))
                .Where(record => string.IsNullOrWhiteSpace(firm) || string.Equals(NormalizeForSearch(record.FirmName), NormalizeForSearch(firm), StringComparison.Ordinal))
                .Where(record => MatchesMonthRange(record.StartDate, monthKey, monthKey))
                .GroupBy(record => $"{record.UniqueId}|{record.StartDate}|{record.FirmName}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderByDescending(record => ParseFlexibleDateOrMin(record.StartDate))
                .ThenBy(record => record.FullName)
                .ToList();

            var latestStarted = startedEmployees.FirstOrDefault();

            return new AiToolExecutionResult
            {
                Employee = BuildConversationEmployeeShadow(latestStarted),
                FirmName = latestStarted?.FirmName ?? firm,
                MonthKey = monthKey,
                Payload = new
                {
                    ok = startedEmployees.Count > 0,
                    month = monthKey,
                    firm,
                    new_count = startedEmployees.Count,
                    latest_started_employee = latestStarted == null
                        ? null
                        : new
                        {
                            full_name = latestStarted.FullName,
                            firm = latestStarted.FirmName,
                            start_date = latestStarted.StartDate,
                            source = latestStarted.Source
                        },
                    employees_started = startedEmployees
                        .Take(limit)
                        .Select(employee => new
                        {
                            full_name = employee.FullName,
                            firm = employee.FirmName,
                            position = employee.PositionTitle,
                            start_date = employee.StartDate,
                            source = employee.Source
                        })
                        .ToList(),
                    formatted_summary = BuildHiringSummaryText(monthKey, firm, startedEmployees, latestStarted),
                    message = startedEmployees.Count == 0 ? "No employees started in this month for the selected filter." : string.Empty
                }
            };
        }

        private AiToolExecutionResult ExecuteGetTerminationSummaryTool(JsonElement args, long? conversationUserId)
        {
            var monthKey = ResolveMonthKeyForAi(GetStringArg(args, "month"), conversationUserId);
            var firm = ResolveFirmNameForAi(GetStringArg(args, "firm"), conversationUserId);
            var limit = Math.Max(1, Math.Min(30, GetIntArg(args, "limit", 10)));
            if (string.IsNullOrWhiteSpace(monthKey) || !TryParseMonthKey(monthKey, out var month))
            {
                return new AiToolExecutionResult
                {
                    FirmName = firm,
                    Payload = new
                    {
                        ok = false,
                        needs_clarification = true,
                        message = "Termination summary requires a month."
                    }
                };
            }

            var endedEmployees = GetAllArchivedEmployees()
                .Where(employee => !string.IsNullOrWhiteSpace(employee.EndDate))
                .Where(employee => string.IsNullOrWhiteSpace(firm) || string.Equals(NormalizeForSearch(employee.FirmName), NormalizeForSearch(firm), StringComparison.Ordinal))
                .Where(employee => MatchesMonthRange(employee.EndDate, monthKey, monthKey))
                .OrderByDescending(employee => employee.ParsedEndDate ?? ParseFlexibleDateOrMin(employee.EndDate))
                .ThenBy(employee => employee.FullName)
                .ToList();

            var latestEnded = endedEmployees.FirstOrDefault();

            return new AiToolExecutionResult
            {
                Employee = latestEnded == null ? null : new EmployeeSummary
                {
                    UniqueId = latestEnded.UniqueId,
                    FullName = latestEnded.FullName,
                    FirmName = latestEnded.FirmName,
                    PositionTitle = latestEnded.PositionTitle,
                    StartDate = latestEnded.StartDate,
                    EndDate = latestEnded.EndDate,
                    EmployeeFolder = latestEnded.EmployeeFolder,
                    Status = "Archived"
                },
                FirmName = latestEnded?.FirmName ?? firm,
                MonthKey = monthKey,
                Payload = new
                {
                    ok = endedEmployees.Count > 0,
                    month = monthKey,
                    firm,
                    ended_count = endedEmployees.Count,
                    latest_ended_employee = latestEnded == null
                        ? null
                        : new
                        {
                            full_name = latestEnded.FullName,
                            firm = latestEnded.FirmName,
                            end_date = latestEnded.EndDate
                        },
                    employees_ended = endedEmployees
                        .Take(limit)
                        .Select(employee => new
                        {
                            full_name = employee.FullName,
                            firm = employee.FirmName,
                            position = employee.PositionTitle,
                            start_date = employee.StartDate,
                            end_date = employee.EndDate
                        })
                        .ToList(),
                    formatted_summary = BuildTerminationSummaryText(monthKey, firm, endedEmployees, latestEnded),
                    message = endedEmployees.Count == 0 ? "No employees finished work in this month for the selected filter." : string.Empty
                }
            };
        }

        private AiToolExecutionResult ExecuteGetEmployeeFlowSummaryTool(JsonElement args, long? conversationUserId)
        {
            var monthKey = ResolveMonthKeyForAi(GetStringArg(args, "month"), conversationUserId);
            var firm = ResolveFirmNameForAi(GetStringArg(args, "firm"), conversationUserId);
            if (string.IsNullOrWhiteSpace(monthKey) || !TryParseMonthKey(monthKey, out var month))
            {
                return new AiToolExecutionResult
                {
                    FirmName = firm,
                    Payload = new
                    {
                        ok = false,
                        needs_clarification = true,
                        message = "Employee flow summary requires a month."
                    }
                };
            }

            var startedEmployees = GetAllEmployeeRecords(includeArchived: true, includeRecentlyDeleted: false)
                .Where(record => !string.IsNullOrWhiteSpace(record.StartDate))
                .Where(record => string.IsNullOrWhiteSpace(firm) || string.Equals(NormalizeForSearch(record.FirmName), NormalizeForSearch(firm), StringComparison.Ordinal))
                .Where(record => MatchesMonthRange(record.StartDate, monthKey, monthKey))
                .GroupBy(record => $"{record.UniqueId}|{record.StartDate}|{record.FirmName}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderByDescending(record => ParseFlexibleDateOrMin(record.StartDate))
                .ThenBy(record => record.FullName)
                .ToList();

            var endedEmployees = GetAllArchivedEmployees()
                .Where(employee => !string.IsNullOrWhiteSpace(employee.EndDate))
                .Where(employee => string.IsNullOrWhiteSpace(firm) || string.Equals(NormalizeForSearch(employee.FirmName), NormalizeForSearch(firm), StringComparison.Ordinal))
                .Where(employee => MatchesMonthRange(employee.EndDate, monthKey, monthKey))
                .OrderByDescending(employee => employee.ParsedEndDate ?? ParseFlexibleDateOrMin(employee.EndDate))
                .ThenBy(employee => employee.FullName)
                .ToList();

            var latestStarted = startedEmployees.FirstOrDefault();
            var latestEnded = endedEmployees.FirstOrDefault();

            return new AiToolExecutionResult
            {
                Employee = BuildConversationEmployeeShadow(latestStarted),
                FirmName = firm,
                MonthKey = monthKey,
                Payload = new
                {
                    ok = true,
                    month = monthKey,
                    firm,
                    started_count = startedEmployees.Count,
                    ended_count = endedEmployees.Count,
                    net_change = startedEmployees.Count - endedEmployees.Count,
                    latest_started = latestStarted == null
                        ? null
                        : new
                        {
                            unique_id = latestStarted.UniqueId,
                            full_name = latestStarted.FullName,
                            firm = latestStarted.FirmName,
                            start_date = latestStarted.StartDate
                        },
                    latest_ended = latestEnded == null
                        ? null
                        : new
                        {
                            unique_id = latestEnded.UniqueId,
                            full_name = latestEnded.FullName,
                            firm = latestEnded.FirmName,
                            end_date = latestEnded.EndDate
                        },
                    formatted_summary = BuildEmployeeFlowSummaryText(monthKey, firm, startedEmployees.Count, endedEmployees.Count, latestStarted, latestEnded)
                }
            };
        }

        private AiToolExecutionResult ExecuteGetEmployeeFlowPeriodTool(JsonElement args, long? conversationUserId)
        {
            var firm = ResolveFirmNameForAi(GetStringArg(args, "firm"), conversationUserId);
            var startMonthKey = ResolveMonthKeyForAi(GetStringArg(args, "start_month"), conversationUserId);
            var endMonthKey = ResolveMonthKeyForAi(GetStringArg(args, "end_month"), conversationUserId);
            if (string.IsNullOrWhiteSpace(startMonthKey) || string.IsNullOrWhiteSpace(endMonthKey))
            {
                return new AiToolExecutionResult
                {
                    FirmName = firm,
                    MonthKey = startMonthKey,
                    Payload = new
                    {
                        ok = false,
                        needs_clarification = true,
                        message = "Employee flow period summary requires start month and end month."
                    }
                };
            }

            if (!TryParseMonthKey(startMonthKey, out var startMonth) || !TryParseMonthKey(endMonthKey, out var endMonth))
            {
                return new AiToolExecutionResult
                {
                    FirmName = firm,
                    Payload = new
                    {
                        ok = false,
                        message = "Could not parse one of the requested months."
                    }
                };
            }

            if (startMonth > endMonth)
                (startMonth, endMonth) = (endMonth, startMonth);

            var resolvedStartMonthKey = $"{startMonth:yyyy-MM}";
            var resolvedEndMonthKey = $"{endMonth:yyyy-MM}";

            var startedEmployees = GetAllEmployeeRecords(includeArchived: true, includeRecentlyDeleted: false)
                .Where(record => !string.IsNullOrWhiteSpace(record.StartDate))
                .Where(record => string.IsNullOrWhiteSpace(firm) || string.Equals(NormalizeForSearch(record.FirmName), NormalizeForSearch(firm), StringComparison.Ordinal))
                .Where(record => MatchesMonthRange(record.StartDate, resolvedStartMonthKey, resolvedEndMonthKey))
                .GroupBy(record => $"{record.UniqueId}|{record.StartDate}|{record.FirmName}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderByDescending(record => ParseFlexibleDateOrMin(record.StartDate))
                .ThenBy(record => record.FullName)
                .ToList();

            var endedEmployees = GetAllArchivedEmployees()
                .Where(employee => !string.IsNullOrWhiteSpace(employee.EndDate))
                .Where(employee => string.IsNullOrWhiteSpace(firm) || string.Equals(NormalizeForSearch(employee.FirmName), NormalizeForSearch(firm), StringComparison.Ordinal))
                .Where(employee => MatchesMonthRange(employee.EndDate, resolvedStartMonthKey, resolvedEndMonthKey))
                .OrderByDescending(employee => employee.ParsedEndDate ?? ParseFlexibleDateOrMin(employee.EndDate))
                .ThenBy(employee => employee.FullName)
                .ToList();

            var monthBreakdown = EnumerateMonths(startMonth, endMonth)
                .Select(month =>
                {
                    var monthKey = $"{month:yyyy-MM}";
                    return new EmployeeFlowPeriodMonthInfo
                    {
                        MonthKey = monthKey,
                        MonthDisplay = $"{month:MM.yyyy}",
                        StartedCount = startedEmployees.Count(employee => MatchesMonthRange(employee.StartDate, monthKey, monthKey)),
                        EndedCount = endedEmployees.Count(employee => MatchesMonthRange(employee.EndDate, monthKey, monthKey))
                    };
                })
                .ToList();

            var byFirm = startedEmployees
                .Select(employee => employee.FirmName)
                .Concat(endedEmployees.Select(employee => employee.FirmName))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .Select(name => new EmployeeFlowPeriodFirmInfo
                {
                    FirmName = name,
                    StartedCount = startedEmployees.Count(employee => string.Equals(employee.FirmName, name, StringComparison.OrdinalIgnoreCase)),
                    EndedCount = endedEmployees.Count(employee => string.Equals(employee.FirmName, name, StringComparison.OrdinalIgnoreCase))
                })
                .ToList();

            var latestStarted = startedEmployees.FirstOrDefault();
            var latestEnded = endedEmployees.FirstOrDefault();

            return new AiToolExecutionResult
            {
                Employee = BuildConversationEmployeeShadow(latestStarted),
                FirmName = firm,
                MonthKey = $"{resolvedStartMonthKey}..{resolvedEndMonthKey}",
                Payload = new
                {
                    ok = true,
                    start_month = resolvedStartMonthKey,
                    end_month = resolvedEndMonthKey,
                    firm,
                    started_total = startedEmployees.Count,
                    ended_total = endedEmployees.Count,
                    net_change = startedEmployees.Count - endedEmployees.Count,
                    months = monthBreakdown.Select(item => new
                    {
                        month = item.MonthKey,
                        month_display = item.MonthDisplay,
                        started_count = item.StartedCount,
                        ended_count = item.EndedCount,
                        net_change = item.NetChange
                    }).ToList(),
                    by_firm = byFirm.Select(item => new
                    {
                        firm = item.FirmName,
                        started_count = item.StartedCount,
                        ended_count = item.EndedCount,
                        net_change = item.NetChange
                    }).ToList(),
                    latest_started = latestStarted == null
                        ? null
                        : new
                        {
                            unique_id = latestStarted.UniqueId,
                            full_name = latestStarted.FullName,
                            firm = latestStarted.FirmName,
                            start_date = latestStarted.StartDate
                        },
                    latest_ended = latestEnded == null
                        ? null
                        : new
                        {
                            unique_id = latestEnded.UniqueId,
                            full_name = latestEnded.FullName,
                            firm = latestEnded.FirmName,
                            end_date = latestEnded.EndDate
                        },
                    formatted_summary = BuildEmployeeFlowPeriodText(firm, startMonth, endMonth, monthBreakdown, byFirm, latestStarted, latestEnded),
                    message = startedEmployees.Count == 0 && endedEmployees.Count == 0 ? "No employee flow found for the selected period." : string.Empty
                }
            };
        }

        private AiToolExecutionResult ExecuteGetProgramHelpTool(JsonElement args)
        {
            var topicQuery = GetStringArg(args, "topic");
            var limit = Math.Max(1, Math.Min(6, GetIntArg(args, "limit", 3)));
            var topics = TelegramProgramKnowledge.Search(topicQuery, limit);

            return new AiToolExecutionResult
            {
                Payload = new
                {
                    ok = topics.Count > 0,
                    topic = topicQuery,
                    help_topics = topics.Select(topic => new
                    {
                        title = topic.Title,
                        summary = topic.Summary,
                        when_to_use = topic.WhenToUse,
                        how_to_open = topic.HowToOpen,
                        common_tasks = topic.CommonTasks,
                        steps = topic.Steps,
                        related_topics = topic.RelatedTopics,
                        formatted_guide = BuildProgramHelpCard(topic)
                    }).ToList(),
                    message = topics.Count == 0 ? "Program help topic not found." : string.Empty
                }
            };
        }

        private AiToolExecutionResult ExecuteListProgramCapabilitiesTool()
        {
            return new AiToolExecutionResult
            {
                Payload = new
                {
                    ok = true,
                    capabilities = TelegramProgramKnowledge.GetCapabilities(),
                    quick_start = TelegramProgramKnowledge.GetQuickStartTips(),
                    modules = TelegramProgramKnowledge.GetTopics()
                        .Select(topic => topic.Title)
                        .ToList(),
                    formatted_overview = BuildProgramCapabilitiesText()
                }
            };
        }

        private async Task<AiToolExecutionResult> ExecuteGetExternalUpdatesToolAsync(JsonElement args, CancellationToken cancellationToken)
        {
            var query = NormalizeForSearch(GetStringArg(args, "query"));
            var limit = Math.Max(1, Math.Min(10, GetIntArg(args, "limit", 5)));
            var articles = await _newsService.GetLatestArticlesAsync(forceRefresh: false, cancellationToken).ConfigureAwait(false);

            var filtered = articles
                .Where(article => MatchesExternalUpdateQuery(article, query))
                .OrderByDescending(article => article.IsImportant)
                .ThenByDescending(article => article.PublishedAtUtc)
                .Take(limit)
                .Select(article => new
                {
                    title = article.Title,
                    summary = article.Summary,
                    source = article.SourceName,
                    published_at_utc = article.PublishedAtUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    is_important = article.IsImportant,
                    url = article.Url
                })
                .ToList();

            return new AiToolExecutionResult
            {
                Payload = new
                {
                    ok = filtered.Count > 0,
                    query,
                    source_note = "These are external public updates from configured official/news feeds. Keep them separate from local program data.",
                    updates = filtered,
                    message = filtered.Count == 0 ? "No matching external updates found." : string.Empty
                }
            };
        }

        private AiToolExecutionResult ExecuteSearchEverythingTool(JsonElement args, long? conversationUserId)
        {
            var query = GetStringArg(args, "query");
            var limit = Math.Max(1, Math.Min(30, GetIntArg(args, "limit", 12)));
            if (string.IsNullOrWhiteSpace(query))
            {
                return new AiToolExecutionResult
                {
                    Payload = new
                    {
                        ok = false,
                        message = "Search query is empty."
                    }
                };
            }

            var normalizedQuery = NormalizeForSearch(query);
            var employeeMatches = FindEmployees(query, limit, conversationUserId, allowContextFallback: false)
                .Select(employee => new
                {
                    kind = "employee",
                    score_hint = "strong",
                    full_name = employee.FullName,
                    firm = employee.FirmName,
                    phone = employee.Phone,
                    passport_number = employee.PassportNumber,
                    insurance_number = employee.InsuranceNumber
                })
                .Cast<object>()
                .ToList();

            var firmMatches = _companyService.VisibleCompanies
                .Where(company =>
                    NormalizeForSearch(company.Name).Contains(normalizedQuery, StringComparison.Ordinal)
                    || normalizedQuery.Contains(NormalizeForSearch(company.Name), StringComparison.Ordinal))
                .Take(limit)
                .Select(company => new
                {
                    kind = "firm",
                    name = company.Name,
                    ico = company.ICO,
                    legal_address = company.LegalAddress
                })
                .Cast<object>()
                .ToList();

            var deepMatches = GetAllEmployees()
                .Select(employee => new
                {
                    Employee = employee,
                    Data = _employeeService.LoadEmployeeData(employee.EmployeeFolder)
                })
                .Where(item => item.Data != null)
                .Where(item =>
                    ContainsSearchValue(item.Employee.UniqueId, normalizedQuery)
                    || ContainsSearchValue(item.Employee.Phone, normalizedQuery)
                    || ContainsSearchValue(item.Employee.Email, normalizedQuery)
                    || ContainsSearchValue(item.Employee.BankAccountNumber, normalizedQuery)
                    || ContainsSearchValue(item.Data!.InsuranceNumber, normalizedQuery)
                    || ContainsSearchValue(item.Data.PassportNumber, normalizedQuery)
                    || ContainsSearchValue(item.Data.VisaNumber, normalizedQuery)
                    || ContainsSearchValue(item.Data.WorkPermitNumber, normalizedQuery)
                    || ContainsSearchValue(FormatAddress(item.Data.AddressLocal), normalizedQuery)
                    || ContainsSearchValue(FormatAddress(item.Data.AddressAbroad), normalizedQuery))
                .Take(limit)
                .Select(item => new
                {
                    kind = "employee_detail",
                    full_name = item.Employee.FullName,
                    firm = item.Employee.FirmName,
                    phone = item.Employee.Phone,
                    email = item.Employee.Email,
                    bank_account = item.Employee.BankAccountNumber,
                    insurance_number = item.Data!.InsuranceNumber,
                    passport_number = item.Data.PassportNumber,
                    visa_number = item.Data.VisaNumber,
                    work_permit_number = item.Data.WorkPermitNumber,
                    local_address = FormatAddress(item.Data.AddressLocal)
                })
                .Cast<object>()
                .ToList();

            var archivedMatches = GetAllArchivedEmployees()
                .Where(item =>
                    ContainsSearchValue(item.UniqueId, normalizedQuery)
                    || ContainsSearchValue(item.FullName, normalizedQuery)
                    || ContainsSearchValue(item.FirmName, normalizedQuery)
                    || ContainsSearchValue(item.EndDate, normalizedQuery))
                .Take(limit)
                .Select(item => new
                {
                    kind = "archived_employee",
                    full_name = item.FullName,
                    firm = item.FirmName,
                    start_date = item.StartDate,
                    end_date = item.EndDate
                })
                .Cast<object>()
                .ToList();

            var recentlyDeletedMatches = GetAllRecentlyDeletedItems()
                .Where(item =>
                    ContainsSearchValue(item.UniqueId, normalizedQuery)
                    || ContainsSearchValue(item.FullName, normalizedQuery)
                    || ContainsSearchValue(item.FirmName, normalizedQuery))
                .Take(limit)
                .Select(item => new
                {
                    kind = "recently_deleted_employee",
                    full_name = item.FullName,
                    firm = item.FirmName,
                    deleted_at_utc = item.DeletedAtUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                })
                .Cast<object>()
                .ToList();

            return new AiToolExecutionResult
            {
                Payload = new
                {
                    ok = true,
                    query,
                    results = employeeMatches
                        .Concat(firmMatches)
                        .Concat(deepMatches)
                        .Concat(archivedMatches)
                        .Concat(recentlyDeletedMatches)
                        .Take(limit * 2)
                        .ToList()
                }
            };
        }

        private object BuildEmployeeToolPayload(EmployeeSummary employee, EmployeeData data)
        {
            return new
            {
                full_name = employee.FullName,
                firm = employee.FirmName,
                position = employee.PositionTitle,
                status = employee.Status,
                employee_type = employee.EmployeeType,
                start_date = data.StartDate,
                end_date = data.EndDate,
                phone = data.Phone,
                email = data.Email,
                bank_account_number = data.BankAccountNumber,
                bank_name = data.BankName,
                address_local = FormatAddress(data.AddressLocal),
                address_abroad = FormatAddress(data.AddressAbroad),
                work_address = data.WorkAddressTag,
                documents = new
                {
                    passport_number = data.PassportNumber,
                    passport_expiry = data.PassportExpiry,
                    visa_number = data.VisaNumber,
                    visa_type = data.VisaType,
                    visa_expiry = data.VisaExpiry,
                    insurance_company = data.InsuranceCompanyShort,
                    insurance_number = data.InsuranceNumber,
                    insurance_expiry = data.InsuranceExpiry,
                    work_permit_name = data.WorkPermitName,
                    work_permit_number = data.WorkPermitNumber,
                    work_permit_expiry = data.WorkPermitExpiry,
                    has_passport_file = HasEmployeeDocumentFile(employee.EmployeeFolder, data, "passport"),
                    has_visa_file = HasEmployeeDocumentFile(employee.EmployeeFolder, data, "visa"),
                    has_insurance_file = HasEmployeeDocumentFile(employee.EmployeeFolder, data, "insurance"),
                    has_work_permit_file = HasEmployeeDocumentFile(employee.EmployeeFolder, data, "work_permit")
                },
                firm_history = data.FirmHistory.Select(history => new
                {
                    firm = history.FirmName,
                    start_date = history.StartDate,
                    end_date = history.EndDate
                }).ToList(),
                custom_documents = data.CustomDocuments.Select(doc => new
                {
                    name = doc.Name,
                    sign_date = doc.SignDate,
                    expiry_date = doc.ExpiryDate,
                    is_hidden = doc.IsHidden
                }).ToList(),
                is_archived = data.IsArchived,
                archived_from_firm = data.ArchivedFromFirm
            };
        }

        private object BuildEmployeeToolPayload(EmployeeLookupResult employee, EmployeeData data)
        {
            return new
            {
                full_name = employee.FullName,
                firm = employee.FirmName,
                position = employee.PositionTitle,
                status = data.Status,
                source = employee.Source,
                employee_type = employee.ActiveEmployee?.EmployeeType ?? "unknown",
                start_date = data.StartDate,
                end_date = data.EndDate,
                phone = data.Phone,
                email = data.Email,
                bank_account_number = data.BankAccountNumber,
                bank_name = data.BankName,
                address_local = FormatAddress(data.AddressLocal),
                address_abroad = FormatAddress(data.AddressAbroad),
                work_address = data.WorkAddressTag,
                documents = new
                {
                    passport_number = data.PassportNumber,
                    passport_expiry = data.PassportExpiry,
                    visa_number = data.VisaNumber,
                    visa_type = data.VisaType,
                    visa_expiry = data.VisaExpiry,
                    insurance_company = data.InsuranceCompanyShort,
                    insurance_number = data.InsuranceNumber,
                    insurance_expiry = data.InsuranceExpiry,
                    work_permit_name = data.WorkPermitName,
                    work_permit_number = data.WorkPermitNumber,
                    work_permit_expiry = data.WorkPermitExpiry,
                    has_passport_file = HasEmployeeDocumentFile(employee.EmployeeFolder, data, "passport"),
                    has_visa_file = HasEmployeeDocumentFile(employee.EmployeeFolder, data, "visa"),
                    has_insurance_file = HasEmployeeDocumentFile(employee.EmployeeFolder, data, "insurance"),
                    has_work_permit_file = HasEmployeeDocumentFile(employee.EmployeeFolder, data, "work_permit")
                },
                firm_history = data.FirmHistory.Select(history => new
                {
                    firm = history.FirmName,
                    start_date = history.StartDate,
                    end_date = history.EndDate
                }).ToList(),
                custom_documents = data.CustomDocuments.Select(doc => new
                {
                    name = doc.Name,
                    sign_date = doc.SignDate,
                    expiry_date = doc.ExpiryDate,
                    is_hidden = doc.IsHidden
                }).ToList(),
                is_archived = data.IsArchived,
                archived_from_firm = data.ArchivedFromFirm,
                recently_deleted_at = employee.DeletedAtUtc?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            };
        }

        private static EmployeeSummary? BuildConversationEmployeeShadow(EmployeeLookupResult? employee)
        {
            if (employee == null)
                return null;

            return employee.ActiveEmployee ?? new EmployeeSummary
            {
                UniqueId = employee.UniqueId,
                FullName = employee.FullName,
                FirmName = employee.FirmName,
                PositionTitle = employee.PositionTitle,
                StartDate = employee.StartDate,
                EndDate = employee.EndDate,
                EmployeeFolder = employee.EmployeeFolder,
                Status = employee.StatusLabel
            };
        }

        private string ResolveFirmNameForAi(string firmQuery, long? conversationUserId)
        {
            if (!string.IsNullOrWhiteSpace(firmQuery))
            {
                var firmMatches = FindFirmMatches(firmQuery, 1);
                if (firmMatches.Count > 0)
                    return firmMatches[0].Company.Name;

                var exact = _companyService.VisibleCompanies.FirstOrDefault(company =>
                    string.Equals(company.Name, firmQuery.Trim(), StringComparison.OrdinalIgnoreCase));
                if (exact != null)
                    return exact.Name;

                return string.Empty;
            }

            return conversationUserId.HasValue
                ? GetConversationContext(conversationUserId.Value)?.LastFirmName ?? string.Empty
                : string.Empty;
        }

        private string ResolveMonthKeyForAi(string monthQuery, long? conversationUserId)
        {
            if (!string.IsNullOrWhiteSpace(monthQuery))
            {
                if (TryExtractMonthKey(monthQuery, out var monthKey))
                    return monthKey;
                if (TryParseMonthKey(monthQuery, out var month))
                    return $"{month.Year:D4}-{month.Month:D2}";
            }

            return conversationUserId.HasValue
                ? GetConversationContext(conversationUserId.Value)?.LastMonthKey ?? string.Empty
                : string.Empty;
        }

        private static bool TrySelectSingleEmployee(string query, IReadOnlyList<EmployeeSummary> candidates, out EmployeeSummary? employee)
        {
            employee = null;
            if (candidates == null || candidates.Count == 0)
                return false;

            if (candidates.Count == 1)
            {
                employee = candidates[0];
                return true;
            }

            var normalizedQuery = NormalizeForSearch(query);
            employee = candidates.FirstOrDefault(candidate =>
                string.Equals(NormalizeForSearch(candidate.FullName), normalizedQuery, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.UniqueId, query, StringComparison.OrdinalIgnoreCase));

            return employee != null;
        }

        private List<(string employee, string firm, string type, string date, int daysLeft)> BuildExpiringItems(int days)
        {
            var expiring = new List<(string employee, string firm, string type, string date, int daysLeft)>();
            foreach (var employee in GetAllEmployees())
            {
                AddIfExpiring(employee, "паспорт", employee.PassportExpiry, days, expiring);
                AddIfExpiring(employee, "віза", employee.VisaExpiry, days, expiring);
                AddIfExpiring(employee, "страхування", employee.InsuranceExpiry, days, expiring);
                AddIfExpiring(employee, "дозвіл на роботу", employee.WorkPermitExpiry, days, expiring);
            }

            return expiring
                .OrderBy(item => item.daysLeft)
                .ThenBy(item => item.employee)
                .ToList();
        }

        private static JsonDocument ParseToolArguments(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return JsonDocument.Parse("{}");

            try
            {
                return JsonDocument.Parse(json);
            }
            catch
            {
                return JsonDocument.Parse("{}");
            }
        }

        private static string GetStringArg(JsonElement args, string name)
        {
            if (!args.TryGetProperty(name, out var value))
                return string.Empty;

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? string.Empty,
                JsonValueKind.Number => value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => string.Empty
            };
        }

        private static int GetIntArg(JsonElement args, string name, int defaultValue)
        {
            if (!args.TryGetProperty(name, out var value))
                return defaultValue;

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result))
                return result;

            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed))
                return parsed;

            return defaultValue;
        }

        private static bool ContainsSearchValue(string value, string normalizedQuery)
        {
            return !string.IsNullOrWhiteSpace(value)
                   && NormalizeForSearch(value).Contains(normalizedQuery, StringComparison.Ordinal);
        }

        private static bool MatchesExternalUpdateQuery(NewsArticle article, string normalizedQuery)
        {
            if (string.IsNullOrWhiteSpace(normalizedQuery))
                return true;

            var combined = NormalizeForSearch($"{article.Title} {article.Summary} {article.SourceName}");
            return combined.Contains(normalizedQuery, StringComparison.Ordinal)
                   || normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                       .All(token => combined.Contains(token, StringComparison.Ordinal));
        }

        private EmployerCompany? FindVisibleCompanyByName(string firmName)
        {
            return _companyService.VisibleCompanies.FirstOrDefault(company =>
                string.Equals(NormalizeForSearch(company.Name), NormalizeForSearch(firmName), StringComparison.Ordinal));
        }

        private static string FormatWorkAddress(WorkAddress address)
        {
            if (address == null)
                return string.Empty;

            var parts = new[]
            {
                address.Street,
                address.Number,
                address.City,
                address.ZipCode
            }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToList();

            return string.Join(", ", parts);
        }

        private static string BuildCompanyProfileText(EmployerCompany company, int employeeCount, IReadOnlyList<string> workAddresses, int positionCount, string hiddenFrom)
        {
            var builder = new StringBuilder()
                .AppendLine($"Профіль фірми {company.Name}:")
                .AppendLine($"• ICO: {(string.IsNullOrWhiteSpace(company.ICO) ? "не вказано" : company.ICO)}")
                .AppendLine($"• Юридична адреса: {(string.IsNullOrWhiteSpace(company.LegalAddress) ? "не вказано" : company.LegalAddress)}")
                .AppendLine($"• Активних працівників: {employeeCount}")
                .AppendLine($"• Годин на тиждень: {company.WeeklyWorkHours:N2}")
                .AppendLine($"• Годин на день: {company.DailyWorkHours:N2}")
                .AppendLine($"• Змін: {company.ShiftCount}")
                .AppendLine($"• Робочих адрес: {workAddresses.Count}")
                .AppendLine($"• Позицій: {positionCount}");

            if (!string.IsNullOrWhiteSpace(hiddenFrom))
                builder.AppendLine($"• Приховано з місяця: {hiddenFrom}");

            if (company.Agency != null && (!string.IsNullOrWhiteSpace(company.Agency.Name) || !string.IsNullOrWhiteSpace(company.Agency.ICO)))
            {
                builder.AppendLine($"• Агенція: {company.Agency.Name} | ICO: {company.Agency.ICO}");
            }

            return builder.ToString().TrimEnd();
        }

        private static string BuildProgramHelpCard(ProgramHelpTopic topic)
        {
            var builder = new StringBuilder()
                .AppendLine(topic.Title)
                .AppendLine($"Що це: {topic.Summary}")
                .AppendLine($"Коли використовувати: {topic.WhenToUse}")
                .AppendLine($"Як відкрити: {topic.HowToOpen}")
                .AppendLine("Основні дії:");

            foreach (var task in topic.CommonTasks.Take(4))
                builder.AppendLine($"• {task}");

            builder.AppendLine("Кроки:");
            for (var i = 0; i < topic.Steps.Length; i++)
                builder.AppendLine($"{i + 1}. {topic.Steps[i]}");

            if (topic.RelatedTopics.Length > 0)
                builder.AppendLine($"Суміжні модулі: {string.Join(", ", topic.RelatedTopics)}");

            return builder.ToString().TrimEnd();
        }

        private static string BuildProgramCapabilitiesText()
        {
            var builder = new StringBuilder()
                .AppendLine("Що вміє програма:")
                .AppendLine(string.Join("\n", TelegramProgramKnowledge.GetCapabilities().Select(item => $"• {item}")))
                .AppendLine("Швидкий старт:")
                .AppendLine(string.Join("\n", TelegramProgramKnowledge.GetQuickStartTips().Select((item, index) => $"{index + 1}. {item}")));

            return builder.ToString().TrimEnd();
        }

        private static string BuildEmployeeStatusOverviewText(EmployeeLookupResult employee, EmployeeData? data, RecentlyDeletedItem? recentlyDeleted, int salaryHistoryCount)
        {
            var builder = new StringBuilder()
                .AppendLine($"Статус працівника {employee.FullName}:")
                .AppendLine($"• Джерело: {employee.Source}")
                .AppendLine($"• Фірма: {employee.FirmName}")
                .AppendLine($"• Початок роботи: {data?.StartDate ?? employee.StartDate}")
                .AppendLine($"• Кінець роботи: {data?.EndDate ?? employee.EndDate}")
                .AppendLine($"• Архів: {(data?.IsArchived == true || employee.IsArchived ? "так" : "ні")}");

            if (!string.IsNullOrWhiteSpace(data?.ArchivedFromFirm))
                builder.AppendLine($"• Архівовано з фірми: {data.ArchivedFromFirm}");
            if (recentlyDeleted != null)
                builder.AppendLine($"• Недавно видалений: {recentlyDeleted.DeletedAtUtc:yyyy-MM-dd HH:mm} UTC");
            if (salaryHistoryCount > 0)
                builder.AppendLine($"• Місяців у зарплатній історії: {salaryHistoryCount}");

            return builder.ToString().TrimEnd();
        }

        private static List<object> BuildEmploymentTimelineItems(EmployeeLookupResult employee, EmployeeData? data)
        {
            var items = new List<object>();

            if (!string.IsNullOrWhiteSpace(data?.StartDate ?? employee.StartDate))
            {
                items.Add(new
                {
                    type = "employment_start",
                    sort_key = data?.StartDate ?? employee.StartDate,
                    title = "Початок роботи",
                    details = $"{employee.FirmName}: {data?.StartDate ?? employee.StartDate}"
                });
            }

            if (data?.FirmHistory != null)
            {
                items.AddRange(data.FirmHistory
                    .OrderByDescending(item => item.StartDate)
                    .Select(item => (object)new
                    {
                        type = "firm_history",
                        sort_key = item.StartDate,
                        title = $"Робота у {item.FirmName}",
                        details = $"{item.StartDate} - {(string.IsNullOrWhiteSpace(item.EndDate) ? "дотепер" : item.EndDate)}"
                    }));
            }

            if (!string.IsNullOrWhiteSpace(data?.EndDate ?? employee.EndDate))
            {
                items.Add(new
                {
                    type = "employment_end",
                    sort_key = data?.EndDate ?? employee.EndDate,
                    title = "Завершення роботи",
                    details = $"{employee.FirmName}: {data?.EndDate ?? employee.EndDate}"
                });
            }

            if (data?.IsArchived == true || employee.IsArchived)
            {
                items.Add(new
                {
                    type = "archived",
                    sort_key = data?.EndDate ?? employee.EndDate,
                    title = "Архів",
                    details = string.IsNullOrWhiteSpace(data?.ArchivedFromFirm) ? employee.FirmName : data!.ArchivedFromFirm
                });
            }

            if (employee.RecentlyDeletedItem != null)
            {
                items.Add(new
                {
                    type = "recently_deleted",
                    sort_key = employee.RecentlyDeletedItem.DeletedAtUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    title = "Недавно видалений",
                    details = $"{employee.RecentlyDeletedItem.DeletedAtUtc:yyyy-MM-dd HH:mm} UTC"
                });
            }

            return items;
        }

        private static string BuildEmployeeTimelineText(EmployeeLookupResult employee, EmployeeData? data, int historyCount, int salaryCount)
        {
            var builder = new StringBuilder()
                .AppendLine($"Таймлайн {employee.FullName}:")
                .AppendLine($"• Поточний або останній стан: {employee.StatusLabel}")
                .AppendLine($"• Фірма: {employee.FirmName}")
                .AppendLine($"• Початок: {data?.StartDate ?? employee.StartDate}")
                .AppendLine($"• Кінець: {data?.EndDate ?? employee.EndDate}");

            if (data?.FirmHistory?.Count > 0)
                builder.AppendLine($"• Записів у історії фірм: {data.FirmHistory.Count}");
            if (historyCount > 0)
                builder.AppendLine($"• Останніх подій історії: {historyCount}");
            if (salaryCount > 0)
                builder.AppendLine($"• Останніх зарплатних подій: {salaryCount}");

            return builder.ToString().TrimEnd();
        }

        private static string BuildEmployeeFullSummaryText(EmployeeLookupResult employee, EmployeeData data, SalaryInfo? latestSalary, SalaryInfo? latestPaidSalary, int historyCount, int employmentEventCount)
        {
            var builder = new StringBuilder()
                .AppendLine($"Повна зведена картка {employee.FullName}:")
                .AppendLine($"• Статус: {data.Status}")
                .AppendLine($"• Джерело: {employee.Source}")
                .AppendLine($"• Фірма: {employee.FirmName}")
                .AppendLine($"• Посада: {employee.PositionTitle}")
                .AppendLine($"• Початок роботи: {data.StartDate}")
                .AppendLine($"• Кінець роботи: {(string.IsNullOrWhiteSpace(data.EndDate) ? "не вказано" : data.EndDate)}");

            if (!string.IsNullOrWhiteSpace(data.Phone))
                builder.AppendLine($"• Телефон: {data.Phone}");
            if (!string.IsNullOrWhiteSpace(data.Email))
                builder.AppendLine($"• Email: {data.Email}");

            builder.AppendLine($"• Віза до: {FormatExpiry(data.VisaExpiry)}")
                .AppendLine($"• Страховка до: {FormatExpiry(data.InsuranceExpiry)}")
                .AppendLine($"• Дозвіл на роботу до: {FormatExpiry(data.WorkPermitExpiry)}");

            if (latestSalary != null)
            {
                builder.AppendLine($"• Поточна зарплата: {latestSalary.Record.MonthDisplay}, нетто {latestSalary.Record.NetSalary:N2} CZK, брутто {latestSalary.Record.GrossSalary:N2} CZK, статус: {FormatPaymentStatus(latestSalary)}");
            }
            else
            {
                builder.AppendLine("• Поточна зарплата: даних немає");
            }

            if (latestPaidSalary != null)
                builder.AppendLine($"• Остання виплачена зарплата: {latestPaidSalary.Record.MonthDisplay}, нетто {latestPaidSalary.Record.NetSalary:N2} CZK, брутто {latestPaidSalary.Record.GrossSalary:N2} CZK, {FormatPaymentStatus(latestPaidSalary)}");
            else
                builder.AppendLine("• Остання виплачена зарплата: даних немає");

            if (data.FirmHistory.Count > 0)
                builder.AppendLine($"• Записів історії фірм: {data.FirmHistory.Count}");
            if (employmentEventCount > 0)
                builder.AppendLine($"• Подій у таймлайні: {employmentEventCount}");
            if (historyCount > 0)
                builder.AppendLine($"• Останніх змін у історії: {historyCount}");

            if (data.IsArchived)
                builder.AppendLine($"• Архівовано з фірми: {data.ArchivedFromFirm}");
            if (employee.DeletedAtUtc.HasValue)
                builder.AppendLine($"• Недавно видалений: {employee.DeletedAtUtc.Value:yyyy-MM-dd HH:mm} UTC");

            return builder.ToString().TrimEnd();
        }

        private static string BuildTopPayoutsText(string monthKey, string firm, string sortBy, IReadOnlyList<(int rank, string employee, string firm, decimal gross, decimal net)> ranking)
        {
            var builder = new StringBuilder()
                .AppendLine(string.IsNullOrWhiteSpace(firm)
                    ? $"Топ виплат за {monthKey} по всіх фірмах:"
                    : $"Топ виплат за {monthKey} по фірмі {firm}:")
                .AppendLine($"• Сортування: {(sortBy == "gross" ? "брутто" : "нетто")}");

            foreach (var item in ranking.Take(10))
                builder.AppendLine($"• #{item.rank} {item.employee} ({item.firm}) | нетто {item.net:N2} CZK | брутто {item.gross:N2} CZK");

            return builder.ToString().TrimEnd();
        }

        private static string BuildHiringSummaryText(string monthKey, string firm, IReadOnlyList<EmployeeLookupResult> startedEmployees, EmployeeLookupResult? latestStarted)
        {
            var builder = new StringBuilder()
                .AppendLine(string.IsNullOrWhiteSpace(firm)
                    ? $"Нові працівники за {monthKey} по всіх фірмах:"
                    : $"Нові працівники за {monthKey} по фірмі {firm}:")
                .AppendLine($"• Кількість нових: {startedEmployees.Count}");

            if (latestStarted != null)
                builder.AppendLine($"• Останній почав: {latestStarted.FullName} ({latestStarted.FirmName}) - {latestStarted.StartDate}");

            foreach (var employee in startedEmployees.Take(10))
                builder.AppendLine($"• {employee.FullName} | {employee.FirmName} | початок {employee.StartDate}");

            return builder.ToString().TrimEnd();
        }

        private static string BuildTerminationSummaryText(string monthKey, string firm, IReadOnlyList<ArchivedEmployeeSummary> endedEmployees, ArchivedEmployeeSummary? latestEnded)
        {
            var builder = new StringBuilder()
                .AppendLine(string.IsNullOrWhiteSpace(firm)
                    ? $"Завершили роботу за {monthKey} по всіх фірмах:"
                    : $"Завершили роботу за {monthKey} по фірмі {firm}:")
                .AppendLine($"• Кількість завершень: {endedEmployees.Count}");

            if (latestEnded != null)
                builder.AppendLine($"• Останній закінчив: {latestEnded.FullName} ({latestEnded.FirmName}) - {latestEnded.EndDate}");

            foreach (var employee in endedEmployees.Take(10))
                builder.AppendLine($"• {employee.FullName} | {employee.FirmName} | завершив {employee.EndDate}");

            return builder.ToString().TrimEnd();
        }

        private static string BuildEmployeeFlowSummaryText(string monthKey, string firm, int startedCount, int endedCount, EmployeeLookupResult? latestStarted, ArchivedEmployeeSummary? latestEnded)
        {
            var builder = new StringBuilder()
                .AppendLine(string.IsNullOrWhiteSpace(firm)
                    ? $"Рух працівників за {monthKey} по всіх фірмах:"
                    : $"Рух працівників за {monthKey} по фірмі {firm}:")
                .AppendLine($"• Нових: {startedCount}")
                .AppendLine($"• Завершили: {endedCount}")
                .AppendLine($"• Чиста зміна: {(startedCount - endedCount):+0;-0;0}");

            if (latestStarted != null)
                builder.AppendLine($"• Останній початок: {latestStarted.FullName} ({latestStarted.FirmName}) - {latestStarted.StartDate}");

            if (latestEnded != null)
                builder.AppendLine($"• Останнє завершення: {latestEnded.FullName} ({latestEnded.FirmName}) - {latestEnded.EndDate}");

            return builder.ToString().TrimEnd();
        }

        private static string BuildEmployeeFlowPeriodText(
            string firm,
            DateTime startMonth,
            DateTime endMonth,
            IReadOnlyList<EmployeeFlowPeriodMonthInfo> monthBreakdown,
            IReadOnlyList<EmployeeFlowPeriodFirmInfo> byFirm,
            EmployeeLookupResult? latestStarted,
            ArchivedEmployeeSummary? latestEnded)
        {
            var startedTotal = monthBreakdown.Sum(item => item.StartedCount);
            var endedTotal = monthBreakdown.Sum(item => item.EndedCount);
            var builder = new StringBuilder()
                .AppendLine(string.IsNullOrWhiteSpace(firm)
                    ? $"Рух працівників за період {startMonth:MM.yyyy} - {endMonth:MM.yyyy} по всіх фірмах:"
                    : $"Рух працівників за період {startMonth:MM.yyyy} - {endMonth:MM.yyyy} по фірмі {firm}:")
                .AppendLine($"• Нових: {startedTotal}")
                .AppendLine($"• Завершили: {endedTotal}")
                .AppendLine($"• Чиста зміна: {(startedTotal - endedTotal):+0;-0;0}");

            if (latestStarted != null)
                builder.AppendLine($"• Останній початок: {latestStarted.FullName} ({latestStarted.FirmName}) - {latestStarted.StartDate}");

            if (latestEnded != null)
                builder.AppendLine($"• Останнє завершення: {latestEnded.FullName} ({latestEnded.FirmName}) - {latestEnded.EndDate}");

            if (monthBreakdown.Count > 0)
            {
                builder.AppendLine("По місяцях:");
                foreach (var item in monthBreakdown.Take(12))
                    builder.AppendLine($"• {item.MonthDisplay}: +{item.StartedCount} / -{item.EndedCount} = {item.NetChange:+0;-0;0}");
            }

            if (string.IsNullOrWhiteSpace(firm) && byFirm.Count > 0)
            {
                builder.AppendLine("По фірмах:");
                foreach (var item in byFirm.Take(8))
                    builder.AppendLine($"• {item.FirmName}: +{item.StartedCount} / -{item.EndedCount} = {item.NetChange:+0;-0;0}");
            }

            return builder.ToString().TrimEnd();
        }

        private static bool MatchesMonthRange(string dateValue, string? startMonthKey, string? endMonthKey)
        {
            if (!TryParseFlexibleDate(dateValue, out var date))
                return string.IsNullOrWhiteSpace(startMonthKey) && string.IsNullOrWhiteSpace(endMonthKey);

            if (!string.IsNullOrWhiteSpace(startMonthKey) && TryParseMonthKey(startMonthKey, out var startMonth))
            {
                var start = new DateTime(startMonth.Year, startMonth.Month, 1);
                if (date < start)
                    return false;
            }

            if (!string.IsNullOrWhiteSpace(endMonthKey) && TryParseMonthKey(endMonthKey, out var endMonth))
            {
                var end = new DateTime(endMonth.Year, endMonth.Month, DateTime.DaysInMonth(endMonth.Year, endMonth.Month));
                if (date > end)
                    return false;
            }

            return true;
        }

        private static bool TryParseFlexibleDate(string? value, out DateTime date)
        {
            if (DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out date))
                return true;

            var formats = new[] { "dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy" };
            return DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out date);
        }

        private static DateTime ParseFlexibleDateOrMin(string? value)
        {
            return TryParseFlexibleDate(value, out var date)
                ? date
                : DateTime.MinValue;
        }

        private static int CalculateArchivedEmployeeScore(ArchivedEmployeeSummary employee, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return 1;

            var shadow = new EmployeeSummary
            {
                UniqueId = employee.UniqueId,
                FullName = employee.FullName,
                FirmName = employee.FirmName,
                PositionTitle = employee.PositionTitle,
                StartDate = employee.StartDate,
                EndDate = employee.EndDate
            };

            return CalculateEmployeeScore(shadow, query);
        }

        private static List<DateTime> EnumerateMonths(DateTime startMonth, DateTime endMonth)
        {
            var result = new List<DateTime>();
            var cursor = new DateTime(startMonth.Year, startMonth.Month, 1);
            var end = new DateTime(endMonth.Year, endMonth.Month, 1);

            while (cursor <= end)
            {
                result.Add(cursor);
                cursor = cursor.AddMonths(1);
            }

            return result;
        }

        private static string BuildFirmPeriodSummaryText(string firmName, DateTime startMonth, DateTime endMonth, IReadOnlyList<FirmSalaryInfo> months)
        {
            var builder = new StringBuilder()
                .AppendLine($"Період по фірмі {firmName}: {startMonth:MM.yyyy} - {endMonth:MM.yyyy}")
                .AppendLine($"• Місяців з даними: {months.Count}")
                .AppendLine($"• Загальне брутто: {months.Sum(item => item.TotalGross):N2} CZK")
                .AppendLine($"• Загальне нетто: {months.Sum(item => item.TotalNet):N2} CZK")
                .AppendLine($"• Загальні аванси: {months.Sum(item => item.TotalAdvances):N2} CZK")
                .AppendLine($"• Загальні витрати: {months.Sum(item => item.TotalExpenses):N2} CZK")
                .AppendLine("По місяцях:");

            foreach (var month in months.OrderBy(item => item.Year).ThenBy(item => item.Month))
                builder.AppendLine($"• {month.MonthDisplay}: нетто {month.TotalNet:N2} CZK | брутто {month.TotalGross:N2} CZK | витрати {month.TotalExpenses:N2} CZK | працівників {month.Entries.Count}");

            return builder.ToString().TrimEnd();
        }

        private static object BuildSalaryComparisonPayload(
            string scope,
            string subject,
            string monthAKey,
            decimal grossA,
            decimal netA,
            decimal advancesA,
            decimal expensesA,
            int paidA,
            int pendingA,
            string monthBDisplay,
            string monthBKey,
            decimal grossB,
            decimal netB,
            decimal advancesB,
            decimal expensesB,
            int paidB,
            int pendingB,
            string monthADisplay,
            string formattedText)
        {
            return new
            {
                scope,
                subject,
                month_a = new
                {
                    month = monthAKey,
                    month_display = monthADisplay,
                    total_gross = grossA,
                    total_net = netA,
                    total_advances = advancesA,
                    total_expenses = expensesA,
                    paid_count = paidA,
                    pending_count = pendingA
                },
                month_b = new
                {
                    month = monthBKey,
                    month_display = monthBDisplay,
                    total_gross = grossB,
                    total_net = netB,
                    total_advances = advancesB,
                    total_expenses = expensesB,
                    paid_count = paidB,
                    pending_count = pendingB
                },
                delta = new
                {
                    gross = grossB - grossA,
                    net = netB - netA,
                    advances = advancesB - advancesA,
                    expenses = expensesB - expensesA,
                    paid_count = paidB - paidA,
                    pending_count = pendingB - pendingA
                },
                formatted_comparison = formattedText
            };
        }

        private static string BuildSalaryComparisonText(
            string subject,
            string monthADisplay,
            decimal netA,
            decimal grossA,
            decimal expensesA,
            string monthBDisplay,
            decimal netB,
            decimal grossB,
            decimal expensesB)
        {
            return new StringBuilder()
                .AppendLine($"Порівняння за {subject}:")
                .AppendLine($"• {monthADisplay}: нетто {netA:N2} CZK, брутто {grossA:N2} CZK, витрати {expensesA:N2} CZK")
                .AppendLine($"• {monthBDisplay}: нетто {netB:N2} CZK, брутто {grossB:N2} CZK, витрати {expensesB:N2} CZK")
                .AppendLine($"• Різниця по нетто: {(netB - netA):N2} CZK")
                .AppendLine($"• Різниця по брутто: {(grossB - grossA):N2} CZK")
                .AppendLine($"• Різниця по витратах: {(expensesB - expensesA):N2} CZK")
                .ToString()
                .TrimEnd();
        }

        private async Task SendTargetedEmployeeDocumentAnswerAsync(
            ITelegramBotClient botClient,
            ChatId chatId,
            EmployeeSummary employee,
            string question,
            CancellationToken cancellationToken,
            long userId)
        {
            var data = _employeeService.LoadEmployeeData(employee.EmployeeFolder);
            if (data == null)
            {
                await SendEmployeeDetailsAsync(botClient, chatId, employee.UniqueId, cancellationToken, userId).ConfigureAwait(false);
                return;
            }

            var normalized = NormalizeForSearch(question);
            string answer;
            if (normalized.Contains("віз", StringComparison.Ordinal))
            {
                answer = $"У {employee.FullName} віза дійсна до {FormatExpiry(data.VisaExpiry)}.";
            }
            else if (normalized.Contains("паспорт", StringComparison.Ordinal))
            {
                answer = $"У {employee.FullName} паспорт {data.PassportNumber}. Дійсний до {FormatExpiry(data.PassportExpiry)}.";
            }
            else if (normalized.Contains("страх", StringComparison.Ordinal))
            {
                if (HasInsuranceNumberIntent(normalized))
                {
                    answer = $"У {employee.FullName} номер страховки {data.InsuranceNumber}. Страхування {data.InsuranceCompanyShort}, дійсне до {FormatExpiry(data.InsuranceExpiry)}.";
                }
                else if (HasInsuranceExpiryIntent(normalized))
                {
                    answer = $"У {employee.FullName} страхування {data.InsuranceCompanyShort}. Дійсне до {FormatExpiry(data.InsuranceExpiry)}.";
                }
                else
                {
                    answer = $"У {employee.FullName} страхування {data.InsuranceCompanyShort}, номер {data.InsuranceNumber}. Дійсне до {FormatExpiry(data.InsuranceExpiry)}.";
                }
            }
            else if (normalized.Contains("дозв", StringComparison.Ordinal))
            {
                answer = $"У {employee.FullName} дозвіл на роботу {data.WorkPermitNumber}. Дійсний до {FormatExpiry(data.WorkPermitExpiry)}.";
            }
            else
            {
                answer = $"У {employee.FullName}: паспорт до {FormatExpiry(data.PassportExpiry)}, віза до {FormatExpiry(data.VisaExpiry)}, страхування до {FormatExpiry(data.InsuranceExpiry)}.";
            }

            await SendMessageAsync(botClient, chatId, answer, cancellationToken, BuildEmployeeDetailsKeyboard(employee, userId)).ConfigureAwait(false);
            TouchConversationContext(userId, employee, employee.FirmName, topic: "documents", action: "employee_document");
            AppendConversationTurn(userId, "user", question);
            AppendConversationTurn(userId, "model", BuildConversationModelHistoryEntry(answer, "employee_document", employee, employee.FirmName, string.Empty));
        }

        private async Task SendTargetedEmployeeInfoAnswerAsync(
            ITelegramBotClient botClient,
            ChatId chatId,
            EmployeeSummary employee,
            string question,
            CancellationToken cancellationToken,
            long userId)
        {
            var data = _employeeService.LoadEmployeeData(employee.EmployeeFolder);
            var normalized = NormalizeForSearch(question);
            string answer;

            if (HasEmploymentEndIntent(normalized))
            {
                if (!string.IsNullOrWhiteSpace(data?.EndDate))
                {
                    answer = $"{employee.FullName} закінчив(ла) роботу {data.EndDate} у фірмі {employee.FirmName}.";
                }
                else
                {
                    answer = $"Для {employee.FullName} дата завершення роботи не заповнена.";
                }
            }
            else if (HasEmploymentStartIntent(normalized))
            {
                if (!string.IsNullOrWhiteSpace(data?.StartDate))
                {
                    answer = $"{employee.FullName} почав(ла) роботу {data.StartDate} у фірмі {employee.FirmName}.";
                }
                else
                {
                    answer = $"Для {employee.FullName} дата початку роботи не заповнена.";
                }
            }
            else if (HasEmploymentStatusIntent(normalized))
            {
                var status = data?.Status ?? employee.Status;
                var employmentState = !string.IsNullOrWhiteSpace(data?.EndDate)
                    ? $", кінець роботи {data.EndDate}"
                    : string.Empty;
                answer = $"Статус {employee.FullName}: {status}{employmentState}.";
            }
            else if (HasFirmHistoryIntent(normalized))
            {
                if (data?.FirmHistory != null && data.FirmHistory.Count > 0)
                {
                    var historyLines = data.FirmHistory
                        .OrderByDescending(item => item.StartDate)
                        .Select(item =>
                        {
                            var end = string.IsNullOrWhiteSpace(item.EndDate) ? "дотепер" : item.EndDate;
                            return $"• {item.FirmName}: {item.StartDate} - {end}";
                        })
                        .Take(8)
                        .ToList();

                    answer = $"Фірми, де працював(ла) {employee.FullName}:\n" + string.Join("\n", historyLines);
                }
                else
                {
                    var end = string.IsNullOrWhiteSpace(data?.EndDate) ? "дотепер" : data!.EndDate;
                    answer = $"{employee.FullName} працював(ла) у фірмі {employee.FirmName}: {data?.StartDate ?? employee.StartDate} - {end}.";
                }
            }
            else if (normalized.Contains("де працю", StringComparison.Ordinal))
            {
                if (!string.IsNullOrWhiteSpace(data?.EndDate) && !HasCurrentEmploymentIntent(normalized))
                {
                    answer = $"{employee.FullName} працював(ла) у фірмі {employee.FirmName} до {data.EndDate}.";
                }
                else
                {
                    answer = $"{employee.FullName} зараз працює у фірмі {employee.FirmName}.";
                }
            }
            else if (normalized.Contains("телефон", StringComparison.Ordinal) || normalized.Contains("номер", StringComparison.Ordinal))
            {
                answer = $"Телефон {employee.FullName}: {employee.Phone}.";
            }
            else if (normalized.Contains("email", StringComparison.Ordinal))
            {
                answer = $"Email {employee.FullName}: {employee.Email}.";
            }
            else if (normalized.Contains("адрес", StringComparison.Ordinal))
            {
                answer = $"Адреса {employee.FullName}: {FormatAddress(data?.AddressLocal ?? new EmployeeAddress())}.";
            }
            else if (normalized.Contains("банк", StringComparison.Ordinal))
            {
                answer = $"Банківські реквізити {employee.FullName}: {employee.BankAccountNumber}.";
            }
            else
            {
                answer = $"{employee.FullName}: телефон {employee.Phone}, email {employee.Email}, фірма {employee.FirmName}.";
            }

            await SendMessageAsync(botClient, chatId, answer, cancellationToken, BuildEmployeeDetailsKeyboard(employee, userId)).ConfigureAwait(false);
            TouchConversationContext(userId, employee, employee.FirmName, topic: "employee", action: "employee_info");
            AppendConversationTurn(userId, "user", question);
            AppendConversationTurn(userId, "model", BuildConversationModelHistoryEntry(answer, "employee_info", employee, employee.FirmName, string.Empty));
        }

        private async Task SendSalaryExplanationAsync(
            ITelegramBotClient botClient,
            ChatId chatId,
            SalaryInfo salaryInfo,
            string question,
            CancellationToken cancellationToken,
            long userId)
        {
            if (!_appSettingsService.Settings.Telegram.AllowAiQuestions || !_geminiApiService.IsConfigured)
            {
                var fallback = BuildSalaryBreakdownText(salaryInfo);
                await SendMessageAsync(botClient, chatId, fallback, cancellationToken, BuildSalaryKeyboard(salaryInfo.Employee, userId)).ConfigureAwait(false);
                TouchConversationContext(userId, salaryInfo.Employee, salaryInfo.Employee.FirmName, salaryInfo.MonthKey, topic: "salary", action: "salary_explanation");
                AppendConversationTurn(userId, "user", question);
                AppendConversationTurn(userId, "model", BuildConversationModelHistoryEntry(fallback, "salary_explanation_fallback", salaryInfo.Employee, salaryInfo.Employee.FirmName, salaryInfo.MonthKey));
                return;
            }

            var answer = await _geminiApiService.ChatWithHistoryAsync(
                GetConversationHistory(userId),
                $"Question: {question}\n\nSalary details:\n{BuildSalaryAiContext(salaryInfo)}",
                systemPrompt: "You are an HR salary assistant. Reply in Ukrainian. Explain salary clearly and briefly. Use only provided salary details. If something is missing, say that directly. Mention hourly rate, hours worked, gross, salary advance, extra advances, custom fields, net and paid date when relevant.",
                ct: cancellationToken).ConfigureAwait(false);

            await SendMessageAsync(botClient, chatId, answer, cancellationToken, BuildSalaryKeyboard(salaryInfo.Employee, userId)).ConfigureAwait(false);
            TouchConversationContext(userId, salaryInfo.Employee, salaryInfo.Employee.FirmName, salaryInfo.MonthKey, topic: "salary", action: "salary_explanation");
            AppendConversationTurn(userId, "user", question);
            AppendConversationTurn(userId, "model", BuildConversationModelHistoryEntry(answer, "salary_explanation", salaryInfo.Employee, salaryInfo.Employee.FirmName, salaryInfo.MonthKey));
        }

        private async Task HandleCallbackQueryAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, CancellationToken cancellationToken)
        {
            await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken).ConfigureAwait(false);

            var userId = callbackQuery.From.Id;
            var callbackChat = callbackQuery.Message?.Chat;
            if (!IsPrivateChat(callbackChat))
            {
                await SendMessageAsync(
                    botClient,
                    callbackQuery.Message?.Chat.Id ?? userId,
                    "Для безпечного доступу кнопки бота працюють тільки в приватному чаті.",
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            if (!IsAuthorized(userId))
            {
                await SendMessageAsync(
                    botClient,
                    callbackQuery.Message?.Chat.Id ?? userId,
                    "Доступ заборонено. Відскануйте QR-код у налаштуваннях програми, щоб прив'язати цей Telegram-акаунт.",
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            UpdateLastSeen(userId);

            if (!TryResolveCallback(callbackQuery.Data, userId, out var action, out var value, out var metadata, out var accessDenied))
            {
                await SendMessageAsync(
                    botClient,
                    callbackQuery.Message?.Chat.Id ?? userId,
                    accessDenied ? "Ця кнопка була створена для іншого Telegram-користувача." : "Дія кнопки вже застаріла. Відкрийте /menu ще раз.",
                    cancellationToken,
                    BuildMainMenuKeyboard(userId)).ConfigureAwait(false);
                return;
            }

            var chatId = callbackQuery.Message?.Chat.Id ?? userId;
            switch (action)
            {
                case "menu":
                    await SendMenuAsync(botClient, chatId, "Головне меню:", cancellationToken, userId).ConfigureAwait(false);
                    break;
                case "help":
                    await SendHelpAsync(botClient, chatId, cancellationToken, userId).ConfigureAwait(false);
                    break;
                case "firms":
                    await SendFirmsAsync(botClient, chatId, cancellationToken, userId).ConfigureAwait(false);
                    break;
                case "employees_all":
                    await SendEmployeesAsync(botClient, chatId, string.Empty, cancellationToken, userId).ConfigureAwait(false);
                    break;
                case "employees_firm":
                    await SendEmployeesAsync(botClient, chatId, value, cancellationToken, userId).ConfigureAwait(false);
                    break;
                case "employee":
                    await SendEmployeeDetailsAsync(botClient, chatId, value, cancellationToken, userId).ConfigureAwait(false);
                    break;
                case "expiring":
                    await SendExpiringAsync(botClient, chatId, value, cancellationToken, userId).ConfigureAwait(false);
                    break;
                case "salary_latest":
                    if (!HasAdminAccess(userId))
                    {
                        await SendMessageAsync(botClient, chatId, "Перегляд зарплати в Telegram доступний тільки користувачам з роллю Admin.", cancellationToken).ConfigureAwait(false);
                        break;
                    }

                    await SendSalaryAsync(botClient, chatId, value, cancellationToken, userId).ConfigureAwait(false);
                    break;
                case "pick_employee":
                    await HandleEmployeePickCallbackAsync(botClient, chatId, value, metadata, cancellationToken, userId).ConfigureAwait(false);
                    break;
                default:
                    await SendMenuAsync(botClient, chatId, "Головне меню:", cancellationToken, userId).ConfigureAwait(false);
                    break;
            }
        }

        private async Task HandleEmployeePickCallbackAsync(
            ITelegramBotClient botClient,
            ChatId chatId,
            string employeeId,
            string metadata,
            CancellationToken cancellationToken,
            long userId)
        {
            var selectedRecord = GetAllEmployeeRecords()
                .FirstOrDefault(candidate => string.Equals(candidate.UniqueId, employeeId, StringComparison.OrdinalIgnoreCase));
            if (selectedRecord == null)
            {
                await SendMessageAsync(botClient, chatId, "Обраного працівника не знайдено. Спробуйте ще раз.", cancellationToken).ConfigureAwait(false);
                return;
            }

            var mode = "employee_details";
            var originalQuery = string.Empty;
            var monthKey = string.Empty;
            var documentType = string.Empty;

            if (!string.IsNullOrWhiteSpace(metadata))
            {
                using var metadataDocument = JsonDocument.Parse(metadata);
                var root = metadataDocument.RootElement;
                if (root.TryGetProperty("mode", out var modeElement) && modeElement.ValueKind == JsonValueKind.String)
                    mode = modeElement.GetString() ?? mode;
                if (root.TryGetProperty("originalQuery", out var queryElement) && queryElement.ValueKind == JsonValueKind.String)
                    originalQuery = queryElement.GetString() ?? string.Empty;
                if (root.TryGetProperty("monthKey", out var monthElement) && monthElement.ValueKind == JsonValueKind.String)
                    monthKey = monthElement.GetString() ?? string.Empty;
                if (root.TryGetProperty("documentType", out var documentElement) && documentElement.ValueKind == JsonValueKind.String)
                    documentType = documentElement.GetString() ?? string.Empty;
            }

            var employeeShadow = BuildConversationEmployeeShadow(selectedRecord);
            if (employeeShadow != null)
                TouchConversationContext(userId, employeeShadow, selectedRecord.FirmName);

            switch (mode)
            {
                case "salary":
                    if (!HasAdminAccess(userId))
                    {
                        await SendMessageAsync(botClient, chatId, "Перегляд зарплати в Telegram доступний тільки користувачам з роллю Admin.", cancellationToken).ConfigureAwait(false);
                        break;
                    }

                    await SendSalaryAsync(
                        botClient,
                        chatId,
                        string.IsNullOrWhiteSpace(monthKey) ? employeeId : $"{employeeId} {monthKey}",
                        cancellationToken,
                        userId).ConfigureAwait(false);
                    break;
                case "document":
                    if (!HasAdminAccess(userId))
                    {
                        await SendMessageAsync(botClient, chatId, "Надсилання файлів документів доступне тільки Telegram-користувачам з роллю Admin.", cancellationToken).ConfigureAwait(false);
                        break;
                    }

                    await SendSelectedEmployeeDocumentAsync(botClient, chatId, employeeId, documentType, cancellationToken, userId).ConfigureAwait(false);
                    break;
                case "ai":
                    if (!HasAdminAccess(userId))
                    {
                        await SendMessageAsync(botClient, chatId, "AI-запити в Telegram доступні тільки користувачам з роллю Admin.", cancellationToken).ConfigureAwait(false);
                        break;
                    }

                    var followUpPrompt = $"Selected exact employee: {selectedRecord.FullName} (ID {selectedRecord.UniqueId}, firm {selectedRecord.FirmName}, source {selectedRecord.Source}). Complete this original request for that exact employee: {originalQuery}";
                    await SendAiAsync(botClient, chatId, followUpPrompt, cancellationToken, userId).ConfigureAwait(false);
                    break;
                case "employee_details":
                default:
                    await SendEmployeeDetailsAsync(botClient, chatId, employeeId, cancellationToken, userId).ConfigureAwait(false);
                    break;
            }
        }

        private bool IsAuthorized(long userId)
        {
            return GetAuthorizedUser(userId) != null;
        }

        private TelegramAuthorizedUser? GetAuthorizedUser(long userId)
        {
            return _appSettingsService.Settings.Telegram.AuthorizedUsers.FirstOrDefault(u => u.TelegramUserId == userId);
        }

        private static bool IsPrivateChat(Chat? chat)
        {
            return chat?.Type == ChatType.Private;
        }

        private static bool IsAdminRole(string? role)
        {
            var normalized = (role ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(normalized)
                   || string.Equals(normalized, TelegramRoleAdmin, StringComparison.OrdinalIgnoreCase);
        }

        private bool HasAdminAccess(long userId)
        {
            return IsAdminRole(GetAuthorizedUser(userId)?.Role);
        }

        private void UpdateLastSeen(long userId)
        {
            var user = GetAuthorizedUser(userId);
            if (user == null)
                return;

            user.LastActiveAtUtc = DateTime.UtcNow.ToString("O");
            _appSettingsService.SaveSettings();
            RaiseStateChanged();
        }

        private List<EmployeeSummary> GetAllEmployees()
        {
            return _companyService.VisibleCompanies
                .SelectMany(company => _employeeService.GetEmployeesForFirm(company.Name))
                .ToList();
        }

        private List<ArchivedEmployeeSummary> GetAllArchivedEmployees()
        {
            return _employeeService.GetArchivedEmployees();
        }

        private List<RecentlyDeletedItem> GetAllRecentlyDeletedItems()
        {
            return _recentlyDeletedService.GetAllItems();
        }

        private List<EmployeeLookupResult> GetAllEmployeeRecords(bool includeArchived = true, bool includeRecentlyDeleted = true)
        {
            var results = GetAllEmployees()
                .Select(employee => new EmployeeLookupResult
                {
                    UniqueId = employee.UniqueId,
                    FullName = employee.FullName,
                    FirmName = employee.FirmName,
                    PositionTitle = employee.PositionTitle,
                    StartDate = employee.StartDate,
                    EndDate = employee.EndDate,
                    EmployeeFolder = employee.EmployeeFolder,
                    Source = "active",
                    StatusLabel = string.IsNullOrWhiteSpace(employee.Status) ? "Active" : employee.Status,
                    ActiveEmployee = employee
                })
                .ToList();

            if (includeArchived)
            {
                results.AddRange(GetAllArchivedEmployees().Select(employee => new EmployeeLookupResult
                {
                    UniqueId = employee.UniqueId,
                    FullName = employee.FullName,
                    FirmName = employee.FirmName,
                    PositionTitle = employee.PositionTitle,
                    StartDate = employee.StartDate,
                    EndDate = employee.EndDate,
                    EmployeeFolder = employee.EmployeeFolder,
                    Source = "archived",
                    StatusLabel = "Archived",
                    ArchivedEmployee = employee
                }));
            }

            if (includeRecentlyDeleted)
            {
                results.AddRange(GetAllRecentlyDeletedItems().Select(item => new EmployeeLookupResult
                {
                    UniqueId = item.UniqueId,
                    FullName = item.FullName,
                    FirmName = item.FirmName,
                    PositionTitle = item.PositionTitle,
                    StartDate = item.StartDate,
                    EmployeeFolder = item.DeletedEmployeeFolder,
                    Source = "recently_deleted",
                    StatusLabel = "RecentlyDeleted",
                    DeletedAtUtc = item.DeletedAtUtc,
                    RecentlyDeletedItem = item
                }));
            }

            return results
                .GroupBy(item => $"{item.Source}|{item.UniqueId}|{item.EmployeeFolder}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }

        private List<EmployeeSummary> FindEmployees(string query, int limit, long? userId = null, bool allowContextFallback = false)
        {
            var normalized = (query ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                if (allowContextFallback && userId.HasValue)
                {
                    var fromContext = ResolveEmployeeFromContext(userId.Value);
                    if (fromContext != null)
                        return new List<EmployeeSummary> { fromContext };
                }

                return new List<EmployeeSummary>();
            }

            var matches = GetAllEmployees()
                .Select(employee => new
                {
                    Employee = employee,
                    Score = CalculateEmployeeScore(employee, normalized)
                })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Employee.FullName)
                .Take(limit)
                .Select(x => x.Employee)
                .ToList();

            if (matches.Count == 0 && allowContextFallback && userId.HasValue)
            {
                var fromContext = ResolveEmployeeFromContext(userId.Value);
                if (fromContext != null)
                    return new List<EmployeeSummary> { fromContext };
            }

            return matches;
        }

        private List<EmployeeLookupResult> FindEmployeeRecords(string query, int limit, long? userId = null, bool allowContextFallback = false, bool includeArchived = true, bool includeRecentlyDeleted = true)
        {
            var normalized = (query ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                if (allowContextFallback && userId.HasValue)
                {
                    var fromContext = ResolveEmployeeRecordFromContext(userId.Value);
                    if (fromContext != null)
                        return new List<EmployeeLookupResult> { fromContext };
                }

                return new List<EmployeeLookupResult>();
            }

            var matches = GetAllEmployeeRecords(includeArchived, includeRecentlyDeleted)
                .Select(employee => new
                {
                    Employee = employee,
                    Score = CalculateEmployeeRecordScore(employee, normalized)
                })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Employee.FullName)
                .Take(limit)
                .Select(x => x.Employee)
                .ToList();

            if (matches.Count == 0 && allowContextFallback && userId.HasValue)
            {
                var fromContext = ResolveEmployeeRecordFromContext(userId.Value);
                if (fromContext != null)
                    return new List<EmployeeLookupResult> { fromContext };
            }

            return matches;
        }

        private static int CalculateEmployeeScore(EmployeeSummary employee, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return 0;

            var normalizedQuery = NormalizeForSearch(query);
            var queryVariants = GetSearchVariants(normalizedQuery);
            var queryTokens = queryVariants
                .SelectMany(Tokenize)
                .Select(NormalizeNameToken)
                .Where(token => !string.IsNullOrWhiteSpace(token))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var employeeName = NormalizeForSearch(employee.FullName);
            var employeeNameVariants = GetSearchVariants(employeeName);
            var employeeTokens = employeeNameVariants
                .SelectMany(Tokenize)
                .Select(NormalizeNameToken)
                .Where(token => !string.IsNullOrWhiteSpace(token))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (queryVariants.Any(variant => string.Equals(employee.UniqueId, variant, StringComparison.OrdinalIgnoreCase)))
                return 100;

            var score = 0;
            if (employeeNameVariants.Any(nameVariant => queryVariants.Any(queryVariant => string.Equals(nameVariant, queryVariant, StringComparison.OrdinalIgnoreCase))))
                score = Math.Max(score, 95);
            if (employeeNameVariants.Any(nameVariant => queryVariants.Any(queryVariant => nameVariant.Contains(queryVariant, StringComparison.OrdinalIgnoreCase))))
                score = Math.Max(score, 80);
            if (!string.IsNullOrWhiteSpace(employee.PassportNumber)
                && queryVariants.Any(variant => employee.PassportNumber.Contains(variant, StringComparison.OrdinalIgnoreCase)))
                return 70;
            if (!string.IsNullOrWhiteSpace(employee.Phone)
                && queryVariants.Any(variant => employee.Phone.Contains(variant, StringComparison.OrdinalIgnoreCase)))
                return 60;
            if (!string.IsNullOrWhiteSpace(employee.FirmName)
                && queryVariants.Any(variant => NormalizeForSearch(employee.FirmName).Contains(variant, StringComparison.OrdinalIgnoreCase)))
                score = Math.Max(score, 20);

            if (queryTokens.Count == 0)
                return score;

            var matchedTokens = 0;
            foreach (var queryToken in queryTokens)
            {
                var normalizedQueryToken = queryToken;
                if (string.IsNullOrWhiteSpace(normalizedQueryToken))
                    continue;

                var bestTokenScore = 0;
                foreach (var employeeToken in employeeTokens)
                {
                    var tokenDistance = ComputeLevenshteinDistance(employeeToken, normalizedQueryToken);
                    if (tokenDistance == 1)
                        bestTokenScore = Math.Max(bestTokenScore, 24);
                    else if (tokenDistance == 2)
                        bestTokenScore = Math.Max(bestTokenScore, 16);
                }

                if (employeeTokens.Any(token => string.Equals(token, normalizedQueryToken, StringComparison.Ordinal)))
                {
                    matchedTokens++;
                    score += 45;
                    continue;
                }

                if (employeeTokens.Any(token => token.StartsWith(normalizedQueryToken, StringComparison.Ordinal)
                                                || normalizedQueryToken.StartsWith(token, StringComparison.Ordinal)))
                {
                    matchedTokens++;
                    score += 30;
                    continue;
                }

                if (employeeTokens.Any(token => token.Contains(normalizedQueryToken, StringComparison.Ordinal)
                                                || normalizedQueryToken.Contains(token, StringComparison.Ordinal)))
                {
                    score += 15;
                    continue;
                }

                if (bestTokenScore > 0)
                    score += bestTokenScore;
            }

            if (matchedTokens >= 2)
                score += 25;
            else if (matchedTokens == queryTokens.Count && matchedTokens > 0)
                score += 10;

            return score;
        }

        private static bool ShouldRetryAiAnswer(string? text)
        {
            return string.IsNullOrWhiteSpace(text) || GeminiApiService.IsFailureResponse(text);
        }

        private static int CalculateEmployeeRecordScore(EmployeeLookupResult employee, string query)
        {
            var shadow = employee.ActiveEmployee ?? new EmployeeSummary
            {
                UniqueId = employee.UniqueId,
                FullName = employee.FullName,
                FirmName = employee.FirmName,
                PositionTitle = employee.PositionTitle,
                StartDate = employee.StartDate,
                EndDate = employee.EndDate
            };

            var score = CalculateEmployeeScore(shadow, query);
            if (employee.IsArchived)
                score += 4;
            if (employee.IsRecentlyDeleted)
                score += 2;

            return score;
        }

        private static bool TrySelectSingleEmployeeRecord(string query, IReadOnlyList<EmployeeLookupResult> candidates, out EmployeeLookupResult? employee)
        {
            employee = null;
            if (candidates == null || candidates.Count == 0)
                return false;

            if (candidates.Count == 1)
            {
                employee = candidates[0];
                return true;
            }

            var normalizedQuery = NormalizeForSearch(query);
            var exactMatches = candidates.Where(candidate =>
                string.Equals(NormalizeForSearch(candidate.FullName), normalizedQuery, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.UniqueId, query, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(GetEmployeeSelectionSourcePriority)
                .ThenBy(candidate => candidate.FullName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (exactMatches.Count == 1)
            {
                employee = exactMatches[0];
                return true;
            }

            if (exactMatches.Count > 1)
            {
                var exactTop = exactMatches[0];
                var exactSecond = exactMatches.Count > 1 ? exactMatches[1] : null;
                if (exactSecond == null || GetEmployeeSelectionSourcePriority(exactTop) > GetEmployeeSelectionSourcePriority(exactSecond))
                {
                    employee = exactTop;
                    return true;
                }
            }

            var queryTokens = Tokenize(normalizedQuery)
                .Select(NormalizeNameToken)
                .Where(token => token.Length >= 2)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var ranked = candidates
                .Select(candidate => new
                {
                    Candidate = candidate,
                    Score = CalculateEmployeeRecordScore(candidate, query),
                    MatchedTokens = CountMatchedEmployeeNameTokens(candidate.FullName, queryTokens),
                    SourcePriority = GetEmployeeSelectionSourcePriority(candidate),
                    StartsWithQuery = NormalizeForSearch(candidate.FullName).StartsWith(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                })
                .OrderByDescending(item => item.Score)
                .ThenByDescending(item => item.MatchedTokens)
                .ThenByDescending(item => item.SourcePriority)
                .ThenByDescending(item => item.StartsWithQuery)
                .ThenBy(item => item.Candidate.FullName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var top = ranked[0];
            var second = ranked.Count > 1 ? ranked[1] : null;
            var scoreGap = second == null ? top.Score : top.Score - second.Score;
            var tokenGap = second == null ? top.MatchedTokens : top.MatchedTokens - second.MatchedTokens;

            if (queryTokens.Count >= 2
                && top.MatchedTokens == queryTokens.Count
                && (second == null
                    || scoreGap >= 8
                    || tokenGap >= 1
                    || top.SourcePriority > second.SourcePriority))
            {
                employee = top.Candidate;
                return true;
            }

            if (queryTokens.Count >= 2
                && top.Score >= 105
                && (second == null || scoreGap >= 18))
            {
                employee = top.Candidate;
                return true;
            }

            if (queryTokens.Count == 1
                && top.StartsWithQuery
                && top.Score >= 120
                && (second == null || scoreGap >= 30))
            {
                employee = top.Candidate;
                return true;
            }

            return employee != null;
        }

        private static int GetEmployeeSelectionSourcePriority(EmployeeLookupResult employee)
        {
            if (!employee.IsArchived && !employee.IsRecentlyDeleted)
                return 3;
            if (employee.IsArchived)
                return 2;
            if (employee.IsRecentlyDeleted)
                return 1;

            return 0;
        }

        private static int CountMatchedEmployeeNameTokens(string fullName, IReadOnlyList<string> queryTokens)
        {
            if (string.IsNullOrWhiteSpace(fullName) || queryTokens.Count == 0)
                return 0;

            var employeeTokens = Tokenize(NormalizeForSearch(fullName))
                .Select(NormalizeNameToken)
                .Where(token => token.Length >= 2)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var matched = 0;
            foreach (var queryToken in queryTokens)
            {
                if (employeeTokens.Any(token => string.Equals(token, queryToken, StringComparison.Ordinal)
                                                || token.StartsWith(queryToken, StringComparison.Ordinal)
                                                || queryToken.StartsWith(token, StringComparison.Ordinal)
                                                || ComputeLevenshteinDistance(token, queryToken) <= 1))
                {
                    matched++;
                }
            }

            return matched;
        }

        private ConversationContext GetOrCreateConversationContext(long userId)
        {
            CleanupExpiredConversationContexts();
            return _conversationContexts.GetOrAdd(userId, _ => new ConversationContext());
        }

        private ConversationContext? GetConversationContext(long userId)
        {
            CleanupExpiredConversationContexts();
            return _conversationContexts.TryGetValue(userId, out var context) ? context : null;
        }

        private void TouchConversationContext(
            long userId,
            EmployeeSummary? employee = null,
            string? firmName = null,
            string? monthKey = null,
            string? topic = null,
            string? action = null,
            string? aiTool = null,
            string? secondaryMonthKey = null)
        {
            var context = GetOrCreateConversationContext(userId);
            if (employee != null)
            {
                context.LastEmployeeId = employee.UniqueId;
                context.LastEmployeeName = employee.FullName;
                context.LastFirmName = employee.FirmName;
            }

            if (!string.IsNullOrWhiteSpace(firmName))
                context.LastFirmName = firmName;
            if (!string.IsNullOrWhiteSpace(monthKey))
            {
                if (!string.IsNullOrWhiteSpace(context.LastMonthKey)
                    && !string.Equals(context.LastMonthKey, monthKey, StringComparison.OrdinalIgnoreCase))
                {
                    context.LastSecondaryMonthKey = context.LastMonthKey;
                }

                context.LastMonthKey = monthKey;
            }
            if (!string.IsNullOrWhiteSpace(secondaryMonthKey))
                context.LastSecondaryMonthKey = secondaryMonthKey;
            if (!string.IsNullOrWhiteSpace(topic))
                context.LastTopic = topic;
            if (!string.IsNullOrWhiteSpace(action))
                context.LastAction = action;
            if (!string.IsNullOrWhiteSpace(aiTool))
                context.LastAiTool = aiTool;

            context.UpdatedAtUtc = DateTime.UtcNow;
        }

        private EmployeeSummary? ResolveEmployeeFromContext(long userId)
        {
            return BuildConversationEmployeeShadow(ResolveEmployeeRecordFromContext(userId));
        }

        private EmployeeLookupResult? ResolveEmployeeRecordFromContext(long userId)
        {
            var context = GetConversationContext(userId);
            if (context == null || string.IsNullOrWhiteSpace(context.LastEmployeeId))
                return null;

            return GetAllEmployeeRecords()
                .FirstOrDefault(e => string.Equals(e.UniqueId, context.LastEmployeeId, StringComparison.OrdinalIgnoreCase));
        }

        private SalaryInfo? ResolveSalaryInfo(EmployeeSummary employee, string? monthKey)
        {
            var history = _financeService.LoadSalaryHistory(employee.EmployeeFolder)
                .OrderByDescending(r => r.Year)
                .ThenByDescending(r => r.Month)
                .ToList();

            if (!string.IsNullOrWhiteSpace(monthKey) && TryParseMonthKey(monthKey, out var requestedMonth))
            {
                var liveInfo = TryResolveLiveSalaryInfo(employee, requestedMonth.Year, requestedMonth.Month, history);
                if (liveInfo != null)
                    return liveInfo;

                var historyRecord = FindSalaryHistoryRecord(history, requestedMonth.Year, requestedMonth.Month, employee.FirmName);
                return BuildHistorySalaryInfo(employee, historyRecord);
            }

            foreach (var availableMonth in _financeService.GetAvailableSalaryMonths())
            {
                var liveInfo = TryResolveLiveSalaryInfo(employee, availableMonth.year, availableMonth.month, history);
                if (liveInfo != null)
                    return liveInfo;
            }

            return BuildHistorySalaryInfo(employee, history.FirstOrDefault());
        }

        private SalaryInfo? ResolveSalaryInfo(EmployeeLookupResult employee, string? monthKey)
        {
            var shadow = employee.ActiveEmployee ?? BuildConversationEmployeeShadow(employee);
            if (shadow == null)
                return null;

            return ResolveSalaryInfo(shadow, monthKey);
        }

        private SalaryInfo? ResolveLatestPaidSalaryInfo(EmployeeLookupResult employee, SalaryInfo? currentSalary)
        {
            var shadow = employee.ActiveEmployee ?? BuildConversationEmployeeShadow(employee);
            if (shadow == null)
                return null;

            return ResolveLatestPaidSalaryInfo(shadow, currentSalary);
        }

        private SalaryInfo? ResolveLatestPaidSalaryInfo(EmployeeSummary employee, SalaryInfo? currentSalary)
        {
            if (currentSalary?.IsPaid == true)
                return currentSalary;

            var historyRecord = _financeService.LoadSalaryHistory(employee.EmployeeFolder)
                .OrderByDescending(r => r.Year)
                .ThenByDescending(r => r.Month)
                .FirstOrDefault();

            return BuildHistorySalaryInfo(employee, historyRecord);
        }

        private SalaryInfo? TryResolveLiveSalaryInfo(
            EmployeeSummary employee,
            int year,
            int month,
            IReadOnlyList<SalaryHistoryRecord> history)
        {
            var payments = _financeService.TryLoadAllFirmPayments(year, month, forceReload: true);
            if (!payments.success)
                return null;

            var normalizedFirm = NormalizeForSearch(employee.FirmName);
            var entry = payments.entries
                .Where(candidate => IsSalaryEntryForEmployee(employee, candidate))
                .OrderByDescending(candidate =>
                    string.Equals(NormalizeForSearch(candidate.FirmName), normalizedFirm, StringComparison.Ordinal))
                .ThenByDescending(candidate =>
                    !string.IsNullOrWhiteSpace(candidate.EmployeeId)
                    && string.Equals(candidate.EmployeeId, employee.UniqueId, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();

            if (entry == null)
                return null;

            var resolvedMonthKey = $"{year:D4}-{month:D2}";
            var record = _financeService.BuildHistoryRecord(entry, year, month, _financeService.GetFieldsForFirm(entry.FirmName));
            var historyRecord = FindSalaryHistoryRecord(history, year, month, entry.FirmName);
            var isPaid = string.Equals(entry.Status, "paid", StringComparison.OrdinalIgnoreCase);
            var paidAt = isPaid ? historyRecord?.PaidAt : null;

            return new SalaryInfo
            {
                Employee = employee,
                Record = record,
                Advances = _financeService.GetAdvancesForEmployeeMonth(employee.EmployeeFolder, resolvedMonthKey),
                MonthKey = resolvedMonthKey,
                IsPaid = isPaid,
                PaidAt = paidAt
            };
        }

        private SalaryInfo? BuildHistorySalaryInfo(EmployeeSummary employee, SalaryHistoryRecord? record)
        {
            if (record == null)
                return null;

            var resolvedMonthKey = $"{record.Year:D4}-{record.Month:D2}";
            return new SalaryInfo
            {
                Employee = employee,
                Record = record,
                Advances = _financeService.GetAdvancesForEmployeeMonth(employee.EmployeeFolder, resolvedMonthKey),
                MonthKey = resolvedMonthKey,
                IsPaid = true,
                PaidAt = record.PaidAt
            };
        }

        private static SalaryHistoryRecord? FindSalaryHistoryRecord(
            IEnumerable<SalaryHistoryRecord> history,
            int year,
            int month,
            string? firmName)
        {
            var normalizedFirm = NormalizeForSearch(firmName ?? string.Empty);
            return history
                .Where(record => record.Year == year && record.Month == month)
                .OrderByDescending(record =>
                    string.Equals(NormalizeForSearch(record.FirmName), normalizedFirm, StringComparison.Ordinal))
                .FirstOrDefault();
        }

        private static bool IsSalaryEntryForEmployee(EmployeeSummary employee, SalaryEntry entry)
        {
            if (!string.IsNullOrWhiteSpace(employee.UniqueId)
                && !string.IsNullOrWhiteSpace(entry.EmployeeId)
                && string.Equals(employee.UniqueId, entry.EmployeeId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(
                NormalizeEmployeePath(employee.EmployeeFolder),
                NormalizeEmployeePath(entry.EmployeeFolder),
                StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return string.Equals(NormalizeForSearch(employee.FullName), NormalizeForSearch(entry.FullName), StringComparison.Ordinal)
                && string.Equals(NormalizeForSearch(employee.FirmName), NormalizeForSearch(entry.FirmName), StringComparison.Ordinal);
        }

        private static string NormalizeEmployeePath(string path)
            => (path ?? string.Empty).Replace('/', '\\').Trim().TrimEnd('\\');

        private AllFirmsSalaryInfo? ResolveAllFirmsSalaryInfo(string monthKey)
        {
            if (string.IsNullOrWhiteSpace(monthKey) || !TryParseMonthKey(monthKey, out var month))
                return null;

            var payments = _financeService.TryLoadAllFirmPayments(month.Year, month.Month, forceReload: true);
            if (!payments.success)
                return null;

            var visibleFirmsByNormalizedName = _companyService.VisibleCompanies
                .GroupBy(company => NormalizeForSearch(company.Name))
                .ToDictionary(group => group.Key, group => group.First().Name, StringComparer.Ordinal);

            var keys = payments.entries
                .Select(entry => NormalizeForSearch(entry.FirmName))
                .Concat(payments.expenses.Select(expense => NormalizeForSearch(expense.FirmName)))
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var firms = new List<FirmSalaryInfo>();
            foreach (var key in keys)
            {
                var displayName = visibleFirmsByNormalizedName.TryGetValue(key, out var knownName)
                    ? knownName
                    : payments.entries.FirstOrDefault(entry => string.Equals(NormalizeForSearch(entry.FirmName), key, StringComparison.Ordinal))?.FirmName
                        ?? payments.expenses.FirstOrDefault(expense => string.Equals(NormalizeForSearch(expense.FirmName), key, StringComparison.Ordinal))?.FirmName
                        ?? key;

                var entries = payments.entries
                    .Where(entry => string.Equals(NormalizeForSearch(entry.FirmName), key, StringComparison.Ordinal))
                    .OrderBy(entry => entry.FullName)
                    .ToList();

                var expenses = payments.expenses
                    .Where(expense => string.Equals(NormalizeForSearch(expense.FirmName), key, StringComparison.Ordinal))
                    .OrderBy(expense => expense.Name)
                    .ToList();

                if (entries.Count == 0 && expenses.Count == 0)
                    continue;

                firms.Add(new FirmSalaryInfo
                {
                    FirmName = displayName,
                    Year = month.Year,
                    Month = month.Month,
                    Entries = entries,
                    Expenses = expenses
                });
            }

            if (firms.Count == 0)
                return null;

            return new AllFirmsSalaryInfo
            {
                Year = month.Year,
                Month = month.Month,
                Firms = firms.OrderByDescending(f => f.TotalNet).ThenBy(f => f.FirmName).ToList()
            };
        }

        private FirmSalaryInfo? ResolveFirmSalaryInfo(string firmName, string monthKey)
        {
            if (string.IsNullOrWhiteSpace(firmName) || !TryParseMonthKey(monthKey, out var month))
                return null;

            var payments = _financeService.TryLoadAllFirmPayments(month.Year, month.Month, forceReload: true);
            if (!payments.success)
                return null;

            var entries = payments.entries
                .Where(entry => string.Equals(NormalizeForSearch(entry.FirmName), NormalizeForSearch(firmName), StringComparison.Ordinal))
                .OrderBy(entry => entry.FullName)
                .ToList();
            var expenses = payments.expenses
                .Where(expense => string.Equals(NormalizeForSearch(expense.FirmName), NormalizeForSearch(firmName), StringComparison.Ordinal))
                .OrderBy(expense => expense.Name)
                .ToList();

            if (entries.Count == 0 && expenses.Count == 0)
                return null;

            return new FirmSalaryInfo
            {
                FirmName = firmName,
                Year = month.Year,
                Month = month.Month,
                Entries = entries,
                Expenses = expenses
            };
        }

        private static decimal GetEntryNetSalary(SalaryEntry entry)
        {
            if (entry.SavedNetSalary != 0)
                return entry.SavedNetSalary;
            if (entry.NetSalary != 0)
                return entry.NetSalary;
            return entry.GrossSalary - entry.Advance;
        }

        private List<FirmEmployeeSalaryDetail> BuildFirmEmployeeSalaryDetails(FirmSalaryInfo info)
        {
            var monthKey = $"{info.Year:D4}-{info.Month:D2}";
            var firmFields = _financeService.GetFieldsForFirm(info.FirmName)
                .OrderBy(field => field.Order)
                .ToList();
            return info.Entries
                .OrderByDescending(GetEntryNetSalary)
                .ThenBy(entry => entry.FullName)
                .Select(entry =>
                {
                    var orderedColumns = new List<(string name, string operation, decimal value)>();

                    foreach (var definition in firmFields)
                    {
                        if (!entry.CustomValues.TryGetValue(definition.Id, out var value) || value == 0)
                            continue;

                        orderedColumns.Add((definition.Name, MapFieldOperation(definition.Operation), value));
                    }

                    foreach (var pair in entry.CustomValues.Where(pair => pair.Value != 0).OrderBy(pair => pair.Key))
                    {
                        if (firmFields.Any(field => string.Equals(field.Id, pair.Key, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        orderedColumns.Add((pair.Key, "?", pair.Value));
                    }

                    var orderedRowText = BuildOrderedFirmEmployeeRowText(
                        entry.FullName,
                        entry.HoursWorked,
                        entry.HourlyRate,
                        entry.Advance,
                        orderedColumns,
                        GetEntryNetSalary(entry),
                        entry.Note);

                    return new FirmEmployeeSalaryDetail
                    {
                        EmployeeName = entry.FullName,
                        EmployeeFolder = entry.EmployeeFolder,
                        HoursWorked = entry.HoursWorked,
                        HourlyRate = entry.HourlyRate,
                        SalaryAdvance = entry.Advance,
                        NetSalary = GetEntryNetSalary(entry),
                        Note = entry.Note,
                        OrderedColumns = orderedColumns,
                        OrderedRowText = orderedRowText
                    };
                })
                .ToList();
        }

        private string BuildFirmSalarySummaryText(FirmSalaryInfo info)
        {
            var employeeDetails = BuildFirmEmployeeSalaryDetails(info);
            var builder = new StringBuilder()
                .AppendLine($"Зарплата по фірмі {info.FirmName} за {info.MonthDisplay}:")
                .AppendLine($"• Працівників у зарплаті: {info.Entries.Count}")
                .AppendLine($"• Брутто: {info.TotalGross:N2} CZK")
                .AppendLine($"• Нетто: {info.TotalNet:N2} CZK")
                .AppendLine($"• Аванси в зарплаті: {info.TotalAdvances:N2} CZK")
                .AppendLine($"• Оплачено: {info.PaidCount}, не оплачено: {info.PendingCount}");

            if (info.Expenses.Count > 0)
            {
                builder
                    .AppendLine($"• Витрати фірми: {info.TotalExpenses:N2} CZK")
                    .AppendLine($"• Разом нетто + витрати: {(info.TotalNet + info.TotalExpenses):N2} CZK");
            }

            if (employeeDetails.Count > 0)
            {
                builder.AppendLine("Працівники:");
                foreach (var detail in employeeDetails.Take(12))
                    builder.AppendLine($"• {detail.OrderedRowText}");

                if (employeeDetails.Count > 12)
                    builder.AppendLine($"… і ще {employeeDetails.Count - 12} працівників");
            }

            return builder.ToString();
        }

        private static string MapFieldOperation(FieldOperation operation)
        {
            return operation switch
            {
                FieldOperation.Add => "+",
                FieldOperation.Subtract => "-",
                FieldOperation.Multiply => "*",
                FieldOperation.Divide => "/",
                _ => "?"
            };
        }

        private static string BuildOrderedFirmEmployeeRowText(
            string employeeName,
            decimal hoursWorked,
            decimal hourlyRate,
            decimal salaryAdvance,
            IReadOnlyList<(string name, string operation, decimal value)> orderedColumns,
            decimal finalPayout,
            string note)
        {
            var parts = new List<string>
            {
                $"{employeeName}: {hoursWorked:N2} год",
                $"{hourlyRate:N2} CZK/год"
            };

            if (salaryAdvance != 0)
                parts.Add($"Аванс -{salaryAdvance:N2} CZK");

            foreach (var column in orderedColumns)
            {
                var prefix = column.operation == "+" ? "+" : column.operation == "-" ? "-" : string.Empty;
                parts.Add($"{column.name} {prefix}{column.value:N2} CZK");
            }

            parts.Add($"До виплати {finalPayout:N2} CZK");

            if (!string.IsNullOrWhiteSpace(note))
                parts.Add($"Примітка: {note}");

            return string.Join(" | ", parts);
        }

        private static string BuildAllFirmsSalarySummaryText(AllFirmsSalaryInfo info)
        {
            var builder = new StringBuilder()
                .AppendLine($"Зарплата по всіх фірмах за {info.MonthDisplay}:")
                .AppendLine($"• Фірм з даними: {info.Firms.Count}")
                .AppendLine($"• Загальне брутто: {info.TotalGross:N2} CZK")
                .AppendLine($"• Загальне нетто: {info.TotalNet:N2} CZK")
                .AppendLine($"• Загальні аванси: {info.TotalAdvances:N2} CZK")
                .AppendLine($"• Загальні витрати: {info.TotalExpenses:N2} CZK")
                .AppendLine($"• Оплачено: {info.TotalPaidCount}, не оплачено: {info.TotalPendingCount}")
                .AppendLine("По фірмах:");

            foreach (var firm in info.Firms.Take(12))
            {
                builder.AppendLine($"• {firm.FirmName}: нетто {firm.TotalNet:N2} CZK, брутто {firm.TotalGross:N2} CZK, працівників {firm.Entries.Count}");
            }

            if (info.Firms.Count > 12)
                builder.AppendLine($"… і ще {info.Firms.Count - 12} фірм");

            return builder.ToString();
        }

        private static string BuildSalarySummaryText(SalaryInfo salaryInfo)
        {
            var record = salaryInfo.Record;
            var text = new StringBuilder()
                .AppendLine($"Зарплата: {record.FullName}")
                .AppendLine($"Місяць: {record.MonthDisplay}")
                .AppendLine($"Фірма: {record.FirmName}")
                .AppendLine($"Годин: {record.HoursWorked:N2}")
                .AppendLine($"Ставка: {record.HourlyRate:N2} CZK/год")
                .AppendLine($"Брутто: {record.GrossSalary:N2} CZK")
                .AppendLine($"Аванс у зарплаті: {record.Advance:N2} CZK")
                .AppendLine($"Додаткові аванси: {salaryInfo.TotalAdvances:N2} CZK");

            AppendCustomFieldLines(text, record.CustomFields);

            text.AppendLine($"Нетто: {record.NetSalary:N2} CZK")
                .AppendLine($"Статус виплати: {FormatPaymentStatus(salaryInfo)}");

            if (!string.IsNullOrWhiteSpace(record.Note))
                text.AppendLine($"Примітка: {record.Note}");

            return text.ToString();
        }

        private static string BuildSalaryAdvanceAnswer(SalaryInfo salaryInfo)
        {
            var record = salaryInfo.Record;
            var extraAdvances = salaryInfo.Advances
                .OrderBy(a => a.Date)
                .Select(a => $"• {a.Date:dd.MM.yyyy}: {a.Amount:N2} CZK")
                .ToList();

            var text = new StringBuilder()
                .AppendLine($"Аванси {record.FullName} за {record.MonthDisplay}:")
                .AppendLine($"• Аванс у зарплаті: {record.Advance:N2} CZK")
                .AppendLine($"• Додаткові аванси: {salaryInfo.TotalAdvances:N2} CZK");

            if (extraAdvances.Count > 0)
            {
                text.AppendLine("Деталі:");
                foreach (var line in extraAdvances.Take(6))
                    text.AppendLine(line);
            }

            return text.ToString();
        }

        private static string BuildSalaryBreakdownText(SalaryInfo salaryInfo)
        {
            var record = salaryInfo.Record;
            var text = new StringBuilder()
                .AppendLine($"Розпис зарплати {record.FullName} за {record.MonthDisplay}:")
                .AppendLine($"• Відпрацьовано: {record.HoursWorked:N2} год")
                .AppendLine($"• Ставка: {record.HourlyRate:N2} CZK/год")
                .AppendLine($"• Брутто: {record.GrossSalary:N2} CZK")
                .AppendLine($"• Аванс у зарплаті: {record.Advance:N2} CZK")
                .AppendLine($"• Додаткові аванси: {salaryInfo.TotalAdvances:N2} CZK");

            AppendCustomFieldLines(text, record.CustomFields, bulleted: true);

            text.AppendLine($"• Нетто: {record.NetSalary:N2} CZK")
                .AppendLine($"• Статус виплати: {FormatPaymentStatus(salaryInfo)}");

            if (!string.IsNullOrWhiteSpace(record.Note))
                text.AppendLine($"• Примітка: {record.Note}");

            return text.ToString();
        }

        private static void AppendCustomFieldLines(StringBuilder text, IReadOnlyCollection<CustomFieldSnapshot> customFields, bool bulleted = false)
        {
            if (customFields == null || customFields.Count == 0)
                return;

            var additions = customFields
                .Where(IsAdditionField)
                .ToList();
            var deductions = customFields
                .Where(field => !IsAdditionField(field))
                .ToList();

            if (additions.Count > 0)
            {
                text.AppendLine(bulleted ? "• Доплати:" : "Доплати:");
                foreach (var field in additions)
                    text.AppendLine($"  • {field.Name}: +{field.Value:N2} CZK");
            }

            if (deductions.Count > 0)
            {
                text.AppendLine(bulleted ? "• Утримання:" : "Утримання:");
                foreach (var field in deductions)
                    text.AppendLine($"  • {field.Name}: -{field.Value:N2} CZK");
            }
        }

        private static bool IsAdditionField(CustomFieldSnapshot field)
        {
            var operation = NormalizeForSearch(field?.Operation ?? string.Empty);
            return operation is "+" or "add" or "plus" or "додати" or "доплата";
        }

        private static string FormatPaymentStatus(SalaryInfo salaryInfo)
        {
            if (!salaryInfo.IsPaid)
                return "не виплачено";

            return salaryInfo.PaidAt.HasValue
                ? $"виплачено {salaryInfo.PaidAt.Value:dd.MM.yyyy}"
                : "виплачено";
        }

        private static string BuildSalaryAiContext(SalaryInfo salaryInfo)
        {
            var record = salaryInfo.Record;
            var builder = new StringBuilder()
                .AppendLine($"employee={record.FullName}")
                .AppendLine($"firm={record.FirmName}")
                .AppendLine($"month={record.MonthDisplay}")
                .AppendLine($"hours_worked={record.HoursWorked:N2}")
                .AppendLine($"hourly_rate={record.HourlyRate:N2} CZK")
                .AppendLine($"gross_salary={record.GrossSalary:N2} CZK")
                .AppendLine($"salary_advance={record.Advance:N2} CZK")
                .AppendLine($"extra_advances_total={salaryInfo.TotalAdvances:N2} CZK")
                .AppendLine($"net_salary={record.NetSalary:N2} CZK")
                .AppendLine($"payment_status={(salaryInfo.IsPaid ? "paid" : "pending")}")
                .AppendLine($"paid_at={(salaryInfo.PaidAt.HasValue ? salaryInfo.PaidAt.Value.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : string.Empty)}");

            if (!string.IsNullOrWhiteSpace(record.Note))
                builder.AppendLine($"note={record.Note}");

            if (salaryInfo.Advances.Count > 0)
            {
                builder.AppendLine("extra_advances:");
                foreach (var advance in salaryInfo.Advances.Take(10))
                    builder.AppendLine($"- {advance.Date:dd.MM.yyyy}: {advance.Amount:N2} CZK; note={advance.Note}");
            }

            if (record.CustomFields.Count > 0)
            {
                builder.AppendLine("custom_fields:");
                foreach (var field in record.CustomFields.Take(12))
                    builder.AppendLine($"- {field.Name}: {field.Value:N2} CZK; operation={field.Operation}");
            }

            return builder.ToString();
        }

        private List<(string role, string text)> GetConversationHistory(long userId)
        {
            var context = GetConversationContext(userId);
            if (context == null)
                return new List<(string role, string text)>();

            var history = context.History
                .TakeLast(ConversationTurnsSentToAi)
                .ToList();

            if (!string.IsNullOrWhiteSpace(context.HistorySummary))
            {
                history.Insert(0, ("user", $"Earlier conversation summary:\n{context.HistorySummary}"));
            }

            return history;
        }

        private void AppendConversationTurn(long userId, string role, string text)
        {
            var context = GetOrCreateConversationContext(userId);
            context.History.Add((NormalizeConversationRole(role), NormalizeConversationText(text)));
            CompressConversationHistoryIfNeeded(context);
            context.UpdatedAtUtc = DateTime.UtcNow;
        }

        private void CleanupExpiredConversationContexts()
        {
            var cutoff = DateTime.UtcNow.AddHours(-ConversationContextTtlHours);
            foreach (var key in _conversationContexts.Where(x => x.Value.UpdatedAtUtc < cutoff).Select(x => x.Key).ToList())
                _conversationContexts.TryRemove(key, out _);
        }

        private async Task RunDailyDigestLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await TrySendDailyDigestAsync(cancellationToken).ConfigureAwait(false);
                    await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("TelegramBot.DailyDigestLoop", ex.Message);
            }
        }

        private async Task TrySendDailyDigestAsync(CancellationToken cancellationToken)
        {
            if (_client == null)
                return;

            var settings = _appSettingsService.Settings.Telegram;
            if (!settings.Enabled || !settings.DailyDigestEnabled || settings.AuthorizedUsers.Count == 0)
                return;

            if (!TimeSpan.TryParse(settings.DailyDigestTime, CultureInfo.InvariantCulture, out var digestTime))
                digestTime = new TimeSpan(8, 0, 0);

            var now = DateTime.Now;
            if (now.Hour != digestTime.Hours || now.Minute != digestTime.Minutes)
                return;

            var localDateKey = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (string.Equals(_lastDailyDigestLocalDate, localDateKey, StringComparison.Ordinal))
                return;

            var digestText = BuildDailyDigestText(now);
            foreach (var user in settings.AuthorizedUsers.Where(user => IsAdminRole(user.Role)).ToList())
            {
                await SendMessageAsync(_client, user.TelegramUserId, digestText, cancellationToken).ConfigureAwait(false);
            }

            _lastDailyDigestLocalDate = localDateKey;
            LoggingService.LogInfo("TelegramBot.DailyDigest", $"Sent digest to {settings.AuthorizedUsers.Count(user => IsAdminRole(user.Role))} admin users for {localDateKey}");
        }

        private string BuildDailyDigestText(DateTime now)
        {
            var builder = new StringBuilder()
                .AppendLine($"Щоденний дайджест на {now:dd.MM.yyyy}:");

            var expiring = BuildExpiringItems(DailyDigestLookAheadDays).Take(5).ToList();
            if (expiring.Count > 0)
            {
                builder.AppendLine($"• Документи, що закінчуються за {DailyDigestLookAheadDays} днів: {expiring.Count}");
                foreach (var item in expiring)
                    builder.AppendLine($"  • {item.employee} ({item.firm}) | {item.type} до {item.date} | залишилось {item.daysLeft} дн.");
            }
            else
            {
                builder.AppendLine($"• Документи, що закінчуються за {DailyDigestLookAheadDays} днів: немає");
            }

            var currentMonthKey = now.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            var monthSalary = ResolveAllFirmsSalaryInfo(currentMonthKey);
            if (monthSalary != null)
                builder.AppendLine($"• Неоплачено зарплат за {currentMonthKey}: {monthSalary.TotalPendingCount}");

            var yesterday = now.Date.AddDays(-1);
            var newEmployeesYesterday = GetAllEmployeeRecords(includeArchived: true, includeRecentlyDeleted: false)
                .Where(employee => TryParseFlexibleDate(employee.StartDate, out var startDate) && startDate.Date == yesterday)
                .GroupBy(employee => $"{employee.UniqueId}|{employee.StartDate}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Take(5)
                .ToList();
            builder.AppendLine($"• Нових працівників за вчора: {newEmployeesYesterday.Count}");
            foreach (var employee in newEmployeesYesterday)
                builder.AppendLine($"  • {employee.FullName} ({employee.FirmName})");

            var endedYesterday = GetAllArchivedEmployees()
                .Where(employee => TryParseFlexibleDate(employee.EndDate, out var endDate) && endDate.Date == yesterday)
                .Take(5)
                .ToList();
            builder.AppendLine($"• Завершили роботу за вчора: {endedYesterday.Count}");
            foreach (var employee in endedYesterday)
                builder.AppendLine($"  • {employee.FullName} ({employee.FirmName})");

            return builder.ToString().TrimEnd();
        }

        private void CompressConversationHistoryIfNeeded(ConversationContext context)
        {
            if (context.History.Count <= MaxConversationTurns)
                return;

            var turnsToSummarize = context.History
                .Take(Math.Max(0, context.History.Count - ConversationTurnsAfterCompression))
                .ToList();
            if (turnsToSummarize.Count == 0)
                return;

            context.History.RemoveRange(0, turnsToSummarize.Count);
            context.HistorySummary = BuildConversationHistorySummary(context.HistorySummary, turnsToSummarize);
        }

        private static string BuildConversationHistorySummary(string existingSummary, IReadOnlyList<(string role, string text)> turns)
        {
            var userItems = turns
                .Where(item => string.Equals(item.role, "user", StringComparison.OrdinalIgnoreCase))
                .Select(item => ShortenConversationSnippet(item.text, 140))
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.Ordinal)
                .Take(3)
                .ToList();
            var modelItems = turns
                .Where(item => string.Equals(item.role, "model", StringComparison.OrdinalIgnoreCase))
                .Select(item => ShortenConversationSnippet(item.text, 140))
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.Ordinal)
                .Take(3)
                .ToList();

            var builder = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(existingSummary))
                builder.AppendLine(existingSummary.Trim());
            if (userItems.Count > 0)
                builder.AppendLine("Раніше користувач питав: " + string.Join(" | ", userItems));
            if (modelItems.Count > 0)
                builder.AppendLine("Раніше бот уже відповів: " + string.Join(" | ", modelItems));

            return ShortenConversationSnippet(builder.ToString().Trim(), MaxConversationSummaryLength);
        }

        private static string NormalizeConversationRole(string role)
        {
            return string.Equals(role, "model", StringComparison.OrdinalIgnoreCase) ? "model" : "user";
        }

        private static string NormalizeConversationText(string? text)
        {
            return ShortenConversationSnippet(text, MaxConversationTurnTextLength);
        }

        private static string BuildConversationModelHistoryEntry(string answer, string toolName, EmployeeSummary? employee, string? firmName, string? monthKey)
        {
            var details = new List<string>();
            if (!string.IsNullOrWhiteSpace(toolName))
                details.Add($"used={toolName}");
            if (!string.IsNullOrWhiteSpace(employee?.FullName))
                details.Add($"employee={employee.FullName}");
            if (!string.IsNullOrWhiteSpace(firmName))
                details.Add($"firm={firmName}");
            if (!string.IsNullOrWhiteSpace(monthKey))
                details.Add($"month={monthKey}");

            if (details.Count == 0)
                return answer;

            return $"[{string.Join("; ", details)}] {answer}";
        }

        private static string ShortenConversationSnippet(string? text, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var compact = Regex.Replace(text.Trim(), @"\s+", " ");
            if (compact.Length <= maxLength)
                return compact;

            return compact[..Math.Max(0, maxLength - 1)].TrimEnd() + "…";
        }

        private EmployerCompany? FindFirmFromText(string text)
            => FindFirmMatches(text, 1).FirstOrDefault()?.Company;

        private List<FirmMatchResult> FindFirmMatches(string text, int limit)
        {
            var normalized = NormalizeForSearch(text);
            if (string.IsNullOrWhiteSpace(normalized))
                return new List<FirmMatchResult>();

            var queryTokens = Tokenize(text)
                .Select(NormalizeNameToken)
                .Where(token => token.Length >= 2)
                .Where(token => !FirmQueryNoiseWords.Contains(token))
                .Where(token => !IsMonthAlias(token))
                .Where(token => !IsPureNumber(token))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var queryPhrase = string.Join(" ", queryTokens);
            var queryVariants = GetSearchVariants(string.IsNullOrWhiteSpace(queryPhrase) ? normalized : queryPhrase);

            return _companyService.VisibleCompanies
                .OrderByDescending(c => c.Name.Length)
                .Select(company =>
                {
                    var score = CalculateFirmScore(company, queryVariants, queryTokens);
                    return new FirmMatchResult
                    {
                        Company = company,
                        Score = score
                    };
                })
                .Where(match => match.Score > 0)
                .OrderByDescending(match => match.Score)
                .ThenBy(match => match.Company.Name)
                .Take(Math.Max(1, limit))
                .ToList();
        }

        private static string ExtractEmployeeQuery(string text, string? firmName = null, IEnumerable<string>? extraNoiseWords = null)
        {
            var tokens = Tokenize(text);
            if (tokens.Count == 0)
                return string.Empty;

            var firmTokens = string.IsNullOrWhiteSpace(firmName)
                ? new HashSet<string>(StringComparer.Ordinal)
                : Tokenize(firmName)
                    .Select(NormalizeNameToken)
                    .Where(token => token.Length >= 2)
                    .ToHashSet(StringComparer.Ordinal);

            var extraNoiseTokens = extraNoiseWords == null
                ? new HashSet<string>(StringComparer.Ordinal)
                : extraNoiseWords
                    .Select(NormalizeNameToken)
                    .Where(token => token.Length >= 2)
                    .ToHashSet(StringComparer.Ordinal);

            var filtered = tokens
                .Select(NormalizeNameToken)
                .Where(token => token.Length >= 2)
                .Where(token => !MatchesNoiseToken(token, EmployeeQueryNoiseWords))
                .Where(token => !MatchesNoiseToken(token, extraNoiseTokens))
                .Where(token => !firmTokens.Contains(token))
                .Where(token => !IsMonthAlias(token))
                .Where(token => !IsPureNumber(token))
                .ToList();

            if (filtered.Count == 0)
                return string.Empty;

            if (filtered.Count == 1)
                return filtered[0];

            return string.Join(" ", filtered.Take(3));
        }

        private static bool MatchesNoiseToken(string token, IEnumerable<string> noiseWords)
        {
            if (string.IsNullOrWhiteSpace(token))
                return false;

            foreach (var rawNoise in noiseWords)
            {
                var noise = NormalizeNameToken(rawNoise);
                if (noise.Length == 0)
                    continue;

                if (string.Equals(token, noise, StringComparison.Ordinal)
                    || token.StartsWith(noise, StringComparison.Ordinal)
                    || noise.StartsWith(token, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeForSearch(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var prepared = text.Trim().Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(text.Length);
            var previousWasSeparator = false;
            foreach (var ch in prepared.ToLowerInvariant())
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                    continue;

                if (char.IsLetterOrDigit(ch))
                {
                    builder.Append(ch);
                    previousWasSeparator = false;
                }
                else if (!previousWasSeparator)
                {
                    builder.Append(' ');
                    previousWasSeparator = true;
                }
            }

            return builder.ToString().Trim();
        }

        private static int CalculateFirmScore(EmployerCompany company, IReadOnlyList<string> queryVariants, IReadOnlyList<string> queryTokens)
        {
            if (queryVariants.Count == 0)
                return 0;

            var firmVariants = GetSearchVariants(company.Name);
            var score = 0;

            foreach (var firmVariant in firmVariants)
            {
                foreach (var queryVariant in queryVariants)
                {
                    if (string.Equals(firmVariant, queryVariant, StringComparison.OrdinalIgnoreCase))
                        score = Math.Max(score, 140);
                    else if (queryVariant.Contains(firmVariant, StringComparison.OrdinalIgnoreCase))
                        score = Math.Max(score, 110);
                    else if (firmVariant.Contains(queryVariant, StringComparison.OrdinalIgnoreCase) && queryVariant.Length >= 4)
                        score = Math.Max(score, 95);
                    else
                    {
                        var distance = ComputeLevenshteinDistance(firmVariant, queryVariant);
                        if (distance <= 2)
                            score = Math.Max(score, 90 - distance * 10);
                        else if (distance <= 4 && queryVariant.Length >= 5)
                            score = Math.Max(score, 55 - distance * 5);
                    }
                }
            }

            var firmTokens = Tokenize(company.Name)
                .Select(NormalizeNameToken)
                .Where(token => token.Length >= 2)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var matchedTokens = 0;
            var matchedShortCode = false;
            var matchedKeyName = false;
            foreach (var firmToken in firmTokens)
            {
                var bestTokenScore = 0;
                var bestMatchedQueryToken = string.Empty;
                var firmTokenVariants = GetFirmTokenVariants(firmToken);
                foreach (var queryToken in queryTokens)
                {
                    foreach (var firmTokenVariant in firmTokenVariants)
                    {
                        foreach (var queryTokenVariant in GetFirmTokenVariants(queryToken))
                        {
                            if (string.Equals(firmTokenVariant, queryTokenVariant, StringComparison.OrdinalIgnoreCase))
                            {
                                if (bestTokenScore < 40)
                                {
                                    bestTokenScore = 40;
                                    bestMatchedQueryToken = queryToken;
                                }
                            }
                            else if (firmTokenVariant.StartsWith(queryTokenVariant, StringComparison.OrdinalIgnoreCase)
                                     || queryTokenVariant.StartsWith(firmTokenVariant, StringComparison.OrdinalIgnoreCase))
                            {
                                if (bestTokenScore < 28)
                                {
                                    bestTokenScore = 28;
                                    bestMatchedQueryToken = queryToken;
                                }
                            }
                            else
                            {
                                var tokenDistance = ComputeLevenshteinDistance(firmTokenVariant, queryTokenVariant);
                                if (tokenDistance == 1 && bestTokenScore < 24)
                                {
                                    bestTokenScore = 24;
                                    bestMatchedQueryToken = queryToken;
                                }
                                else if (tokenDistance == 2 && bestTokenScore < 18)
                                {
                                    bestTokenScore = 18;
                                    bestMatchedQueryToken = queryToken;
                                }
                            }
                        }
                    }
                }

                if (bestTokenScore > 0)
                {
                    matchedTokens++;
                    score += bestTokenScore;
                    if (firmToken.Length <= 4 && !string.IsNullOrWhiteSpace(bestMatchedQueryToken))
                        matchedShortCode = true;
                    if (firmToken.Length >= 5 && !string.IsNullOrWhiteSpace(bestMatchedQueryToken))
                        matchedKeyName = true;
                }
            }

            if (matchedTokens >= 2)
                score += 20;
            if (matchedShortCode)
                score += 18;
            if (matchedKeyName)
                score += 22;
            if (matchedShortCode && matchedKeyName)
                score += 35;

            return score >= 60 ? score : 0;
        }

        private static int ComputeLevenshteinDistance(string source, string target)
        {
            source ??= string.Empty;
            target ??= string.Empty;
            if (source.Length == 0)
                return target.Length;
            if (target.Length == 0)
                return source.Length;

            var matrix = new int[source.Length + 1, target.Length + 1];
            for (var i = 0; i <= source.Length; i++)
                matrix[i, 0] = i;
            for (var j = 0; j <= target.Length; j++)
                matrix[0, j] = j;

            for (var i = 1; i <= source.Length; i++)
            {
                for (var j = 1; j <= target.Length; j++)
                {
                    var cost = source[i - 1] == target[j - 1] ? 0 : 1;
                    matrix[i, j] = Math.Min(
                        Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                        matrix[i - 1, j - 1] + cost);
                }
            }

            return matrix[source.Length, target.Length];
        }

        private static List<string> GetSearchVariants(string text)
        {
            var normalized = NormalizeForSearch(text);
            if (string.IsNullOrWhiteSpace(normalized))
                return new List<string>();

            var transliterated = TransliterateCyrillicToLatin(normalized);
            var variants = new HashSet<string>(StringComparer.Ordinal)
            {
                normalized,
                transliterated
            };

            AddFirmVariantForms(variants, normalized);
            AddFirmVariantForms(variants, transliterated);

            if (transliterated.Contains("j", StringComparison.Ordinal))
                AddFirmVariantForms(variants, transliterated.Replace("j", "y", StringComparison.Ordinal));
            if (transliterated.Contains("kh", StringComparison.Ordinal))
                AddFirmVariantForms(variants, transliterated.Replace("kh", "h", StringComparison.Ordinal));
            if (transliterated.Contains("shch", StringComparison.Ordinal))
                AddFirmVariantForms(variants, transliterated.Replace("shch", "sch", StringComparison.Ordinal));
            if (transliterated.Contains("sh", StringComparison.Ordinal))
                AddFirmVariantForms(variants, transliterated.Replace("sh", "s", StringComparison.Ordinal));
            if (transliterated.Contains("zh", StringComparison.Ordinal))
                AddFirmVariantForms(variants, transliterated.Replace("zh", "z", StringComparison.Ordinal));
            if (transliterated.Contains("ou", StringComparison.Ordinal))
                AddFirmVariantForms(variants, transliterated.Replace("ou", "u", StringComparison.Ordinal));
            if (transliterated.Contains("ck", StringComparison.Ordinal))
                AddFirmVariantForms(variants, transliterated.Replace("ck", "sk", StringComparison.Ordinal));
            if (transliterated.Contains("sk", StringComparison.Ordinal))
                AddFirmVariantForms(variants, transliterated.Replace("sk", "ck", StringComparison.Ordinal));
            if (transliterated.Contains("h", StringComparison.Ordinal))
                AddFirmVariantForms(variants, transliterated.Replace("h", "g", StringComparison.Ordinal));
            if (transliterated.Contains("g", StringComparison.Ordinal))
                AddFirmVariantForms(variants, transliterated.Replace("g", "h", StringComparison.Ordinal));

            return variants.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        }

        private static void AddFirmVariantForms(HashSet<string> variants, string value)
        {
            var normalized = NormalizeForSearch(value);
            if (string.IsNullOrWhiteSpace(normalized))
                return;

            variants.Add(normalized);

            var compact = normalized.Replace(" ", string.Empty, StringComparison.Ordinal);
            if (!string.IsNullOrWhiteSpace(compact))
                variants.Add(compact);

            var stripped = StripFirmSuffixNoise(normalized);
            if (!string.IsNullOrWhiteSpace(stripped))
            {
                variants.Add(stripped);
                var strippedCompact = stripped.Replace(" ", string.Empty, StringComparison.Ordinal);
                if (!string.IsNullOrWhiteSpace(strippedCompact))
                    variants.Add(strippedCompact);
            }
        }

        private static string StripFirmSuffixNoise(string text)
        {
            var tokens = Tokenize(text)
                .Where(token => token.Length >= 1)
                .Where(token => !FirmSuffixNoiseWords.Contains(token))
                .ToList();

            if (tokens.Count >= 2)
                return string.Join(" ", tokens);

            return NormalizeForSearch(text);
        }

        private static IReadOnlyList<string> GetFirmTokenVariants(string token)
        {
            var normalized = NormalizeNameToken(token);
            if (string.IsNullOrWhiteSpace(normalized))
                return Array.Empty<string>();

            var transliterated = TransliterateCyrillicToLatin(normalized);
            var variants = new HashSet<string>(StringComparer.Ordinal)
            {
                normalized,
                transliterated
            };

            void AddReplacementPair(string from, string to)
            {
                foreach (var current in variants.ToList())
                {
                    if (current.Contains(from, StringComparison.Ordinal))
                        variants.Add(current.Replace(from, to, StringComparison.Ordinal));
                }
            }

            AddReplacementPair("j", "y");
            AddReplacementPair("kh", "h");
            AddReplacementPair("ou", "u");
            AddReplacementPair("u", "ou");
            AddReplacementPair("sh", "s");
            AddReplacementPair("s", "sh");
            AddReplacementPair("zh", "z");
            AddReplacementPair("z", "zh");
            AddReplacementPair("ck", "sk");
            AddReplacementPair("sk", "ck");
            AddReplacementPair("h", "g");
            AddReplacementPair("g", "h");
            AddReplacementPair("y", "i");
            AddReplacementPair("i", "y");

            if (normalized.Length <= 4)
            {
                AddReplacementPair("c", "k");
                AddReplacementPair("k", "c");
            }

            return variants.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        }

        private static string TransliterateCyrillicToLatin(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var builder = new StringBuilder(text.Length);
            foreach (var ch in text)
            {
                builder.Append(ch switch
                {
                    'а' => "a",
                    'б' => "b",
                    'в' => "v",
                    'г' => "h",
                    'ґ' => "g",
                    'д' => "d",
                    'е' => "e",
                    'є' => "ye",
                    'ж' => "zh",
                    'з' => "z",
                    'и' => "y",
                    'і' => "i",
                    'ї' => "yi",
                    'й' => "y",
                    'к' => "k",
                    'л' => "l",
                    'м' => "m",
                    'н' => "n",
                    'о' => "o",
                    'п' => "p",
                    'р' => "r",
                    'с' => "s",
                    'т' => "t",
                    'у' => "u",
                    'ф' => "f",
                    'х' => "kh",
                    'ц' => "ts",
                    'ч' => "ch",
                    'ш' => "sh",
                    'щ' => "shch",
                    'ь' => "",
                    'ю' => "yu",
                    'я' => "ya",
                    'ы' => "y",
                    'э' => "e",
                    'ё' => "yo",
                    'ъ' => "",
                    _ => ch.ToString()
                });
            }

            return builder.ToString();
        }

        private static List<string> Tokenize(string text)
        {
            var normalized = NormalizeForSearch(text);
            return normalized
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }

        private static string NormalizeNameToken(string token)
        {
            var value = NormalizeForSearch(token);
            if (value.Length <= 3)
                return value;

            string[] endings =
            {
                "ового", "евому", "ового", "ому", "ого", "ими", "ами", "ями",
                "еві", "ові", "єві", "ом", "ем", "ою", "ею", "ий", "ій",
                "а", "я", "у", "ю", "і"
            };

            foreach (var ending in endings.OrderByDescending(x => x.Length))
            {
                if (value.Length > ending.Length + 2 && value.EndsWith(ending, StringComparison.Ordinal))
                    return value[..^ending.Length];
            }

            return value;
        }

        private static bool IsMonthAlias(string token)
        {
            var normalized = NormalizeForSearch(token);
            return MonthAliases.Values.Any(aliases => aliases.Contains(normalized, StringComparer.Ordinal));
        }

        private static bool IsPureNumber(string token)
        {
            return token.All(char.IsDigit);
        }

        private static bool ContainsAny(string text, IEnumerable<string> keywords)
        {
            return keywords.Any(keyword => text.Contains(keyword, StringComparison.Ordinal));
        }

        private static bool IsFollowUpQuestion(string normalizedText)
        {
            if (string.IsNullOrWhiteSpace(normalizedText))
                return false;

            if (ContainsAny(normalizedText, PronounMarkers))
                return true;

            if (IsComparisonRequest(normalizedText) && normalizedText.Length <= 40)
                return true;

            return FollowUpMarkers.Any(marker => normalizedText.StartsWith(marker, StringComparison.Ordinal)
                                                 || normalizedText.Contains($" {marker}", StringComparison.Ordinal));
        }

        private static bool RefersToContextFirm(string normalizedText)
        {
            return normalizedText.Contains("цій же фірм", StringComparison.Ordinal)
                   || normalizedText.Contains("цій фірм", StringComparison.Ordinal)
                   || normalizedText.Contains("тій же фірм", StringComparison.Ordinal)
                   || normalizedText.Contains("тій фірм", StringComparison.Ordinal)
                   || normalizedText.Contains("по цій", StringComparison.Ordinal)
                   || normalizedText.Contains("по тій", StringComparison.Ordinal)
                   || normalizedText.Contains("на цій", StringComparison.Ordinal)
                   || normalizedText.Contains("на тій", StringComparison.Ordinal);
        }

        private static bool IsComparisonRequest(string normalizedText)
        {
            return normalizedText.Contains("порівня", StringComparison.Ordinal)
                   || normalizedText.Contains("різниц", StringComparison.Ordinal)
                   || normalizedText.Contains("поривня", StringComparison.Ordinal);
        }

        private static bool TryResolveRelativeMonthKey(string normalizedText, string? baseMonthKey, out string monthKey)
        {
            monthKey = string.Empty;
            if (string.IsNullOrWhiteSpace(baseMonthKey) || !TryParseMonthKey(baseMonthKey, out var baseMonth))
                return false;

            if (normalizedText.Contains("наступ", StringComparison.Ordinal)
                || normalizedText.Contains("слідую", StringComparison.Ordinal))
            {
                var nextMonth = baseMonth.AddMonths(1);
                monthKey = $"{nextMonth.Year:D4}-{nextMonth.Month:D2}";
                return true;
            }

            if (normalizedText.Contains("поперед", StringComparison.Ordinal)
                || normalizedText.Contains("минулий", StringComparison.Ordinal)
                || normalizedText.Contains("предидущ", StringComparison.Ordinal))
            {
                var previousMonth = baseMonth.AddMonths(-1);
                monthKey = $"{previousMonth.Year:D4}-{previousMonth.Month:D2}";
                return true;
            }

            if (normalizedText.Contains("цей місяц", StringComparison.Ordinal)
                || normalizedText.Contains("за місяц", StringComparison.Ordinal))
            {
                monthKey = $"{baseMonth.Year:D4}-{baseMonth.Month:D2}";
                return true;
            }

            return false;
        }

        private static bool MentionsFirmScope(string normalizedText)
        {
            return normalizedText.Contains("фірм", StringComparison.Ordinal)
                   || normalizedText.Contains("компан", StringComparison.Ordinal);
        }

        private static bool IsFirmSalaryQuestion(string normalizedText)
        {
            return normalizedText.Contains("фірм", StringComparison.Ordinal)
                   || normalizedText.Contains("компан", StringComparison.Ordinal)
                   || normalizedText.Contains("всієї", StringComparison.Ordinal)
                   || normalizedText.Contains("всю", StringComparison.Ordinal)
                   || normalizedText.Contains("цілу", StringComparison.Ordinal);
        }

        private static bool IsAllFirmsSalaryQuestion(string normalizedText)
        {
            return AllFirmsMarkers.Any(marker => normalizedText.Contains(marker, StringComparison.Ordinal))
                   || (normalizedText.Contains("всіх", StringComparison.Ordinal) && normalizedText.Contains("фірм", StringComparison.Ordinal))
                   || (normalizedText.Contains("усіх", StringComparison.Ordinal) && normalizedText.Contains("фірм", StringComparison.Ordinal));
        }

        private static int DetectExpiringDays(string normalizedText)
        {
            if (normalizedText.Contains("завтра", StringComparison.Ordinal))
                return 1;
            if (normalizedText.Contains("тиж", StringComparison.Ordinal) || normalizedText.Contains("7", StringComparison.Ordinal))
                return 7;
            if (normalizedText.Contains("місяц", StringComparison.Ordinal) || normalizedText.Contains("30", StringComparison.Ordinal))
                return 30;
            return 30;
        }

        private static string InferTopicFromTool(string toolName, string userMessage)
        {
            if (string.IsNullOrWhiteSpace(toolName))
                return ContainsAny(NormalizeForSearch(userMessage), SalaryKeywords) ? "salary" : string.Empty;

            return toolName switch
            {
                "get_salary" or "get_firm_salary" or "get_all_firms_salary" or "get_firm_period_summary" or "compare_salary_months" => "salary",
                "export_firm_salary_excel" => "salary",
                "get_top_payouts_for_month" => "salary",
                "get_hiring_summary" or "get_termination_summary" or "get_employee_flow_summary" or "get_employee_flow_period" => "employee_flow",
                "get_employee" or "resolve_employee" or "get_employee_employment" or "get_employee_full_summary" or "get_employee_history" or "get_employee_status_overview" or "get_employee_timeline" or "list_archived_employees" => "employee",
                "send_employee_document" => "documents",
                "get_company_profile" or "resolve_firm" or "list_firms" => "firm",
                "get_program_help" or "list_program_capabilities" => "program_help",
                "get_external_updates" => "external_updates",
                "get_expiring_documents" => "documents",
                _ => string.Empty
            };
        }

        private bool TryExtractMonthKey(string text, out string monthKey)
        {
            monthKey = string.Empty;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var trimmed = text.Trim();
            var compactMonthMatch = Regex.Match(trimmed, @"(?<!\d)(?<month>\d{2})[./-](?<year>\d{4})(?!\d)");
            if (compactMonthMatch.Success
                && TryParseMonthKey(compactMonthMatch.Value, out var compactMonth))
            {
                monthKey = $"{compactMonth.Year:D4}-{compactMonth.Month:D2}";
                return true;
            }

            var isoMonthMatch = Regex.Match(trimmed, @"(?<!\d)(?<year>\d{4})-(?<month>\d{2})(?!\d)");
            if (isoMonthMatch.Success
                && TryParseMonthKey(isoMonthMatch.Value, out var isoMonth))
            {
                monthKey = $"{isoMonth.Year:D4}-{isoMonth.Month:D2}";
                return true;
            }

            var parts = trimmed.Split(new[] { ' ', ',', ';', ':', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (TryParseMonthKey(part, out var parsedMonth))
                {
                    monthKey = $"{parsedMonth.Year:D4}-{parsedMonth.Month:D2}";
                    return true;
                }
            }

            var normalized = NormalizeForSearch(text);
            if (normalized.Contains("за місяц", StringComparison.Ordinal) || normalized.Contains("за місяць", StringComparison.Ordinal))
            {
                var now = DateTime.Today;
                monthKey = $"{now.Year:D4}-{now.Month:D2}";
                return true;
            }
            if (normalized.Contains("цей місяц", StringComparison.Ordinal))
            {
                var now = DateTime.Today;
                monthKey = $"{now.Year:D4}-{now.Month:D2}";
                return true;
            }

            if (normalized.Contains("минулий місяц", StringComparison.Ordinal) || normalized.Contains("попередній місяц", StringComparison.Ordinal))
            {
                var previous = DateTime.Today.AddMonths(-1);
                monthKey = $"{previous.Year:D4}-{previous.Month:D2}";
                return true;
            }

            var year = DateTime.Today.Year;
            foreach (var entry in MonthAliases)
            {
                if (entry.Value.Any(alias => normalized.Contains(alias, StringComparison.Ordinal)))
                {
                    var explicitYear = parts.FirstOrDefault(p => p.Length == 4 && int.TryParse(p, out _));
                    if (!string.IsNullOrWhiteSpace(explicitYear) && int.TryParse(explicitYear, out var parsedYear))
                        year = parsedYear;

                    monthKey = $"{year:D4}-{entry.Key:D2}";
                    return true;
                }
            }

            return false;
        }

        private static void AddIfExpiring(EmployeeSummary employee, string type, string date, int days, ICollection<(string employee, string firm, string type, string date, int daysLeft)> target)
        {
            if (string.IsNullOrWhiteSpace(date))
                return;

            var daysLeft = DateParsingHelper.GetDaysRemaining(date);
            if (daysLeft <= days)
            {
                target.Add((employee.FullName, employee.FirmName, type, date, daysLeft));
            }
        }

        private static string FormatDaysLeft(int daysLeft)
        {
            return daysLeft switch
            {
                < 0 => $"прострочено {-daysLeft} дн. тому",
                0 => "сьогодні",
                1 => "1 день залишився",
                _ => $"{daysLeft} дн. залишилось"
            };
        }

        private static string FormatExpiry(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "не задано";

            var days = DateParsingHelper.GetDaysRemaining(value);
            return $"{value} ({FormatDaysLeft(days)})";
        }

        private static string FormatAddress(EmployeeAddress address)
        {
            if (address == null)
                return string.Empty;

            return string.Join(", ",
                new[] { address.Street, address.Number, address.City, address.Zip }
                    .Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        private static bool TryParseMonthKey(string input, out DateTime month)
        {
            var value = (input ?? string.Empty).Trim();
            return DateTime.TryParseExact(
                value,
                new[] { "yyyy-MM", "MM.yyyy", "MM-yyyy", "MM/yyyy", "yyyy.MM", "yyyy/MM" },
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out month);
        }

        private static bool HasInsuranceNumberIntent(string normalizedQuestion)
        {
            return normalizedQuestion.Contains("номер", StringComparison.Ordinal)
                   || normalizedQuestion.Contains("ном", StringComparison.Ordinal)
                   || normalizedQuestion.Contains("поліс", StringComparison.Ordinal)
                   || normalizedQuestion.Contains("полис", StringComparison.Ordinal);
        }

        private static bool HasDocumentFileRequestIntent(string normalizedQuestion)
        {
            if (string.IsNullOrWhiteSpace(normalizedQuestion))
                return false;

            return ContainsAny(normalizedQuestion, DocumentFileRequestKeywords)
                   || normalizedQuestion.StartsWith("візу ", StringComparison.Ordinal)
                   || normalizedQuestion.StartsWith("віза ", StringComparison.Ordinal)
                   || normalizedQuestion.StartsWith("страхов", StringComparison.Ordinal)
                   || normalizedQuestion.StartsWith("страхув", StringComparison.Ordinal)
                   || normalizedQuestion.StartsWith("паспорт ", StringComparison.Ordinal)
                   || normalizedQuestion.StartsWith("дозвіл ", StringComparison.Ordinal)
                   || normalizedQuestion.StartsWith("фото ", StringComparison.Ordinal);
        }

        private static bool TryDetectRequestedDocumentType(string normalizedQuestion, out string documentType)
        {
            documentType = string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedQuestion))
                return false;

            if (normalizedQuestion.Contains("страх", StringComparison.Ordinal)
                || normalizedQuestion.Contains("поліс", StringComparison.Ordinal)
                || normalizedQuestion.Contains("полис", StringComparison.Ordinal))
            {
                documentType = "insurance";
                return true;
            }

            if (normalizedQuestion.Contains("віз", StringComparison.Ordinal)
                || normalizedQuestion.Contains("виза", StringComparison.Ordinal)
                || normalizedQuestion.Contains("visa", StringComparison.Ordinal))
            {
                documentType = "visa";
                return true;
            }

            if (normalizedQuestion.Contains("паспорт", StringComparison.Ordinal))
            {
                documentType = "passport";
                return true;
            }

            if (normalizedQuestion.Contains("дозв", StringComparison.Ordinal)
                || normalizedQuestion.Contains("work permit", StringComparison.Ordinal))
            {
                documentType = "work_permit";
                return true;
            }

            if (normalizedQuestion.Contains("фото", StringComparison.Ordinal)
                || normalizedQuestion.Contains("photo", StringComparison.Ordinal))
            {
                documentType = "photo";
                return true;
            }

            return false;
        }

        private static bool HasInsuranceExpiryIntent(string normalizedQuestion)
        {
            return normalizedQuestion.Contains("до коли", StringComparison.Ordinal)
                   || normalizedQuestion.Contains("доколи", StringComparison.Ordinal)
                   || normalizedQuestion.Contains("дійсн", StringComparison.Ordinal)
                   || normalizedQuestion.Contains("термін", StringComparison.Ordinal)
                   || normalizedQuestion.Contains("закінч", StringComparison.Ordinal);
        }

        private static bool HasEmploymentEndIntent(string normalizedQuestion)
        {
            return normalizedQuestion.Contains("закінчив", StringComparison.Ordinal)
                   || normalizedQuestion.Contains("закінчила", StringComparison.Ordinal)
                   || normalizedQuestion.Contains("закінчило", StringComparison.Ordinal)
                   || normalizedQuestion.Contains("кінець роботи", StringComparison.Ordinal)
                   || normalizedQuestion.Contains("до коли працю", StringComparison.Ordinal)
                   || normalizedQuestion.Contains("звіль", StringComparison.Ordinal)
                   || (normalizedQuestion.Contains("закінч", StringComparison.Ordinal)
                       && normalizedQuestion.Contains("робот", StringComparison.Ordinal));
        }

        private static bool HasEmploymentStartIntent(string normalizedQuestion)
        {
            return normalizedQuestion.Contains("коли почав", StringComparison.Ordinal)
                   || normalizedQuestion.Contains("коли почала", StringComparison.Ordinal)
                   || normalizedQuestion.Contains("почав працю", StringComparison.Ordinal)
                   || normalizedQuestion.Contains("почала працю", StringComparison.Ordinal)
                   || normalizedQuestion.Contains("дата початку", StringComparison.Ordinal)
                   || normalizedQuestion.Contains("початок роботи", StringComparison.Ordinal)
                   || normalizedQuestion.Contains("з якого числа", StringComparison.Ordinal);
        }

        private static bool HasEmploymentStatusIntent(string normalizedQuestion)
        {
            return normalizedQuestion.Contains("статус", StringComparison.Ordinal)
                   || normalizedQuestion.Contains("активн", StringComparison.Ordinal)
                   || normalizedQuestion.Contains("працює чи", StringComparison.Ordinal);
        }

        private static bool HasFirmHistoryIntent(string normalizedQuestion)
        {
            return normalizedQuestion.Contains("на яких фірм", StringComparison.Ordinal)
                   || normalizedQuestion.Contains("де працював", StringComparison.Ordinal)
                   || normalizedQuestion.Contains("де працювала", StringComparison.Ordinal)
                   || normalizedQuestion.Contains("які фірм", StringComparison.Ordinal)
                   || normalizedQuestion.Contains("історі", StringComparison.Ordinal);
        }

        private static bool HasCurrentEmploymentIntent(string normalizedQuestion)
        {
            return normalizedQuestion.Contains("зараз", StringComparison.Ordinal)
                   || normalizedQuestion.Contains("на даний момент", StringComparison.Ordinal)
                   || normalizedQuestion.Contains("тепер", StringComparison.Ordinal);
        }

        private static (string command, string args) SplitCommand(string text)
        {
            var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 0
                ? (string.Empty, string.Empty)
                : (parts[0], parts.Length > 1 ? parts[1] : string.Empty);
        }

        private static string BuildDisplayName(User? user)
        {
            if (user == null)
                return string.Empty;

            var parts = new[] { user.FirstName, user.LastName }
                .Where(x => !string.IsNullOrWhiteSpace(x));
            var fullName = string.Join(' ', parts);
            return string.IsNullOrWhiteSpace(fullName)
                ? user.Username ?? $"User {user.Id}"
                : fullName;
        }

        private Task SendMenuAsync(ITelegramBotClient botClient, ChatId chatId, string text, CancellationToken cancellationToken, long? ownerUserId = null)
        {
            return SendMessageAsync(botClient, chatId, text, cancellationToken, BuildMainMenuKeyboard(ownerUserId));
        }

        private InlineKeyboardMarkup BuildMainMenuKeyboard(long? ownerUserId = null)
        {
            return new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🏢 Фірми", RegisterCallback("firms", ownerUserId: ownerUserId)),
                    InlineKeyboardButton.WithCallbackData("👥 Працівники", RegisterCallback("employees_all", ownerUserId: ownerUserId))
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("⚠️ Документи 7 днів", RegisterCallback("expiring", "7", ownerUserId: ownerUserId)),
                    InlineKeyboardButton.WithCallbackData("📅 Документи 30 днів", RegisterCallback("expiring", "30", ownerUserId: ownerUserId))
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("ℹ️ Довідка", RegisterCallback("help", ownerUserId: ownerUserId))
                }
            });
        }

        private InlineKeyboardMarkup BuildFirmsKeyboard(IReadOnlyList<EmployerCompany> firms, long? ownerUserId = null)
        {
            var rows = new List<InlineKeyboardButton[]>();
            foreach (var firm in firms.Take(12))
            {
                rows.Add(new[]
                {
                    InlineKeyboardButton.WithCallbackData(
                        firm.Name,
                        RegisterCallback("employees_firm", firm.Name, ownerUserId: ownerUserId))
                });
            }

            rows.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData("👥 Всі працівники", RegisterCallback("employees_all", ownerUserId: ownerUserId)),
                InlineKeyboardButton.WithCallbackData("🏠 Меню", RegisterCallback("menu", ownerUserId: ownerUserId))
            });

            return new InlineKeyboardMarkup(rows);
        }

        private InlineKeyboardMarkup BuildEmployeesKeyboard(IReadOnlyList<EmployeeSummary> employees, string? firmName, long? ownerUserId = null)
        {
            var rows = new List<InlineKeyboardButton[]>();
            foreach (var employee in employees.Take(10))
            {
                rows.Add(new[]
                {
                    InlineKeyboardButton.WithCallbackData(
                        employee.FullName,
                        RegisterCallback("employee", employee.UniqueId, ownerUserId: ownerUserId))
                });
            }

            if (!string.IsNullOrWhiteSpace(firmName))
            {
                rows.Add(new[]
                {
                    InlineKeyboardButton.WithCallbackData("⬅️ До фірм", RegisterCallback("firms", ownerUserId: ownerUserId)),
                    InlineKeyboardButton.WithCallbackData("🏠 Меню", RegisterCallback("menu", ownerUserId: ownerUserId))
                });
            }
            else
            {
                rows.Add(new[]
                {
                    InlineKeyboardButton.WithCallbackData("🏢 Обрати фірму", RegisterCallback("firms", ownerUserId: ownerUserId)),
                    InlineKeyboardButton.WithCallbackData("🏠 Меню", RegisterCallback("menu", ownerUserId: ownerUserId))
                });
            }

            return new InlineKeyboardMarkup(rows);
        }

        private InlineKeyboardMarkup BuildEmployeeDetailsKeyboard(EmployeeSummary employee, long? ownerUserId = null)
        {
            return new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("💰 Остання зарплата", RegisterCallback("salary_latest", employee.UniqueId, ownerUserId: ownerUserId))
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("👥 Працівники фірми", RegisterCallback("employees_firm", employee.FirmName, ownerUserId: ownerUserId)),
                    InlineKeyboardButton.WithCallbackData("🏠 Меню", RegisterCallback("menu", ownerUserId: ownerUserId))
                }
            });
        }

        private InlineKeyboardMarkup BuildExpiringKeyboard(long? ownerUserId = null)
        {
            return new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("7 днів", RegisterCallback("expiring", "7", ownerUserId: ownerUserId)),
                    InlineKeyboardButton.WithCallbackData("30 днів", RegisterCallback("expiring", "30", ownerUserId: ownerUserId))
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🏠 Меню", RegisterCallback("menu", ownerUserId: ownerUserId))
                }
            });
        }

        private InlineKeyboardMarkup BuildSalaryKeyboard(EmployeeSummary employee, long? ownerUserId = null)
        {
            return new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("👤 Картка працівника", RegisterCallback("employee", employee.UniqueId, ownerUserId: ownerUserId)),
                    InlineKeyboardButton.WithCallbackData("🏠 Меню", RegisterCallback("menu", ownerUserId: ownerUserId))
                }
            });
        }

        private InlineKeyboardMarkup BuildEmployeeSelectionKeyboard(IReadOnlyList<EmployeeLookupResult> candidates, string metadata, long? ownerUserId = null)
        {
            var duplicateNames = candidates
                .GroupBy(candidate => candidate.FullName, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var rows = new List<InlineKeyboardButton[]>();
            foreach (var candidate in candidates.Take(5))
            {
                var label = duplicateNames.Contains(candidate.FullName)
                    ? $"{candidate.FullName} | {candidate.FirmName}"
                    : candidate.FullName;

                rows.Add(new[]
                {
                    InlineKeyboardButton.WithCallbackData(label, RegisterCallback("pick_employee", candidate.UniqueId, metadata, ownerUserId))
                });
            }

            rows.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData("🏠 Меню", RegisterCallback("menu", ownerUserId: ownerUserId))
            });

            return new InlineKeyboardMarkup(rows);
        }

        private static string BuildEmployeeSelectionText(string message, IReadOnlyList<EmployeeLookupResult> candidates)
        {
            var header = string.IsNullOrWhiteSpace(message)
                ? "Знайшов кількох працівників. Натисніть потрібне ім'я:"
                : message;

            var lines = candidates
                .Take(5)
                .Select(candidate =>
                {
                    var source = candidate.IsArchived
                        ? "архів"
                        : candidate.IsRecentlyDeleted
                            ? "недавно видалений"
                            : "активний";
                    return $"• {candidate.FullName} | {candidate.FirmName} | {source}";
                })
                .ToList();

            return lines.Count == 0 ? header : header + "\n" + string.Join("\n", lines);
        }

        private static string BuildEmployeePickMetadata(string mode, string? originalQuery = null, string? toolName = null, string? monthKey = null, string? documentType = null)
        {
            return JsonSerializer.Serialize(new
            {
                mode,
                originalQuery = originalQuery ?? string.Empty,
                toolName = toolName ?? string.Empty,
                monthKey = monthKey ?? string.Empty,
                documentType = documentType ?? string.Empty
            });
        }

        private string RegisterCallback(string action, string value = "", string metadata = "", long? ownerUserId = null)
        {
            CleanupExpiredCallbacks();
            var key = Guid.NewGuid().ToString("N")[..10];
            _callbackPayloads[key] = new CallbackPayload
            {
                Action = action,
                Value = value,
                Metadata = metadata,
                OwnerUserId = ownerUserId ?? 0,
                ExpiresAtUtc = DateTime.UtcNow.AddHours(6)
            };
            return $"cb:{key}";
        }

        private bool TryResolveCallback(string? data, long userId, out string action, out string value, out string metadata, out bool accessDenied)
        {
            action = string.Empty;
            value = string.Empty;
            metadata = string.Empty;
            accessDenied = false;
            CleanupExpiredCallbacks();

            if (string.IsNullOrWhiteSpace(data) || !data.StartsWith("cb:", StringComparison.Ordinal))
                return false;

            var key = data[3..];
            if (!_callbackPayloads.TryGetValue(key, out var payload))
                return false;

            if (payload.OwnerUserId != 0 && payload.OwnerUserId != userId)
            {
                accessDenied = true;
                return false;
            }

            _callbackPayloads.TryRemove(key, out _);
            action = payload.Action;
            value = payload.Value;
            metadata = payload.Metadata;
            return true;
        }

        private void CleanupExpiredCallbacks()
        {
            var now = DateTime.UtcNow;
            foreach (var key in _callbackPayloads.Where(x => x.Value.ExpiresAtUtc < now).Select(x => x.Key).ToList())
                _callbackPayloads.TryRemove(key, out _);
        }

        private static bool TryBuildEmployeeDocumentPendingFile(
            EmployeeLookupResult employee,
            EmployeeData data,
            string documentType,
            out PendingFile? pendingFile,
            out string documentLabel,
            out string errorMessage)
        {
            pendingFile = null;
            documentLabel = GetEmployeeDocumentLabel(documentType);
            errorMessage = string.Empty;

            var fullPath = ResolveEmployeeDocumentFullPath(employee.EmployeeFolder, data, documentType);
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                errorMessage = $"Для працівника {employee.FullName} не знайдено файл документа {documentLabel}.";
                return false;
            }

            if (!File.Exists(fullPath))
            {
                errorMessage = $"Файл документа {documentLabel} для працівника {employee.FullName} відсутній на диску.";
                return false;
            }

            pendingFile = new PendingFile
            {
                FilePath = fullPath,
                FileName = Path.GetFileName(fullPath),
                Caption = $"{employee.FullName} — {documentLabel}",
                IsPhoto = IsPhotoFile(fullPath)
            };
            return true;
        }

        private static string GetEmployeeDocumentRelativePath(EmployeeData data, string documentType)
        {
            return documentType switch
            {
                "passport" => data.Files.Passport,
                "visa" => data.Files.Visa,
                "insurance" => data.Files.Insurance,
                "work_permit" => data.Files.WorkPermit,
                "photo" => data.Files.Photo,
                "passport_page2" => data.Files.PassportPage2,
                "visa_page2" => data.Files.VisaPage2,
                _ => string.Empty
            };
        }

        private static bool HasEmployeeDocumentFile(string employeeFolder, EmployeeData data, string documentType)
        {
            return !string.IsNullOrWhiteSpace(ResolveEmployeeDocumentFullPath(employeeFolder, data, documentType));
        }

        private static string ResolveEmployeeDocumentFullPath(string employeeFolder, EmployeeData data, string documentType)
        {
            var storedPath = GetEmployeeDocumentRelativePath(data, documentType);
            var resolvedStoredPath = ResolveStoredEmployeeDocumentPath(employeeFolder, storedPath);
            if (!string.IsNullOrWhiteSpace(resolvedStoredPath))
                return resolvedStoredPath;

            return FindEmployeeDocumentByFolderScan(employeeFolder, data, documentType);
        }

        private static string ResolveStoredEmployeeDocumentPath(string employeeFolder, string storedPath)
        {
            if (string.IsNullOrWhiteSpace(storedPath))
                return string.Empty;

            if (Path.IsPathRooted(storedPath))
                return File.Exists(storedPath) ? storedPath : string.Empty;

            var combined = Path.Combine(employeeFolder, storedPath);
            return File.Exists(combined) ? combined : string.Empty;
        }

        private static string FindEmployeeDocumentByFolderScan(string employeeFolder, EmployeeData data, string documentType)
        {
            if (string.IsNullOrWhiteSpace(employeeFolder) || !Directory.Exists(employeeFolder))
                return string.Empty;

            var files = Directory.GetFiles(employeeFolder);
            if (files.Length == 0)
                return string.Empty;

            var fullName = $"{data.FirstName} {data.LastName}".Trim();
            var fullNameLower = fullName.ToLowerInvariant();
            var insuranceShort = (data.InsuranceCompanyShort ?? string.Empty).Trim().ToLowerInvariant();
            var insuranceFull = (data.InsuranceCompanyFull ?? string.Empty).Trim().ToLowerInvariant();

            var bestMatch = files
                .Select(path => new
                {
                    Path = path,
                    Score = ScoreEmployeeDocumentCandidate(path, documentType, fullNameLower, insuranceShort, insuranceFull)
                })
                .Where(item => item.Score > 0)
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            return bestMatch?.Path ?? string.Empty;
        }

        private static int ScoreEmployeeDocumentCandidate(
            string fullPath,
            string documentType,
            string fullNameLower,
            string insuranceShortLower,
            string insuranceFullLower)
        {
            var fileName = Path.GetFileName(fullPath);
            var nameLower = fileName.ToLowerInvariant();
            var score = 0;

            if (!string.IsNullOrWhiteSpace(fullNameLower) && nameLower.StartsWith(fullNameLower, StringComparison.Ordinal))
                score += 15;

            if (!string.IsNullOrWhiteSpace(insuranceShortLower) && nameLower.Contains(insuranceShortLower, StringComparison.Ordinal))
                score += documentType == "insurance" ? 80 : -20;

            if (!string.IsNullOrWhiteSpace(insuranceFullLower) && nameLower.Contains(insuranceFullLower, StringComparison.Ordinal))
                score += documentType == "insurance" ? 80 : -20;

            var hasPassportKeyword = nameLower.Contains("- pass", StringComparison.Ordinal) || nameLower.Contains("passport", StringComparison.Ordinal);
            var hasVisaKeyword = nameLower.Contains("- viza", StringComparison.Ordinal)
                                 || nameLower.Contains("- visa", StringComparison.Ordinal)
                                 || nameLower.Contains("- vize", StringComparison.Ordinal)
                                 || nameLower.Contains("- víza", StringComparison.Ordinal)
                                 || nameLower.Contains("residence", StringComparison.Ordinal)
                                 || nameLower.Contains("pobyt", StringComparison.Ordinal);
            var hasInsuranceKeyword = nameLower.Contains("insurance", StringComparison.Ordinal)
                                      || nameLower.Contains("pojist", StringComparison.Ordinal)
                                      || nameLower.Contains("poji", StringComparison.Ordinal)
                                      || nameLower.Contains("страх", StringComparison.Ordinal)
                                      || nameLower.Contains("vzp", StringComparison.Ordinal)
                                      || nameLower.Contains("pvzp", StringComparison.Ordinal)
                                      || nameLower.Contains("slavia", StringComparison.Ordinal)
                                      || nameLower.Contains("maxima", StringComparison.Ordinal)
                                      || nameLower.Contains("uniqa", StringComparison.Ordinal);
            var hasPhotoKeyword = nameLower.Contains("- photo", StringComparison.Ordinal) || nameLower.Contains("- foto", StringComparison.Ordinal);
            var hasWorkPermitKeyword = nameLower.Contains("- povolen", StringComparison.Ordinal)
                                       || nameLower.Contains("work permit", StringComparison.Ordinal)
                                       || nameLower.Contains("workpermit", StringComparison.Ordinal);
            var hasPage2Keyword = nameLower.Contains("page2", StringComparison.Ordinal) || nameLower.Contains("page 2", StringComparison.Ordinal);

            score += documentType switch
            {
                "passport" => hasPassportKeyword && !hasPage2Keyword ? 90 : 0,
                "visa" => hasVisaKeyword ? 95 : 0,
                "insurance" => hasInsuranceKeyword ? 95 : 0,
                "photo" => hasPhotoKeyword ? 95 : 0,
                "work_permit" => hasWorkPermitKeyword ? 95 : 0,
                "passport_page2" => hasPassportKeyword && hasPage2Keyword ? 95 : 0,
                "visa_page2" => hasVisaKeyword && hasPage2Keyword ? 95 : 0,
                _ => 0
            };

            if (documentType == "insurance"
                && !hasPassportKeyword
                && !hasVisaKeyword
                && !hasPhotoKeyword
                && !hasWorkPermitKeyword
                && !hasPage2Keyword
                && !nameLower.EndsWith(".json", StringComparison.Ordinal)
                && !nameLower.EndsWith(".tmp", StringComparison.Ordinal)
                && !nameLower.EndsWith(".bak", StringComparison.Ordinal)
                && !nameLower.EndsWith(".xlsx", StringComparison.Ordinal))
            {
                score += 35;
            }

            if (documentType != "passport" && hasPassportKeyword)
                score -= 60;
            if (documentType != "visa" && documentType != "visa_page2" && hasVisaKeyword)
                score -= 60;
            if (documentType != "insurance" && hasInsuranceKeyword)
                score -= 60;
            if (documentType != "photo" && hasPhotoKeyword)
                score -= 60;
            if (documentType != "work_permit" && hasWorkPermitKeyword)
                score -= 60;

            return score;
        }

        private static string GetEmployeeDocumentLabel(string documentType)
        {
            return documentType switch
            {
                "passport" => "паспорт",
                "visa" => "віза",
                "insurance" => "страхування",
                "work_permit" => "дозвіл на роботу",
                "photo" => "фото",
                "passport_page2" => "паспорт сторінка 2",
                "visa_page2" => "віза сторінка 2",
                _ => "документ"
            };
        }

        private static bool IsPhotoFile(string filePath)
        {
            var extension = Path.GetExtension(filePath);
            return extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".webp", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".gif", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase);
        }

        private static string SanitizeFileNamePart(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "file";

            var invalidChars = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);
            foreach (var ch in value)
                builder.Append(invalidChars.Contains(ch) ? '_' : ch);

            return builder.ToString().Replace(' ', '_').Trim('_');
        }

        private async Task SendPendingFilesAsync(
            ITelegramBotClient botClient,
            ChatId chatId,
            IReadOnlyList<PendingFile> pendingFiles,
            CancellationToken cancellationToken)
        {
            foreach (var file in pendingFiles.Where(item => item != null))
            {
                if (file.ContentBytes.Length > 0)
                {
                    await using var memoryStream = new MemoryStream(file.ContentBytes, writable: false);
                    var inputFile = InputFile.FromStream(memoryStream, string.IsNullOrWhiteSpace(file.FileName) ? "file.bin" : file.FileName);
                    if (file.IsPhoto)
                    {
                        await botClient.SendPhoto(chatId, inputFile, caption: file.Caption, cancellationToken: cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await botClient.SendDocument(chatId, inputFile, caption: file.Caption, cancellationToken: cancellationToken).ConfigureAwait(false);
                    }

                    continue;
                }

                if (string.IsNullOrWhiteSpace(file.FilePath) || !File.Exists(file.FilePath))
                    continue;

                await using var fileStream = new FileStream(file.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var fileName = string.IsNullOrWhiteSpace(file.FileName) ? Path.GetFileName(file.FilePath) : file.FileName;
                var streamFile = InputFile.FromStream(fileStream, fileName);
                if (file.IsPhoto)
                {
                    await botClient.SendPhoto(chatId, streamFile, caption: file.Caption, cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await botClient.SendDocument(chatId, streamFile, caption: file.Caption, cancellationToken: cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private async Task SendMessageAsync(
            ITelegramBotClient botClient,
            ChatId chatId,
            string text,
            CancellationToken cancellationToken,
            InlineKeyboardMarkup? replyMarkup = null)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                text = System.Text.RegularExpressions.Regex.Replace(text, @"\*\*(.*?)\*\*", "$1");
                text = System.Text.RegularExpressions.Regex.Replace(text, @"\*(.*?)\*", "$1");
                text = System.Text.RegularExpressions.Regex.Replace(text, @"#{1,6}\s", "");
            }

            var chunks = SplitTelegramMessage(text);
            for (var i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                if (chunks.Count > 1)
                {
                    var prefix = i == 0
                        ? $"(1/{chunks.Count})\n"
                        : $"(продовження {i + 1}/{chunks.Count})\n";
                    chunk = prefix + chunk;
                }

                await botClient.SendMessage(
                    chatId,
                    chunk,
                    replyMarkup: i == chunks.Count - 1 ? replyMarkup : null,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }

        private static List<string> SplitTelegramMessage(string? text)
        {
            var safeText = string.IsNullOrWhiteSpace(text) ? " " : text.Trim();
            var chunks = new List<string>();
            var remaining = safeText;

            while (remaining.Length > MaxTelegramMessageLength)
            {
                var splitIndex = FindTelegramSplitIndex(remaining, MaxTelegramMessageLength);
                chunks.Add(remaining[..splitIndex].TrimEnd());
                remaining = remaining[splitIndex..].TrimStart();
            }

            if (!string.IsNullOrWhiteSpace(remaining))
                chunks.Add(remaining);

            return chunks.Count > 0 ? chunks : new List<string> { " " };
        }

        private static int FindTelegramSplitIndex(string text, int maxLength)
        {
            var candidate = text.LastIndexOf("\n\n", Math.Min(maxLength, text.Length - 1), StringComparison.Ordinal);
            if (candidate >= maxLength / 2)
                return candidate + 2;

            candidate = text.LastIndexOf('\n', Math.Min(maxLength, text.Length - 1));
            if (candidate >= maxLength / 2)
                return candidate + 1;

            candidate = text.LastIndexOf(' ', Math.Min(maxLength, text.Length - 1));
            if (candidate >= maxLength / 2)
                return candidate + 1;

            return Math.Min(maxLength, text.Length);
        }

        private void SetStatus(string status)
        {
            LastStatus = status;
            RaiseStateChanged();
        }

        private void RaiseStateChanged()
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
