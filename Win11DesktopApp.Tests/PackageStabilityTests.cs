using System;
using System.IO;
using System.Linq;
using System.Management;
using iTextSharp.text;
using iTextSharp.text.pdf;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using QRCoder;
using Telegram.Bot;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.Tests
{
    public class PackageStabilityTests : IDisposable
    {
        private readonly string _root;

        public PackageStabilityTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "PackageStability_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try
            {
                var databasePath = Path.Combine(_root, "stability.db");
                if (File.Exists(databasePath))
                {
                    using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
                    SqliteConnection.ClearPool(connection);
                }

                if (Directory.Exists(_root))
                    Directory.Delete(_root, true);
            }
            catch (IOException)
            {
            }
        }

        [Fact]
        public void Sqlite_Reopen_KeepsCzechText()
        {
            var path = Path.Combine(_root, "stability.db");
            using (var connection = new SqliteConnection($"Data Source={path};Cache=Shared;Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE note (id INTEGER PRIMARY KEY, body TEXT); INSERT INTO note (body) VALUES ($body);";
                command.Parameters.AddWithValue("$body", "Plzeň — Čierna");
                command.ExecuteNonQuery();
            }

            using var reopened = new SqliteConnection($"Data Source={path};Cache=Shared;Pooling=False");
            reopened.Open();
            using var read = reopened.CreateCommand();
            read.CommandText = "SELECT body FROM note";
            Assert.Equal("Plzeň — Čierna", read.ExecuteScalar());
        }

        [Fact]
        public void QrCoder_SamePayload_ProducesStablePng()
        {
            const string payload = "SPD*1.0*ACC:CZ6508000000192000145399*AM:1250.00*CC:CZK";
            var first = CreateQrPng(payload);
            var second = CreateQrPng(payload);

            Assert.True(first.Length > 100);
            Assert.Equal(new byte[] { 137, 80, 78, 71 }, first.Take(4).ToArray());
            Assert.Equal(first, second);
        }

        [Fact]
        public void ProtectedData_RoundTripsSecret()
        {
            const string secret = "bot-token:абв-123";
            var protectedText = LocalSecretProtection.Protect(secret);

            Assert.NotEqual(secret, protectedText);
            Assert.Equal(secret, LocalSecretProtection.Unprotect(protectedText));
        }

        [Fact]
        public void DependencyInjection_ResolvesSameSingleton()
        {
            var services = new ServiceCollection();
            services.AddSingleton<StabilityMarker>();
            using var provider = services.BuildServiceProvider();

            Assert.Same(
                provider.GetRequiredService<StabilityMarker>(),
                provider.GetRequiredService<StabilityMarker>());
        }

        [Fact]
        public void SystemManagement_ProcessorQuery_Completes()
        {
            using var searcher = new ManagementObjectSearcher("SELECT ProcessorId FROM Win32_Processor");
            using var results = searcher.Get();
            var sawRow = false;
            foreach (ManagementObject row in results)
            {
                using (row)
                {
                    sawRow = true;
                    _ = row["ProcessorId"]?.ToString();
                }
            }

            Assert.True(sawRow);
        }

        [Fact]
        public void TelegramClient_ConstructsWithoutNetworkCall()
        {
            var client = new TelegramBotClient("123456789:AAH-stability-check-token-value");
            Assert.NotNull(client);
        }

        [Fact]
        public void IText_WritesAndReopensOnePagePdf()
        {
            var path = Path.Combine(_root, "page.pdf");
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                var document = new Document();
                PdfWriter.GetInstance(document, stream);
                document.Open();
                document.Add(new Paragraph("Plzeň"));
                document.Close();
            }

            var reader = new PdfReader(path);
            try
            {
                Assert.Equal(1, reader.NumberOfPages);
            }
            finally
            {
                reader.Close();
            }
        }

        [Fact]
        public void WebView2_CoreTypesLoad()
        {
            var environmentType = Type.GetType(
                "Microsoft.Web.WebView2.Core.CoreWebView2Environment, Microsoft.Web.WebView2.Core",
                throwOnError: false);
            Assert.NotNull(environmentType);
        }

        private static byte[] CreateQrPng(string payload)
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
            return new PngByteQRCode(data).GetGraphic(10);
        }

        private sealed class StabilityMarker
        {
        }
    }
}
