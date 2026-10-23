using PeydaBrowser.Core.Security;
using PeydaBrowser.Core.Services;
using PeydaBrowser.Infrastructure.Logging;

namespace PeydaBrowser.UnitTests;

public class DownloadMathTests
{
    [Fact]
    public void SpeedCalculator_AveragesOverWindow()
    {
        var calc = new DownloadSpeedCalculator(TimeSpan.FromSeconds(10));
        var t0 = new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(0, calc.AddSample(t0, 0));
        var speed = calc.AddSample(t0.AddSeconds(2), 2048);
        Assert.Equal(1024, speed, precision: 3);
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1 MB")]
    public void ByteFormatter_FormatsSizes(long bytes, string expected)
    {
        Assert.Equal(expected, ByteSizeFormatter.Format(bytes));
    }

    [Fact]
    public void Progress_Clamps()
    {
        Assert.Equal(50, ByteSizeFormatter.ProgressPercent(50, 100));
        Assert.Equal(0, ByteSizeFormatter.ProgressPercent(10, 0));
        Assert.Equal(100, ByteSizeFormatter.ProgressPercent(200, 100));
    }
}

public class DangerousFileTests
{
    [Theory]
    [InlineData("setup.exe", true)]
    [InlineData("SETUP.MSI", true)]
    [InlineData("script.ps1", true)]
    [InlineData("photo.jpg", false)]
    [InlineData("archive.zip", false)]
    [InlineData("document.pdf", false)]
    public void Executables_AreFlagged(string fileName, bool expected)
    {
        Assert.Equal(expected, DangerousFileClassifier.IsPotentiallyDangerous(fileName));
    }

    [Theory]
    [InlineData("invoice.pdf.exe", true)]
    [InlineData("photo.jpg.scr", true)]
    [InlineData("setup.exe", false)]
    [InlineData("app.msi", false)]
    public void DoubleExtensions_AreDeceptive(string fileName, bool expected)
    {
        Assert.Equal(expected, DangerousFileClassifier.HasDeceptiveDoubleExtension(fileName));
    }
}

public class RedactorTests
{
    [Fact]
    public void Secrets_AreRedactedFromLogText()
    {
        var input = "login failed password=hunter2 token: abc123 Authorization: Bearer xyz987 Cookie: session=zzz";
        var output = SensitiveDataRedactor.Redact(input);
        Assert.DoesNotContain("hunter2", output);
        Assert.DoesNotContain("xyz987", output);
        Assert.DoesNotContain("session=zzz", output);
        Assert.Contains("[redacted]", output);
    }
}

public class FileIntegrityTests
{
    [Fact]
    public async Task ComputeAndVerify_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), "peyda-hash-" + Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            await File.WriteAllBytesAsync(path, System.Text.Encoding.UTF8.GetBytes("peyda"));
            var hash = await PeydaBrowser.Infrastructure.Files.FileIntegrity.ComputeSha256Async(path);
            Assert.Equal(64, hash.Length);
            Assert.True(await PeydaBrowser.Infrastructure.Files.FileIntegrity.VerifySha256Async(path, hash));
            Assert.False(await PeydaBrowser.Infrastructure.Files.FileIntegrity.VerifySha256Async(path, new string('0', 64)));
            Assert.False(await PeydaBrowser.Infrastructure.Files.FileIntegrity.VerifySha256Async(path, "not-hex"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ParseSha256Sums_ReadsStandardFormat()
    {
        var hashA = new string('a', 64);
        var hashB = new string('B', 64);
        var content = "# checksums\n" + hashA + "  PeydaBrowser-0.1.0-win-x64-portable.zip\n" + hashB + " *PeydaBrowser-0.1.0-win-x64.msix\njunk line\n";
        var parsed = PeydaBrowser.Infrastructure.Files.FileIntegrity.ParseSha256Sums(content);
        Assert.Equal(2, parsed.Count);
        Assert.Equal(hashA, parsed["PeydaBrowser-0.1.0-win-x64-portable.zip"]);
        Assert.Equal(hashB.ToLowerInvariant(), parsed["PeydaBrowser-0.1.0-win-x64.msix"]);
    }
}
