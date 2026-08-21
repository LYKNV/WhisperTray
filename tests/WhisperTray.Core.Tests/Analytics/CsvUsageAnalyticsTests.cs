using System.Globalization;
using FluentAssertions;
using WhisperTray.Core.Analytics;
using WhisperTray.Core.Configuration;
using WhisperTray.Core.Transcription;

namespace WhisperTray.Core.Tests.Analytics;

public class CsvUsageAnalyticsTests : IDisposable
{
    private static readonly DateTimeOffset Aug21 =
        new(2026, 8, 21, 14, 3, 11, 482, TimeSpan.FromHours(3));

    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "WhisperTrayAnalyticsTests", Guid.NewGuid().ToString("N"));

    // Drives the writer's clock — assign to move a test through time.
    private DateTimeOffset _now = Aug21;

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private CsvUsageAnalytics NewSut(string? directory = null) =>
        new(directory ?? _dir, () => _now);

    private static UsageRecord Record(
        UsageStatus status = UsageStatus.Ok,
        UsageErrorKind? error = null,
        double audioSeconds = 12.34,
        TranscriptionProvider provider = TranscriptionProvider.OpenAi,
        string model = "gpt-4o-transcribe",
        TranscriptionUsage? usage = null) =>
        new()
        {
            Status = status,
            Error = error,
            AudioDuration = TimeSpan.FromSeconds(audioSeconds),
            Provider = provider,
            Model = model,
            Usage = usage,
        };

    private string[] LinesOf(string fileName) =>
        File.ReadAllLines(Path.Combine(_dir, fileName));

    [Fact]
    public void Record_NewFile_WritesHeaderThenRow()
    {
        var sut = NewSut();

        sut.Record(Record());

        var lines = LinesOf("usage-2026-08-21.csv");
        lines.Should().HaveCount(2);
        lines[0].Should().Be(
            "timestamp,status,error,audio_seconds,provider,model,tokens_in,tokens_out,tokens_total,billed_seconds");
        lines[1].Should().StartWith("2026-08-21T14:03:11");
        lines[1].Should().EndWith(",ok,,12.34,openAi,gpt-4o-transcribe,,,,");
    }

    [Fact]
    public void Record_ExistingFile_AppendsWithoutRepeatingHeader()
    {
        var sut = NewSut();

        sut.Record(Record());
        sut.Record(Record(status: UsageStatus.Empty, audioSeconds: 0.41));

        var lines = LinesOf("usage-2026-08-21.csv");
        lines.Should().HaveCount(3);
        lines.Count(l => l.StartsWith("timestamp,", StringComparison.Ordinal)).Should().Be(1);
        lines[2].Should().EndWith(",empty,,0.41,openAi,gpt-4o-transcribe,,,,");
    }

    [Fact]
    public void Record_NextDay_WritesSecondFileAndLeavesFirstIntact()
    {
        var sut = NewSut();

        sut.Record(Record());
        _now = Aug21.AddDays(1);
        sut.Record(Record());

        LinesOf("usage-2026-08-21.csv").Should().HaveCount(2);
        LinesOf("usage-2026-08-22.csv").Should().HaveCount(2);
        LinesOf("usage-2026-08-22.csv")[1].Should().StartWith("2026-08-22T");
    }

    [Fact]
    public void Record_ErrorStatus_WritesErrorKindInSnakeCase()
    {
        var sut = NewSut();

        sut.Record(Record(status: UsageStatus.Error, error: UsageErrorKind.RateLimit, audioSeconds: 3.1));

        LinesOf("usage-2026-08-21.csv")[1]
            .Should().EndWith(",error,rate_limit,3.10,openAi,gpt-4o-transcribe,,,,");
    }

    [Fact]
    public void Record_TokenUsage_FillsTokenColumnsAndLeavesBilledSecondsEmpty()
    {
        var sut = NewSut();

        sut.Record(Record(usage: new TranscriptionUsage(InputTokens: 412, OutputTokens: 48, TotalTokens: 460)));

        LinesOf("usage-2026-08-21.csv")[1]
            .Should().EndWith(",ok,,12.34,openAi,gpt-4o-transcribe,412,48,460,");
    }

    [Fact]
    public void Record_DurationUsage_FillsBilledSecondsAndLeavesTokenColumnsEmpty()
    {
        var sut = NewSut();

        sut.Record(Record(
            provider: TranscriptionProvider.Lemonfox,
            model: "whisper-1",
            audioSeconds: 8.72,
            usage: new TranscriptionUsage(BilledSeconds: 8.72)));

        LinesOf("usage-2026-08-21.csv")[1]
            .Should().EndWith(",ok,,8.72,lemonfox,whisper-1,,,,8.72");
    }

    [Fact]
    public void Record_ZeroTokens_WritesZeroRatherThanEmptyCell()
    {
        var sut = NewSut();

        sut.Record(Record(usage: new TranscriptionUsage(InputTokens: 0, OutputTokens: 0, TotalTokens: 0)));

        LinesOf("usage-2026-08-21.csv")[1]
            .Should().EndWith(",ok,,12.34,openAi,gpt-4o-transcribe,0,0,0,");
    }

    [Fact]
    public void Record_OneClockReading_DrivesBothFileNameAndTimestampColumn()
    {
        var sut = NewSut();

        sut.Record(Record());

        var row = LinesOf("usage-2026-08-21.csv")[1];
        row.Should().StartWith(_now.ToString("O", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Record_WithoutExplicitClock_WritesTodaysFile()
    {
        var sut = new CsvUsageAnalytics(_dir);

        sut.Record(Record());

        File.Exists(Path.Combine(_dir, CsvUsageAnalytics.FileNameFor(DateTimeOffset.Now)))
            .Should().BeTrue();
    }

    [Fact]
    public void Record_MissingDirectory_CreatesIt()
    {
        var nested = Path.Combine(_dir, "does", "not", "exist");
        var sut = new CsvUsageAnalytics(nested, () => _now);

        sut.Record(Record());

        File.Exists(Path.Combine(nested, "usage-2026-08-21.csv")).Should().BeTrue();
    }

    [Fact]
    public void Record_ModelWithCommaAndQuote_IsEscapedPerRfc4180()
    {
        var sut = NewSut();

        sut.Record(Record(model: "weird,model\"name"));

        var row = LinesOf("usage-2026-08-21.csv")[1];
        row.Should().Contain("\"weird,model\"\"name\"");
        // The escaped model must not add columns: nine commas separate ten fields.
        SplitCsv(row).Should().HaveCount(10);
        SplitCsv(row)[5].Should().Be("weird,model\"name");
    }

    [Fact]
    public void Record_UnderCommaDecimalCulture_StillWritesDotSeparatedNumbers()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("ru-RU");
        try
        {
            var sut = NewSut();

            sut.Record(Record(audioSeconds: 12.34, usage: new TranscriptionUsage(BilledSeconds: 8.72)));

            var row = LinesOf("usage-2026-08-21.csv")[1];
            row.Should().Contain(",12.34,");
            row.Should().EndWith(",8.72");
            SplitCsv(row).Should().HaveCount(10);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Record_UnwritableDirectory_DoesNotThrow()
    {
        // A path whose parent is an existing *file* can never be created as a directory.
        var blocker = Path.Combine(_dir, "blocker");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(blocker, "not a directory");
        var sut = NewSut(Path.Combine(blocker, "analytics"));

        var act = () => sut.Record(Record());

        act.Should().NotThrow();
    }

    // Minimal RFC 4180 splitter — enough to prove the writer's escaping round-trips.
    private static string[] SplitCsv(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields.ToArray();
    }
}
