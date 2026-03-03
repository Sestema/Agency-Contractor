using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Win11DesktopApp.Services
{
    public class ChatSession
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime LastMessageAt { get; set; } = DateTime.Now;
        public List<ChatMessageDto> Messages { get; set; } = new();
    }

    public class ChatMessageDto
    {
        public string Text { get; set; } = "";
        public bool IsUser { get; set; }
        public bool IsSystem { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    public class ChatPersistenceService
    {
        private readonly string _chatsFolder;
        private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

        public ChatPersistenceService()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _chatsFolder = Path.Combine(appData, "AgencyContractor", "chats");
            Directory.CreateDirectory(_chatsFolder);
        }

        public List<ChatSession> LoadAllSessions()
        {
            var sessions = new List<ChatSession>();
            if (!Directory.Exists(_chatsFolder)) return sessions;

            foreach (var file in Directory.GetFiles(_chatsFolder, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file, Encoding.UTF8);
                    var session = JsonSerializer.Deserialize<ChatSession>(json);
                    if (session != null)
                        sessions.Add(session);
                }
                catch (Exception ex)
                {
                    LoggingService.LogError("ChatPersistence.Load", ex);
                }
            }

            sessions.Sort((a, b) => b.LastMessageAt.CompareTo(a.LastMessageAt));
            return sessions;
        }

        public void SaveSession(ChatSession session)
        {
            var path = Path.Combine(_chatsFolder, session.Id + ".json");
            var tempPath = path + ".tmp";
            try
            {
                var json = JsonSerializer.Serialize(session, _json);
                File.WriteAllText(tempPath, json, Encoding.UTF8);
                File.Move(tempPath, path, true);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("ChatPersistence.Save", ex);
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
        }

        public void DeleteSession(string sessionId)
        {
            try
            {
                var path = Path.Combine(_chatsFolder, sessionId + ".json");
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("ChatPersistence.Delete", ex);
            }
        }
    }
}
