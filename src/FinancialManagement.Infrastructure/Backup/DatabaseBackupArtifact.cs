using System.IO.Compression;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using FinancialManagement.Application.Backup;

namespace FinancialManagement.Infrastructure.Backup;

/// <summary>
/// V1 is a stored (uncompressed) ZIP with exactly two entries. SHA-256 detects
/// accidental corruption, not an attacker able to rebuild the manifest.
/// No decompression amplification is accepted. Streaming is additionally bounded
/// to 64 GiB per database and 16 KiB per manifest (technical resource ceilings,
/// not business limits). The same limits apply to creation and restoration.
/// </summary>
internal static class DatabaseBackupArtifact
{
    internal const string Extension = ".fmbak";
    internal const string Format = "FinancialManagementBackup";
    internal const long MaximumDatabaseLength = 64L * 1024 * 1024 * 1024;
    private const int MaximumManifestLength = 16 * 1024;
    private const string DatabaseEntry = "database.sqlite";
    private const string ManifestEntry = "manifest.json";

    public static async Task WriteAsync(string databasePath, string artifactPath,
        DateTimeOffset createdAtUtc, CancellationToken cancellationToken)
    {
        await using var database = new FileStream(databasePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (database.Length <= 0 || database.Length > MaximumDatabaseLength)
            throw new InvalidDataException("The snapshot exceeds the supported artifact size.");

        var hash = Convert.ToHexString(await SHA256.HashDataAsync(database, cancellationToken));
        database.Position = 0;
        var manifest = new Manifest(Format, 1, createdAtUtc.ToUniversalTime(), DatabaseEntry, database.Length, hash);
        await using var output = new FileStream(artifactPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var entry = archive.CreateEntry(DatabaseEntry, CompressionLevel.NoCompression).Open())
                await database.CopyToAsync(entry, cancellationToken);
            using (var entry = archive.CreateEntry(ManifestEntry, CompressionLevel.NoCompression).Open())
                await JsonSerializer.SerializeAsync(entry, manifest, cancellationToken: cancellationToken);
        }
        await output.FlushAsync(cancellationToken);
        output.Flush(flushToDisk: true);
    }

    // Only a caller-generated staging path is used for output. Archive names are
    // never combined with a directory or passed to ExtractToDirectory.
    public static async Task<DatabaseBackupFailureReason> ExtractAsync(string artifactPath,
        string stagingPath, CancellationToken cancellationToken)
    {
        try
        {
            await using var input = new FileStream(artifactPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length > MaximumDatabaseLength + MaximumManifestLength + 4096 || !HasBoundedDirectory(input))
                return DatabaseBackupFailureReason.InvalidBackup;
            using var archive = new ZipArchive(input, ZipArchiveMode.Read);
            if (archive.Entries.Count != 2)
                return DatabaseBackupFailureReason.InvalidBackup;
            var databases = archive.Entries.Where(entry => entry.FullName == DatabaseEntry).ToArray();
            var manifests = archive.Entries.Where(entry => entry.FullName == ManifestEntry).ToArray();
            if (databases.Length != 1 || manifests.Length != 1)
                return DatabaseBackupFailureReason.InvalidBackup;
            var database = databases[0];
            var manifestEntry = manifests[0];
            if (manifestEntry.Length <= 0 || manifestEntry.Length > MaximumManifestLength ||
                manifestEntry.CompressedLength != manifestEntry.Length ||
                database.Length <= 0 || database.Length > MaximumDatabaseLength ||
                database.CompressedLength != database.Length)
                return DatabaseBackupFailureReason.InvalidBackup;

            using var manifestBytes = new MemoryStream();
            using (var entry = manifestEntry.Open())
                await CopyExactlyAsync(entry, manifestBytes, manifestEntry.Length, cancellationToken);
            using var document = JsonDocument.Parse(manifestBytes.ToArray());
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return DatabaseBackupFailureReason.InvalidBackup;
            var fields = document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
            string[] expected = ["BackupFormat", "BackupFormatVersion", "CreatedAtUtc", "DatabaseFileName", "DatabaseLength", "DatabaseSha256"];
            if (fields.Length != expected.Length || !fields.ToHashSet(StringComparer.Ordinal).SetEquals(expected))
                return DatabaseBackupFailureReason.InvalidBackup;
            var manifest = document.Deserialize<Manifest>();
            if (manifest is null || manifest.BackupFormat != Format || manifest.DatabaseFileName != DatabaseEntry ||
                manifest.CreatedAtUtc == default || manifest.CreatedAtUtc.Offset != TimeSpan.Zero ||
                manifest.DatabaseLength != database.Length || manifest.DatabaseSha256 is not { Length: 64 })
                return DatabaseBackupFailureReason.InvalidBackup;
            if (manifest.BackupFormatVersion != 1)
                return DatabaseBackupFailureReason.IncompatibleBackup;
            var expectedHash = Convert.FromHexString(manifest.DatabaseSha256);

            // Checked again against bytes actually read, not just ZIP metadata.
            await using (var output = new FileStream(stagingPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                using (var entry = database.Open())
                    await CopyExactlyAsync(entry, output, manifest.DatabaseLength, cancellationToken);
                output.Position = 0;
                var actualHash = await SHA256.HashDataAsync(output, cancellationToken);
                if (!CryptographicOperations.FixedTimeEquals(expectedHash, actualHash))
                    return DatabaseBackupFailureReason.InvalidBackup;
            }
            return DatabaseBackupFailureReason.None;
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException or FormatException)
        {
            return DatabaseBackupFailureReason.InvalidBackup;
        }
    }

    // Bound the central directory before ZipArchive allocates its entry collection.
    // Our writer emits a single-disk ZIP, no archive comment, with two entries.
    // ZIP64 is supported for large snapshots, but not extensible metadata records.
    private static bool HasBoundedDirectory(FileStream input)
    {
        if (input.Length < 22) return false;
        Span<byte> end = stackalloc byte[22];
        input.Position = input.Length - end.Length;
        input.ReadExactly(end);
        if (BinaryPrimitives.ReadUInt32LittleEndian(end) != 0x06054b50 ||
            BinaryPrimitives.ReadUInt16LittleEndian(end[4..]) != 0 ||
            BinaryPrimitives.ReadUInt16LittleEndian(end[6..]) != 0 ||
            BinaryPrimitives.ReadUInt16LittleEndian(end[8..]) != 2 ||
            BinaryPrimitives.ReadUInt16LittleEndian(end[10..]) != 2 ||
            BinaryPrimitives.ReadUInt16LittleEndian(end[20..]) != 0)
            return false;
        ulong size = BinaryPrimitives.ReadUInt32LittleEndian(end[12..]);
        ulong offset = BinaryPrimitives.ReadUInt32LittleEndian(end[16..]);
        var directoryEnd = (ulong)input.Length - 22;
        if (offset == uint.MaxValue || size == uint.MaxValue)
        {
            if (input.Length < 98) return false;
            Span<byte> locator = stackalloc byte[20];
            input.Position = input.Length - 42;
            input.ReadExactly(locator);
            if (BinaryPrimitives.ReadUInt32LittleEndian(locator) != 0x07064b50 ||
                BinaryPrimitives.ReadUInt32LittleEndian(locator[4..]) != 0 ||
                BinaryPrimitives.ReadUInt32LittleEndian(locator[16..]) != 1)
                return false;
            var zip64Offset = BinaryPrimitives.ReadUInt64LittleEndian(locator[8..]);
            if (zip64Offset != (ulong)input.Length - 98) return false;
            Span<byte> zip64 = stackalloc byte[56];
            input.Position = (long)zip64Offset;
            input.ReadExactly(zip64);
            if (BinaryPrimitives.ReadUInt32LittleEndian(zip64) != 0x06064b50 ||
                BinaryPrimitives.ReadUInt64LittleEndian(zip64[4..]) != 44 ||
                BinaryPrimitives.ReadUInt32LittleEndian(zip64[16..]) != 0 ||
                BinaryPrimitives.ReadUInt32LittleEndian(zip64[20..]) != 0 ||
                BinaryPrimitives.ReadUInt64LittleEndian(zip64[24..]) != 2 ||
                BinaryPrimitives.ReadUInt64LittleEndian(zip64[32..]) != 2)
                return false;
            size = BinaryPrimitives.ReadUInt64LittleEndian(zip64[40..]);
            offset = BinaryPrimitives.ReadUInt64LittleEndian(zip64[48..]);
            directoryEnd = zip64Offset;
        }
        input.Position = 0;
        return size <= 4096 && offset <= directoryEnd && size == directoryEnd - offset;
    }

    private static async Task CopyExactlyAsync(Stream input, Stream output, long length, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        var remaining = length;
        while (remaining > 0)
        {
            var read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellationToken);
            if (read == 0) throw new InvalidDataException("The artifact entry is truncated.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            remaining -= read;
        }
        if (await input.ReadAsync(buffer.AsMemory(0, 1), cancellationToken) != 0)
            throw new InvalidDataException("The artifact entry exceeds its declared length.");
    }

    private sealed record Manifest(string BackupFormat, int BackupFormatVersion, DateTimeOffset CreatedAtUtc,
        string DatabaseFileName, long DatabaseLength, string DatabaseSha256);
}
