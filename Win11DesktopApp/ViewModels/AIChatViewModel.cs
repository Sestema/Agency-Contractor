using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.ViewModels
{
    public class ChatMessage
    {
        public string Text { get; set; } = "";
        public bool IsUser { get; set; }
        public bool IsSystem { get; set; }
        public string? FilePath { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;

        public string TimeDisplay => Timestamp.ToString("HH:mm");
        public bool HasImage => !string.IsNullOrEmpty(FilePath) && File.Exists(FilePath) && GeminiApiService.IsImageFile(FilePath);
        public bool HasFile => !string.IsNullOrEmpty(FilePath) && File.Exists(FilePath);
        public string FileName => !string.IsNullOrEmpty(FilePath) ? Path.GetFileName(FilePath) : "";
    }

    public class ChatSessionItem : ViewModelBase
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public DateTime LastMessageAt { get; set; }
        public string DateDisplay => LastMessageAt.ToString("dd.MM HH:mm");

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }

    public class AIChatViewModel : ViewModelBase
    {
        private CancellationTokenSource? _cts;
        private readonly ChatPersistenceService _chatService = new();
        private ChatSession? _currentSession;

        private const string SystemPrompt =
@"You are an expert AI assistant for 'Agency Contractor' — a Czech employment agency management application (agentura práce).

YOUR ROLE:
- You are the main AI consultant for a staffing/employment agency operating in the Czech Republic
- You help with employee management, documents, contracts, labor law, work permits, visas, insurance
- You analyze documents (PDF, XLSX, DOCX) and images of employee documents

CZECH LABOR LAW (your core knowledge):
- Zákoník práce (Zákon č. 262/2006 Sb.) — Labor Code: working hours, overtime, holidays, termination
- Zákon o zaměstnanosti (Zákon č. 435/2004 Sb.) — Employment Act: agency employment, foreign workers
- Zákon o pobytu cizinců (Zákon č. 326/1999 Sb.) — Foreigners Residence Act
- Agenturní zaměstnávání — agency employment rules (§ 307a–309 ZP): equal treatment, temporary assignment
- BOZP (Bezpečnost a ochrana zdraví při práci) — workplace safety obligations
- Minimum wage 2025: 20,800 CZK/month, 124.40 CZK/hour
- Working week: max 40 hours, overtime max 8h/week averaged over 26 weeks
- Holiday: min 4 weeks per year
- Trial period: max 3 months (6 months for management)

WORK PERMITS & VISA TYPES:
- Dočasná ochrana (D/DO/667, D/DO/668, D/DO/669) — temporary protection for Ukrainian refugees
- Dočasná ochrana opakovaná (D/DO/767–769) — repeated temporary protection
- Dočasná ochrana prodloužená (D/DO/867–869) — extended temporary protection
- Strpění (D/VS/91, D/SD/91) — tolerance visa
- Přechodný pobyt — temporary residence for EU citizens (registration at OAMP)
- Trvalý pobyt — permanent residence (after 5 years)
- Zaměstnanecká karta — employee card for non-EU workers (dual permit: residence + work)
- Modrá karta — EU Blue Card for highly qualified workers
- Krátkodobé vízum (C) — Schengen short-stay visa (max 90 days)
- Osvědčení o registraci — EU citizen registration certificate (NOT a work permit)

CZ-ISCO JOB CLASSIFICATION CODES (common in agency work):
- 93290 Pomocní pracovníci ve výrobě (production helpers)
- 93293 Manipulační dělníci (handling workers)
- 93110 Pomocní pracovníci v dolech a lomech
- 82191 Montážní dělníci (assembly workers)
- 82199 Obsluha stacionárních strojů a zařízení
- 75231 Seřizovači a obsluha obráběcích strojů (CNC operators)
- 72230 Seřizovači a obsluha strojů na zpracování kovů
- 51201 Kuchaři (cooks)
- 94120 Pomocníci v kuchyni (kitchen helpers)
- 83441 Řidiči nákladních automobilů (truck drivers)
- 83442 Řidiči autobusů
- 91120 Uklízeči a pomocníci (cleaners)
- 93210 Ruční baliči (manual packers)
- 72221 Svářeči (welders)
- 74110 Stavební elektrikáři
- 71110 Stavbyvedoucí (construction managers)

CZECH HEALTH INSURANCE COMPANIES:
- VZP ČR (kód 111) — Všeobecná zdravotní pojišťovna
- ZPMV (kód 211) — Zdravotní pojišťovna ministerstva vnitra
- OZP (kód 207) — Oborová zdravotní pojišťovna
- ČPZP (kód 205) — Česká průmyslová zdravotní pojišťovna
- VoZP (kód 201) — Vojenská zdravotní pojišťovna
- ZPŠ (kód 209) — Zaměstnanecká pojišťovna Škoda
- RBP (kód 213) — Revírní bratrská pokladna

KEY CZECH CITIES (common work locations):
Praha, Brno, Ostrava, Plzeň, Liberec, Olomouc, České Budějovice, Hradec Králové, Ústí nad Labem, Pardubice, Zlín, Karlovy Vary, Jihlava, Most, Mladá Boleslav, Kolín, Kladno

AGENCY OBLIGATIONS:
- Must hold a valid license (povolení ke zprostředkování zaměstnání) from MPSV
- Must provide equal pay and conditions as direct employees (§ 309 odst. 5 ZP)
- Cannot assign to employers in strike/lockout
- Max temporary assignment: law doesn't set a hard limit, but repeated assignments to same employer may be challenged
- Must report to Úřad práce (Labor Office)

DOCUMENT TYPES IN THE APPLICATION:
- Passport (cestovní pas)
- Visa sticker / residence permit stamp
- Health insurance card (průkaz pojištěnce)
- Work permit (pracovní povolení)
- Employment contract (pracovní smlouva)
- Temporary assignment agreement (dohoda o dočasném přidělení)

CRITICAL RULES:
- ALWAYS respond in the SAME language the user writes in (Ukrainian, Czech, Russian, English)
- Be precise with legal references — cite law numbers when relevant
- When unsure about current regulations, say so honestly — do NOT invent laws or numbers
- Format responses clearly with bullet points, headers, and structure
- For salary calculations, consider: gross salary, social insurance (24.8% employer), health insurance (9% employer), income tax
- Dates should be in DD.MM.YYYY format";

        public ICommand GoBackCommand { get; }
        public ICommand SendMessageCommand { get; }
        public ICommand AttachFileCommand { get; }
        public ICommand RemoveFileCommand { get; }
        public ICommand NewChatCommand { get; }
        public ICommand DeleteChatCommand { get; }
        public ICommand SelectChatCommand { get; }

        public ObservableCollection<ChatMessage> Messages { get; } = new();
        public ObservableCollection<ChatSessionItem> ChatSessions { get; } = new();

        private string _inputText = "";
        public string InputText
        {
            get => _inputText;
            set => SetProperty(ref _inputText, value);
        }

        private string? _attachedFilePath;
        public string? AttachedFilePath
        {
            get => _attachedFilePath;
            set
            {
                SetProperty(ref _attachedFilePath, value);
                OnPropertyChanged(nameof(HasAttachedFile));
                OnPropertyChanged(nameof(AttachedFileName));
            }
        }

        public bool HasAttachedFile => !string.IsNullOrEmpty(AttachedFilePath);
        public string AttachedFileName => !string.IsNullOrEmpty(AttachedFilePath) ? Path.GetFileName(AttachedFilePath) : "";

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        private bool _isTyping;
        public bool IsTyping
        {
            get => _isTyping;
            set => SetProperty(ref _isTyping, value);
        }

        public bool IsGeminiConfigured => App.GeminiApiService?.IsConfigured ?? false;

        public AIChatViewModel()
        {
            GoBackCommand = new RelayCommand(o =>
            {
                SaveCurrentSession();
                App.NavigationService.NavigateTo(new MainViewModel());
            });

            SendMessageCommand = new RelayCommand(async o => await SendMessage(), o => !IsBusy);

            AttachFileCommand = new RelayCommand(o =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "All supported|*.jpg;*.jpeg;*.png;*.bmp;*.pdf;*.xlsx;*.docx|" +
                             "Images (*.jpg, *.png)|*.jpg;*.jpeg;*.png;*.bmp|" +
                             "PDF (*.pdf)|*.pdf|" +
                             "Excel (*.xlsx)|*.xlsx|" +
                             "Word (*.docx)|*.docx|" +
                             "All files (*.*)|*.*",
                    Title = Res("AIChatAttachImage")
                };
                if (dlg.ShowDialog() == true)
                    AttachedFilePath = dlg.FileName;
            });

            RemoveFileCommand = new RelayCommand(o => AttachedFilePath = null);

            NewChatCommand = new RelayCommand(o =>
            {
                SaveCurrentSession();
                CreateNewSession();
            });

            DeleteChatCommand = new RelayCommand(o =>
            {
                if (o is ChatSessionItem item)
                {
                    _chatService.DeleteSession(item.Id);
                    ChatSessions.Remove(item);
                    if (_currentSession?.Id == item.Id)
                    {
                        _currentSession = null;
                        Messages.Clear();
                        if (ChatSessions.Count > 0)
                            LoadSession(ChatSessions[0].Id);
                        else
                            CreateNewSession();
                    }
                }
            });

            SelectChatCommand = new RelayCommand(o =>
            {
                if (o is ChatSessionItem item && item.Id != _currentSession?.Id)
                {
                    SaveCurrentSession();
                    LoadSession(item.Id);
                }
            });

            LoadChatSessions();
        }

        private void LoadChatSessions()
        {
            var sessions = _chatService.LoadAllSessions();
            ChatSessions.Clear();

            foreach (var s in sessions)
            {
                ChatSessions.Add(new ChatSessionItem
                {
                    Id = s.Id,
                    Title = s.Title,
                    LastMessageAt = s.LastMessageAt
                });
            }

            if (sessions.Count > 0)
                LoadSession(sessions[0].Id);
            else
                CreateNewSession();
        }

        private void LoadSession(string sessionId)
        {
            var sessions = _chatService.LoadAllSessions();
            var session = sessions.FirstOrDefault(s => s.Id == sessionId);
            if (session == null) return;

            _currentSession = session;
            Messages.Clear();

            foreach (var dto in session.Messages)
            {
                Messages.Add(new ChatMessage
                {
                    Text = dto.Text,
                    IsUser = dto.IsUser,
                    IsSystem = dto.IsSystem,
                    Timestamp = dto.Timestamp
                });
            }

            foreach (var item in ChatSessions)
                item.IsSelected = item.Id == sessionId;
        }

        private void CreateNewSession()
        {
            _currentSession = new ChatSession
            {
                Title = Res("AIChatNewChat")
            };

            Messages.Clear();

            if (IsGeminiConfigured)
            {
                Messages.Add(new ChatMessage
                {
                    Text = Res("AIChatWelcome") + "\n[Google Gemini]",
                    IsUser = false,
                    IsSystem = true
                });
            }
            else
            {
                Messages.Add(new ChatMessage
                {
                    Text = Res("AIChatNoModel"),
                    IsUser = false,
                    IsSystem = true
                });
            }

            SaveCurrentSession();

            var newItem = new ChatSessionItem
            {
                Id = _currentSession.Id,
                Title = _currentSession.Title,
                LastMessageAt = _currentSession.LastMessageAt,
                IsSelected = true
            };

            foreach (var item in ChatSessions)
                item.IsSelected = false;

            ChatSessions.Insert(0, newItem);
        }

        private void SaveCurrentSession()
        {
            if (_currentSession == null) return;

            _currentSession.Messages = Messages
                .Where(m => !string.IsNullOrEmpty(m.Text))
                .Select(m => new ChatMessageDto
                {
                    Text = m.Text,
                    IsUser = m.IsUser,
                    IsSystem = m.IsSystem,
                    Timestamp = m.Timestamp
                }).ToList();

            if (_currentSession.Messages.Count > 0)
                _currentSession.LastMessageAt = _currentSession.Messages.Last().Timestamp;

            _chatService.SaveSession(_currentSession);

            var existing = ChatSessions.FirstOrDefault(s => s.Id == _currentSession.Id);
            if (existing != null)
            {
                existing.Title = _currentSession.Title;
                existing.LastMessageAt = _currentSession.LastMessageAt;
            }
        }

        private async Task SendMessage()
        {
            var text = InputText?.Trim() ?? "";
            var filePath = AttachedFilePath;

            if (string.IsNullOrEmpty(text) && string.IsNullOrEmpty(filePath))
                return;

            if (!IsGeminiConfigured)
            {
                Messages.Add(new ChatMessage
                {
                    Text = Res("AIChatNoModel"),
                    IsUser = false,
                    IsSystem = true
                });
                return;
            }

            Messages.Add(new ChatMessage
            {
                Text = text,
                IsUser = true,
                FilePath = filePath
            });

            if (_currentSession != null && _currentSession.Title == Res("AIChatNewChat") && !string.IsNullOrEmpty(text))
            {
                _currentSession.Title = text.Length > 40 ? text[..40] + "..." : text;
                var sessionItem = ChatSessions.FirstOrDefault(s => s.Id == _currentSession.Id);
                if (sessionItem != null)
                    sessionItem.Title = _currentSession.Title;
            }

            InputText = "";
            AttachedFilePath = null;
            IsBusy = true;
            IsTyping = true;

            var thinkingMsg = new ChatMessage
            {
                Text = Res("AIChatThinking"),
                IsUser = false,
                IsSystem = true
            };
            Messages.Add(thinkingMsg);

            try
            {
                _cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));

                var history = BuildConversationHistory();
                string response = await ProcessMessageAsync(text, filePath, SystemPrompt, history, _cts.Token);

                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    Messages.Remove(thinkingMsg);
                    Messages.Add(new ChatMessage
                    {
                        Text = response,
                        IsUser = false
                    });
                    SaveCurrentSession();
                });
            }
            catch (Exception ex)
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    Messages.Remove(thinkingMsg);
                    Messages.Add(new ChatMessage
                    {
                        Text = $"[Error: {ex.Message}]",
                        IsUser = false,
                        IsSystem = true
                    });
                });
            }
            finally
            {
                IsBusy = false;
                IsTyping = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        private List<(string role, string text)> BuildConversationHistory()
        {
            var history = new List<(string role, string text)>();
            const int maxMessages = 40;

            var relevantMessages = Messages
                .Where(m => !m.IsSystem && !string.IsNullOrWhiteSpace(m.Text))
                .TakeLast(maxMessages)
                .ToList();

            // Exclude the last user message (it's sent separately as the current turn)
            if (relevantMessages.Count > 0 && relevantMessages.Last().IsUser)
                relevantMessages.RemoveAt(relevantMessages.Count - 1);

            foreach (var msg in relevantMessages)
                history.Add((msg.IsUser ? "user" : "model", msg.Text));

            return history;
        }

        private static async Task<string> ProcessMessageAsync(
            string text, string? filePath, string systemPrompt,
            List<(string role, string text)>? history, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return await App.GeminiApiService.ChatWithHistoryAsync(history, text, systemPrompt, ct);

            var ext = Path.GetExtension(filePath).ToLowerInvariant();

            if (GeminiApiService.IsImageFile(filePath))
                return await App.GeminiApiService.ChatWithImageAsync(filePath, text, systemPrompt, ct);

            if (ext == ".pdf")
                return await App.GeminiApiService.ChatWithFileAsync(filePath, text, systemPrompt, ct);

            var docText = ext switch
            {
                ".xlsx" => ExtractXlsxText(filePath),
                ".docx" => ExtractDocxText(filePath),
                _ => null
            };

            if (docText != null)
            {
                var combined = $"The user attached a document ({Path.GetFileName(filePath)}). Here is the document content:\n\n" +
                               $"---DOCUMENT START---\n{docText}\n---DOCUMENT END---\n\n" +
                               $"User message: {text}";
                return await App.GeminiApiService.ChatWithHistoryAsync(history, combined, systemPrompt, ct);
            }

            return await App.GeminiApiService.ChatWithHistoryAsync(history, text, systemPrompt, ct);
        }

        private static string ExtractXlsxText(string path)
        {
            var sb = new StringBuilder();
            using var workbook = new ClosedXML.Excel.XLWorkbook(path);
            foreach (var ws in workbook.Worksheets)
            {
                sb.AppendLine($"[Sheet: {ws.Name}]");
                var range = ws.RangeUsed();
                if (range == null) continue;

                foreach (var row in range.RowsUsed())
                {
                    var cells = new List<string>();
                    foreach (var cell in row.CellsUsed())
                        cells.Add(cell.GetFormattedString());
                    sb.AppendLine(string.Join(" | ", cells));
                }
                sb.AppendLine();
            }
            return sb.ToString().TrimEnd();
        }

        private static string ExtractDocxText(string path)
        {
            var sb = new StringBuilder();
            using var doc = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(path, false);
            var body = doc?.MainDocumentPart?.Document?.Body;
            if (body == null) return "[Empty document]";

            foreach (var para in body.Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>())
            {
                var line = para.InnerText;
                if (!string.IsNullOrWhiteSpace(line))
                    sb.AppendLine(line);
            }
            return sb.ToString().TrimEnd();
        }
    }
}
