using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.IO.Compression;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    public class TemplateService
    {
        private readonly AppSettingsService _appSettingsService;
        private readonly TagCatalogService? _tagCatalogService;
        private readonly FolderService _folderService;
        private const string LogFileName = "template-errors.log";

        public TemplateService(AppSettingsService appSettingsService, FolderService folderService, TagCatalogService? tagCatalogService = null)
        {
            _appSettingsService = appSettingsService;
            _folderService = folderService;
            _tagCatalogService = tagCatalogService;
        }

        public List<TemplateEntry> GetTemplates(string firmName)
        {
            if (string.IsNullOrEmpty(firmName))
                return new List<TemplateEntry>();

            var templatesFolder = _folderService.GetTemplatesFolder(firmName);
            var templatesIndexFile = Path.Combine(templatesFolder, "index.json");

            if (!File.Exists(templatesIndexFile))
                return new List<TemplateEntry>();

            try
            {
                var json = File.ReadAllText(templatesIndexFile);
                var indexEntries = JsonSerializer.Deserialize<List<TemplateIndexEntry>>(json);

                return indexEntries?.Select(e => new TemplateEntry
                {
                    Name = e.Name,
                    Format = e.Format,
                    FilePath = e.Path,
                    UpdatedAt = e.Updated
                }).ToList() ?? new List<TemplateEntry>();
            }
            catch
            {
                return new List<TemplateEntry>();
            }
        }

        public void AddTemplate(string firmName, string templateName, string description, string format, string sourceFilePath)
        {
            if (string.IsNullOrEmpty(_folderService.RootPath)) return;

            var templatesRoot = _folderService.GetTemplatesFolder(firmName);
            Directory.CreateDirectory(templatesRoot);

            // Create Template Folder
            var safeName = FolderService.NormalizeFolderName(templateName);
            var templateFolder = Path.Combine(templatesRoot, safeName);

            // Handle duplicate names
            int counter = 1;
            while (Directory.Exists(templateFolder))
            {
                templateFolder = Path.Combine(templatesRoot, $"{safeName}_{counter}");
                counter++;
            }
            Directory.CreateDirectory(templateFolder);

            // Copy and Rename File
            var ext = Path.GetExtension(sourceFilePath).ToLower();
            var destFileName = $"template{ext}";
            var destPath = Path.Combine(templateFolder, destFileName);
            File.Copy(sourceFilePath, destPath);

            // Create metadata.json
            var now = DateTime.Now;
            var metadata = new TemplateMetadata
            {
                Name = templateName,
                Format = format,
                Description = description,
                CreatedAt = now,
                UpdatedAt = now,
                TagsUsed = new List<string>()
            };

            var metaJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(templateFolder, "metadata.json"), metaJson);

            // Update index.json
            var indexFile = Path.Combine(templatesRoot, "index.json");
            List<TemplateIndexEntry> index = new List<TemplateIndexEntry>();

            if (File.Exists(indexFile))
            {
                try
                {
                    var existingJson = File.ReadAllText(indexFile);
                    index = JsonSerializer.Deserialize<List<TemplateIndexEntry>>(existingJson) ?? new List<TemplateIndexEntry>();
                }
                catch (Exception ex) { LoggingService.LogError("TemplateService.AddTemplate", ex); }
            }

            // Relative path for storage (uses the current templates folder name)
            var templatesFolderName = Path.GetFileName(templatesRoot);
            var relativePath = Path.Combine(templatesFolderName, Path.GetFileName(templateFolder), destFileName);

            index.Add(new TemplateIndexEntry
            {
                Name = templateName,
                Format = format,
                Path = relativePath,
                Updated = now
            });

            var newIndexJson = JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(indexFile, newIndexJson);

            // Create versions folder
            Directory.CreateDirectory(Path.Combine(templateFolder, "versions"));

            // Create preview folder
            Directory.CreateDirectory(Path.Combine(templateFolder, "preview"));
        }

        /// <summary>
        /// Creates a template without a source file (for DOCX — user will use built-in editor).
        /// </summary>
        public void AddTemplateWithoutFile(string firmName, string templateName, string description, string format)
        {
            if (string.IsNullOrEmpty(_folderService.RootPath)) return;

            var templatesRoot = _folderService.GetTemplatesFolder(firmName);
            Directory.CreateDirectory(templatesRoot);

            var safeName = FolderService.NormalizeFolderName(templateName);
            var templateFolder = Path.Combine(templatesRoot, safeName);

            int counter = 1;
            while (Directory.Exists(templateFolder))
            {
                templateFolder = Path.Combine(templatesRoot, $"{safeName}_{counter}");
                counter++;
            }
            Directory.CreateDirectory(templateFolder);

            // No file to copy — create a placeholder for the path
            var destFileName = "template.docx";

            // Create metadata.json
            var now = DateTime.Now;
            var metadata = new TemplateMetadata
            {
                Name = templateName,
                Format = format,
                Description = description,
                CreatedAt = now,
                UpdatedAt = now,
                TagsUsed = new List<string>()
            };

            var metaJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(templateFolder, "metadata.json"), metaJson);

            // Update index.json
            var indexFile = Path.Combine(templatesRoot, "index.json");
            List<TemplateIndexEntry> index = new List<TemplateIndexEntry>();

            if (File.Exists(indexFile))
            {
                try
                {
                    var existingJson = File.ReadAllText(indexFile);
                    index = JsonSerializer.Deserialize<List<TemplateIndexEntry>>(existingJson) ?? new List<TemplateIndexEntry>();
                }
                catch (Exception ex) { LoggingService.LogError("TemplateService.AddTemplateFromRtf", ex); }
            }

            var templatesFolderName = Path.GetFileName(templatesRoot);
            var relativePath = Path.Combine(templatesFolderName, Path.GetFileName(templateFolder), destFileName);

            index.Add(new TemplateIndexEntry
            {
                Name = templateName,
                Format = format,
                Path = relativePath,
                Updated = now
            });

            var newIndexJson = JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(indexFile, newIndexJson);

            // Create subfolders
            Directory.CreateDirectory(Path.Combine(templateFolder, "versions"));
            Directory.CreateDirectory(Path.Combine(templateFolder, "preview"));
        }

        public string? DetectTemplateFormat(string filePath)
        {
            var ext = Path.GetExtension(filePath).ToLower();
            return ext switch
            {
                ".docx" => "DOCX",
                ".xlsx" => "XLSX",
                ".pdf" => "PDF",
                _ => null
            };
        }

        public bool TryValidateTemplateFile(string filePath, out string detectedFormat, out string error)
        {
            detectedFormat = string.Empty;
            error = string.Empty;

            if (!File.Exists(filePath))
            {
                error = "File not found.";
                return false;
            }

            var info = new FileInfo(filePath);
            if (info.Length == 0)
            {
                error = "File is empty.";
                return false;
            }

            var format = DetectTemplateFormat(filePath);
            if (format == null)
            {
                error = "Unsupported file format.";
                return false;
            }

            try
            {
                if (format == "PDF")
                {
                    using var stream = File.OpenRead(filePath);
                    var buffer = new byte[4];
                    var read = stream.Read(buffer, 0, buffer.Length);
                    var header = System.Text.Encoding.ASCII.GetString(buffer, 0, read);
                    if (!header.StartsWith("%PDF", StringComparison.Ordinal))
                    {
                        error = "Invalid PDF header.";
                        return false;
                    }
                }
                else if (format == "DOCX" || format == "XLSX")
                {
                    using var stream = File.OpenRead(filePath);
                    using var archive = new ZipArchive(stream, ZipArchiveMode.Read, false);
                    if (!archive.Entries.Any())
                    {
                        error = "Document archive is empty.";
                        return false;
                    }
                }

                detectedFormat = format;
                return true;
            }
            catch (Exception ex)
            {
                error = $"Invalid file content: {ex.Message}";
                return false;
            }
        }

        public void LogTemplateError(string message)
        {
            LoggingService.LogError("TemplateService", message);
        }

        public string GetTemplateFullPath(string firmName, string relativePath)
        {
            if (string.IsNullOrEmpty(_folderService.RootPath)) return string.Empty;

            var companyFolder = _folderService.GetCompanyFolder(firmName);
            return Path.Combine(companyFolder, relativePath);
        }

        public string GenerateDocumentFromTemplate(string firmName, TemplateEntry template)
        {
            if (string.IsNullOrEmpty(_folderService.RootPath)) return string.Empty;

            var companyFolder = _folderService.GetCompanyFolder(firmName);
            var fullPath = Path.Combine(companyFolder, template.FilePath);
            var templateDirectory = Path.GetDirectoryName(fullPath);
            if (templateDirectory == null || !Directory.Exists(templateDirectory)) return string.Empty;

            var mdPath = Path.Combine(templateDirectory, "content.md");
            if (!File.Exists(mdPath)) return string.Empty;

            var content = File.ReadAllText(mdPath);
            var tagMap = _tagCatalogService?.GetTagValueMap(firmName) ?? new Dictionary<string, string>();

            var result = System.Text.RegularExpressions.Regex.Replace(content, @"\$\{(.*?)\}", match =>
            {
                var key = match.Groups[1].Value;
                return tagMap.TryGetValue(key, out var value) ? value : match.Value;
            });

            var outputPath = Path.Combine(templateDirectory, $"generated_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            File.WriteAllText(outputPath, result);
            return outputPath;
        }

        public void RenameTemplate(string firmName, TemplateEntry template, string newName)
        {
            if (string.IsNullOrEmpty(_folderService.RootPath) || template == null || string.IsNullOrWhiteSpace(newName)) return;

            var companyFolder = _folderService.GetCompanyFolder(firmName);
            var templatesRoot = _folderService.GetTemplatesFolder(firmName);
            var indexFile = Path.Combine(templatesRoot, "index.json");

            if (File.Exists(indexFile))
            {
                var json = File.ReadAllText(indexFile);
                var index = JsonSerializer.Deserialize<List<TemplateIndexEntry>>(json) ?? new List<TemplateIndexEntry>();
                var item = index.FirstOrDefault(x => x.Path == template.FilePath);
                if (item != null)
                {
                    item.Name = newName;
                    item.Updated = DateTime.Now;
                    var newJson = JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(indexFile, newJson);
                }
            }

            var fullPath = Path.Combine(companyFolder, template.FilePath);
            var templateDirectory = Path.GetDirectoryName(fullPath);
            if (templateDirectory != null)
            {
                var metadataPath = Path.Combine(templateDirectory, "metadata.json");
                if (File.Exists(metadataPath))
                {
                    var json = File.ReadAllText(metadataPath);
                    var meta = JsonSerializer.Deserialize<TemplateMetadata>(json);
                    if (meta != null)
                    {
                        meta.Name = newName;
                        meta.UpdatedAt = DateTime.Now;
                        var newJson = JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
                        File.WriteAllText(metadataPath, newJson);
                    }
                }
            }

            template.Name = newName;
            template.UpdatedAt = DateTime.Now;
        }

        public void DeleteTemplate(string firmName, TemplateEntry template)
        {
            if (string.IsNullOrEmpty(_folderService.RootPath) || template == null) return;

            var companyFolder = _folderService.GetCompanyFolder(firmName);
            var templatesRoot = _folderService.GetTemplatesFolder(firmName);
            var indexFile = Path.Combine(templatesRoot, "index.json");

            // 1. Remove from index.json
            if (File.Exists(indexFile))
            {
                try
                {
                    var json = File.ReadAllText(indexFile);
                    var index = JsonSerializer.Deserialize<List<TemplateIndexEntry>>(json) ?? new List<TemplateIndexEntry>();

                    var itemToRemove = index.FirstOrDefault(x => x.Path == template.FilePath);
                    if (itemToRemove != null)
                    {
                        index.Remove(itemToRemove);
                        var newJson = JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true });
                        File.WriteAllText(indexFile, newJson);
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception("Failed to update template index.", ex);
                }
            }

            // 2. Delete the template folder
            var fullPath = Path.Combine(companyFolder, template.FilePath);
            var templateDirectory = Path.GetDirectoryName(fullPath);

            if (Directory.Exists(templateDirectory))
            {
                try
                {
                    Directory.Delete(templateDirectory, true);
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to delete template files at {templateDirectory}", ex);
                }
            }
        }

        public void SaveTagPositions(string firmName, TemplateEntry template, List<TagPosition> positions)
        {
            if (string.IsNullOrEmpty(_folderService.RootPath) || template == null) return;

            var companyFolder = _folderService.GetCompanyFolder(firmName);
            var fullPath = Path.Combine(companyFolder, template.FilePath);
            var templateDirectory = Path.GetDirectoryName(fullPath);
            if (templateDirectory == null || !Directory.Exists(templateDirectory)) return;

            var path = Path.Combine(templateDirectory, "tag_positions.json");
            var json = JsonSerializer.Serialize(positions ?? new List<TagPosition>(), new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }

        public List<TagPosition> LoadTagPositions(string firmName, TemplateEntry template)
        {
            if (string.IsNullOrEmpty(_folderService.RootPath) || template == null) return new List<TagPosition>();

            var companyFolder = _folderService.GetCompanyFolder(firmName);
            var fullPath = Path.Combine(companyFolder, template.FilePath);
            var templateDirectory = Path.GetDirectoryName(fullPath);
            if (templateDirectory == null || !Directory.Exists(templateDirectory)) return new List<TagPosition>();

            var path = Path.Combine(templateDirectory, "tag_positions.json");
            if (!File.Exists(path)) return new List<TagPosition>();

            try
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<List<TagPosition>>(json) ?? new List<TagPosition>();
            }
            catch
            {
                return new List<TagPosition>();
            }
        }

        public void SaveTemplateContent(string firmName, TemplateEntry template, string markdownContent, List<string> tagsUsed)
        {
            if (string.IsNullOrEmpty(_folderService.RootPath) || template == null) return;

            var companyFolder = _folderService.GetCompanyFolder(firmName);
            var fullPath = Path.Combine(companyFolder, template.FilePath);
            var templateDirectory = Path.GetDirectoryName(fullPath);

            if (templateDirectory != null && Directory.Exists(templateDirectory))
            {
                // Save Markdown
                var mdPath = Path.Combine(templateDirectory, "content.md");
                File.WriteAllText(mdPath, markdownContent);

                // Update Metadata
                var metadataPath = Path.Combine(templateDirectory, "metadata.json");
                if (File.Exists(metadataPath))
                {
                    try
                    {
                        var json = File.ReadAllText(metadataPath);
                        var metadata = JsonSerializer.Deserialize<TemplateMetadata>(json);
                        if (metadata != null)
                        {
                            metadata.UpdatedAt = DateTime.Now;
                            metadata.TagsUsed = tagsUsed ?? new List<string>();

                            var newJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
                            File.WriteAllText(metadataPath, newJson);
                        }
                    }
                    catch (Exception ex) { LoggingService.LogError("TemplateService.SaveTagPositions.Metadata", ex); }
                }

                // Update Index timestamp
                var templatesRoot = _folderService.GetTemplatesFolder(firmName);
                var indexFile = Path.Combine(templatesRoot, "index.json");
                if (File.Exists(indexFile))
                {
                    try
                    {
                        var json = File.ReadAllText(indexFile);
                        var index = JsonSerializer.Deserialize<List<TemplateIndexEntry>>(json);
                        var entry = index?.FirstOrDefault(x => x.Path == template.FilePath);
                        if (entry != null)
                        {
                            entry.Updated = DateTime.Now;
                            var newJson = JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true });
                            File.WriteAllText(indexFile, newJson);
                        }
                    }
                    catch (Exception ex) { LoggingService.LogError("TemplateService.SaveTagPositions.Index", ex); }
                }
            }
        }
    }
}
