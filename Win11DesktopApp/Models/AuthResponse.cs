namespace Win11DesktopApp.Models
{
    public sealed class AuthResponse
    {
        public bool Ok { get; set; }
        public bool Exists { get; set; }
        public string Error { get; set; } = string.Empty;
        public int CooldownSeconds { get; set; }
        public ClientProfileRecord? Profile { get; set; }
    }
}
