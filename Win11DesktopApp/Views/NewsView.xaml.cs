using System;
using System.ComponentModel;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Views
{
    public partial class NewsView : UserControl
    {
        private NewsViewModel? _vm;
        private string _requestedArticleUrl = string.Empty;
        private string _completedArticleUrl = string.Empty;
        private TaskCompletionSource<bool>? _navigationCompletionSource;

        public NewsView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (ArticleWebView != null)
                ArticleWebView.NavigationCompleted += ArticleWebView_NavigationCompleted;
            NavigateToSelectedArticle();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (ArticleWebView != null)
                ArticleWebView.NavigationCompleted -= ArticleWebView_NavigationCompleted;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_vm != null)
            {
                _vm.PropertyChanged -= ViewModel_PropertyChanged;
                _vm.RequestArticleTextAsync = null;
            }

            _vm = DataContext as NewsViewModel;

            if (_vm != null)
            {
                _vm.PropertyChanged += ViewModel_PropertyChanged;
                _vm.RequestArticleTextAsync = GetCurrentArticleTextAsync;
                NavigateToSelectedArticle();
            }
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(NewsViewModel.SelectedArticleUrl))
                NavigateToSelectedArticle();
        }

        private void NavigateToSelectedArticle()
        {
            if (ArticleWebView == null)
                return;

            var url = _vm?.SelectedArticleUrl;
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                _requestedArticleUrl = string.Empty;
                _completedArticleUrl = string.Empty;
                _navigationCompletionSource?.TrySetCanceled();
                _navigationCompletionSource = null;
                ArticleWebView.Source = null;
                return;
            }

            if (string.Equals(_requestedArticleUrl, url, StringComparison.OrdinalIgnoreCase)
                && string.Equals(ArticleWebView.Source?.AbsoluteUri, uri.AbsoluteUri, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _requestedArticleUrl = uri.AbsoluteUri;
            _completedArticleUrl = string.Empty;
            _navigationCompletionSource?.TrySetCanceled();
            _navigationCompletionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ArticleWebView.Source = uri;
        }

        private async Task<string> GetCurrentArticleTextAsync()
        {
            if (ArticleWebView == null)
                return string.Empty;

            try
            {
                var expectedUrl = _vm?.SelectedArticleUrl;
                if (string.IsNullOrWhiteSpace(expectedUrl) || !Uri.TryCreate(expectedUrl, UriKind.Absolute, out var expectedUri))
                    return string.Empty;

                var normalizedExpectedUrl = expectedUri.AbsoluteUri;
                var navigationReady = await WaitForExpectedNavigationAsync(normalizedExpectedUrl);
                var currentUrl = ArticleWebView.Source?.AbsoluteUri ?? string.Empty;
                if (!navigationReady || !string.Equals(currentUrl, normalizedExpectedUrl, StringComparison.OrdinalIgnoreCase))
                    return string.Empty;

                var script = """
                    (() => {
                        const host = (window.location.hostname || '').toLowerCase();
                        const sourceSelectors = [];

                        if (host.includes('mpsv.cz')) {
                            sourceSelectors.push(
                                'article',
                                'main article',
                                '.article-detail',
                                '.article-content',
                                '.entry-content',
                                '.page-content',
                                '.content'
                            );
                        }

                        if (host.includes('mvcr.cz')) {
                            sourceSelectors.push(
                                'article',
                                'main article',
                                '.article-detail',
                                '.detail',
                                '.article-content',
                                '.entry-content',
                                '.page-content'
                            );
                        }

                        if (host.includes('uradprace.cz')) {
                            sourceSelectors.push(
                                'article',
                                '.journal-content-article',
                                '.asset-full-content',
                                '.news-detail',
                                '.article-content',
                                '.entry-content',
                                '.portlet-body'
                            );
                        }

                        sourceSelectors.push(
                            'article',
                            'main article',
                            '[role="main"] article',
                            '.article-detail',
                            '.article-content',
                            '.entry-content',
                            '.page-content',
                            'main',
                            '[role="main"]'
                        );

                        const noiseSelectors = [
                            'script',
                            'style',
                            'noscript',
                            'header',
                            'footer',
                            'nav',
                            'aside',
                            'form',
                            'button',
                            '[role="navigation"]',
                            '.sidebar',
                            '.side-nav',
                            '.navigation',
                            '.nav',
                            '.menu',
                            '.breadcrumb',
                            '.breadcrumbs',
                            '.related',
                            '.related-articles',
                            '.share',
                            '.social',
                            '.cookie',
                            '.newsletter',
                            '.article-list',
                            '.news-list',
                            '.portlet-topper',
                            '.portlet-borderless-bar',
                            '.portlet-title-default',
                            '.portlet-decorate'
                        ];

                        const normalizeText = (text) => (text || '')
                            .replace(/\u00a0/g, ' ')
                            .replace(/[ \t]+\n/g, '\n')
                            .replace(/\n{3,}/g, '\n\n')
                            .trim();

                        const extractFromNode = (node) => {
                            if (!(node instanceof HTMLElement)) {
                                return '';
                            }

                            const clone = node.cloneNode(true);
                            clone.querySelectorAll(noiseSelectors.join(',')).forEach(element => element.remove());
                            return normalizeText(clone.innerText || '');
                        };

                        const candidates = [];
                        const seen = new Set();
                        for (const selector of sourceSelectors) {
                            for (const node of document.querySelectorAll(selector)) {
                                if (!(node instanceof HTMLElement) || seen.has(node)) {
                                    continue;
                                }

                                seen.add(node);
                                candidates.push(node);
                            }
                        }

                        if (candidates.length === 0 && document.body) {
                            candidates.push(document.body);
                        }

                        let best = '';
                        for (const candidate of candidates) {
                            const text = extractFromNode(candidate);
                            if (text.length > best.length) {
                                best = text;
                            }
                        }

                        return best;
                    })();
                    """;

                for (var attempt = 0; attempt < 3; attempt++)
                {
                    if (!string.Equals(ArticleWebView.Source?.AbsoluteUri, normalizedExpectedUrl, StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(_completedArticleUrl, normalizedExpectedUrl, StringComparison.OrdinalIgnoreCase))
                    {
                        return string.Empty;
                    }

                    var rawResult = await ArticleWebView.ExecuteScriptAsync(script);
                    if (!string.IsNullOrWhiteSpace(rawResult) && !string.Equals(rawResult, "null", StringComparison.OrdinalIgnoreCase))
                    {
                        var text = JsonSerializer.Deserialize<string>(rawResult) ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(text))
                            return text;
                    }

                    await Task.Delay(350);
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                Services.LoggingService.LogWarning("NewsView.GetCurrentArticleTextAsync", ex.Message);
                return string.Empty;
            }
        }

        private async Task<bool> WaitForExpectedNavigationAsync(string expectedUrl)
        {
            if (string.Equals(_completedArticleUrl, expectedUrl, StringComparison.OrdinalIgnoreCase))
                return true;

            var completionSource = _navigationCompletionSource;
            if (completionSource == null || !string.Equals(_requestedArticleUrl, expectedUrl, StringComparison.OrdinalIgnoreCase))
                return false;

            var completedTask = await Task.WhenAny(completionSource.Task, Task.Delay(8000));
            return completedTask == completionSource.Task
                && completionSource.Task.Status == TaskStatus.RanToCompletion
                && string.Equals(_completedArticleUrl, expectedUrl, StringComparison.OrdinalIgnoreCase);
        }

        private void ArticleWebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (ArticleWebView == null)
                return;

            var currentUrl = ArticleWebView.Source?.AbsoluteUri ?? string.Empty;
            if (!e.IsSuccess)
            {
                _navigationCompletionSource?.TrySetResult(false);
                return;
            }

            if (!string.IsNullOrWhiteSpace(currentUrl)
                && string.Equals(currentUrl, _requestedArticleUrl, StringComparison.OrdinalIgnoreCase))
            {
                _completedArticleUrl = currentUrl;
                _navigationCompletionSource?.TrySetResult(true);
            }
        }

        private void AiQuestionTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None)
                return;

            if (_vm?.AskAboutArticleCommand?.CanExecute(null) == true)
            {
                _vm.AskAboutArticleCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
