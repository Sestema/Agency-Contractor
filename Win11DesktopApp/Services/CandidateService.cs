using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Windows.Media.Imaging;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    public class CandidateService
    {
        private readonly FolderService _folderService;
        private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

        public CandidateService(FolderService folderService)
        {
            _folderService = folderService;
        }

        private string CandidatesRoot => _folderService.GetCandidatesFolder();

        public List<CandidateSummary> GetAll()
        {
            var root = CandidatesRoot;
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                return new();

            var result = new List<CandidateSummary>();
            foreach (var folder in Directory.GetDirectories(root))
            {
                var data = LoadData(folder);
                if (data == null) continue;

                var photo = ResolvePhoto(folder, data);
                result.Add(new CandidateSummary
                {
                    FullName = $"{data.FirstName} {data.LastName}",
                    Phone = data.Phone,
                    DesiredPosition = data.DesiredPosition,
                    LocationPreference = data.LocationPreference,
                    LocationDetails = data.LocationDetails,
                    DateAdded = data.DateAdded,
                    PassportNumber = data.PassportNumber,
                    PassportCountry = data.PassportCountry,
                    PhotoPath = photo,
                    HasPhoto = !string.IsNullOrEmpty(photo),
                    CandidateFolder = folder
                });
            }
            return result;
        }

        public List<string> GetAllPositions()
        {
            return GetAll()
                .Select(c => c.DesiredPosition)
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct()
                .OrderBy(p => p)
                .ToList();
        }

        public CandidateData? LoadData(string candidateFolder)
        {
            try
            {
                var path = Path.Combine(candidateFolder, "candidate.json");
                if (!File.Exists(path)) return null;
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<CandidateData>(json);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("CandidateService.LoadData", ex);
                return null;
            }
        }

        public bool SaveData(string candidateFolder, CandidateData data)
        {
            try
            {
                Directory.CreateDirectory(candidateFolder);
                var path = Path.Combine(candidateFolder, "candidate.json");
                var json = JsonSerializer.Serialize(data, _json);
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, path, true);
                return true;
            }
            catch (Exception ex)
            {
                LoggingService.LogError("CandidateService.SaveData", ex);
                return false;
            }
        }

        public string SaveNewCandidate(CandidateData data, string? photoPath, string? passportPath)
        {
            try
            {
                var root = CandidatesRoot;
                if (string.IsNullOrEmpty(root)) return string.Empty;
                Directory.CreateDirectory(root);

                var folderName = $"{data.LastName}_{data.FirstName}_{DateTime.Now:yyyyMMdd_HHmmss}";
                var safe = string.Concat(folderName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
                var folder = Path.Combine(root, safe);
                Directory.CreateDirectory(folder);

                if (!string.IsNullOrEmpty(photoPath) && File.Exists(photoPath))
                {
                    var ext = Path.GetExtension(photoPath);
                    var dest = Path.Combine(folder, $"photo{ext}");
                    File.Copy(photoPath, dest, true);
                    data.Files.Photo = $"photo{ext}";
                }

                if (!string.IsNullOrEmpty(passportPath) && File.Exists(passportPath))
                {
                    var ext = Path.GetExtension(passportPath);
                    var dest = Path.Combine(folder, $"passport{ext}");
                    File.Copy(passportPath, dest, true);
                    data.Files.Passport = $"passport{ext}";
                }

                SaveData(folder, data);
                return folder;
            }
            catch (Exception ex)
            {
                LoggingService.LogError("CandidateService.SaveNewCandidate", ex);
                return string.Empty;
            }
        }

        public bool DeleteCandidate(string candidateFolder)
        {
            if (string.IsNullOrEmpty(candidateFolder) || !Directory.Exists(candidateFolder))
                return true;

            try
            {
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        foreach (var file in Directory.GetFiles(candidateFolder, "*", SearchOption.AllDirectories))
                            File.SetAttributes(file, FileAttributes.Normal);
                        Directory.Delete(candidateFolder, true);
                        return true;
                    }
                    catch
                    {
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        Thread.Sleep(200);
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogError("CandidateService.DeleteCandidate", ex);
            }
            return !Directory.Exists(candidateFolder);
        }

        private static string ResolvePhoto(string folder, CandidateData data)
        {
            if (!string.IsNullOrEmpty(data.Files.Photo))
            {
                var p = Path.Combine(folder, data.Files.Photo);
                if (File.Exists(p)) return p;
            }

            foreach (var ext in new[] { ".jpg", ".jpeg", ".png" })
            {
                var p = Path.Combine(folder, $"photo{ext}");
                if (File.Exists(p)) return p;
            }
            return string.Empty;
        }
    }
}
