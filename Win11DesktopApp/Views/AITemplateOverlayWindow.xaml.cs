using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.Views
{
    public class OverlayMessage
    {
        public string Text { get; set; } = "";
        public bool IsUser { get; set; }
    }

    public partial class AITemplateOverlayWindow : Window
    {
        private readonly ObservableCollection<OverlayMessage> _messages = new();
        private readonly List<(string role, string text)> _history = new();
        private Func<string?>? _getTemplateContent;
        private Func<string?>? _getTagCatalog;
        private CancellationTokenSource? _cts;

        private const string SystemPrompt =
@"You are an expert document analyst and template tag assistant for 'Agency Contractor' — a Czech employment agency app.

You help users with TWO tasks:
1) ANALYZING document templates — legal correctness, structure, grammar, completeness
2) INSERTING TAGS — suggesting where to place ${tag_name} placeholders in the template

You maintain full context of the conversation. When the user asks about tags after analyzing a template, you already know the template content from earlier messages.

Template tags use the syntax ${TAG_NAME}. When the user clicks 'Suggest Tags', you will receive the full list of available tags — use ONLY those tags.

For each tag suggestion, show: the exact ${TAG_NAME}, what text in the template it should replace, and the location (e.g. row/section).

RULES:
- ALWAYS respond in the SAME language the user writes in
- For Czech employment documents, you know Zákoník práce, Zákon o zaměstnanosti, and related regulations
- Be concise — this is a small overlay chat
- Remember everything discussed in this conversation";

        public AITemplateOverlayWindow()
        {
            InitializeComponent();
            MessagesList.ItemsSource = _messages;

            _messages.Add(new OverlayMessage
            {
                Text = Res("AIOverlayWelcome"),
                IsUser = false
            });
        }

        public void SetContentProviders(Func<string?> getTemplateContent, Func<string?> getTagCatalog)
        {
            _getTemplateContent = getTemplateContent;
            _getTagCatalog = getTagCatalog;
        }

        public void ResetConversation()
        {
            _messages.Clear();
            _history.Clear();
            _messages.Add(new OverlayMessage { Text = Res("AIOverlayWelcome"), IsUser = false });
        }

        private static string Res(string key)
        {
            try { return Application.Current.FindResource(key) as string ?? key; }
            catch { return key; }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
                DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        private void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                SendMessage_Click(sender, e);
                e.Handled = true;
            }
        }

        private async void SendMessage_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var text = InputBox.Text?.Trim();
                if (string.IsNullOrEmpty(text)) return;
                await SendToAI(text);
            }
            catch (Exception ex) { LoggingService.LogError("SendMessage_Click", ex); }
        }

        private async void AnalyzeTemplate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var content = _getTemplateContent?.Invoke();
                if (string.IsNullOrEmpty(content))
                {
                    _messages.Add(new OverlayMessage { Text = Res("AIOverlayNoContent"), IsUser = false });
                    ScrollToEnd();
                    return;
                }

                var userInstruction = InputBox.Text?.Trim();
                if (string.IsNullOrEmpty(userInstruction))
                    userInstruction = "Review this document template. Check legal correctness, structure, completeness, and suggest what should be added or changed.";

                var plainText = ExtractPlainText(content);
                var prompt = $"{userInstruction}\n\n---TEMPLATE---\n{plainText}\n---END---";
                InputBox.Text = "";
                await SendToAI(prompt, $"\U0001F4CB {userInstruction}");
            }
            catch (Exception ex) { LoggingService.LogError("AnalyzeTemplate_Click", ex); }
        }

        private async void SuggestTags_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var content = _getTemplateContent?.Invoke();
                if (string.IsNullOrEmpty(content))
                {
                    _messages.Add(new OverlayMessage { Text = Res("AIOverlayNoContent"), IsUser = false });
                    ScrollToEnd();
                    return;
                }

                var userInstruction = InputBox.Text?.Trim();
                var plainText = ExtractPlainText(content);
                var tagCatalog = _getTagCatalog?.Invoke() ?? "";

                var promptParts = new System.Text.StringBuilder();
                if (!string.IsNullOrEmpty(userInstruction))
                    promptParts.AppendLine(userInstruction);
                else
                    promptParts.AppendLine("Suggest which tags should be inserted into this template and WHERE. For each suggestion, show the exact ${TAG_NAME} and what text it should replace.");

                promptParts.AppendLine();
                promptParts.AppendLine("---AVAILABLE TAGS---");
                promptParts.AppendLine(tagCatalog);
                promptParts.AppendLine("---END TAGS---");
                promptParts.AppendLine();
                promptParts.AppendLine("---TEMPLATE---");
                promptParts.AppendLine(plainText);
                promptParts.AppendLine("---END---");

                InputBox.Text = "";
                await SendToAI(promptParts.ToString(), $"\U0001F3F7 {Res("AISuggestTags")} ({tagCatalog.Split('\n').Length} tags)");
            }
            catch (Exception ex) { LoggingService.LogError("SuggestTags_Click", ex); }
        }

        private async Task SendToAI(string fullPrompt, string? displayMessage = null)
        {
            if (!App.GeminiApiService.IsConfigured)
            {
                _messages.Add(new OverlayMessage { Text = Res("AIChatNoModel"), IsUser = false });
                ScrollToEnd();
                return;
            }

            _messages.Add(new OverlayMessage { Text = displayMessage ?? fullPrompt, IsUser = true });
            _history.Add(("user", fullPrompt));

            _messages.Add(new OverlayMessage { Text = "...", IsUser = false });
            ScrollToEnd();

            InputBox.Text = "";
            BtnAnalyze.IsEnabled = false;
            BtnSuggestTags.IsEnabled = false;

            try
            {
                _cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));

                var historyForApi = _history.Count > 1
                    ? _history.GetRange(0, _history.Count - 1)
                    : null;

                var response = await App.GeminiApiService.ChatWithHistoryAsync(
                    historyForApi, fullPrompt, SystemPrompt, _cts.Token);

                _history.Add(("model", response));

                Dispatcher.Invoke(() =>
                {
                    if (_messages.Count > 0 && _messages.Last().Text == "...")
                        _messages.RemoveAt(_messages.Count - 1);

                    _messages.Add(new OverlayMessage { Text = response, IsUser = false });
                    ScrollToEnd();
                });
            }
            catch (Exception ex)
            {
                if (_history.Count > 0 && _history.Last().role == "user")
                    _history.RemoveAt(_history.Count - 1);

                Dispatcher.Invoke(() =>
                {
                    if (_messages.Count > 0 && _messages.Last().Text == "...")
                        _messages.RemoveAt(_messages.Count - 1);

                    _messages.Add(new OverlayMessage { Text = $"[Error: {ex.Message}]", IsUser = false });
                    ScrollToEnd();
                });
            }
            finally
            {
                Dispatcher.Invoke(() =>
                {
                    BtnAnalyze.IsEnabled = true;
                    BtnSuggestTags.IsEnabled = true;
                });
                _cts?.Dispose();
                _cts = null;
            }
        }

        private void ScrollToEnd()
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
            {
                MessagesScroll.ScrollToEnd();
            });
        }

        private static string ExtractPlainText(string rtfOrText)
        {
            if (string.IsNullOrEmpty(rtfOrText)) return "";
            if (!rtfOrText.TrimStart().StartsWith("{\\rtf"))
                return rtfOrText;

            var sb = new System.Text.StringBuilder();
            bool inControl = false;
            int braceDepth = 0;

            for (int i = 0; i < rtfOrText.Length; i++)
            {
                char c = rtfOrText[i];
                if (c == '{') { braceDepth++; continue; }
                if (c == '}') { braceDepth--; continue; }
                if (c == '\\')
                {
                    inControl = true;
                    int j = i + 1;
                    while (j < rtfOrText.Length && char.IsLetter(rtfOrText[j])) j++;
                    var cmd = rtfOrText[(i + 1)..j];
                    if (cmd == "par" || cmd == "line") sb.AppendLine();
                    else if (cmd == "tab") sb.Append('\t');
                    while (j < rtfOrText.Length && (char.IsDigit(rtfOrText[j]) || rtfOrText[j] == '-')) j++;
                    if (j < rtfOrText.Length && rtfOrText[j] == ' ') j++;
                    i = j - 1;
                    inControl = false;
                    continue;
                }
                if (!inControl && c != '\r' && c != '\n')
                    sb.Append(c);
            }
            return sb.ToString().Trim();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }
    }
}
