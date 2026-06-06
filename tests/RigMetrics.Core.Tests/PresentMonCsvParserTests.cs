using RigMetrics.Core.PresentMon;
using Xunit;

namespace RigMetrics.Core.Tests;

public class PresentMonCsvParserTests
{
    [Fact]
    public void ParsesPresentMon1xFormat()
    {
        var parser = new PresentMonCsvParser();
        const string header =
            "Application,ProcessID,SwapChainAddress,Runtime,SyncInterval,PresentFlags,Dropped," +
            "TimeInSeconds,msBetweenPresents,msInPresentAPI,msBetweenDisplayChange,msUntilRenderComplete,msUntilDisplayed";

        Assert.False(parser.TryParseLine(header, out _, out _)); // header consumed
        Assert.True(parser.HasHeader);
        Assert.True(parser.CanProduceFrameTimes);

        const string row =
            "cs2.exe,1234,0xABCD,DXGI,1,0,0,12.5,6.94,0.5,6.94,3.2,7.0";

        Assert.True(parser.TryParseLine(row, out var sample, out var app));
        Assert.Equal("cs2.exe", app);
        Assert.NotNull(sample);
        Assert.Equal(6.94, sample!.FrameTimeMs, 3);
        Assert.Equal(12.5, sample.TimeInSeconds, 3);
        Assert.False(sample.Dropped);
    }

    [Fact]
    public void ParsesPresentMon2xLikeFormat_ByHeaderName()
    {
        // A 2.x-style header that reorders columns and renames the time column.
        var parser = new PresentMonCsvParser();
        const string header =
            "Application,ProcessID,SwapChainAddress,PresentRuntime,SyncInterval,PresentFlags," +
            "AllowsTearing,PresentMode,CPUStartTime,FrameTime,Dropped";

        parser.SetHeader(header);
        Assert.True(parser.CanProduceFrameTimes);

        const string row =
            "cs2.exe,1234,0xABCD,DXGI,1,0,1,Hardware,3.5,8.33,0";

        Assert.True(parser.TryParseLine(row, out var sample, out var app));
        Assert.Equal("cs2.exe", app);
        Assert.Equal(8.33, sample!.FrameTimeMs, 3);
        Assert.Equal(3.5, sample.TimeInSeconds, 3);
    }

    [Fact]
    public void DroppedFrame_IsFlagged()
    {
        var parser = new PresentMonCsvParser();
        parser.SetHeader("Application,Dropped,msBetweenPresents");
        Assert.True(parser.TryParseLine("cs2.exe,1,16.6", out var s, out _));
        Assert.True(s!.Dropped);
    }

    [Fact]
    public void AutoDetectsHeaderFromFirstLine()
    {
        var parser = new PresentMonCsvParser();
        // First line is a header; parser should consume it and report no sample.
        Assert.False(parser.TryParseLine("Application,Dropped,msBetweenPresents", out _, out _));
        Assert.True(parser.HasHeader);
    }

    [Fact]
    public void RepeatedHeaderMidStream_IsReParsed_NotTreatedAsData()
    {
        var parser = new PresentMonCsvParser();
        parser.SetHeader("Application,Dropped,msBetweenPresents");
        Assert.True(parser.TryParseLine("cs2.exe,0,16.6", out _, out _));
        // PresentMon can re-emit the header if the session restarts.
        Assert.False(parser.TryParseLine("Application,Dropped,msBetweenPresents", out _, out _));
    }

    [Fact]
    public void MalformedNumber_IsSkipped()
    {
        var parser = new PresentMonCsvParser();
        parser.SetHeader("Application,Dropped,msBetweenPresents");
        Assert.False(parser.TryParseLine("cs2.exe,0,N/A", out var s, out _));
        Assert.Null(s);
    }

    [Fact]
    public void BlankLine_ReturnsFalse()
    {
        var parser = new PresentMonCsvParser();
        parser.SetHeader("Application,Dropped,msBetweenPresents");
        Assert.False(parser.TryParseLine("", out _, out _));
        Assert.False(parser.TryParseLine("   ", out _, out _));
    }
}
