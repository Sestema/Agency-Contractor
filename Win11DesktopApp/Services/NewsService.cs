using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    public sealed class NewsService
    {
        private static readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private static readonly Regex _htmlRegex = new("<.*?>", RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly string[] _importantKeywords =
        {
            "zmena", "změna", "novela", "zakon", "zákon", "legislativ", "povoleni",
            "povolení", "viz", "viza", "víza", "pobyt", "cizinc", "zamestnan", "zaměstnan",
            "urad prace", "úřad práce", "migrac", "hlaseni", "hlášení"
        };

        private readonly string _cachePath;
        private readonly List<NewsSourceDefinition> _sources;

        public NewsService()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appFolder = System.IO.Path.Combine(appData, "AgencyContractor");
            System.IO.Directory.CreateDirectory(appFolder);
            _cachePath = System.IO.Path.Combine(appFolder, "news-cache.json");

            _sources = new List<NewsSourceDefinition>
            {
                new()
                {
                    Id = "urad-prace",
                    Name = "Úřad práce",
                    FeedUrl = "https://www.uradprace.cz/feed/aktuality"
                },
                new()
                {
                    Id = "mpsv",
                    Name = "MPSV",
                    FeedUrl = "https://news.google.com/rss/search?q=site:mpsv.cz+(zamestnanost+OR+legislativa+OR+prace+OR+urad+prace)&hl=cs&gl=CZ&ceid=CZ:cs"
                },
                new()
                {
                    Id = "mvcr",
                    Name = "MVČR",
                    FeedUrl = "https://news.google.com/rss/search?q=site:mvcr.cz+(migrace+OR+cizinci+OR+pobyt+OR+zamestnani)&hl=cs&gl=CZ&ceid=CZ:cs"
                }
            };
        }

        public IReadOnlyList<NewsSourceDefinition> GetSources()
        {
            return _sources;
        }

        public async Task<IReadOnlyList<NewsArticle>> GetLatestArticlesAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
        {
            var cache = LoadCache();
            if (!forceRefresh && cache.Articles.Count > 0 && DateTime.UtcNow - cache.CachedAtUtc < TimeSpan.FromMinutes(30))
                return cache.Articles.OrderByDescending(x => x.PublishedAtUtc).ToList();

            var loadedArticles = new List<NewsArticle>();

            foreach (var source in _sources)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var xml = await _httpClient.GetStringAsync(source.FeedUrl, cancellationToken).ConfigureAwait(false);
                    loadedArticles.AddRange(ParseRss(xml, source));
                }
                catch (Exception ex)
                {
                    LoggingService.LogWarning("NewsService.GetLatestArticlesAsync", $"{source.Id}: {ex.Message}");
                }
            }

            if (loadedArticles.Count == 0)
                return cache.Articles.OrderByDescending(x => x.PublishedAtUtc).ToList();

            var distinctArticles = loadedArticles
                .Where(x => !string.IsNullOrWhiteSpace(x.Title) && !string.IsNullOrWhiteSpace(x.Url))
                .GroupBy(x => $"{x.SourceId}|{x.Title.Trim()}|{x.Url.Trim()}", StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(x => x.PublishedAtUtc).First())
                .OrderByDescending(x => x.PublishedAtUtc)
                .Take(80)
                .ToList();

            MergeCachedArticleData(cache.Articles, distinctArticles);

            SaveCache(new NewsCacheFile
            {
                CachedAtUtc = DateTime.UtcNow,
                Articles = distinctArticles
            });

            return distinctArticles;
        }

        public NewsArticleTranslation? GetCachedTranslation(NewsArticle article, string languageCode)
        {
            if (article == null || string.IsNullOrWhiteSpace(languageCode))
                return null;

            return article.Translations.FirstOrDefault(t =>
                string.Equals(t.LanguageCode, languageCode, StringComparison.OrdinalIgnoreCase));
        }

        public NewsArticleAiInsight? GetCachedAiInsight(NewsArticle article, string languageCode)
        {
            if (article == null || string.IsNullOrWhiteSpace(languageCode))
                return null;

            return article.AiInsights.FirstOrDefault(t =>
                string.Equals(t.LanguageCode, languageCode, StringComparison.OrdinalIgnoreCase));
        }

        public void SaveArticleTranslation(NewsArticle article, NewsArticleTranslation translation)
        {
            ArgumentNullException.ThrowIfNull(article);
            ArgumentNullException.ThrowIfNull(translation);

            var cache = LoadCache();
            var cacheArticle = FindMatchingArticle(cache.Articles, article);
            if (cacheArticle == null)
            {
                cacheArticle = article;
                cache.Articles.Add(cacheArticle);
            }

            cacheArticle.Translations ??= new List<NewsArticleTranslation>();
            var existing = cacheArticle.Translations.FirstOrDefault(t =>
                string.Equals(t.LanguageCode, translation.LanguageCode, StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                cacheArticle.Translations.Add(translation);
            }
            else
            {
                existing.LanguageName = translation.LanguageName;
                existing.TranslatedTitle = translation.TranslatedTitle;
                existing.TranslatedBody = translation.TranslatedBody;
                existing.TranslatedAtUtc = translation.TranslatedAtUtc;
            }

            article.Translations ??= new List<NewsArticleTranslation>();
            var localExisting = article.Translations.FirstOrDefault(t =>
                string.Equals(t.LanguageCode, translation.LanguageCode, StringComparison.OrdinalIgnoreCase));

            if (localExisting == null)
            {
                article.Translations.Add(translation);
            }
            else
            {
                localExisting.LanguageName = translation.LanguageName;
                localExisting.TranslatedTitle = translation.TranslatedTitle;
                localExisting.TranslatedBody = translation.TranslatedBody;
                localExisting.TranslatedAtUtc = translation.TranslatedAtUtc;
            }

            SaveCache(cache);
        }

        public void SaveArticleAiInsight(NewsArticle article, NewsArticleAiInsight insight)
        {
            ArgumentNullException.ThrowIfNull(article);
            ArgumentNullException.ThrowIfNull(insight);

            var cache = LoadCache();
            var cacheArticle = FindMatchingArticle(cache.Articles, article);
            if (cacheArticle == null)
            {
                cacheArticle = article;
                cache.Articles.Add(cacheArticle);
            }

            cacheArticle.AiInsights ??= new List<NewsArticleAiInsight>();
            var existing = cacheArticle.AiInsights.FirstOrDefault(t =>
                string.Equals(t.LanguageCode, insight.LanguageCode, StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                cacheArticle.AiInsights.Add(insight);
            }
            else
            {
                existing.Version = insight.Version;
                existing.LanguageName = insight.LanguageName;
                existing.Summary = insight.Summary;
                existing.KeyPoints = insight.KeyPoints?.ToList() ?? new List<string>();
                existing.PracticalImpact = insight.PracticalImpact;
                existing.GeneratedAtUtc = insight.GeneratedAtUtc;
            }

            article.AiInsights ??= new List<NewsArticleAiInsight>();
            var localExisting = article.AiInsights.FirstOrDefault(t =>
                string.Equals(t.LanguageCode, insight.LanguageCode, StringComparison.OrdinalIgnoreCase));

            if (localExisting == null)
            {
                article.AiInsights.Add(CloneAiInsight(insight));
            }
            else
            {
                localExisting.Version = insight.Version;
                localExisting.LanguageName = insight.LanguageName;
                localExisting.Summary = insight.Summary;
                localExisting.KeyPoints = insight.KeyPoints?.ToList() ?? new List<string>();
                localExisting.PracticalImpact = insight.PracticalImpact;
                localExisting.GeneratedAtUtc = insight.GeneratedAtUtc;
            }

            SaveCache(cache);
        }

        private List<NewsArticle> ParseRss(string xml, NewsSourceDefinition source)
        {
            var result = new List<NewsArticle>();

            var document = XDocument.Parse(xml);
            var items = document.Descendants("item");

            foreach (var item in items)
            {
                var title = NormalizeText(item.Element("title")?.Value);
                var link = NormalizeText(item.Element("link")?.Value);
                var description = NormalizeText(item.Element("description")?.Value);
                var pubDate = NormalizeText(item.Element("pubDate")?.Value);

                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(link))
                    continue;

                result.Add(new NewsArticle
                {
                    Title = title,
                    Url = link,
                    Summary = TrimSummary(description),
                    SourceId = source.Id,
                    SourceName = source.Name,
                    PublishedAtUtc = ParsePublishedAt(pubDate),
                    IsImportant = IsImportantArticle(title, description)
                });
            }

            return result;
        }

        private NewsCacheFile LoadCache()
        {
            try
            {
                return SafeFileService.ReadJsonOrDefault(_cachePath, new NewsCacheFile(), _jsonOptions, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("NewsService.LoadCache", ex.Message);
                return new NewsCacheFile();
            }
        }

        private void SaveCache(NewsCacheFile cache)
        {
            try
            {
                SafeFileService.WriteJsonAtomic(_cachePath, cache, _jsonOptions, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("NewsService.SaveCache", ex.Message);
            }
        }

        private static void MergeCachedArticleData(IEnumerable<NewsArticle> cachedArticles, IEnumerable<NewsArticle> freshArticles)
        {
            var cachedList = cachedArticles?.ToList() ?? new List<NewsArticle>();
            foreach (var freshArticle in freshArticles)
            {
                var cachedArticle = FindMatchingArticle(cachedList, freshArticle);
                if (cachedArticle == null)
                    continue;

                freshArticle.Id = string.IsNullOrWhiteSpace(cachedArticle.Id) ? freshArticle.Id : cachedArticle.Id;
                freshArticle.Translations = cachedArticle.Translations?
                    .Select(CloneTranslation)
                    .ToList() ?? new List<NewsArticleTranslation>();
                freshArticle.AiInsights = cachedArticle.AiInsights?
                    .Select(CloneAiInsight)
                    .ToList() ?? new List<NewsArticleAiInsight>();
            }
        }

        private static NewsArticle? FindMatchingArticle(IEnumerable<NewsArticle> articles, NewsArticle target)
        {
            return articles.FirstOrDefault(article =>
                string.Equals(article.SourceId, target.SourceId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(article.Url?.Trim(), target.Url?.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        private static NewsArticleTranslation CloneTranslation(NewsArticleTranslation source)
        {
            return new NewsArticleTranslation
            {
                LanguageCode = source.LanguageCode,
                LanguageName = source.LanguageName,
                TranslatedTitle = source.TranslatedTitle,
                TranslatedBody = source.TranslatedBody,
                TranslatedAtUtc = source.TranslatedAtUtc
            };
        }

        private static NewsArticleAiInsight CloneAiInsight(NewsArticleAiInsight source)
        {
            return new NewsArticleAiInsight
            {
                Version = source.Version,
                LanguageCode = source.LanguageCode,
                LanguageName = source.LanguageName,
                Summary = source.Summary,
                KeyPoints = source.KeyPoints?.ToList() ?? new List<string>(),
                PracticalImpact = source.PracticalImpact,
                GeneratedAtUtc = source.GeneratedAtUtc
            };
        }

        private static string NormalizeText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var decoded = WebUtility.HtmlDecode(value);
            decoded = _htmlRegex.Replace(decoded, " ");
            decoded = decoded.Replace("\r", " ", StringComparison.Ordinal)
                             .Replace("\n", " ", StringComparison.Ordinal);
            while (decoded.Contains("  ", StringComparison.Ordinal))
                decoded = decoded.Replace("  ", " ", StringComparison.Ordinal);

            return decoded.Trim();
        }

        private static string TrimSummary(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return value.Length <= 220 ? value : value[..217].TrimEnd() + "...";
        }

        private static DateTime ParsePublishedAt(string value)
        {
            if (DateTimeOffset.TryParse(value, out var parsed))
                return parsed.UtcDateTime;

            return DateTime.UtcNow;
        }

        private static bool IsImportantArticle(string title, string summary)
        {
            var haystack = $"{title} {summary}".ToLowerInvariant();
            return _importantKeywords.Any(keyword => haystack.Contains(keyword, StringComparison.Ordinal));
        }
    }
}
