using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Win11DesktopApp.Services
{
    public sealed class HostWhitelistMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly AppSettingsService _settingsService;

        public HostWhitelistMiddleware(RequestDelegate next, AppSettingsService settingsService)
        {
            _next = next;
            _settingsService = settingsService;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var lanAccessEnabled = IsLanAccessEnabled();
            var allowedNetwork = IsLocalRequest(context) || lanAccessEnabled && IsPrivateNetworkRequest(context);
            if (!allowedNetwork || !IsAllowedHost(context.Request.Host.Host, lanAccessEnabled))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { error = "Forbidden" }).ConfigureAwait(false);
                return;
            }

            await _next(context).ConfigureAwait(false);
        }

        private bool IsAllowedHost(string? host, bool lanAccessEnabled)
        {
            if (string.IsNullOrWhiteSpace(host))
                return false;

            var configuredHost = NormalizeHost(_settingsService.Settings.WebPanelBindAddress);
            var requestedHost = NormalizeHost(host);
            if (lanAccessEnabled)
                return true;

            return requestedHost == "localhost"
                || requestedHost == "127.0.0.1"
                || requestedHost == "::1"
                || requestedHost == configuredHost;
        }

        private bool IsLanAccessEnabled()
        {
            var bindAddress = NormalizeHost(_settingsService.Settings.WebPanelBindAddress);
            return bindAddress == "0.0.0.0" || bindAddress == "*" || bindAddress == "+";
        }

        private static bool IsLocalRequest(HttpContext context)
        {
            var remoteIp = context.Connection.RemoteIpAddress;
            if (remoteIp == null)
                return false;

            return IPAddress.IsLoopback(remoteIp)
                || context.Connection.LocalIpAddress != null && remoteIp.Equals(context.Connection.LocalIpAddress);
        }

        private static bool IsPrivateNetworkRequest(HttpContext context)
        {
            var remoteIp = context.Connection.RemoteIpAddress;
            if (remoteIp == null)
                return false;

            if (remoteIp.IsIPv4MappedToIPv6)
                remoteIp = remoteIp.MapToIPv4();

            if (IPAddress.IsLoopback(remoteIp))
                return true;

            var bytes = remoteIp.GetAddressBytes();
            if (bytes.Length != 4)
                return false;

            return bytes[0] == 10
                || bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31
                || bytes[0] == 192 && bytes[1] == 168
                || bytes[0] == 169 && bytes[1] == 254;
        }

        private static string NormalizeHost(string host)
        {
            return host.Trim().Trim('[', ']').ToLowerInvariant();
        }
    }
}
