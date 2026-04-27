using System;
using System.Collections.Generic;

namespace Win11DesktopApp.Telegram
{
    public sealed class TelegramAuthorizedUser
    {
        public long TelegramUserId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Role { get; set; } = "Admin";
        public string LinkedAtUtc { get; set; } = string.Empty;
        public string LastActiveAtUtc { get; set; } = string.Empty;
    }

    public sealed class TelegramBotSettings
    {
        public bool Enabled { get; set; }
        public string EncryptedBotToken { get; set; } = string.Empty;
        public string BotUsername { get; set; } = string.Empty;
        public bool AllowAiQuestions { get; set; } = true;
        public bool DailyDigestEnabled { get; set; } = true;
        public string DailyDigestTime { get; set; } = "08:00";
        public List<TelegramAuthorizedUser> AuthorizedUsers { get; set; } = new();
    }
}
