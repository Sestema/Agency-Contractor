using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.ViewModels
{
    public class TemplateEditorViewModel : ViewModelBase
    {
        private readonly string _firmName;
        private readonly TemplateEntry _template;
        private readonly TemplateService _templateService;
        private readonly TagCatalogService _tagCatalogService;

        private static new string Res(string key)
        {
            try { return Application.Current.FindResource(key) as string ?? key; }
            catch { return key; }
        }

        private static string ResF(string key, params object[] args)
        {
            var fmt = Res(key);
            try { return string.Format(fmt, args); }
            catch { return fmt; }
        }

        public string Title => ResF("EditorTitleFmt", _template.Name);
        public string RtfFilePath { get; }
        public string TemplateFolderPath { get; }

        public ObservableCollection<TagGroupViewModel> TagGroups { get; }

        private string _tagSearchQuery = string.Empty;
        public string TagSearchQuery
        {
            get => _tagSearchQuery;
            set
            {
                if (SetProperty(ref _tagSearchQuery, value))
                    OnPropertyChanged(nameof(FilteredTagGroups));
            }
        }

        public ObservableCollection<TagGroupViewModel> FilteredTagGroups
            => TagGroups != null ? TagGroupViewModel.FilterTagGroups(TagGroups, TagSearchQuery) : new ObservableCollection<TagGroupViewModel>();

        public ICommand GoBackCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand InsertTagCommand { get; }
        public ICommand CopyTagCommand { get; }
        public ICommand AIInsertTagsCommand { get; private set; }
        public ICommand CloseAITagsCommand { get; private set; }

        public event Action<string, string>? SendMessageToWebView;
        public event Action<string>? RequestInsertTag;
        public event Action<List<(string ContextBefore, string ReplaceWhat, string Tag)>>? RequestReplaceTagsInDocument;
        public Func<string?>? RequestGetRtfContent { get; set; }
        public Func<string?>? RequestGetPlainText { get; set; }

        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private string _editorStatus = string.Empty;
        public string EditorStatus
        {
            get => _editorStatus;
            set => SetProperty(ref _editorStatus, value);
        }

        // ---- AI Insert Tags ----
        private bool _isAITagsRunning;
        public bool IsAITagsRunning
        {
            get => _isAITagsRunning;
            set => SetProperty(ref _isAITagsRunning, value);
        }

        private bool _isAITagsOpen;
        public bool IsAITagsOpen
        {
            get => _isAITagsOpen;
            set => SetProperty(ref _isAITagsOpen, value);
        }

        private string _aiTagsStatus = string.Empty;
        public string AITagsStatus
        {
            get => _aiTagsStatus;
            set => SetProperty(ref _aiTagsStatus, value);
        }

        public TemplateEditorViewModel(string firmName, TemplateEntry template, TemplateService? templateService = null)
        {
            _firmName = firmName;
            _template = template;
            _templateService = templateService ?? App.TemplateService;
            _tagCatalogService = App.TagCatalogService;

            try
            {
                var fullPath = _templateService.GetTemplateFullPath(firmName, template.FilePath);
                TemplateFolderPath = Path.GetDirectoryName(fullPath) ?? string.Empty;
                RtfFilePath = Path.Combine(TemplateFolderPath, "content.rtf");
            }
            catch
            {
                TemplateFolderPath = string.Empty;
                RtfFilePath = string.Empty;
            }

            try
            {
                var allTags = _tagCatalogService?.GetAllTagDefinitions() ?? new List<TagEntry>();
                var groups = TagGroupViewModel.BuildTagGroups(allTags);
                TagGroups = TagGroupViewModel.ApplyHiddenTagsFilter(groups, App.AppSettingsService?.Settings?.HiddenTags ?? new List<string>());
            }
            catch
            {
                TagGroups = new ObservableCollection<TagGroupViewModel>();
            }

            GoBackCommand = new RelayCommand(o => NavigateBack());
            SaveCommand = new RelayCommand(o => Save());
            InsertTagCommand = new RelayCommand(o => InsertTag(o));
            CopyTagCommand = new RelayCommand(o => CopyTag(o));
            AIInsertTagsCommand = new RelayCommand(o => RunAIInsertTags(), o => !IsAITagsRunning);
            CloseAITagsCommand = new RelayCommand(o => IsAITagsOpen = false);
        }

        public TemplateEditorViewModel(string firmName, TemplateEntry template, TagCatalogService tagCatalogService, TemplateService templateService)
        {
            _firmName = firmName;
            _template = template;
            _templateService = templateService;
            _tagCatalogService = tagCatalogService;

            try
            {
                var fullPath = _templateService.GetTemplateFullPath(firmName, template.FilePath);
                TemplateFolderPath = Path.GetDirectoryName(fullPath) ?? string.Empty;
                RtfFilePath = Path.Combine(TemplateFolderPath, "content.rtf");
            }
            catch
            {
                TemplateFolderPath = string.Empty;
                RtfFilePath = string.Empty;
            }

            try
            {
                var allTags = tagCatalogService.GetAllTagDefinitions();
                var groups = TagGroupViewModel.BuildTagGroups(allTags);
                TagGroups = TagGroupViewModel.ApplyHiddenTagsFilter(groups, App.AppSettingsService?.Settings?.HiddenTags ?? new List<string>());
            }
            catch
            {
                TagGroups = new ObservableCollection<TagGroupViewModel>();
            }

            GoBackCommand = new RelayCommand(o => NavigateBack());
            SaveCommand = new RelayCommand(o => Save());
            InsertTagCommand = new RelayCommand(o => InsertTag(o));
            CopyTagCommand = new RelayCommand(o => CopyTag(o));
            AIInsertTagsCommand = new RelayCommand(o => RunAIInsertTags(), o => !IsAITagsRunning);
            CloseAITagsCommand = new RelayCommand(o => IsAITagsOpen = false);
        }

        private void NavigateBack()
        {
            var company = App.CompanyService?.Companies?.FirstOrDefault(c => c.Name == _firmName);
            if (company != null)
                App.NavigationService?.NavigateTo(new TemplatesViewModel(company));
            else
                App.NavigationService?.NavigateTo(new MainViewModel());
        }

        private void Save()
        {
            try
            {
                if (string.IsNullOrEmpty(TemplateFolderPath))
                {
                    StatusMessage = Res("EditorErrPath");
                    return;
                }

                Directory.CreateDirectory(TemplateFolderPath);
                var rtfContent = RequestGetRtfContent?.Invoke();
                if (string.IsNullOrEmpty(rtfContent))
                {
                    StatusMessage = Res("EditorErrEmpty");
                    return;
                }

                File.WriteAllText(RtfFilePath, rtfContent);
                StatusMessage = Res("EditorSaved");
            }
            catch (Exception ex)
            {
                StatusMessage = ResF("EditorErrFmt", ex.Message);
            }
        }

        private void InsertTag(object? parameter)
        {
            if (parameter is string tag)
            {
                var tagText = $"${{{tag}}}";
                RequestInsertTag?.Invoke(tagText);
            }
            else if (parameter is TagEntry entry)
            {
                var tagText = $"${{{entry.Tag}}}";
                RequestInsertTag?.Invoke(tagText);
            }
        }

        private async void RunAIInsertTags()
        {
            var geminiService = App.GeminiApiService;
            if (geminiService == null || !geminiService.IsConfigured)
            {
                AITagsStatus = Res("AIChatNoModel");
                IsAITagsOpen = true;
                return;
            }

            var plainText = RequestGetPlainText?.Invoke();
            if (string.IsNullOrWhiteSpace(plainText))
            {
                AITagsStatus = "Документ порожній. Спочатку напишіть текст шаблону.";
                IsAITagsOpen = true;
                return;
            }

            IsAITagsRunning = true;
            AITagsStatus = Res("AIChatThinking");
            IsAITagsOpen = true;

            try
            {
                var allTags = _tagCatalogService?.GetAllTagDefinitions() ?? new List<TagEntry>();
                var tagListSb = new StringBuilder();
                foreach (var t in allTags)
                    tagListSb.AppendLine($"  {t.Tag} — {t.Description}");

                var prompt = new StringBuilder();
                prompt.AppendLine("You are a smart document analyzer for Czech/Slovak employment contract templates.");
                prompt.AppendLine("Read the ENTIRE document carefully and find ALL places where template tags should be inserted.");
                prompt.AppendLine();
                prompt.AppendLine("The document has blank placeholders (em-dash '—', en-dash '–', underscores '___', or just empty space after a label).");
                prompt.AppendLine("Some positions may already have ${...} tags — leave those unchanged.");
                prompt.AppendLine();
                prompt.AppendLine("Available template tags (use ONLY these exact tag names):");
                prompt.Append(tagListSb);
                prompt.AppendLine();
                prompt.AppendLine("Return ONLY a valid JSON array. Each element:");
                prompt.AppendLine("  \"context_before\" — 2-6 words of text immediately BEFORE the placeholder (the label, e.g. 'zaměstnanec:' or 'č. dokladu:')");
                prompt.AppendLine("  \"replace_what\"   — the EXACT placeholder characters (e.g. '—' or '–' or '___'), copy exactly as in the document");
                prompt.AppendLine("  \"tag\"            — tag name from the list above");
                prompt.AppendLine();
                prompt.AppendLine("Example for a contract like:");
                prompt.AppendLine("  zaměstnanec:   —   , č. dokladu:  —  ,");
                prompt.AppendLine("  narozen/á:  —  ,    místo narození:  —  ,");
                prompt.AppendLine("  Druh práce (funkce):  —  .");
                prompt.AppendLine("Return:");
                prompt.AppendLine("[");
                prompt.AppendLine("  {\"context_before\": \"zaměstnanec:\",        \"replace_what\": \"—\", \"tag\": \"EMPLOYEE_FullName\"},");
                prompt.AppendLine("  {\"context_before\": \"č. dokladu:\",         \"replace_what\": \"—\", \"tag\": \"EMPLOYEE_PassportNumber\"},");
                prompt.AppendLine("  {\"context_before\": \"narozen/á:\",          \"replace_what\": \"—\", \"tag\": \"EMPLOYEE_BirthDate\"},");
                prompt.AppendLine("  {\"context_before\": \"místo narození:\",      \"replace_what\": \"—\", \"tag\": \"EMPLOYEE_PassportCity\"},");
                prompt.AppendLine("  {\"context_before\": \"Druh práce (funkce):\",\"replace_what\": \"—\", \"tag\": \"EMPLOYEE_Position\"}");
                prompt.AppendLine("]");
                prompt.AppendLine();
                prompt.AppendLine("Critical rules:");
                prompt.AppendLine("- 'context_before' must be text that actually appears in the document, immediately before the placeholder");
                prompt.AppendLine("- 'replace_what' must be the EXACT placeholder character(s) as they appear in the document — copy them exactly");
                prompt.AppendLine("- Analyze the WHOLE document, not just the beginning");
                prompt.AppendLine("- Do NOT replace structural/legal text, article titles, or real content");
                prompt.AppendLine("- Skip any position that already has ${...}");
                prompt.AppendLine("- Cover salary, dates, addresses, positions, contract type — everything that needs data");
                prompt.AppendLine("- If no placeholders found, return []");
                prompt.AppendLine();
                prompt.AppendLine("IMPORTANT — Czech employment contract field mapping (use EXACTLY these tags for these labels):");
                prompt.AppendLine("  'Zaměstnavatel:'                        → AGENCY_Name  (legal employer = the employment agency)");
                prompt.AppendLine("  'se sídlem:', 'sídlem:'                 → AGENCY_FullAddress  (agency address)");
                prompt.AppendLine("  'IČO:', 'IČ:'                           → AGENCY_ICO");
                prompt.AppendLine("  'zaměstnanec:', 'Zaměstnanec:'          → EMPLOYEE_FullName");
                prompt.AppendLine("  'č. dokladu:', 'číslo dokladu:'         → EMPLOYEE_PassportNumber");
                prompt.AppendLine("  'narozen/á:', 'datum narození:'         → EMPLOYEE_BirthDate");
                prompt.AppendLine("  'místo narození:'                       → EMPLOYEE_PassportCity");
                prompt.AppendLine("  'státní občanství:'                     → EMPLOYEE_PassportCountry");
                prompt.AppendLine("  'bydliště v ČR:', 'adresa v ČR:'        → EMPLOYEE_LocalAddress_Full  (employee's Czech Republic address)");
                prompt.AppendLine("  'trvalé bydliště:', 'adresa v zemi původu:' → EMPLOYEE_AbroadAddress_Full  (employee's home country address)");
                prompt.AppendLine("  'Druh práce', 'druh práce (funkce):'    → EMPLOYEE_Position");
                prompt.AppendLine("  'Místo výkonu práce:'                   → EMPLOYEE_WorkAddress");
                prompt.AppendLine("  'uživatel:', 'Uživatel:'                → COMPANY_Name  (the user company where employee actually works)");
                prompt.AppendLine("  'Den nástupu do práce:', 'od:'          → EMPLOYEE_StartDate");
                prompt.AppendLine("  'základní mzda', 'měsíční mzda'         → EMPLOYEE_SalaryBrutto");
                prompt.AppendLine("  'hodinová mzda', 'hodinový výdělek'     → EMPLOYEE_HourlySalary");
                prompt.AppendLine("  'V [city] dne', 'dne' (signature line)  → EMPLOYEE_ContractSignDate  (signing date, NOT start date)");
                prompt.AppendLine("  'zastoupený/á:'                         → leave as-is (real person's name, do not replace)");
                prompt.AppendLine();
                prompt.AppendLine("Document text (analyze completely):");
                prompt.AppendLine(plainText);

                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                var response = await geminiService.ChatAsync(prompt.ToString(), null, cts.Token);

                var replacements = new List<(string ContextBefore, string ReplaceWhat, string Tag)>();
                var jsonMatch = Regex.Match(response, @"\[[\s\S]*?\]", RegexOptions.Singleline);
                if (jsonMatch.Success)
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(jsonMatch.Value);
                        foreach (var elem in doc.RootElement.EnumerateArray())
                        {
                            var contextBefore = elem.TryGetProperty("context_before", out var cb) ? (cb.GetString() ?? string.Empty) : string.Empty;
                            var replaceWhat = elem.TryGetProperty("replace_what", out var rw) ? (rw.GetString() ?? string.Empty) : string.Empty;
                            var tag = elem.TryGetProperty("tag", out var tg) ? (tg.GetString()?.Trim() ?? string.Empty) : string.Empty;

                            if (!string.IsNullOrEmpty(replaceWhat) && !string.IsNullOrEmpty(tag)
                                && allTags.Any(t => t.Tag == tag))
                                replacements.Add((contextBefore, replaceWhat, $"${{{tag}}}"));
                        }
                    }
                    catch (Exception ex) { LoggingService.LogWarning("TemplateEditorViewModel.RunAIInsertTags", $"AI JSON parse error: {ex.Message}"); }
                }

                if (replacements.Count == 0)
                {
                    AITagsStatus = "AI не знайшов місць для тегів. Переконайтесь що в документі є порожні заглушки (—, ___, пробіли після лейблів).";
                }
                else
                {
                    RequestReplaceTagsInDocument?.Invoke(replacements);

                    var sb = new StringBuilder();
                    sb.AppendLine($"✅ Вставлено {replacements.Count} тегів:");
                    foreach (var (ctx, what, tag) in replacements)
                        sb.AppendLine($"  • після \"{ctx}\" → {tag}");
                    AITagsStatus = sb.ToString();
                }
            }
            catch (Exception ex)
            {
                AITagsStatus = $"Помилка: {ex.Message}";
                LoggingService.LogError("TemplateEditorViewModel.RunAIInsertTags", ex);
            }
            finally
            {
                IsAITagsRunning = false;
            }
        }

        private void CopyTag(object? parameter)
        {
            try
            {
                string tagText;
                if (parameter is string tag)
                    tagText = $"${{{tag}}}";
                else if (parameter is TagEntry entry)
                    tagText = $"${{{entry.Tag}}}";
                else
                    return;

                Clipboard.SetText(tagText);
                StatusMessage = ResF("EditorCopied", tagText);
            }
            catch (Exception ex) { LoggingService.LogWarning("TemplateEditorViewModel.CopyTag", ex.Message); }
        }
    }
}
