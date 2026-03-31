using System;
using System.Collections.Generic;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.Models
{
    public sealed class GatewayResponse
    {
        public bool Ok { get; set; }
        public string ClientId { get; set; } = string.Empty;
        public bool IsBlocked { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public string Plan { get; set; } = string.Empty;
        public string GeminiApiKey { get; set; } = string.Empty;
        public RemotePolicy? Policy { get; set; }
        public List<RemoteCommand> PendingCommands { get; set; } = new();
        public string MigrationResult { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
    }
}
