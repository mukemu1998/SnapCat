using System.Text;
using System.Text.Json;
using SnapCat.Core.Models;
using SnapCat.Core.Services;

namespace SnapCat.Infrastructure.Services;

public sealed class JsonHistoryStore : IHistoryStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly string _historyPath;

    public JsonHistoryStore(string appDataDirectory)
    {
        Directory.CreateDirectory(appDataDirectory);
        _historyPath = Path.Combine(appDataDirectory, "history.jsonl");
    }

    public async Task<IReadOnlyList<CaptureTranslationRecord>> LoadRecentAsync(int count, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_historyPath))
        {
            return [];
        }

        var lines = await File.ReadAllLinesAsync(_historyPath, cancellationToken);
        var records = new List<CaptureTranslationRecord>();

        foreach (var line in lines.Reverse().Take(count))
        {
            if (TryDeserializeRecord(line, out var record))
            {
                EnsureRecordId(record);
                records.Add(record);
            }
        }

        return records;
    }

    public async Task AppendAsync(CaptureTranslationRecord record, CancellationToken cancellationToken = default)
    {
        EnsureRecordId(record);
        var json = JsonSerializer.Serialize(record, SerializerOptions);
        await File.AppendAllTextAsync(_historyPath, json + Environment.NewLine, Encoding.UTF8, cancellationToken);
    }

    public async Task DeleteAsync(CaptureTranslationRecord record, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_historyPath))
        {
            return;
        }

        var records = await ReadAllRecordsAsync(cancellationToken);
        var updatedRecords = records
            .Where(candidate => !IsSameRecord(candidate, record))
            .ToList();

        await RewriteAllRecordsAsync(updatedRecords, cancellationToken);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(_historyPath))
        {
            File.Delete(_historyPath);
        }

        return Task.CompletedTask;
    }

    private async Task<List<CaptureTranslationRecord>> ReadAllRecordsAsync(CancellationToken cancellationToken)
    {
        var lines = await File.ReadAllLinesAsync(_historyPath, cancellationToken);
        var records = new List<CaptureTranslationRecord>();

        foreach (var line in lines)
        {
            if (TryDeserializeRecord(line, out var record))
            {
                EnsureRecordId(record);
                records.Add(record);
            }
        }

        return records;
    }

    private async Task RewriteAllRecordsAsync(
        IReadOnlyList<CaptureTranslationRecord> records,
        CancellationToken cancellationToken)
    {
        if (records.Count == 0)
        {
            if (File.Exists(_historyPath))
            {
                File.Delete(_historyPath);
            }

            return;
        }

        var builder = new StringBuilder();
        foreach (var record in records)
        {
            EnsureRecordId(record);
            builder.AppendLine(JsonSerializer.Serialize(record, SerializerOptions));
        }

        await File.WriteAllTextAsync(_historyPath, builder.ToString(), Encoding.UTF8, cancellationToken);
    }

    private static bool TryDeserializeRecord(string? line, out CaptureTranslationRecord record)
    {
        record = null!;

        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        record = JsonSerializer.Deserialize<CaptureTranslationRecord>(line, SerializerOptions)!;
        return record is not null;
    }

    private static void EnsureRecordId(CaptureTranslationRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.RecordId))
        {
            record.RecordId = Guid.NewGuid().ToString("N");
        }
    }

    private static bool IsSameRecord(CaptureTranslationRecord left, CaptureTranslationRecord right)
    {
        if (!string.IsNullOrWhiteSpace(left.RecordId) && !string.IsNullOrWhiteSpace(right.RecordId))
        {
            return string.Equals(left.RecordId, right.RecordId, StringComparison.Ordinal);
        }

        return left.Timestamp == right.Timestamp
            && string.Equals(left.WorkflowType, right.WorkflowType, StringComparison.Ordinal)
            && string.Equals(left.ImagePath, right.ImagePath, StringComparison.Ordinal)
            && string.Equals(left.SourceText, right.SourceText, StringComparison.Ordinal)
            && string.Equals(left.TranslatedText, right.TranslatedText, StringComparison.Ordinal)
            && string.Equals(left.QrCodeText, right.QrCodeText, StringComparison.Ordinal);
    }
}
