using System.Diagnostics;

namespace FinancialManagement.Tests;

[CollectionDefinition("Foundation filesystem safety guard", DisableParallelization = true)]
public sealed class FoundationFilesystemSafetyGuardCollection;

[Collection("Foundation filesystem safety guard")]
public sealed class FoundationProgramDataSafetyGuardTests
{
    [Fact]
    public async Task Unchanged_files_pass_without_creating_sqlite_sidecars()
    {
        using var fixture = new GuardFixture();
        await File.WriteAllBytesAsync(fixture.DatabasePath, [1, 2, 3, 4]);

        var capture = await fixture.RunAsync("Capture");
        var verify = await fixture.RunAsync("Verify");

        Assert.True(capture.ExitCode == 0, capture.Output);
        Assert.True(verify.ExitCode == 0, verify.Output);
        Assert.Contains("remained unchanged", verify.Output, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(fixture.DatabasePath + "-wal"));
        Assert.False(File.Exists(fixture.DatabasePath + "-shm"));
    }

    [Fact]
    public async Task Database_content_mutation_fails_verification_with_diagnostics()
    {
        using var fixture = new GuardFixture();
        await File.WriteAllBytesAsync(fixture.DatabasePath, [1, 2, 3, 4]);
        var capture = await fixture.RunAsync("Capture");
        Assert.True(capture.ExitCode == 0, capture.Output);

        await File.AppendAllTextAsync(fixture.DatabasePath, "changed");
        var verify = await fixture.RunAsync("Verify");

        Assert.NotEqual(0, verify.ExitCode);
        Assert.Contains("Database.Length", verify.Output, StringComparison.Ordinal);
        Assert.Contains("Database.Sha256", verify.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task New_wal_sidecar_fails_verification()
    {
        using var fixture = new GuardFixture();
        await File.WriteAllBytesAsync(fixture.DatabasePath, [1, 2, 3, 4]);
        var capture = await fixture.RunAsync("Capture");
        Assert.True(capture.ExitCode == 0, capture.Output);

        await File.WriteAllBytesAsync(fixture.DatabasePath + "-wal", [5, 6]);
        var verify = await fixture.RunAsync("Verify");

        Assert.NotEqual(0, verify.ExitCode);
        Assert.Contains("Wal.Exists", verify.Output, StringComparison.Ordinal);
    }

    private sealed class GuardFixture : IDisposable
    {
        private readonly string root = Path.Combine(
            Path.GetTempPath(),
            $"FinancialManagement-FoundationGuard-{Guid.NewGuid():N}");

        internal GuardFixture()
        {
            Directory.CreateDirectory(root);
            DatabasePath = Path.Combine(root, "FinancialManagement.db");
            SnapshotPath = Path.Combine(root, "snapshot.json");
        }

        internal string DatabasePath { get; }
        private string SnapshotPath { get; }

        internal async Task<GuardResult> RunAsync(string mode)
        {
            var scriptPath = FindRepositoryFile("scripts", "verify-programdata-unchanged.ps1");
            var startInfo = new ProcessStartInfo("powershell.exe")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var argument in new[]
            {
                "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
                "-File", scriptPath,
                "-Mode", mode,
                "-SnapshotPath", SnapshotPath,
                "-DatabasePath", DatabasePath
            })
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("PowerShell safety guard did not start.");
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return new GuardResult(
                process.ExitCode,
                (await standardOutput) + Environment.NewLine + (await standardError));
        }

        public void Dispose()
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static string FindRepositoryFile(params string[] components)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. components]);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            $"Could not locate repository file: {Path.Combine(components)}");
    }

    private sealed record GuardResult(int ExitCode, string Output);
}
