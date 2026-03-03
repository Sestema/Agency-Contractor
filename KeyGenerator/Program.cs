using System.Security.Cryptography;
using System.Text;

var secret = "AC-2026-Kachalin-OA-LicKey";
Console.WriteLine("Searching for valid activator key...");

var rng = RandomNumberGenerator.Create();
var buf = new byte[24];

for (long i = 0; i < 50_000_000; i++)
{
    rng.GetBytes(buf);
    var candidate = "ACK-" + Convert.ToHexString(buf);
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(candidate + secret));
    var hash = Convert.ToHexString(bytes)[..6];
    if (hash == "AC2026")
    {
        var outputDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..");
        var outputPath = Path.Combine(outputDir, "activator.key");
        File.WriteAllText(outputPath, candidate);
        Console.WriteLine($"Found after {i + 1} attempts");
        Console.WriteLine($"Key: {candidate}");
        Console.WriteLine($"Saved to: {Path.GetFullPath(outputPath)}");
        return;
    }
    if (i % 1_000_000 == 0 && i > 0)
        Console.WriteLine($"  ...{i:N0} attempts...");
}
Console.WriteLine("Could not find key. Trying fallback...");

// Fallback: just create a key with embedded marker
var fallback = "AC2026-MASTER-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
var fbDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..");
File.WriteAllText(Path.Combine(fbDir, "activator.key"), fallback);
Console.WriteLine($"Fallback key: {fallback}");
