using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.IO.Compression;
using System.Threading;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Win11DesktopApp.Helpers;
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
                var indexEntries = SafeFileService.ReadJsonOrDefault(templatesIndexFile, new List<TemplateIndexEntry>());
                NormalizeTemplateIndexPaths(templatesFolder, indexEntries, templatesIndexFile);

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

        public TemplateEntry? AddTemplate(string firmName, string templateName, string description, string format, string sourceFilePath)
        {
            if (string.IsNullOrEmpty(_folderService.RootPath)) return null;

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
            SafeFileService.CopyFile(sourceFilePath, destPath);

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

            SafeFileService.WriteJsonAtomic(Path.Combine(templateFolder, "metadata.json"), metadata);

            // Update index.json
            var indexFile = Path.Combine(templatesRoot, "index.json");
            List<TemplateIndexEntry> index = new List<TemplateIndexEntry>();

            if (File.Exists(indexFile))
            {
                try
                {
                    index = SafeFileService.ReadJsonOrDefault(indexFile, new List<TemplateIndexEntry>());
                }
                catch (Exception ex) { LoggingService.LogError("TemplateService.AddTemplate", ex); }
            }

            // Relative path for storage (uses the current templates folder name)
            var templatesFolderName = Path.GetFileName(templatesRoot);
            var relativePath = Path.Combine(templatesFolderName, Path.GetFileName(templateFolder), destFileName);

            var entry = new TemplateIndexEntry
            {
                Name = templateName,
                Format = format,
                Path = relativePath,
                Updated = now
            };
            index.Add(entry);

            SafeFileService.WriteJsonAtomic(indexFile, index);

            return new TemplateEntry
            {
                Name = templateName,
                Format = format,
                Description = description,
                FilePath = relativePath,
                CreatedAt = now,
                UpdatedAt = now,
                TagsUsed = new List<string>()
            };
        }

        /// <summary>
        /// Creates an empty native DOCX template for the built-in editor.
        /// </summary>
        public TemplateEntry? AddTemplateWithoutFile(string firmName, string templateName, string description, string format)
        {
            if (string.IsNullOrEmpty(_folderService.RootPath)) return null;

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

            var destFileName = "template.docx";
            WriteEmptyTemplateDocx(Path.Combine(templateFolder, destFileName));

            var now = DateTime.Now;
            var metadata = new TemplateMetadata
            {
                Name = templateName,
                Format = format,
                Description = description,
                CreatedAt = now,
                UpdatedAt = now,
                TagsUsed = new List<string>(),
                LayoutSource = TemplateLayoutSource.Editor,
                EditorFormat = TemplateEditorFormat.Docx
            };

            SafeFileService.WriteJsonAtomic(Path.Combine(templateFolder, "metadata.json"), metadata);

            // Update index.json
            var indexFile = Path.Combine(templatesRoot, "index.json");
            List<TemplateIndexEntry> index = new List<TemplateIndexEntry>();

            if (File.Exists(indexFile))
            {
                try
                {
                    index = SafeFileService.ReadJsonOrDefault(indexFile, new List<TemplateIndexEntry>());
                }
                catch (Exception ex) { LoggingService.LogError("TemplateService.AddTemplateWithoutFile", ex); }
            }

            var templatesFolderName = Path.GetFileName(templatesRoot);
            var relativePath = Path.Combine(templatesFolderName, Path.GetFileName(templateFolder), destFileName);

            var entry = new TemplateIndexEntry
            {
                Name = templateName,
                Format = format,
                Path = relativePath,
                Updated = now
            };
            index.Add(entry);

            SafeFileService.WriteJsonAtomic(indexFile, index);

            return new TemplateEntry
            {
                Name = templateName,
                Format = format,
                Description = description,
                FilePath = relativePath,
                CreatedAt = now,
                UpdatedAt = now,
                TagsUsed = new List<string>()
            };
        }

        public TemplateEntry? AddTemplateFromDocxImport(string firmName, string templateName, string description, string sourceFilePath)
        {
            if (string.IsNullOrEmpty(_folderService.RootPath)) return null;
            if (string.IsNullOrWhiteSpace(sourceFilePath) || !File.Exists(sourceFilePath))
                throw new FileNotFoundException("DOCX import file was not found.", sourceFilePath);

            var templatesRoot = _folderService.GetTemplatesFolder(firmName);
            Directory.CreateDirectory(templatesRoot);

            var safeName = FolderService.NormalizeFolderName(templateName);
            var templateFolder = CreateUniqueTemplateFolder(templatesRoot, safeName);

            const string destFileName = "template.docx";
            var destDocxPath = Path.Combine(templateFolder, destFileName);
            SafeFileService.CopyFile(sourceFilePath, destDocxPath);

            var now = DateTime.Now;
            var metadata = new TemplateMetadata
            {
                Name = templateName,
                Format = "DOCX",
                Description = description,
                CreatedAt = now,
                UpdatedAt = now,
                TagsUsed = new List<string>(),
                LayoutSource = TemplateLayoutSource.Word
            };

            SafeFileService.WriteJsonAtomic(Path.Combine(templateFolder, "metadata.json"), metadata);

            var indexFile = Path.Combine(templatesRoot, "index.json");
            List<TemplateIndexEntry> index;
            if (File.Exists(indexFile))
            {
                try
                {
                    index = SafeFileService.ReadJsonOrDefault(indexFile, new List<TemplateIndexEntry>());
                }
                catch (Exception ex)
                {
                    LoggingService.LogError("TemplateService.AddTemplateFromDocxImport", ex);
                    index = new List<TemplateIndexEntry>();
                }
            }
            else
            {
                index = new List<TemplateIndexEntry>();
            }

            var relativePath = Path.Combine(Path.GetFileName(templatesRoot), Path.GetFileName(templateFolder), destFileName);
            var entry = new TemplateIndexEntry
            {
                Name = templateName,
                Format = "DOCX",
                Path = relativePath,
                Updated = now
            };
            index.Add(entry);

            SafeFileService.WriteJsonAtomic(indexFile, index);

            return new TemplateEntry
            {
                Name = templateName,
                Format = "DOCX",
                Description = description,
                FilePath = relativePath,
                CreatedAt = now,
                UpdatedAt = now,
                TagsUsed = new List<string>()
            };
        }

        private static string CreateUniqueTemplateFolder(string templatesRoot, string safeName)
        {
            var templateFolder = Path.Combine(templatesRoot, safeName);
            var counter = 1;
            while (Directory.Exists(templateFolder))
            {
                templateFolder = Path.Combine(templatesRoot, $"{safeName}_{counter}");
                counter++;
            }

            Directory.CreateDirectory(templateFolder);
            return templateFolder;
        }

        private static void WriteEmptyTemplateDocx(string path)
        {
            using var buffer = new MemoryStream();
            using (var document = WordprocessingDocument.Create(buffer, WordprocessingDocumentType.Document))
            {
                var mainPart = document.AddMainDocumentPart();
                mainPart.Document = new Document(new Body(
                    new Paragraph(),
                    new SectionProperties(
                        new PageSize
                        {
                            Width = 11906,
                            Height = 16838,
                            Orient = PageOrientationValues.Portrait
                        },
                        new PageMargin
                        {
                            Top = 1440,
                            Right = (UInt32Value)1440U,
                            Bottom = 1440,
                            Left = (UInt32Value)1440U,
                            Header = (UInt32Value)720U,
                            Footer = (UInt32Value)720U
                        })));
                mainPart.Document.Save();
            }

            SafeFileService.WriteBytesAtomic(path, buffer.ToArray());
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
            if (string.IsNullOrEmpty(_folderService.RootPath) || string.IsNullOrWhiteSpace(relativePath))
                return string.Empty;

            var companyFolder = _folderService.GetCompanyFolder(firmName);
            if (string.IsNullOrWhiteSpace(companyFolder))
                return string.Empty;

            var directPath = Path.Combine(companyFolder, relativePath);
            if (TemplatePathExists(directPath))
                return directPath;

            var templatesFolder = _folderService.GetTemplatesFolder(firmName);
            var currentFolderName = Path.GetFileName(templatesFolder);
            var normalizedRelativePath = NormalizeTemplateRelativePath(relativePath, currentFolderName);
            var normalizedPath = Path.Combine(companyFolder, normalizedRelativePath);
            if (TemplatePathExists(normalizedPath))
            {
                HealTemplateIndexPath(firmName, relativePath, normalizedRelativePath);
                return normalizedPath;
            }

            var segments = SplitRelativePath(relativePath);
            if (segments.Length >= 2)
            {
                var pathInsideTemplates = segments.Skip(1).ToArray();
                foreach (var folderName in FolderNames.AllTemplatesFolderNames)
                {
                    var candidatePath = Path.Combine(companyFolder, folderName, Path.Combine(pathInsideTemplates));
                    if (!TemplatePathExists(candidatePath))
                        continue;

                    var healedRelativePath = Path.Combine(folderName, Path.Combine(pathInsideTemplates));
                    HealTemplateIndexPath(firmName, relativePath, healedRelativePath);
                    return candidatePath;
                }
            }

            return normalizedPath;
        }

        public string GetTemplateLayoutSource(string templateFolder)
        {
            if (string.IsNullOrWhiteSpace(templateFolder))
                return TemplateLayoutSource.Editor;

            try
            {
                var metadataPath = Path.Combine(templateFolder, "metadata.json");
                if (!File.Exists(metadataPath))
                    return TemplateLayoutSource.Editor;

                var metadata = SafeFileService.ReadJson<TemplateMetadata>(metadataPath);
                if (metadata == null || string.IsNullOrWhiteSpace(metadata.LayoutSource))
                    return TemplateLayoutSource.Editor;

                return string.Equals(metadata.LayoutSource.Trim(), TemplateLayoutSource.Word, StringComparison.OrdinalIgnoreCase)
                    ? TemplateLayoutSource.Word
                    : TemplateLayoutSource.Editor;
            }
            catch (Exception ex)
            {
                LoggingService.LogError("TemplateService.GetTemplateLayoutSource", ex);
                return TemplateLayoutSource.Editor;
            }
        }

        public void SetTemplateLayoutSource(string templateFolder, string layoutSource)
        {
            if (string.IsNullOrWhiteSpace(templateFolder) || !Directory.Exists(templateFolder))
                return;

            var normalized = string.Equals(layoutSource, TemplateLayoutSource.Word, StringComparison.OrdinalIgnoreCase)
                ? TemplateLayoutSource.Word
                : TemplateLayoutSource.Editor;

            try
            {
                var metadataPath = Path.Combine(templateFolder, "metadata.json");
                TemplateMetadata metadata;
                if (File.Exists(metadataPath))
                {
                    metadata = SafeFileService.ReadJson<TemplateMetadata>(metadataPath) ?? new TemplateMetadata();
                }
                else
                {
                    metadata = new TemplateMetadata();
                }

                metadata.LayoutSource = normalized;
                metadata.UpdatedAt = DateTime.Now;
                SafeFileService.WriteJsonAtomic(metadataPath, metadata);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("TemplateService.SetTemplateLayoutSource", ex);
                throw;
            }
        }

        /// <summary>
        /// Single resolve for DOCX generation (employee profile + batch).
        /// Prefers an expanded native template.docx. Legacy RTF and AltChunk shells
        /// are still reported here; <see cref="DocumentGenerationService.GeneratePreparedDocx"/>
        /// turns them into native pages before tag replacement.
        /// </summary>
        public TemplateDocxGenerationSource ResolveDocxGenerationSource(string templateFolder, string templateDocxPath)
        {
            var rtfPath = string.IsNullOrWhiteSpace(templateFolder)
                ? string.Empty
                : Path.Combine(templateFolder, "content.rtf");
            var hasRtf = !string.IsNullOrEmpty(rtfPath) && File.Exists(rtfPath);
            var hasDocx = !string.IsNullOrWhiteSpace(templateDocxPath) && File.Exists(templateDocxPath);

            if (hasDocx && DocumentGenerationService.CanOpenAsTemplateDocx(templateDocxPath))
                return new TemplateDocxGenerationSource(TemplateDocxSourceKind.NativeDocx, templateDocxPath, null);

            if (hasRtf)
                return new TemplateDocxGenerationSource(TemplateDocxSourceKind.Rtf, rtfPath, null);

            if (hasDocx)
                return new TemplateDocxGenerationSource(TemplateDocxSourceKind.NativeDocx, templateDocxPath, null);

            return new TemplateDocxGenerationSource(TemplateDocxSourceKind.None, string.Empty, "MsgTemplateNotFound");
        }

        public string GenerateDocumentFromTemplate(string firmName, TemplateEntry template)
        {
            if (string.IsNullOrEmpty(_folderService.RootPath)) return string.Empty;

            var fullPath = GetTemplateFullPath(firmName, template.FilePath);
            var templateDirectory = Path.GetDirectoryName(fullPath);
            if (templateDirectory == null || !Directory.Exists(templateDirectory)) return string.Empty;

            var mdPath = Path.Combine(templateDirectory, "content.md");
            if (!File.Exists(mdPath)) return string.Empty;

            var content = SafeFileService.ReadAllText(mdPath);
            var tagMap = _tagCatalogService?.GetTagValueMap(firmName) ?? new Dictionary<string, string>();

            var result = System.Text.RegularExpressions.Regex.Replace(content, @"\$\{(.*?)\}", match =>
            {
                var key = match.Groups[1].Value;
                return tagMap.TryGetValue(key, out var value) ? value : match.Value;
            });

            var outputPath = Path.Combine(templateDirectory, $"generated_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            SafeFileService.WriteTextAtomic(outputPath, result);
            return outputPath;
        }

        public TemplateRenameResult RenameTemplate(string firmName, TemplateEntry template, string newName)
        {
            if (string.IsNullOrEmpty(_folderService.RootPath) || template == null || string.IsNullOrWhiteSpace(newName))
                return TemplateRenameResult.Renamed;

            newName = newName.Trim();
            var fullPath = GetTemplateFullPath(firmName, template.FilePath);
            var templateDirectory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(templateDirectory) || !Directory.Exists(templateDirectory))
                throw new DirectoryNotFoundException("Template folder was not found.");

            var templatesRoot = Directory.GetParent(templateDirectory)?.FullName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(templatesRoot))
                throw new DirectoryNotFoundException("Templates folder was not found.");

            var indexFile = Path.Combine(templatesRoot, "index.json");
            var index = File.Exists(indexFile)
                ? SafeFileService.ReadJsonOrDefault(indexFile, new List<TemplateIndexEntry>())
                : new List<TemplateIndexEntry>();

            var newFolderName = FolderService.NormalizeFolderName(newName);
            var newDirectory = Path.Combine(templatesRoot, newFolderName);
            var sameFolder = string.Equals(templateDirectory, newDirectory, StringComparison.OrdinalIgnoreCase);
            var nameTaken = index.Any(item =>
                !string.Equals(item.Path, template.FilePath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.Name?.Trim(), newName, StringComparison.OrdinalIgnoreCase));
            if (nameTaken || (!sameFolder && Directory.Exists(newDirectory)))
                return TemplateRenameResult.NameTaken;

            var moved = false;
            var previousDirectory = templateDirectory;
            try
            {
                if (!sameFolder)
                {
                    Directory.Move(templateDirectory, newDirectory);
                    moved = true;
                    templateDirectory = newDirectory;
                }

                var now = DateTime.Now;
                var metadataPath = Path.Combine(templateDirectory, "metadata.json");
                if (File.Exists(metadataPath))
                {
                    var meta = SafeFileService.ReadJson<TemplateMetadata>(metadataPath);
                    if (meta != null)
                    {
                        meta.Name = newName;
                        meta.UpdatedAt = now;
                        SafeFileService.WriteJsonAtomic(metadataPath, meta);
                    }
                }

                var relativePath = Path.Combine(Path.GetFileName(templatesRoot), newFolderName, Path.GetFileName(template.FilePath));
                var item = index.FirstOrDefault(x => string.Equals(x.Path, template.FilePath, StringComparison.OrdinalIgnoreCase));
                if (item != null)
                {
                    item.Name = newName;
                    item.Path = relativePath;
                    item.Updated = now;
                    SafeFileService.WriteJsonAtomic(indexFile, index);
                }

                template.Name = newName;
                template.FilePath = relativePath;
                template.UpdatedAt = now;
                return TemplateRenameResult.Renamed;
            }
            catch
            {
                if (moved && Directory.Exists(newDirectory) && !Directory.Exists(previousDirectory))
                {
                    try { Directory.Move(newDirectory, previousDirectory); }
                    catch (Exception rollbackEx)
                    {
                        LoggingService.LogError("TemplateService.RenameTemplate.Rollback", rollbackEx);
                    }
                }

                throw;
            }
        }

        public TemplateEntry CopyTemplateToCompany(string sourceFirmName, TemplateEntry template, string targetFirmName, string newName)
        {
            if (string.IsNullOrEmpty(_folderService.RootPath))
                throw new InvalidOperationException("Root folder is not configured.");
            if (template == null)
                throw new ArgumentNullException(nameof(template));
            if (string.IsNullOrWhiteSpace(newName))
                throw new ArgumentException("Template name is required.", nameof(newName));

            var targetTemplatesRoot = _folderService.GetTemplatesFolder(targetFirmName);
            Directory.CreateDirectory(targetTemplatesRoot);

            var sourceFullPath = GetTemplateFullPath(sourceFirmName, template.FilePath);
            var sourceTemplateDirectory = Path.GetDirectoryName(sourceFullPath);
            if (string.IsNullOrEmpty(sourceTemplateDirectory) || !Directory.Exists(sourceTemplateDirectory))
                throw new DirectoryNotFoundException("Source template folder was not found.");

            var safeName = FolderService.NormalizeFolderName(newName);
            var targetTemplateDirectory = Path.Combine(targetTemplatesRoot, safeName);
            var counter = 1;
            while (Directory.Exists(targetTemplateDirectory))
            {
                targetTemplateDirectory = Path.Combine(targetTemplatesRoot, $"{safeName}_{counter}");
                counter++;
            }

            CopyDirectory(sourceTemplateDirectory, targetTemplateDirectory);

            var now = DateTime.Now;
            var templateFileName = Path.GetFileName(template.FilePath);
            var metadataPath = Path.Combine(targetTemplateDirectory, "metadata.json");
            if (File.Exists(metadataPath))
            {
                try
                {
                    var metadata = SafeFileService.ReadJsonOrDefault(metadataPath, new TemplateMetadata());
                    metadata.Name = newName.Trim();
                    metadata.Format = string.IsNullOrWhiteSpace(metadata.Format) ? template.Format : metadata.Format;
                    metadata.CreatedAt = now;
                    metadata.UpdatedAt = now;
                    SafeFileService.WriteJsonAtomic(metadataPath, metadata);
                }
                catch (Exception ex)
                {
                    LoggingService.LogError("TemplateService.CopyTemplateToCompany.Metadata", ex);
                }
            }

            var relativePath = Path.Combine(Path.GetFileName(targetTemplatesRoot), Path.GetFileName(targetTemplateDirectory), templateFileName);
            var indexFile = Path.Combine(targetTemplatesRoot, "index.json");
            List<TemplateIndexEntry> index;
            if (File.Exists(indexFile))
            {
                try
                {
                    index = SafeFileService.ReadJsonOrDefault(indexFile, new List<TemplateIndexEntry>());
                }
                catch
                {
                    index = new List<TemplateIndexEntry>();
                }
            }
            else
            {
                index = new List<TemplateIndexEntry>();
            }

            index.Add(new TemplateIndexEntry
            {
                Name = newName.Trim(),
                Format = template.Format,
                Path = relativePath,
                Updated = now
            });

            SafeFileService.WriteJsonAtomic(indexFile, index);

            return new TemplateEntry
            {
                Name = newName.Trim(),
                Description = template.Description,
                Format = template.Format,
                FilePath = relativePath,
                CreatedAt = now,
                UpdatedAt = now,
                TagsUsed = new List<string>(template.TagsUsed ?? new List<string>())
            };
        }

        public async Task DeleteTemplateAsync(string firmName, TemplateEntry template)
        {
            if (string.IsNullOrEmpty(_folderService.RootPath) || template == null) return;

            var templatesRoot = _folderService.GetTemplatesFolder(firmName);
            var indexFile = Path.Combine(templatesRoot, "index.json");

            // 1. Delete the template folder FIRST
            var fullPath = GetTemplateFullPath(firmName, template.FilePath);
            var templateDirectory = Path.GetDirectoryName(fullPath);

            if (!string.IsNullOrEmpty(templateDirectory) && Directory.Exists(templateDirectory))
            {
                var deleted = await TryDeleteTemplateDirectoryAsync(templateDirectory);
                if (!deleted && Directory.Exists(templateDirectory))
                {
                    var message = $"Folder still exists after delete, scheduling deferred cleanup: {templateDirectory}";
                    LoggingService.LogWarning("TemplateService.DeleteTemplate", message);
                    await PendingCleanupService.EnqueueAsync(templateDirectory, "template-delete-folder");
                    _ = System.Threading.Tasks.Task.Run(async () =>
                    {
                        try
                        {
                            await System.Threading.Tasks.Task.Delay(15000);
                            if (await TryDeleteTemplateDirectoryAsync(templateDirectory))
                                await PendingCleanupService.RemoveAsync(templateDirectory);
                        }
                        catch (Exception ex)
                        {
                            LoggingService.LogWarning("TemplateService.DeleteTemplateCleanup", ex.Message);
                        }
                    });
                }
            }

            // 2. Remove from index.json AFTER folder is deleted
            if (File.Exists(indexFile))
            {
                try
                {
                    var index = SafeFileService.ReadJsonOrDefault(indexFile, new List<TemplateIndexEntry>());

                    var itemToRemove = index.FirstOrDefault(x => x.Path == template.FilePath);
                    if (itemToRemove != null)
                    {
                        index.Remove(itemToRemove);
                        SafeFileService.WriteJsonAtomic(indexFile, index);
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.LogError("TemplateService.DeleteTemplate", ex);
                    throw new Exception("Failed to update template index.", ex);
                }
            }
        }

        private static async Task<bool> TryDeleteTemplateDirectoryAsync(string templateDirectory)
        {
            if (!Directory.Exists(templateDirectory))
                return true;

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Task.Delay(300);

            NormalizeAttributesRecursive(templateDirectory);

            if (await TryBulkDeleteTemplateDirectoryAsync(templateDirectory))
                return true;

            TryDeleteTemplateFilesIndividually(templateDirectory, logWarnings: false);
            TryRemoveEmptyTemplateDirectories(templateDirectory, logWarnings: false);

            if (Directory.Exists(templateDirectory))
            {
                if (TryForceDeleteTemplateDirectory(templateDirectory))
                    return true;

                LoggingService.LogWarning("TemplateService.TryDeleteTemplateDirectory",
                    $"Template folder cleanup deferred because Windows still denies access: {templateDirectory}");
            }

            return !Directory.Exists(templateDirectory);
        }

        private static async Task<bool> TryBulkDeleteTemplateDirectoryAsync(string templateDirectory)
        {
            Exception? lastError = null;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (!Directory.Exists(templateDirectory))
                        return true;

                    NormalizeAttributesRecursive(templateDirectory);

                    Directory.Delete(templateDirectory, true);
                    return true;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    await Task.Delay(400 * (attempt + 1));
                }
            }

            if (Directory.Exists(templateDirectory) && lastError != null)
                LoggingService.LogInfo("TemplateService.TryBulkDeleteTemplateDirectory", $"Bulk delete fallback needed for '{templateDirectory}': {lastError.Message}");

            return false;
        }

        private static void TryDeleteTemplateFilesIndividually(string templateDirectory, bool logWarnings = true)
        {
            try
            {
                foreach (var file in Directory.GetFiles(templateDirectory, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        SafeFileService.DeleteFile(file);
                    }
                    catch (Exception ex)
                    {
                        if (logWarnings)
                            LoggingService.LogWarning("TemplateService.TryDeleteTemplateFilesIndividually", $"Cannot delete {file}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                if (logWarnings)
                    LoggingService.LogWarning("TemplateService.TryDeleteTemplateFilesIndividually", ex.Message);
            }
        }

        private static void TryRemoveEmptyTemplateDirectories(string templateDirectory, bool logWarnings = true)
        {
            if (!Directory.Exists(templateDirectory))
                return;

            foreach (var subDirectory in Directory.GetDirectories(templateDirectory))
                TryRemoveEmptyTemplateDirectories(subDirectory, logWarnings);

            try
            {
                if (Directory.Exists(templateDirectory)
                    && Directory.GetFiles(templateDirectory).Length == 0
                    && Directory.GetDirectories(templateDirectory).Length == 0)
                {
                    Directory.Delete(templateDirectory, false);
                }
            }
            catch (Exception ex)
            {
                if (logWarnings)
                    LoggingService.LogWarning("TemplateService.TryRemoveEmptyTemplateDirectories", $"Cannot delete {templateDirectory}: {ex.Message}");
            }
        }

        private static bool TryForceDeleteTemplateDirectory(string templateDirectory)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c rd /s /q \"{templateDirectory}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true
                };

                var process = System.Diagnostics.Process.Start(psi);
                if (process != null)
                {
                    process.WaitForExit(10000);
                    if (!Directory.Exists(templateDirectory))
                    {
                        LoggingService.LogInfo("TemplateService", $"Force-deleted via cmd: {templateDirectory}");
                        return true;
                    }

                    var error = process.StandardError.ReadToEnd();
                    if (!string.IsNullOrWhiteSpace(error))
                        LoggingService.LogWarning("TemplateService.TryForceDeleteTemplateDirectory", error.Trim());
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("TemplateService.TryForceDeleteTemplateDirectory", $"Force delete failed: {ex.Message}");
            }

            return !Directory.Exists(templateDirectory);
        }

        private static void NormalizeAttributesRecursive(string directory)
        {
            if (!Directory.Exists(directory))
                return;

            try
            {
                foreach (var file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                    }
                    catch
                    {
                        // OneDrive may temporarily deny attribute changes; deletion fallbacks handle that.
                    }
                }

                foreach (var subDirectory in Directory.GetDirectories(directory, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        File.SetAttributes(subDirectory, FileAttributes.Normal);
                    }
                    catch
                    {
                        // Keep deletion moving even when a synced folder is temporarily locked.
                    }
                }

                File.SetAttributes(directory, FileAttributes.Normal);
            }
            catch
            {
                // Best-effort cleanup only; the next delete path will decide the final result.
            }
        }

        private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
        {
            var sourceInfo = new DirectoryInfo(sourceDirectory);
            if (!sourceInfo.Exists)
                throw new DirectoryNotFoundException($"Source directory not found: {sourceDirectory}");

            Directory.CreateDirectory(destinationDirectory);

            foreach (var file in sourceInfo.GetFiles())
            {
                var targetFile = Path.Combine(destinationDirectory, file.Name);
                file.CopyTo(targetFile, false);
            }

            foreach (var directory in sourceInfo.GetDirectories())
            {
                var targetSubdirectory = Path.Combine(destinationDirectory, directory.Name);
                CopyDirectory(directory.FullName, targetSubdirectory);
            }
        }

        public void SaveTagPositions(string firmName, TemplateEntry template, List<TagPosition> positions)
        {
            if (string.IsNullOrEmpty(_folderService.RootPath) || template == null) return;

            var fullPath = GetTemplateFullPath(firmName, template.FilePath);
            var templateDirectory = Path.GetDirectoryName(fullPath);
            if (templateDirectory == null || !Directory.Exists(templateDirectory)) return;

            var path = Path.Combine(templateDirectory, "tag_positions.json");
            SafeFileService.WriteJsonAtomic(path, positions ?? new List<TagPosition>());
        }

        public List<TagPosition> LoadTagPositions(string firmName, TemplateEntry template)
        {
            if (string.IsNullOrEmpty(_folderService.RootPath) || template == null) return new List<TagPosition>();

            var fullPath = GetTemplateFullPath(firmName, template.FilePath);
            var templateDirectory = Path.GetDirectoryName(fullPath);
            if (templateDirectory == null || !Directory.Exists(templateDirectory)) return new List<TagPosition>();

            var path = Path.Combine(templateDirectory, "tag_positions.json");
            if (!File.Exists(path)) return new List<TagPosition>();

            try
            {
                return SafeFileService.ReadJsonOrDefault(path, new List<TagPosition>());
            }
            catch
            {
                return new List<TagPosition>();
            }
        }

        public void SaveTemplateContent(string firmName, TemplateEntry template, string markdownContent, List<string> tagsUsed)
        {
            if (string.IsNullOrEmpty(_folderService.RootPath) || template == null) return;

            var fullPath = GetTemplateFullPath(firmName, template.FilePath);
            var templateDirectory = Path.GetDirectoryName(fullPath);

            if (templateDirectory != null && Directory.Exists(templateDirectory))
            {
                // Save Markdown
                var mdPath = Path.Combine(templateDirectory, "content.md");
                SafeFileService.WriteTextAtomic(mdPath, markdownContent);

                // Update Metadata
                var metadataPath = Path.Combine(templateDirectory, "metadata.json");
                if (File.Exists(metadataPath))
                {
                    try
                    {
                        var metadata = SafeFileService.ReadJson<TemplateMetadata>(metadataPath);
                        if (metadata != null)
                        {
                            metadata.UpdatedAt = DateTime.Now;
                            metadata.TagsUsed = tagsUsed ?? new List<string>();

                            SafeFileService.WriteJsonAtomic(metadataPath, metadata);
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
                        var index = SafeFileService.ReadJson<List<TemplateIndexEntry>>(indexFile);
                        var entry = index?.FirstOrDefault(x => x.Path == template.FilePath);
                        if (entry != null)
                        {
                            entry.Updated = DateTime.Now;
                            SafeFileService.WriteJsonAtomic(indexFile, index);
                        }
                    }
                    catch (Exception ex) { LoggingService.LogError("TemplateService.SaveTagPositions.Index", ex); }
                }
            }
        }

        private static string[] SplitRelativePath(string relativePath)
        {
            return (relativePath ?? string.Empty)
                .Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static string NormalizeTemplateRelativePath(string relativePath, string? currentTemplatesFolderName)
        {
            var segments = SplitRelativePath(relativePath);
            if (segments.Length == 0)
                return relativePath ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(currentTemplatesFolderName)
                && FolderNames.AllTemplatesFolderNames.Contains(segments[0], StringComparer.OrdinalIgnoreCase))
            {
                segments[0] = currentTemplatesFolderName;
            }

            return Path.Combine(segments);
        }

        private static bool TemplatePathExists(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
                return false;

            if (File.Exists(fullPath))
                return true;

            var templateDirectory = Path.GetDirectoryName(fullPath);
            return !string.IsNullOrWhiteSpace(templateDirectory) && Directory.Exists(templateDirectory);
        }

        private void NormalizeTemplateIndexPaths(string templatesFolder, List<TemplateIndexEntry>? indexEntries, string indexFile)
        {
            if (indexEntries == null || indexEntries.Count == 0)
                return;

            var currentFolderName = Path.GetFileName(templatesFolder);
            var changed = false;

            foreach (var entry in indexEntries)
            {
                var normalizedPath = NormalizeTemplateRelativePath(entry.Path, currentFolderName);
                if (string.Equals(entry.Path, normalizedPath, StringComparison.Ordinal))
                    continue;

                entry.Path = normalizedPath;
                changed = true;
            }

            if (changed)
                SafeFileService.WriteJsonAtomic(indexFile, indexEntries);
        }

        private void HealTemplateIndexPath(string firmName, string oldRelativePath, string newRelativePath)
        {
            if (string.IsNullOrWhiteSpace(oldRelativePath)
                || string.IsNullOrWhiteSpace(newRelativePath)
                || string.Equals(oldRelativePath, newRelativePath, StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                var templatesRoot = _folderService.GetTemplatesFolder(firmName);
                var indexFile = Path.Combine(templatesRoot, "index.json");
                if (!File.Exists(indexFile))
                    return;

                var index = SafeFileService.ReadJsonOrDefault(indexFile, new List<TemplateIndexEntry>());
                var changed = false;
                foreach (var entry in index.Where(e => string.Equals(e.Path, oldRelativePath, StringComparison.Ordinal)))
                {
                    entry.Path = newRelativePath;
                    changed = true;
                }

                if (changed)
                    SafeFileService.WriteJsonAtomic(indexFile, index);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("TemplateService.HealTemplateIndexPath", ex.Message);
            }
        }
    }
}
