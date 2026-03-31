using System;
using System.Collections.Generic;
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
                    ErrorMessage = Res("ProfileErrClientIdMissing", "Client ID is not available.")
                };
            }

            try
            {
                var response = await CallAuthAsync("check", new Dictionary<string, object?>
                {
                    ["client_id"] = clientId
                }).ConfigureAwait(false);

                if (response == null)
                {
                    return new ProfileCheckResult
                    {
                        IsFeatureAvailable = false,
                        ErrorMessage = Res("ProfileErrStorageUnavailable", "Profile storage is not available yet in Supabase.")
                    };
                }

                return new ProfileCheckResult
                {
                    IsFeatureAvailable = true,
                    RequiresSetup = !response.Exists,
                    Profile = response.Profile
                };
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("ProfileAuthService.CheckProfile", ex.Message);
                return new ProfileCheckResult
                {
                    IsFeatureAvailable = false,
                    ErrorMessage = NormalizeProfileException(ex, Res("ProfileErrLoadFailed", "Failed to load profile."))
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

            var response = await CallAuthAsync("create", new Dictionary<string, object?>
            {
                ["client_id"] = clientId,
                ["first_name"] = normalizedFirstName,
                ["last_name"] = normalizedLastName,
                ["password"] = password
            }).ConfigureAwait(false);

            if (response?.Ok == true)
                return (true, string.Empty);

            return (false, BuildErrorMessage(response?.Error, Res("ProfileErrCreateFailed", "Failed to create profile.")));
        }

        public async Task<ClientProfileRecord?> GetProfileByClientIdAsync(string clientId)
        {
            var response = await CallAuthAsync("check", new Dictionary<string, object?>
            {
                ["client_id"] = clientId
            }).ConfigureAwait(false);
            return response?.Profile;
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
                var response = await CallAuthAsync("login", new Dictionary<string, object?>
                {
                    ["client_id"] = clientId,
                    ["password"] = password
                }).ConfigureAwait(false);

                if (response?.Ok != true || response.Profile == null)
                {
                    return new ProfileOperationResult
                    {
                        ErrorMessage = BuildErrorMessage(response?.Error, Res("ProfileErrWrongPassword", "Wrong password."))
                    };
                }

                return new ProfileOperationResult
                {
                    Success = true,
                    Profile = response.Profile
                };
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("ProfileAuthService.Authenticate", ex.Message);
                return new ProfileOperationResult
                {
                    ErrorMessage = NormalizeProfileException(ex, Res("ProfileErrServiceUnavailable", "Could not contact the profile service. Try again."))
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
                return await MapProfileResponseAsync(CallAuthAsync("update_name", new Dictionary<string, object?>
                {
                    ["client_id"] = clientId,
                    ["first_name"] = normalizedFirstName,
                    ["last_name"] = normalizedLastName
                }), Res("ProfileErrUpdateFailed", "Failed to update profile.")).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("ProfileAuthService.UpdateProfileName", ex.Message);
                return new ProfileOperationResult
                {
                    ErrorMessage = NormalizeProfileException(ex, Res("ProfileErrUpdateFailed", "Failed to update profile."))
                };
            }
        }

        public async Task<ProfileOperationResult> UpdateRememberMeAsync(string clientId, bool enabled)
        {
            try
            {
                return await MapProfileResponseAsync(CallAuthAsync("update_remember", new Dictionary<string, object?>
                {
                    ["client_id"] = clientId,
                    ["remember_me_enabled"] = enabled
                }), Res("ProfileErrUpdateFailed", "Failed to update profile.")).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("ProfileAuthService.UpdateRememberMe", ex.Message);
                return new ProfileOperationResult
                {
                    ErrorMessage = NormalizeProfileException(ex, Res("ProfileErrUpdateFailed", "Failed to update profile."))
                };
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
                return await MapProfileResponseAsync(CallAuthAsync("change_password", new Dictionary<string, object?>
                {
                    ["client_id"] = clientId,
                    ["current_password"] = currentPassword,
                    ["new_password"] = newPassword
                }), Res("ProfileErrUpdateFailed", "Failed to update profile.")).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("ProfileAuthService.ChangePassword", ex.Message);
                return new ProfileOperationResult
                {
                    ErrorMessage = NormalizeProfileException(ex, Res("ProfileErrUpdateFailed", "Failed to update profile."))
                };
            }
        }

        public async Task<ProfileOperationResult> CompleteForcedResetAsync(string clientId, string newPassword)
        {
            if (string.IsNullOrWhiteSpace(newPassword))
                return new ProfileOperationResult { ErrorMessage = Res("ProfileErrNewPasswordRequired", "Enter new password.") };

            try
            {
                return await MapProfileResponseAsync(CallAuthAsync("forced_reset", new Dictionary<string, object?>
                {
                    ["client_id"] = clientId,
                    ["new_password"] = newPassword
                }), Res("ProfileErrUpdateFailed", "Failed to update profile.")).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("ProfileAuthService.CompleteForcedReset", ex.Message);
                return new ProfileOperationResult
                {
                    ErrorMessage = NormalizeProfileException(ex, Res("ProfileErrUpdateFailed", "Failed to update profile."))
                };
            }
        }

        private static async Task<ProfileOperationResult> MapProfileResponseAsync(Task<AuthResponse?> responseTask, string fallbackError)
        {
            var response = await responseTask.ConfigureAwait(false);
            return new ProfileOperationResult
            {
                Success = response?.Ok == true && response.Profile != null,
                ErrorMessage = response?.Ok == true
                    ? string.Empty
                    : BuildErrorMessage(response?.Error, fallbackError),
                Profile = response?.Profile
            };
        }

        private static async Task<AuthResponse?> CallAuthAsync(string action, Dictionary<string, object?> payload)
        {
            payload["action"] = action;
            payload["machine_id"] = LicenseService.GetMachineId();
            var request = new HttpRequestMessage(HttpMethod.Post, $"{TelemetryService.BaseUrl}/functions/v1/client-auth")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload, _jsonOptions), Encoding.UTF8, "application/json")
            };
            TelemetryService.ApplyHeaders(request);

            var response = await TelemetryService.HttpClient.SendAsync(request).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LoggingService.LogWarning("ProfileAuthService.Http",
                    $"{action}: {(int)response.StatusCode} {body}");
                return new AuthResponse
                {
                    Ok = false,
                    Error = BuildErrorMessage(body, Res("ProfileErrStorageUnavailable", "Profile storage is not available yet in Supabase."))
                };
            }

            return JsonSerializer.Deserialize<AuthResponse>(body, _jsonOptions);
        }

        private static string BuildErrorMessage(string? body, string fallback)
        {
            if (string.IsNullOrWhiteSpace(body))
                return fallback;

            var trimmed = body.Trim();
            if (!trimmed.StartsWith("{", StringComparison.Ordinal))
                return MapProfileErrorCode(trimmed, null, fallback);

            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                var root = doc.RootElement;
                if (root.TryGetProperty("error", out var error))
                {
                    var text = error.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                        return MapProfileErrorCode(text!, root, fallback);
                }

                if (root.TryGetProperty("message", out var message))
                {
                    var text = message.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                        return MapProfileErrorCode(text!, root, fallback);
                }
            }
            catch
            {
            }

            return fallback;
        }

        private static string MapProfileErrorCode(string code, JsonElement? root, string fallback)
        {
            var normalized = (code ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
                return fallback;

            return normalized switch
            {
                "machine_id_required" => Res("ProfileErrClientIdMissing", "Client ID is not available."),
                "client_not_found" => Res("ProfileErrNotFound", "Profile was not found."),
                "client_blocked" => Res("MsgLicenseBlocked", "License is blocked. Contact the administrator."),
                "profile_exists" => Res("ProfileErrAlreadyExists", "A profile for this app already exists."),
                "password_required" => Res("ProfileErrPasswordRequired", "Enter password."),
                "profile_not_found" => Res("ProfileErrNotFound", "Profile was not found."),
                "wrong_password" => Res("ProfileErrWrongPassword", "Wrong password."),
                "current_password_wrong" => Res("ProfileErrCurrentPasswordWrong", "Current password is wrong."),
                "cooldown_active" => string.Format(
                    Res("ProfileErrCooldownActive", "Too many attempts. Try again in {0} sec."),
                    TryGetCooldownSeconds(root)),
                "method_not_allowed" => Res("ProfileErrServiceUnavailable", "Could not contact the profile service. Try again."),
                "unknown_action" => fallback,
                "client_auth_failed" => fallback,
                _ => fallback
            };
        }

        private static int TryGetCooldownSeconds(JsonElement? root)
        {
            if (root.HasValue
                && root.Value.TryGetProperty("cooldown_seconds", out var cooldown)
                && cooldown.TryGetInt32(out var seconds)
                && seconds > 0)
            {
                return seconds;
            }

            return 30;
        }

        private static string NormalizeProfileException(Exception ex, string fallback)
        {
            return ex switch
            {
                TaskCanceledException => Res("ProfileErrRequestTimedOut", "The profile service did not respond in time. Try again."),
                HttpRequestException => Res("ProfileErrServiceUnavailable", "Could not contact the profile service. Try again."),
                JsonException => Res("ProfileErrInvalidResponse", "Received an invalid response from the profile service."),
                _ => fallback
            };
        }

        private static string Res(string key, string fallback)
        {
            return Application.Current?.TryFindResource(key) as string ?? fallback;
        }
    }
}
