using System.Globalization;
using System.Text;
using WhisperTray.Core.Configuration;

namespace WhisperTray.Core.Analytics;

/// <summary>
/// Appends usage rows to <c>usage-YYYY-MM-DD.csv</c>, one file per local calendar
/// date, with a header written only when the file is created or found empty.
/// The file is opened and closed per record rather than held open: the log stays
/// readable by Excel and other tools at all times, and day rollover falls out for
/// free.
/// </summary>
public sealed class CsvUsageAnalytics : IUsageAnalytics
{
    public const string Header =
        "timestamp,status,error,audio_seconds,provider,model,tokens_in,tokens_out,tokens_total,billed_seconds";

    private readonly string _directory;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();

    public CsvUsageAnalytics(string directory, Func<DateTimeOffset>? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    public static string FileNameFor(DateTimeOffset timestamp) =>
        "usage-" + timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".csv";

    public void Record(UsageRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        try
        {
            // Read once: filename and timestamp column must agree across midnight.
            var timestamp = _clock();
            var line = FormatRow(timestamp, record);
            var path = Path.Combine(_directory, FileNameFor(timestamp));

            lock (_gate)
            {
                Directory.CreateDirectory(_directory);

                using var stream = new FileStream(
                    path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                if (stream.Position == 0)
                {
                    writer.Write(Header);
                    writer.Write('\n');
                }

                writer.Write(line);
                writer.Write('\n');
            }
        }
        catch (Exception)
        {
            // Best-effort by contract: a write failure costs a row, never a dictation.
        }
    }

    private static string FormatRow(DateTimeOffset timestamp, UsageRecord r)
    {
        var usage = r.Usage;

        return string.Join(
            ',',
            timestamp.ToString("O", CultureInfo.InvariantCulture),
            StatusToken(r.Status),
            r.Error is null ? string.Empty : ErrorToken(r.Error.Value),
            r.AudioDuration.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture),
            ProviderToken(r.Provider),
            Escape(r.Model),
            Int(usage?.InputTokens),
            Int(usage?.OutputTokens),
            Int(usage?.TotalTokens),
            Seconds(usage?.BilledSeconds));
    }

    /// <summary>RFC 4180 escaping. Only <c>model</c> needs it — it is free-form user input.</summary>
    private static string Escape(string value)
    {
        if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
        {
            return value;
        }

        return '"' + value.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';
    }

    private static string Int(int? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Seconds(double? value) =>
        value?.ToString("F2", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string StatusToken(UsageStatus status) => status switch
    {
        UsageStatus.Ok => "ok",
        UsageStatus.Empty => "empty",
        UsageStatus.Error => "error",
        _ => "unknown",
    };

    private static string ErrorToken(UsageErrorKind kind) => kind switch
    {
        UsageErrorKind.Auth => "auth",
        UsageErrorKind.RateLimit => "rate_limit",
        UsageErrorKind.Request => "request",
        UsageErrorKind.Server => "server",
        UsageErrorKind.Timeout => "timeout",
        UsageErrorKind.Cancelled => "cancelled",
        UsageErrorKind.NoTarget => "no_target",
        UsageErrorKind.Unexpected => "unexpected",
        _ => "unknown",
    };

    private static string ProviderToken(TranscriptionProvider provider) => provider switch
    {
        TranscriptionProvider.OpenAi => "openAi",
        TranscriptionProvider.Lemonfox => "lemonfox",
        TranscriptionProvider.HuggingFace => "huggingFace",
        TranscriptionProvider.WhisperApi => "whisperApi",
        _ => "unknown",
    };
}
