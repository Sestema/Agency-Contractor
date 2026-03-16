using System;
using System.Collections.Generic;

namespace Win11DesktopApp.Models
{
    public sealed class NewsArticle
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string SourceId { get; set; } = string.Empty;
        public string SourceName { get; set; } = string.Empty;
        public DateTime PublishedAtUtc { get; set; } = DateTime.UtcNow;
        public bool IsImportant { get; set; }
        public List<NewsArticleTranslation> Translations { get; set; } = new();
        public List<NewsArticleAiInsight> AiInsights { get; set; } = new();
    }

    public sealed class NewsArticleTranslation
    {
        public string LanguageCode { get; set; } = string.Empty;
        public string LanguageName { get; set; } = string.Empty;
        public string TranslatedTitle { get; set; } = string.Empty;
        public string TranslatedBody { get; set; } = string.Empty;
        public DateTime TranslatedAtUtc { get; set; } = DateTime.UtcNow;
    }

    public sealed class NewsArticleAiInsight
    {
        public int Version { get; set; }
        public string LanguageCode { get; set; } = string.Empty;
        public string LanguageName { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public List<string> KeyPoints { get; set; } = new();
        public string PracticalImpact { get; set; } = string.Empty;
        public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
    }

    public sealed class NewsCacheFile
    {
        public DateTime CachedAtUtc { get; set; } = DateTime.UtcNow;
        public List<NewsArticle> Articles { get; set; } = new();
    }

    public sealed class NewsSourceDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string FeedUrl { get; set; } = string.Empty;
    }
}
