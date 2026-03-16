using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    public sealed class ProfileAuthService
    {
        private static readonly Regex _latinProfileNameRegex = new("^[A-Z][a-z]+$", RegexOptions.Compiled);
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true
        };

        public static bool IsValidProfileName(string? value)
        {
            return !string.IsNullOrWhiteSpace(value) && _latinProfileNameRegex.IsMatch(value.Trim());
        }

        public async Task<ProfileCheckResult> CheckProfileAsync(string clientId)
        {
            if (string.IsNullOrWhiteSpace(clientId))
            {
                return new ProfileCheckResult
                {
                    IsFeatureAvailable = false,
                    ErrorMessage = "Client ID is not available."
                };
            }

            try
            {
                var profile = await GetProfileAsync(clientId).ConfigureAwait(false);
                return new ProfileCheckResult
                {
                    IsFeatureAvailable = true,
                    RequiresSetup = profile == null,
                    Profile = profile
                };
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("ProfileAuthService.CheckProfile", ex.Message);
                return new ProfileCheckResult
                {
                    IsFeatureAvailable = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<(bool Success, string ErrorMessage)> CreateProfileAsync(
            string clientId,
            string firstName,
            string lastName,
            string password)
        {
            if (string.IsNullOrWhiteSpace(clientId))
                return (false, Res("ProfileErrClientIdMissing", "Client ID is not available."));
            if (string.IsNullOrWhiteSpace(firstName))
                return (false, Res("ProfileErrFirstNameRequired", "Enter first name."));
            if (string.IsNullOrWhiteSpace(lastName))
                return (false, Res("ProfileErrLastNameRequired", "Enter last name."));
            if (string.IsNullOrWhiteSpace(password))
                return (false, Res("ProfileErrPasswordRequired", "Enter password."));

            var normalizedFirstName = firstName.Trim();
            var normalizedLastName = lastName.Trim();

            if (!IsValidProfileName(normalizedFirstName))
                return (false, Res("ProfileErrFirstNameLatin", "First name must use Latin letters in Abc format."));
            if (!IsValidProfileName(normalizedLastName))
                return (false, Res("ProfileErrLastNameLatin", "Last name must use Latin letters in Abc format."));

            var salt = PasswordHashService.CreateSalt();
            var hash = PasswordHashService.HashPassword(password, salt);

            var payload = new Dictionary<string, object?>
            {
                ["client_id"] = clientId,
                ["first_name"] = normalizedFirstName,
                ["last_name"] = normalizedLastName,
                ["password_hash"] = hash,
                ["password_salt"] = salt,
                ["must_reset_password"] = false,
                ["remember_me_enabled"] = false,
                ["session_version"] = 1
            };

            var request = CreateRequest(HttpMethod.Post, $"{TelemetryService.BaseUrl}/rest/v1/client_profiles");
            request.Headers.Add("Prefer", "return=representation");
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload, _jsonOptions),
                Encoding.UTF8,
                "application/json");

            var response = await TelemetryService.HttpClient.SendAsync(request).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
                return (true, string.Empty);

            if (response.StatusCode == HttpStatusCode.Conflict || body.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
                return (false, Res("ProfileErrAlreadyExists", "A profile for this app already exists."));

            if (IsSchemaUnavailable(response.StatusCode, body))
                return (false, Res("ProfileErrStorageUnavailable", "Profile storage is not available yet in Supabase."));

            return (false, BuildErrorMessage(body, Res("ProfileErrCreateFailed", "Failed to create profile.")));
        }

        public Task<ClientProfileRecord?> GetProfileByClientIdAsync(string clientId)
        {
            return GetProfileAsync(clientId);
        }

        public async Task<ProfileOperationResult> AuthenticateAsync(string clientId, string password)
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                return new ProfileOperationResult
                {
                    ErrorMessage = Res("ProfileErrPasswordRequired", "Enter password.")
                };
            }

            try
            {
                var profile = await GetProfileAsync(clientId).ConfigureAwait(false);
                if (profile == null)
                {
                    return new ProfileOperationResult
                    {
                        ErrorMessage = Res("ProfileErrNotFound", "Profile was not found.")
                    };
                }

                var isValid = PasswordHashService.VerifyPassword(password, profile.PasswordSalt, profile.PasswordHash);
                if (!isValid)
                {
                    return new ProfileOperationResult
                    {
                        ErrorMessage = Res("ProfileErrWrongPassword", "Wrong password.")
                    };
                }

                return new ProfileOperationResult
                {
                    Success = true,
                    Profile = profile
                };
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("ProfileAuthService.Authenticate", ex.Message);
                return new ProfileOperationResult
                {
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<ProfileOperationResult> UpdateProfileNameAsync(string clientId, string firstName, string lastName)
        {
            if (string.IsNullOrWhiteSpace(firstName))
                return new ProfileOperationResult { ErrorMessage = Res("ProfileErrFirstNameRequired", "Enter first name.") };
            if (string.IsNullOrWhiteSpace(lastName))
                return new ProfileOperationResult { ErrorMessage = Res("ProfileErrLastNameRequired", "Enter last name.") };

            var normalizedFirstName = firstName.Trim();
            var normalizedLastName = lastName.Trim();

            if (!IsValidProfileName(normalizedFirstName))
                return new ProfileOperationResult { ErrorMessage = Res("ProfileErrFirstNameLatin", "First name must use Latin letters in Abc format.") };
            if (!IsValidProfileName(normalizedLastName))
                return new ProfileOperationResult { ErrorMessage = Res("ProfileErrLastNameLatin", "Last name must use Latin letters in Abc format.") };

            try
            {
                return await PatchProfileAsync(clientId, new Dictionary<string, object?>
                {
                    ["first_name"] = normalizedFirstName,
                    ["last_name"] = normalizedLastName
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("ProfileAuthService.UpdateProfileName", ex.Message);
                return new ProfileOperationResult { ErrorMessage = ex.Message };
            }
        }

        public async Task<ProfileOperationResult> UpdateRememberMeAsync(string clientId, bool enabled)
        {
            try
            {
                return await PatchProfileAsync(clientId, new Dictionary<string, object?>
                {
                    ["remember_me_enabled"] = enabled
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("ProfileAuthService.UpdateRememberMe", ex.Message);
                return new ProfileOperationResult { ErrorMessage = ex.Message };
            }
        }

        public async Task<ProfileOperationResult> ChangePasswordAsync(string clientId, string currentPassword, string newPassword)
        {
            if (string.IsNullOrWhiteSpace(currentPassword))
                return new ProfileOperationResult { ErrorMessage = Res("ProfileErrCurrentPasswordRequired", "Enter current password.") };
            if (string.IsNullOrWhiteSpace(newPassword))
                return new ProfileOperationResult { ErrorMessage = Res("ProfileErrNewPasswordRequired", "Enter new password.") };

            try
            {
                var profile = await GetProfileAsync(clientId).ConfigureAwait(false);
                if (profile == null)
                    return new ProfileOperationResult { ErrorMessage = Res("ProfileErrNotFound", "Profile was not found.") };

                var isCurrentValid = PasswordHashService.VerifyPassword(currentPassword, profile.PasswordSalt, profile.PasswordHash);
                if (!isCurrentValid)
                    return new ProfileOperationResult { ErrorMessage = Res("ProfileErrCurrentPasswordWrong", "Current password is wrong.") };

                var newSalt = PasswordHashService.CreateSalt();
                var newHash = PasswordHashService.HashPassword(newPassword, newSalt);

                return await PatchProfileAsync(clientId, new Dictionary<string, object?>
                {
                    ["password_hash"] = newHash,
                    ["password_salt"] = newSalt,
                    ["must_reset_password"] = false,
                    ["session_version"] = profile.SessionVersion + 1
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("ProfileAuthService.ChangePassword", ex.Message);
                return new ProfileOperationResult { ErrorMessage = ex.Message };
            }
        }

        public async Task<ProfileOperationResult> CompleteForcedResetAsync(string clientId, string newPassword)
        {
            if (string.IsNullOrWhiteSpace(newPassword))
                return new ProfileOperationResult { ErrorMessage = Res("ProfileErrNewPasswordRequired", "Enter new password.") };

            try
            {
                var profile = await GetProfileAsync(clientId).ConfigureAwait(false);
                if (profile == null)
                    return new ProfileOperationResult { ErrorMessage = Res("ProfileErrNotFound", "Profile was not found.") };

                var newSalt = PasswordHashService.CreateSalt();
                var newHash = PasswordHashService.HashPassword(newPassword, newSalt);

                return await PatchProfileAsync(clientId, new Dictionary<string, object?>
                {
                    ["password_hash"] = newHash,
                    ["password_salt"] = newSalt,
                    ["must_reset_password"] = false,
                    ["remember_me_enabled"] = false
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("ProfileAuthService.CompleteForcedReset", ex.Message);
                return new ProfileOperationResult { ErrorMessage = ex.Message };
            }
        }

        private static async Task<ClientProfileRecord?> GetProfileAsync(string clientId)
        {
            var request = CreateRequest(
                HttpMethod.Get,
                $"{TelemetryService.BaseUrl}/rest/v1/client_profiles?client_id=eq.{Uri.EscapeDataString(clientId)}&select=*");

            var response = await TelemetryService.HttpClient.SendAsync(request).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var profiles = JsonSerializer.Deserialize<List<ClientProfileApiResponse>>(body, _jsonOptions) ?? new List<ClientProfileApiResponse>();
                return profiles.Count == 0 ? null : profiles[0].ToRecord();
            }

            if (IsSchemaUnavailable(response.StatusCode, body))
                throw new InvalidOperationException("Profile storage is not available yet.");

            throw new InvalidOperationException(BuildErrorMessage(body, Res("ProfileErrLoadFailed", "Failed to load profile.")));
        }

        private static async Task<ProfileOperationResult> PatchProfileAsync(string clientId, Dictionary<string, object?> payload)
        {
            var request = CreateRequest(
                new HttpMethod("PATCH"),
                $"{TelemetryService.BaseUrl}/rest/v1/client_profiles?client_id=eq.{Uri.EscapeDataString(clientId)}");
            request.Headers.Add("Prefer", "return=representation");
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload, _jsonOptions),
                Encoding.UTF8,
                "application/json");

            var response = await TelemetryService.HttpClient.SendAsync(request).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return new ProfileOperationResult
                {
                    ErrorMessage = BuildErrorMessage(body, Res("ProfileErrUpdateFailed", "Failed to update profile."))
                };
            }

            var profiles = JsonSerializer.Deserialize<List<ClientProfileApiResponse>>(body, _jsonOptions) ?? new List<ClientProfileApiResponse>();
            return new ProfileOperationResult
            {
                Success = profiles.Count > 0,
                ErrorMessage = profiles.Count > 0 ? string.Empty : Res("ProfileErrEmptyServerResponse", "Empty response from server."),
                Profile = profiles.Count > 0 ? profiles[0].ToRecord() : null
            };
        }

        private static HttpRequestMessage CreateRequest(HttpMethod method, string url)
        {
            TelemetryService.ConfigureHeaders();
            return new HttpRequestMessage(method, url);
        }

        private static bool IsSchemaUnavailable(HttpStatusCode statusCode, string body)
        {
            return statusCode == HttpStatusCode.NotFound
                || body.Contains("client_profiles", StringComparison.OrdinalIgnoreCase)
                || body.Contains("42P01", StringComparison.OrdinalIgnoreCase)
                || body.Contains("relation", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildErrorMessage(string body, string fallback)
        {
            if (string.IsNullOrWhiteSpace(body))
                return fallback;

            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("message", out var message))
                {
                    var text = message.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                        return text!;
                }
            }
            catch
            {
            }

            return fallback;
        }

        private static string Res(string key, string fallback)
        {
            return Application.Current?.TryFindResource(key) as string ?? fallback;
        }
    }
}
