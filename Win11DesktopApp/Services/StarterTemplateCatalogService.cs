using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    public sealed class StarterTemplateCatalogService
    {
        private readonly string _catalogRoot;
        private readonly string _catalogFilePath;
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };

        public StarterTemplateCatalogService(string? catalogRoot = null)
        {
            _catalogRoot = catalogRoot ?? Path.Combine(AppContext.BaseDirectory, "TemplateCatalog");
            _catalogFilePath = Path.Combine(_catalogRoot, "catalog.json");
        }

        public IReadOnlyList<StarterTemplateCatalogEntry> GetContractTemplates()
        {
            if (!File.Exists(_catalogFilePath))
                return Array.Empty<StarterTemplateCatalogEntry>();

            try
            {
                var catalog = SafeFileService.ReadJsonOrDefault(_catalogFilePath, new StarterTemplateCatalogFile(), _jsonOptions);
                return catalog.Templates
                    .Where(t => string.Equals(t.Category, "contract", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(t.Format, "DOCX", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(t.RelativeContentPath))
                    .OrderBy(t => t.Title)
                    .ToList();
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("StarterTemplateCatalogService.GetContractTemplates", ex.Message);
                return Array.Empty<StarterTemplateCatalogEntry>();
            }
        }

        public string? LoadTemplateRtf(StarterTemplateCatalogEntry? entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.RelativeContentPath))
                return null;

            try
            {
                var fullPath = Path.Combine(_catalogRoot, entry.RelativeContentPath);
                if (!File.Exists(fullPath))
                    return null;

                return SafeFileService.ReadAllText(fullPath);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("StarterTemplateCatalogService.LoadTemplateRtf", ex.Message);
                return null;
            }
        }
    }
}
