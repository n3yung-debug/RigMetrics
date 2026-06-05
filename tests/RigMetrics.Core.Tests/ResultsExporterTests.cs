using RigMetrics.Core.Export;
using RigMetrics.Core.Models;
using Xunit;

namespace RigMetrics.Core.Tests;

public class ResultsExporterTests
{
    private static SessionSummaryRow SampleRow(string label = "Shadows Low") => new()
    {
        Date = "2026-06-05 14:00",
        Label = label,
        AvgFps = 144.4,
        Low1Fps = 95.2,
        Low01Fps = 60.1,
        Stutters = 3,
        AvgCpuLoad = 55,
        MaxGpuTempC = 72,
        Notes = "test, with comma",
    };

    [Fact]
    public void ExportCsv_WritesHeaderAndRows()
    {
        var path = Path.Combine(Path.GetTempPath(), $"results_{Guid.NewGuid():N}.csv");
        try
        {
            ResultsExporter.ExportCsv(new[] { SampleRow(), SampleRow("AA Off") }, path);
            var lines = File.ReadAllLines(path);

            Assert.Equal(3, lines.Length); // header + 2 rows
            Assert.StartsWith("Date,Label,Avg FPS", lines[0]);
            Assert.Contains("144.4", lines[1]);
            // A value containing a comma must be quoted.
            Assert.Contains("\"test, with comma\"", lines[1]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AppendToMaster_WritesHeaderOnceThenAppends()
    {
        var path = Path.Combine(Path.GetTempPath(), $"master_{Guid.NewGuid():N}.csv");
        try
        {
            ResultsExporter.AppendToMaster(SampleRow("Run 1"), path);
            ResultsExporter.AppendToMaster(SampleRow("Run 2"), path);
            var lines = File.ReadAllLines(path);

            Assert.Equal(3, lines.Length); // one header + two appended rows
            Assert.StartsWith("Date,Label", lines[0]);
            Assert.Contains("Run 1", lines[1]);
            Assert.Contains("Run 2", lines[2]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FormatValue_HandlesNullAndNumbers()
    {
        Assert.Equal("", ResultsExporter.FormatValue(null, "0.0"));
        Assert.Equal("12.3", ResultsExporter.FormatValue(12.34, "0.0"));
        Assert.Equal("7", ResultsExporter.FormatValue(7, "0"));
    }

    [Fact]
    public void From_MapsStatsOntoRow()
    {
        var session = new SessionRecording
        {
            Label = "My Run",
            StartedUtc = new DateTime(2026, 6, 5, 12, 0, 0, DateTimeKind.Utc),
            Stats = new SessionStats
            {
                FrameCount = 1000,
                AvgFps = 120.456,
                Percentile1LowFps = 80.4,
                Percentile01LowFps = 55.9,
                StutterCount = 4,
                MaxGpuTempC = 75.2,
            },
        };

        var row = SessionSummaryRow.From(session);

        Assert.Equal("My Run", row.Label);
        Assert.Equal(120.5, row.AvgFps, 3);   // rounded to 1 decimal
        Assert.Equal(80.4, row.Low1Fps, 3);
        Assert.Equal(4, row.Stutters);
        Assert.Equal(75, row.MaxGpuTempC!.Value, 3);
    }
}
