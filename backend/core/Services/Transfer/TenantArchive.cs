using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Api.Constants;
using Api.Helpers;
using Api.Models;

namespace Api.Services.Transfer;

/// <summary>
/// The file format of a tenant transfer archive, version 1: a ZIP with <c>manifest.json</c>,
/// <c>users.ndjson</c> and one <c>tables/&lt;table&gt;.ndjson</c> per travelling table. Each
/// NDJSON line is one row as PostgreSQL's <c>to_jsonb</c> writes it.
/// <para>
/// An archive is external input. The reader accepts only the entries the catalog names, and
/// it bounds what it decompresses and how long one row may be, so a crafted file cannot
/// exhaust the server's memory or disk.
/// </para>
/// </summary>
public static class TenantArchive
{
    public const string ManifestEntry = "manifest.json";
    public const string UsersEntry = "users.ndjson";

    // A decompression bomb is small on the wire and huge once read. Real archives compress
    // well below this ratio: row text shrinks five- to tenfold and floorplan images hardly at all.
    public const int MaxExpansionFactor = 10;

    // The largest row a tenant can hold is a floorplan: Upload_MaxFileSizeMb caps the image,
    // and the archive carries it hex-encoded at twice the size. This is far above that and
    // far below what one string may cost.
    public const int MaxRowChars = 64 * 1024 * 1024;

    // The manifest is a few kilobytes: a version list and a row count per table.
    public const int MaxManifestChars = 1024 * 1024;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonSerializerOptions ManifestJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static string TableEntry(TransferTable table) => $"tables/{table.Name}.ndjson";

    /// <summary>Writes <paramref name="lines"/> as one NDJSON entry and returns the number of lines.</summary>
    public static async Task<long> WriteLinesAsync(
        ZipArchive zip, string entryName, IAsyncEnumerable<string> lines, CancellationToken ct)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, Utf8NoBom) { NewLine = "\n" };

        long count = 0;
        await foreach (var line in lines.WithCancellation(ct))
        {
            await writer.WriteLineAsync(line.AsMemory(), ct);
            count++;
        }
        return count;
    }

    public static async Task WriteManifestAsync(ZipArchive zip, TransferManifest manifest, CancellationToken ct)
    {
        var entry = zip.CreateEntry(ManifestEntry, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await JsonSerializer.SerializeAsync(stream, manifest, ManifestJson, ct);
    }

    /// <summary>
    /// Opens an uploaded archive for reading and checks its entry list against the catalog.
    /// <paramref name="maxArchiveBytes"/> is the configured upload ceiling; the reader will
    /// decompress at most <see cref="MaxExpansionFactor"/> times that.
    /// </summary>
    public static Reader Open(Stream archive, long maxArchiveBytes)
    {
        ZipArchive zip;
        try
        {
            zip = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException)
        {
            throw Invalid("The file is not a transfer archive.");
        }

        var expected = TenantTransferTables.Travelling.Select(TableEntry)
            .Append(ManifestEntry).Append(UsersEntry)
            .ToHashSet(StringComparer.Ordinal);
        // Count and set both: a ZIP may hold one name twice, which would hide a missing entry.
        var actual = zip.Entries.Select(e => e.FullName).ToList();
        if (actual.Count != expected.Count || !expected.SetEquals(actual))
        {
            zip.Dispose();
            throw Invalid("The archive does not hold exactly the entries a transfer archive has.");
        }

        return new Reader(zip, checked(maxArchiveBytes * MaxExpansionFactor));
    }

    public static TransferRejectedException Invalid(string message) =>
        new(422, ApiErrorCodes.TransferArchiveInvalid, message);

    /// <summary>An open archive. The byte budget is shared by every entry read through it.</summary>
    public sealed class Reader(ZipArchive zip, long uncompressedBudget) : IDisposable
    {
        private long _remaining = uncompressedBudget;

        public void Dispose() => zip.Dispose();

        public async Task<TransferManifest> ReadManifestAsync(CancellationToken ct)
        {
            var json = new StringBuilder();
            await foreach (var line in ReadLinesAsync(ManifestEntry, ct))
            {
                json.Append(line).Append('\n');
                if (json.Length > MaxManifestChars) throw Invalid("The archive manifest is not valid.");
            }

            TransferManifest? manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<TransferManifest>(json.ToString(), ManifestJson);
            }
            catch (JsonException)
            {
                throw Invalid("The archive manifest is not valid.");
            }

            if (manifest is null
                || manifest.SchemaVersion is null || manifest.Tables is null || manifest.EncryptedColumns is null)
                throw Invalid("The archive manifest is not valid.");
            if (manifest.FormatVersion != TransferManifest.CurrentFormatVersion)
                throw Invalid($"The archive has format version {manifest.FormatVersion}, which this version cannot read.");
            return manifest;
        }

        /// <summary>The lines of one entry, in order. Empty lines are skipped.</summary>
        public async IAsyncEnumerable<string> ReadLinesAsync(
            string entryName, [EnumeratorCancellation] CancellationToken ct)
        {
            var entry = zip.GetEntry(entryName)!; // Open() checked that every expected entry exists.
            await using var stream = entry.Open();
            using var reader = new StreamReader(stream, Utf8NoBom);

            var buffer = new char[81920];
            var line = new StringBuilder();
            while (true)
            {
                int read;
                try
                {
                    read = await reader.ReadAsync(buffer.AsMemory(), ct);
                }
                catch (InvalidDataException)
                {
                    throw Invalid("The archive is damaged.");
                }
                if (read == 0) break;

                // Counted in characters. A UTF-8 character is at least one byte, so this never
                // refuses an archive that is inside the byte budget.
                _remaining -= read;
                if (_remaining < 0)
                    throw Invalid("The archive expands to more data than this installation accepts.");

                var start = 0;
                for (var i = 0; i < read; i++)
                {
                    if (buffer[i] != '\n') continue;
                    line.Append(buffer, start, i - start);
                    start = i + 1;
                    if (line.Length > 0)
                    {
                        yield return line.ToString();
                        line.Clear();
                    }
                }
                line.Append(buffer, start, read - start);
                if (line.Length > MaxRowChars)
                    throw Invalid("The archive holds a row that is too large.");
            }

            if (line.Length > 0)
                yield return line.ToString();
        }
    }
}
