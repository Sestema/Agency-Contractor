using System.Security.Cryptography;
using System.Text;

Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine("═══════════════════════════════════════════");
Console.WriteLine("  Agency Contractor — Key Generator");
Console.WriteLine("═══════════════════════════════════════════");
Console.WriteLine();

var outputDir = Path.Combine(AppContext.BaseDirectory, "keys");
Directory.CreateDirectory(outputDir);

while (true)
{
    Console.Write("Кількість ключів для генерації (1-100, або 'q' для виходу): ");
    var input = Console.ReadLine()?.Trim();
    if (input == "q" || input == "Q") break;
    if (!int.TryParse(input, out var count) || count < 1 || count > 100)
    {
        Console.WriteLine("Невірне значення. Спробуйте ще.");
        continue;
    }

    Console.Write("Термін дії в днях (0 = безліміт): ");
    var daysInput = Console.ReadLine()?.Trim();
    if (!int.TryParse(daysInput, out var days)) days = 0;

    Console.Write("Нотатка (опціонально): ");
    var note = Console.ReadLine()?.Trim() ?? "";

    Console.WriteLine();

    var batch = DateTime.Now.ToString("yyyyMMdd-HHmmss");
    var batchDir = Path.Combine(outputDir, $"batch_{batch}");
    Directory.CreateDirectory(batchDir);

    var logLines = new List<string>
    {
        $"Batch: {batch}",
        $"Count: {count}",
        $"Days: {(days == 0 ? "unlimited" : days.ToString())}",
        $"Note: {note}",
        $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
        "─────────────────────────────────────",
    };

    for (int i = 0; i < count; i++)
    {
        var key = GenerateKey();
        var fileName = $"key_{i + 1:D3}.key";
        var filePath = Path.Combine(batchDir, fileName);

        File.WriteAllText(filePath, key);

        Console.WriteLine($"  [{i + 1}/{count}] {fileName} → {key[..30]}...");
        logLines.Add($"{fileName}: {key}");
    }

    logLines.Add("");
    logLines.Add($"Plan: {(days == 0 ? "unlimited" : $"{days} days")}");
    logLines.Add("Usage: Open Agency Contractor → License Window → Select .key file");

    File.WriteAllLines(Path.Combine(batchDir, "_batch_info.txt"), logLines);

    Console.WriteLine();
    Console.WriteLine($"✅ Згенеровано {count} ключів у: {batchDir}");
    Console.WriteLine($"   Інфо: _batch_info.txt");
    Console.WriteLine();
}

Console.WriteLine("Вихід.");

static string GenerateKey()
{
    var guid1 = Guid.NewGuid().ToString("N");
    var guid2 = Guid.NewGuid().ToString("N");
    var raw = $"{guid1}{guid2}";

    using var sha = SHA256.Create();
    var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(raw + DateTime.UtcNow.Ticks));
    var hashStr = Convert.ToHexString(hash)[..24];

    return $"ACK-{hashStr[..8]}-{hashStr[8..16]}-{hashStr[16..24]}-{guid1[..8]}";
}
