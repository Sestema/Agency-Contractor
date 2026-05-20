using System;
using System.Collections.Generic;

namespace Win11DesktopApp.Models
{
    public sealed class ClientProfileRecord
    {
        public string Id { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string PasswordSalt { get; set; } = string.Empty;
        public bool MustResetPassword { get; set; }
        public bool RememberMeEnabled { get; set; }
        public int SessionVersion { get; set; } = 1;
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string TenantId { get; set; } = string.Empty;
        public string RoleKey { get; set; } = string.Empty;
        public bool? IsActive { get; set; }
        public List<string> Permissions { get; set; } = new();
    }

    public sealed class ProfileCheckResult
    {
        public bool IsFeatureAvailable { get; set; }
        public bool RequiresSetup { get; set; }
        public string? ErrorMessage { get; set; }
        public ClientProfileRecord? Profile { get; set; }
    }

    public sealed class ProfileOperationResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public ClientProfileRecord? Profile { get; set; }
    }

    internal sealed class RememberedProfileSession
    {
        public string ClientId { get; set; } = string.Empty;
        public int SessionVersion { get; set; }
        public DateTime RememberedAtUtc { get; set; }
    }

    internal sealed class ClientProfileApiResponse
    {
        public string Id { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string PasswordSalt { get; set; } = string.Empty;
        public bool MustResetPassword { get; set; }
        public bool RememberMeEnabled { get; set; }
        public int SessionVersion { get; set; } = 1;
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string TenantId { get; set; } = string.Empty;
        public string RoleKey { get; set; } = string.Empty;
        public bool? IsActive { get; set; }
        public List<string> Permissions { get; set; } = new();

        public ClientProfileRecord ToRecord()
        {
            return new ClientProfileRecord
            {
                Id = Id,
                ClientId = ClientId,
                FirstName = FirstName,
                LastName = LastName,
                PasswordHash = PasswordHash,
                PasswordSalt = PasswordSalt,
                MustResetPassword = MustResetPassword,
                RememberMeEnabled = RememberMeEnabled,
                SessionVersion = SessionVersion,
                CreatedAt = CreatedAt,
                UpdatedAt = UpdatedAt,
                TenantId = TenantId,
                RoleKey = RoleKey,
                IsActive = IsActive,
                Permissions = Permissions ?? new List<string>()
            };
        }
    }
}
