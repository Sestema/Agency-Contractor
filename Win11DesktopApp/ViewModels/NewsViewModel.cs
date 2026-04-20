using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.ViewModels
{
    public sealed class NewsViewModel : ViewModelBase, ICleanable
    {
        private const int CurrentAiInsightVersion = 3;
        private const int MinimumReliableArticleTextLength = 500;

        public sealed class NewsTranslationLanguageOption
        {
            public string Code { get; init; } = string.Empty;
            public string DisplayName { get; init; } = string.Empty;
            public string PromptName { get; init; } = string.Empty;

            public override string ToString() => DisplayName;
        }

        public sealed class NewsAiConversationEntry
        {
            public string Text { get; init; } = string.Empty;
            public bool IsUser { get; init; }
            public string Header { get; init; } = string.Empty;
            public bool IsPending { get; init; }
        }

        private readonly NavigationService _navigationService;
        private readonly NewsService _newsService;
        private readonly AppSettingsService _appSettingsService;
        private readonly GeminiApiService _geminiApiService;
        private CancellationTokenSource? _loadCts;
        private readonly ObservableCollection<NewsArticle> _allArticles = new();
        private readonly Dictionary<string, List<(string role, string text)>> _aiHistoryByContext = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<NewsAiConversationEntry>> _aiConversationByContext = new(StringComparer.OrdinalIgnoreCase);
        private NewsArticleTranslation? _currentTranslation;
        private NewsArticleAiInsight? _currentAiInsight;
        private string _cachedArticleTextKey = string.Empty;
        private string _cachedArticleText = string.Empty;

        public ICommand GoBackCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand OpenOriginalCommand { get; }
        public ICommand TranslateSelectedArticleCommand { get; }
        public ICommand ShowOriginalCommand { get; }
        public ICommand ShowTranslatedCommand { get; }
        public ICommand AnalyzeSelectedArticleCommand { get; }
        public ICommand AskAboutArticleCommand { get; }
        public ICommand CloseAiPanelCommand { get; }

        public ObservableCollection<NewsArticle> Articles { get; } = new();
        public ObservableCollection<string> SourceFilters { get; } = new();
        public ObservableCollection<NewsTranslationLanguageOption> TranslationLanguages { get; } = new();
        public ObservableCollection<string> AiKeyPoints { get; } = new();
        public ObservableCollection<NewsAiConversationEntry> AiConversationMessages { get; } = new();
        public Func<Task<string>>? RequestArticleTextAsync { get; set; }

        public string Title => Res("NewsTitle");

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        private bool _isTranslationLoading;
        public bool IsTranslationLoading
        {
            get => _isTranslationLoading;
            set
            {
                if (!SetProperty(ref _isTranslationLoading, value))
                    return;

                CommandManager.InvalidateRequerySuggested();
            }
        }

        private bool _isAiLoading;
        public bool IsAiLoading
        {
            get => _isAiLoading;
            set
            {
                if (!SetProperty(ref _isAiLoading, value))
                    return;

                CommandManager.InvalidateRequerySuggested();
            }
        }

        private bool _isAiPanelOpen;
        public bool IsAiPanelOpen
        {
            get => _isAiPanelOpen;
            set
            {
                if (!SetProperty(ref _isAiPanelOpen, value))
                    return;

                if (!value)
                    AiQuestion = string.Empty;

                CommandManager.InvalidateRequerySuggested();
            }
        }

        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private string _searchQuery = string.Empty;
        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (!SetProperty(ref _searchQuery, value))
                    return;

                ApplyFilters();
            }
        }

        private string _selectedSource = string.Empty;
        public string SelectedSource
        {
            get => _selectedSource;
            set
            {
                if (!SetProperty(ref _selectedSource, value))
                    return;

                ApplyFilters();
            }
        }

        private string _aiQuestion = string.Empty;
        public string AiQuestion
        {
            get => _aiQuestion;
            set
            {
                if (!SetProperty(ref _aiQuestion, value))
                    return;

                CommandManager.InvalidateRequerySuggested();
            }
        }

        private string _aiSummary = string.Empty;
        public string AiSummary
        {
            get => _aiSummary;
            set => SetProperty(ref _aiSummary, value);
        }

        private string _aiPracticalImpact = string.Empty;
        public string AiPracticalImpact
        {
            get => _aiPracticalImpact;
            set => SetProperty(ref _aiPracticalImpact, value);
        }

        private NewsTranslationLanguageOption? _selectedTranslationLanguage;
        public NewsTranslationLanguageOption? SelectedTranslationLanguage
        {
            get => _selectedTranslationLanguage;
            set
            {
                if (!SetProperty(ref _selectedTranslationLanguage, value))
                    return;

                if (!string.Equals(_currentTranslation?.LanguageCode, value?.Code, StringComparison.OrdinalIgnoreCase))
                    _currentTranslation = null;

                RefreshCurrentArticleState(keepTranslatedView: ShowTranslatedArticle);
            }
        }

        private NewsArticle? _selectedArticle;
        public NewsArticle? SelectedArticle
        {
            get => _selectedArticle;
            set
            {
                if (!SetProperty(ref _selectedArticle, value))
                    return;

                _currentTranslation = null;
                _cachedArticleTextKey = string.Empty;
                _cachedArticleText = string.Empty;
                IsAiPanelOpen = false;
                RefreshCurrentArticleState(keepTranslatedView: false);
                OnPropertyChanged(nameof(SelectedArticleUrl));
                OnPropertyChanged(nameof(SelectedArticleSummary));
                OnPropertyChanged(nameof(SelectedArticleMeta));
                OnPropertyChanged(nameof(ArticlePanelTitle));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private bool _showTranslatedArticle;
        public bool ShowTranslatedArticle
        {
            get => _showTranslatedArticle;
            set
            {
                if (!SetProperty(ref _showTranslatedArticle, value))
                    return;

                OnPropertyChanged(nameof(IsOriginalView));
                OnPropertyChanged(nameof(ArticlePanelTitle));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool IsOriginalView => !ShowTranslatedArticle;
        public bool HasTranslatedArticle => !string.IsNullOrWhiteSpace(_currentTranslation?.TranslatedBody);
        public bool HasAiInsight => !string.IsNullOrWhiteSpace(AiSummary) || AiKeyPoints.Count > 0;
        public bool HasAiConversation => AiConversationMessages.Count > 0;
        public string SelectedArticleUrl => SelectedArticle?.Url ?? string.Empty;
        public string SelectedArticleSummary => SelectedArticle?.Summary ?? Res("NewsSelectArticle");
        public string TranslatedArticleBody => _currentTranslation?.TranslatedBody ?? string.Empty;
        public string ArticlePanelTitle => ShowTranslatedArticle && !string.IsNullOrWhiteSpace(_currentTranslation?.TranslatedTitle)
            ? _currentTranslation!.TranslatedTitle
            : SelectedArticle?.Title ?? string.Empty;
        public string SelectedArticleMeta
        {
            get
            {
                if (SelectedArticle == null)
                    return string.Empty;

                var meta = $"{SelectedArticle.SourceName} · {SelectedArticle.PublishedAtUtc.ToLocalTime():dd.MM.yyyy HH:mm}";
                if (ShowTranslatedArticle && _currentTranslation != null)
                    meta += $" · {string.Format(Res("NewsTranslatedForLanguageFmt"), _currentTranslation.LanguageName)}";

                return meta;
            }
        }

        public NewsViewModel(
            NewsService newsService,
            NavigationService? navigationService = null,
            AppSettingsService? appSettingsService = null,
            GeminiApiService? geminiApiService = null)
        {
            _navigationService = navigationService ?? throw new InvalidOperationException("NavigationService is not initialized.");
            _newsService = newsService;
            _appSettingsService = appSettingsService ?? throw new InvalidOperationException("AppSettingsService is not initialized.");
            _geminiApiService = geminiApiService ?? throw new InvalidOperationException("GeminiApiService is not initialized.");

            GoBackCommand = new RelayCommand(_ => _navigationService.NavigateTo<MainViewModel>());
            RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync(true), _ => !IsLoading);
            OpenOriginalCommand = new RelayCommand(_ => OpenOriginalArticle(), _ => SelectedArticle != null && !string.IsNullOrWhiteSpace(SelectedArticle.Url));
            TranslateSelectedArticleCommand = new AsyncRelayCommand(_ => TranslateSelectedArticleAsync(), _ => CanTranslateSelectedArticle());
            ShowOriginalCommand = new RelayCommand(_ => ShowTranslatedArticle = false, _ => SelectedArticle != null && ShowTranslatedArticle);
            ShowTranslatedCommand = new RelayCommand(_ => ShowTranslatedArticle = true, _ => SelectedArticle != null && HasTranslatedArticle && !ShowTranslatedArticle);
            AnalyzeSelectedArticleCommand = new AsyncRelayCommand(_ => AnalyzeSelectedArticleAsync(), _ => CanAnalyzeSelectedArticle());
            AskAboutArticleCommand = new AsyncRelayCommand(_ => AskAboutArticleAsync(), _ => CanAskAboutArticle());
            CloseAiPanelCommand = new RelayCommand(_ => IsAiPanelOpen = false, _ => IsAiPanelOpen);

            SourceFilters.Add(Res("NewsAllSources"));
            foreach (var source in _newsService.GetSources())
                SourceFilters.Add(source.Name);

            TranslationLanguages.Add(new NewsTranslationLanguageOption { Code = "uk", DisplayName = "Українська", PromptName = "Ukrainian" });
            TranslationLanguages.Add(new NewsTranslationLanguageOption { Code = "cs", DisplayName = "Čeština", PromptName = "Czech" });
            TranslationLanguages.Add(new NewsTranslationLanguageOption { Code = "en", DisplayName = "English", PromptName = "English" });
            TranslationLanguages.Add(new NewsTranslationLanguageOption { Code = "ru", DisplayName = "Русский", PromptName = "Russian" });

            SelectedSource = SourceFilters.FirstOrDefault() ?? string.Empty;
            var preferredLanguageCode = _appSettingsService.Settings.LanguageCode ?? "uk";
            SelectedTranslationLanguage = TranslationLanguages.FirstOrDefault(x => string.Equals(x.Code, preferredLanguageCode, StringComparison.OrdinalIgnoreCase))
                ?? TranslationLanguages.FirstOrDefault();

            _ = RefreshAsync(false);
        }

        public void Cleanup()
        {
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = null;
            RequestArticleTextAsync = null;
        }

        private async Task RefreshAsync(bool forceRefresh)
        {
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = new CancellationTokenSource();

            IsLoading = true;
            StatusMessage = Res("NewsLoading");

            try
            {
                var articles = await _newsService.GetLatestArticlesAsync(forceRefresh, _loadCts.Token).ConfigureAwait(false);
                App.Current?.Dispatcher?.Invoke(() =>
                {
                    _allArticles.Clear();
                    foreach (var article in articles)
                        _allArticles.Add(article);

                    ApplyFilters();
                    StatusMessage = _allArticles.Count == 0
                        ? Res("NewsEmpty")
                        : string.Format(Res("NewsStatusFmt"), _allArticles.Count);
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("NewsViewModel.RefreshAsync", ex.Message);
                App.Current?.Dispatcher?.Invoke(() => StatusMessage = Res("NewsLoadError"));
            }
            finally
            {
                App.Current?.Dispatcher?.Invoke(() => IsLoading = false);
            }
        }

        private async Task TranslateSelectedArticleAsync()
        {
            if (SelectedArticle == null || SelectedTranslationLanguage == null)
                return;

            var translationLanguage = SelectedTranslationLanguage;
            if (_geminiApiService?.IsConfigured != true)
            {
                StatusMessage = Res("NewsTranslateConfigureAi");
                return;
            }

            IsTranslationLoading = true;
            StatusMessage = string.Format(Res("NewsTranslatingFmt"), translationLanguage.DisplayName);

            try
            {
                var articleText = await GetSelectedArticleTextAsync();
                var prompt = BuildTranslationPrompt(SelectedArticle, articleText, translationLanguage);
                var response = await _geminiApiService.ChatAsync(
                    prompt,
                    "Translate official news articles accurately. Return plain text only, no markdown, no explanations.",
                    CancellationToken.None);

                if (IsInvalidModelResponse(response))
                {
                    StatusMessage = string.Format(Res("NewsTranslateFailedFmt"), translationLanguage.DisplayName);
                    return;
                }

                var translation = ParseTranslationResponse(response, SelectedArticle, translationLanguage);
                if (string.IsNullOrWhiteSpace(translation.TranslatedBody))
                {
                    StatusMessage = string.Format(Res("NewsTranslateFailedFmt"), translationLanguage.DisplayName);
                    return;
                }

                _currentTranslation = translation;

                ShowTranslatedArticle = true;
                RaiseTranslationPropertiesChanged();
                StatusMessage = string.Format(Res("NewsTranslatedFmt"), translationLanguage.DisplayName);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("NewsViewModel.TranslateSelectedArticleAsync", ex.Message);
                StatusMessage = string.Format(Res("NewsTranslateFailedFmt"), translationLanguage.DisplayName);
            }
            finally
            {
                IsTranslationLoading = false;
            }
        }

        private async Task AnalyzeSelectedArticleAsync()
        {
            if (SelectedArticle == null || SelectedTranslationLanguage == null)
                return;

            if (_geminiApiService?.IsConfigured != true)
            {
                StatusMessage = Res("NewsAiConfigure");
                return;
            }

            var aiLanguage = GetCurrentAiLanguage();
            IsAiPanelOpen = true;
            var cachedInsight = _newsService.GetCachedAiInsight(SelectedArticle, aiLanguage.Code);
            if (cachedInsight != null
                && cachedInsight.Version >= CurrentAiInsightVersion
                && (!string.IsNullOrWhiteSpace(cachedInsight.Summary) || cachedInsight.KeyPoints.Count > 0 || !string.IsNullOrWhiteSpace(cachedInsight.PracticalImpact)))
            {
                _currentAiInsight = cachedInsight;
                ApplyAiInsight(cachedInsight);
                StatusMessage = string.Format(Res("NewsAiCachedFmt"), cachedInsight.LanguageName);
                return;
            }

            IsAiLoading = true;
            StatusMessage = string.Format(Res("NewsAiAnalyzingFmt"), aiLanguage.DisplayName);

            try
            {
                var articleText = await GetSelectedArticleTextAsync(allowSummaryFallback: false);
                if (!HasReliableArticleText(articleText))
                {
                    StatusMessage = Res("NewsAiReaderUnavailable");
                    return;
                }

                var prompt = BuildAiInsightPrompt(SelectedArticle, articleText, aiLanguage);
                var response = await _geminiApiService.ChatAsync(
                    prompt,
                    "Analyze official news articles accurately. Use only the extracted article text. Do not guess or add outside legal knowledge. Return plain text only.",
                    CancellationToken.None);

                if (IsInvalidModelResponse(response))
                {
                    StatusMessage = string.Format(Res("NewsAiFailedFmt"), aiLanguage.DisplayName);
                    return;
                }

                var insight = ParseAiInsightResponse(response, aiLanguage);
                if (string.IsNullOrWhiteSpace(insight.Summary) && insight.KeyPoints.Count == 0 && string.IsNullOrWhiteSpace(insight.PracticalImpact))
                {
                    StatusMessage = string.Format(Res("NewsAiFailedFmt"), aiLanguage.DisplayName);
                    return;
                }

                insight.Version = CurrentAiInsightVersion;
                if (!IsValidAiInsight(insight))
                {
                    StatusMessage = string.Format(Res("NewsAiFailedFmt"), aiLanguage.DisplayName);
                    return;
                }

                _newsService.SaveArticleAiInsight(SelectedArticle, insight);
                _currentAiInsight = insight;
                ApplyAiInsight(insight);
                StatusMessage = string.Format(Res("NewsAiReadyFmt"), aiLanguage.DisplayName);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("NewsViewModel.AnalyzeSelectedArticleAsync", ex.Message);
                StatusMessage = string.Format(Res("NewsAiFailedFmt"), aiLanguage.DisplayName);
            }
            finally
            {
                IsAiLoading = false;
            }
        }

        private async Task AskAboutArticleAsync()
        {
            if (SelectedArticle == null)
                return;

            var question = (AiQuestion ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(question))
                return;

            if (_geminiApiService?.IsConfigured != true)
            {
                StatusMessage = Res("NewsAiConfigure");
                return;
            }

            var aiLanguage = GetCurrentAiLanguage();
            IsAiPanelOpen = true;
            IsAiLoading = true;
            StatusMessage = Res("NewsAiThinking");
            var conversationKey = string.Empty;
            List<NewsAiConversationEntry>? conversation = null;

            try
            {
                var articleText = await GetSelectedArticleTextAsync(allowSummaryFallback: false);
                if (!HasReliableArticleText(articleText))
                {
                    StatusMessage = Res("NewsAiReaderUnavailable");
                    return;
                }

                conversationKey = GetConversationContextKey(SelectedArticle, aiLanguage.Code);
                if (!_aiHistoryByContext.TryGetValue(conversationKey, out var history))
                {
                    history = new List<(string role, string text)>();
                    _aiHistoryByContext[conversationKey] = history;
                }

                if (!_aiConversationByContext.TryGetValue(conversationKey, out conversation))
                {
                    conversation = new List<NewsAiConversationEntry>();
                    _aiConversationByContext[conversationKey] = conversation;
                }

                conversation.Add(new NewsAiConversationEntry
                {
                    IsUser = true,
                    Text = question,
                    Header = Res("NewsAiYou")
                });
                conversation.Add(new NewsAiConversationEntry
                {
                    IsUser = false,
                    IsPending = true,
                    Text = Res("NewsAiThinkingBubble"),
                    Header = Res("NewsAiAssistantName")
                });

                AiQuestion = string.Empty;
                LoadConversation(conversationKey);

                var systemPrompt = BuildAiQuestionSystemPrompt(SelectedArticle, articleText, aiLanguage);
                var response = await _geminiApiService.ChatWithHistoryAsync(history, question, systemPrompt, CancellationToken.None);
                if (IsInvalidModelResponse(response))
                {
                    ReplacePendingAiMessage(conversation, Res("NewsAiAskFailed"));
                    LoadConversation(conversationKey);
                    StatusMessage = Res("NewsAiAskFailed");
                    return;
                }

                var answer = NormalizeArticleText(StripCodeFence(response));
                if (string.IsNullOrWhiteSpace(answer))
                {
                    ReplacePendingAiMessage(conversation, Res("NewsAiAskFailed"));
                    LoadConversation(conversationKey);
                    StatusMessage = Res("NewsAiAskFailed");
                    return;
                }

                history.Add(("user", question));
                history.Add(("model", answer));
                ReplacePendingAiMessage(conversation, answer);

                LoadConversation(conversationKey);
                StatusMessage = Res("NewsAiAnswerReady");
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("NewsViewModel.AskAboutArticleAsync", ex.Message);
                if (conversation != null)
                {
                    ReplacePendingAiMessage(conversation, Res("NewsAiAskFailed"));
                    LoadConversation(conversationKey);
                }
                StatusMessage = Res("NewsAiAskFailed");
            }
            finally
            {
                IsAiLoading = false;
            }
        }

        private void ApplyFilters()
        {
            var query = SearchQuery?.Trim() ?? string.Empty;
            var selectedSource = SelectedSource;

            var filtered = _allArticles.Where(article =>
            {
                var matchesSource = string.IsNullOrWhiteSpace(selectedSource)
                    || selectedSource == Res("NewsAllSources")
                    || string.Equals(article.SourceName, selectedSource, StringComparison.OrdinalIgnoreCase);

                if (!matchesSource)
                    return false;

                if (string.IsNullOrWhiteSpace(query))
                    return true;

                return article.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || article.Summary.Contains(query, StringComparison.OrdinalIgnoreCase);
            }).ToList();

            Articles.Clear();
            foreach (var article in filtered)
                Articles.Add(article);

            if (SelectedArticle == null || !Articles.Contains(SelectedArticle))
                SelectedArticle = Articles.FirstOrDefault();
        }

        private bool CanTranslateSelectedArticle()
        {
            return SelectedArticle != null
                && SelectedTranslationLanguage != null
                && !IsTranslationLoading;
        }

        private bool CanAnalyzeSelectedArticle()
        {
            return SelectedArticle != null
                && !IsAiLoading;
        }

        private bool CanAskAboutArticle()
        {
            return SelectedArticle != null
                && !IsAiLoading
                && IsAiPanelOpen
                && !string.IsNullOrWhiteSpace(AiQuestion);
        }

        private void RefreshCurrentArticleState(bool keepTranslatedView)
        {
            if (SelectedArticle == null || SelectedTranslationLanguage == null)
            {
                _currentTranslation = null;
            }
            else if (!string.Equals(_currentTranslation?.LanguageCode, SelectedTranslationLanguage.Code, StringComparison.OrdinalIgnoreCase))
            {
                _currentTranslation = null;
            }

            _currentAiInsight = SelectedArticle == null
                ? null
                : _newsService.GetCachedAiInsight(SelectedArticle, GetCurrentAiLanguage().Code);

            ShowTranslatedArticle = keepTranslatedView && _currentTranslation != null;
            ApplyAiInsight(_currentAiInsight);
            LoadConversation(GetConversationContextKey(SelectedArticle, GetCurrentAiLanguage().Code));
            RaiseTranslationPropertiesChanged();
        }

        private void RaiseTranslationPropertiesChanged()
        {
            OnPropertyChanged(nameof(HasTranslatedArticle));
            OnPropertyChanged(nameof(TranslatedArticleBody));
            OnPropertyChanged(nameof(ArticlePanelTitle));
            OnPropertyChanged(nameof(SelectedArticleMeta));
            OnPropertyChanged(nameof(IsOriginalView));
            OnPropertyChanged(nameof(HasAiInsight));
            OnPropertyChanged(nameof(HasAiConversation));
            CommandManager.InvalidateRequerySuggested();
        }

        private void ApplyAiInsight(NewsArticleAiInsight? insight)
        {
            AiSummary = insight?.Summary ?? string.Empty;
            AiPracticalImpact = insight?.PracticalImpact ?? string.Empty;

            AiKeyPoints.Clear();
            foreach (var point in insight?.KeyPoints ?? Enumerable.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(point))
                    AiKeyPoints.Add(point);
            }

            OnPropertyChanged(nameof(HasAiInsight));
        }

        private NewsTranslationLanguageOption GetCurrentAiLanguage()
        {
            var code = _appSettingsService.Settings.LanguageCode ?? "uk";
            return TranslationLanguages.FirstOrDefault(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase))
                ?? new NewsTranslationLanguageOption
                {
                    Code = code,
                    DisplayName = code,
                    PromptName = code
                };
        }

        private void LoadConversation(string contextKey)
        {
            AiConversationMessages.Clear();
            if (!string.IsNullOrWhiteSpace(contextKey) && _aiConversationByContext.TryGetValue(contextKey, out var messages))
            {
                foreach (var message in messages)
                    AiConversationMessages.Add(message);
            }

            OnPropertyChanged(nameof(HasAiConversation));
        }

        private void ReplacePendingAiMessage(List<NewsAiConversationEntry> conversation, string text)
        {
            var pendingIndex = conversation.FindLastIndex(entry => !entry.IsUser && entry.IsPending);
            if (pendingIndex >= 0)
            {
                conversation[pendingIndex] = new NewsAiConversationEntry
                {
                    IsUser = false,
                    Text = text,
                    Header = Res("NewsAiAssistantName")
                };
            }
            else
            {
                conversation.Add(new NewsAiConversationEntry
                {
                    IsUser = false,
                    Text = text,
                    Header = Res("NewsAiAssistantName")
                });
            }
        }

        private async Task<string> GetSelectedArticleTextAsync(bool allowSummaryFallback = true)
        {
            if (SelectedArticle == null)
                return string.Empty;

            var articleKey = GetArticleKey(SelectedArticle);
            if (string.Equals(_cachedArticleTextKey, articleKey, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(_cachedArticleText))
                return _cachedArticleText;

            string articleText = string.Empty;
            if (RequestArticleTextAsync != null)
                articleText = NormalizeArticleText(await RequestArticleTextAsync());

            if (allowSummaryFallback && string.IsNullOrWhiteSpace(articleText))
                articleText = NormalizeArticleText($"{SelectedArticle.Title}\n\n{SelectedArticle.Summary}");

            _cachedArticleTextKey = articleKey;
            _cachedArticleText = articleText;
            return articleText;
        }

        private static bool HasReliableArticleText(string articleText)
        {
            if (string.IsNullOrWhiteSpace(articleText))
                return false;

            return articleText.Length >= MinimumReliableArticleTextLength;
        }

        private void OpenOriginalArticle()
        {
            if (SelectedArticle == null || string.IsNullOrWhiteSpace(SelectedArticle.Url))
                return;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = SelectedArticle.Url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("NewsViewModel.OpenOriginalArticle", ex.Message);
            }
        }

        private static string BuildTranslationPrompt(NewsArticle article, string articleText, NewsTranslationLanguageOption language)
        {
            var body = TrimContext(articleText);
            var prompt = new StringBuilder();
            prompt.AppendLine($"Translate this news article into {language.PromptName}.");
            prompt.AppendLine("Preserve legal and official terminology accurately.");
            prompt.AppendLine("Return exactly in this format:");
            prompt.AppendLine("TITLE: <translated title>");
            prompt.AppendLine("BODY:");
            prompt.AppendLine("<translated article text>");
            prompt.AppendLine();
            prompt.AppendLine("SOURCE TITLE:");
            prompt.AppendLine(article.Title);
            prompt.AppendLine();
            prompt.AppendLine("SOURCE BODY:");
            prompt.AppendLine(body);
            return prompt.ToString();
        }

        private static string BuildAiInsightPrompt(NewsArticle article, string articleText, NewsTranslationLanguageOption language)
        {
            var body = TrimContext(articleText);
            var prompt = new StringBuilder();
            prompt.AppendLine($"Read this official news article and write your answer in {language.PromptName}.");
            prompt.AppendLine("Base your answer only on the article content provided below.");
            prompt.AppendLine("Do not translate the full article. Do not use outside knowledge. If the article does not say something, state that clearly.");
            prompt.AppendLine("Keep the summary short and practical.");
            prompt.AppendLine("Return exactly in this format:");
            prompt.AppendLine("SUMMARY:");
            prompt.AppendLine("<short summary in 2-3 sentences>");
            prompt.AppendLine("KEY_POINTS:");
            prompt.AppendLine("- <very short point 1>");
            prompt.AppendLine("- <very short point 2>");
            prompt.AppendLine("- <very short point 3>");
            prompt.AppendLine();
            prompt.AppendLine("ARTICLE TITLE:");
            prompt.AppendLine(article.Title);
            prompt.AppendLine();
            prompt.AppendLine("ARTICLE BODY:");
            prompt.AppendLine(body);
            return prompt.ToString();
        }

        private static string BuildAiQuestionSystemPrompt(NewsArticle article, string articleText, NewsTranslationLanguageOption language)
        {
            var body = TrimContext(articleText);
            var prompt = new StringBuilder();
            prompt.AppendLine($"You are an assistant answering questions about one official news article. Answer in {language.PromptName}.");
            prompt.AppendLine("Answer only from the article content below.");
            prompt.AppendLine("If the article does not contain the answer, say so clearly.");
            prompt.AppendLine("Do not guess. Do not add outside legal knowledge.");
            prompt.AppendLine("Do not translate the full article. Answer the specific user question briefly and clearly.");
            prompt.AppendLine("When possible, include one short exact phrase from the article as evidence.");
            prompt.AppendLine();
            prompt.AppendLine("ARTICLE TITLE:");
            prompt.AppendLine(article.Title);
            prompt.AppendLine();
            prompt.AppendLine("ARTICLE BODY:");
            prompt.AppendLine(body);
            return prompt.ToString();
        }

        private static NewsArticleTranslation ParseTranslationResponse(string response, NewsArticle article, NewsTranslationLanguageOption language)
        {
            var cleaned = StripCodeFence(response).Trim();
            var bodyMarkerIndex = cleaned.IndexOf("BODY:", StringComparison.OrdinalIgnoreCase);
            var titleMarkerIndex = cleaned.IndexOf("TITLE:", StringComparison.OrdinalIgnoreCase);

            var translatedTitle = article.Title;
            var translatedBody = cleaned;

            if (titleMarkerIndex >= 0 && bodyMarkerIndex > titleMarkerIndex)
            {
                translatedTitle = cleaned[(titleMarkerIndex + "TITLE:".Length)..bodyMarkerIndex].Trim();
                translatedBody = cleaned[(bodyMarkerIndex + "BODY:".Length)..].Trim();
            }
            else if (bodyMarkerIndex >= 0)
            {
                translatedBody = cleaned[(bodyMarkerIndex + "BODY:".Length)..].Trim();
            }

            if (string.IsNullOrWhiteSpace(translatedTitle))
                translatedTitle = article.Title;

            translatedBody = NormalizeArticleText(translatedBody);

            return new NewsArticleTranslation
            {
                LanguageCode = language.Code,
                LanguageName = language.DisplayName,
                TranslatedTitle = translatedTitle,
                TranslatedBody = translatedBody,
                TranslatedAtUtc = DateTime.UtcNow
            };
        }

        private static NewsArticleAiInsight ParseAiInsightResponse(string response, NewsTranslationLanguageOption language)
        {
            var cleaned = NormalizeArticleText(StripCodeFence(response));
            var summary = ExtractSection(cleaned, "SUMMARY:", "KEY_POINTS:");
            var keyPointsRaw = ExtractSection(cleaned, "KEY_POINTS:", null);
            var practicalImpact = string.Empty;

            var keyPoints = keyPointsRaw
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(CleanBulletLine)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .ToList();

            if (string.IsNullOrWhiteSpace(summary) && keyPoints.Count == 0 && string.IsNullOrWhiteSpace(practicalImpact))
                summary = cleaned;

            return new NewsArticleAiInsight
            {
                LanguageCode = language.Code,
                LanguageName = language.DisplayName,
                Summary = summary,
                KeyPoints = keyPoints,
                PracticalImpact = practicalImpact,
                GeneratedAtUtc = DateTime.UtcNow
            };
        }

        private static string ExtractSection(string text, string startMarker, string? endMarker)
        {
            var startIndex = text.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase);
            if (startIndex < 0)
                return string.Empty;

            startIndex += startMarker.Length;
            var endIndex = string.IsNullOrWhiteSpace(endMarker)
                ? text.Length
                : text.IndexOf(endMarker, startIndex, StringComparison.OrdinalIgnoreCase);

            if (endIndex < 0)
                endIndex = text.Length;

            return NormalizeArticleText(text[startIndex..endIndex]);
        }

        private static string CleanBulletLine(string line)
        {
            var trimmed = line.Trim();
            while (!string.IsNullOrWhiteSpace(trimmed) && ("-*•0123456789.) ".Contains(trimmed[0])))
                trimmed = trimmed[1..].TrimStart();

            return trimmed.Trim();
        }

        private static string StripCodeFence(string value)
        {
            var trimmed = value?.Trim() ?? string.Empty;
            if (!trimmed.StartsWith("```", StringComparison.Ordinal))
                return trimmed;

            var firstLineBreak = trimmed.IndexOf('\n');
            if (firstLineBreak < 0)
                return trimmed.Trim('`', ' ');

            trimmed = trimmed[(firstLineBreak + 1)..];
            var closingFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (closingFence >= 0)
                trimmed = trimmed[..closingFence];

            return trimmed.Trim();
        }

        private static string NormalizeArticleText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n');

            while (normalized.Contains("\n\n\n", StringComparison.Ordinal))
                normalized = normalized.Replace("\n\n\n", "\n\n", StringComparison.Ordinal);

            return normalized.Trim();
        }

        private static string TrimContext(string articleText)
        {
            return articleText.Length > 18000 ? articleText[..18000] : articleText;
        }

        private static bool IsValidAiInsight(NewsArticleAiInsight insight)
        {
            if (insight == null)
                return false;

            var hasSummary = !string.IsNullOrWhiteSpace(insight.Summary);
            var hasPracticalImpact = !string.IsNullOrWhiteSpace(insight.PracticalImpact);
            var hasKeyPoints = insight.KeyPoints?.Any(point => !string.IsNullOrWhiteSpace(point)) == true;

            return hasSummary || hasPracticalImpact || hasKeyPoints;
        }

        private static bool IsInvalidModelResponse(string response)
        {
            return GeminiApiService.IsFailureResponse(response);
        }

        private static string GetArticleKey(NewsArticle article)
        {
            return $"{article.SourceId}|{article.Url?.Trim()}";
        }

        private static string GetConversationContextKey(NewsArticle? article, string? languageCode)
        {
            if (article == null || string.IsNullOrWhiteSpace(languageCode))
                return string.Empty;

            return $"{GetArticleKey(article)}|{languageCode}";
        }
    }
}
