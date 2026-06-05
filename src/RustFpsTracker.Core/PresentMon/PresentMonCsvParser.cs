using System.Globalization;
using RustFpsTracker.Core.Models;

namespace RustFpsTracker.Core.PresentMon;

/// <summary>
/// Parses PresentMon CSV output (from --output_stdout or a .csv file) into
/// <see cref="FrameSample"/>s.
///
/// PresentMon's exact column layout changes between versions (1.x vs 2.x), so
/// this parser is driven by the <b>header names</b> rather than fixed column
/// indices. Feed it the header line first (via <see cref="SetHeader"/> or by
/// letting <see cref="TryParseLine"/> auto-detect it), then feed data lines.
/// </summary>
public sealed class PresentMonCsvParser
{
    // Candidate column names for each field, in priority order. Matching is
    // case-insensitive. This covers PresentMon 1.x and 2.x naming.
    private static readonly string[] AppNames = { "Application" };
    private static readonly string[] PidNames = { "ProcessID", "ProcessId" };
    private static readonly string[] TimeNames = { "TimeInSeconds", "CPUStartTime" };
    private static readonly string[] FrameTimeNames =
        { "msBetweenPresents", "MsBetweenPresents", "FrameTime", "msBetweenDisplayChange", "MsBetweenDisplayChange" };
    private static readonly string[] DroppedNames = { "Dropped" };

    private int _appIdx = -1;
    private int _pidIdx = -1;
    private int _timeIdx = -1;
    private int _frameTimeIdx = -1;
    private int _droppedIdx = -1;
    private int _columnCount;

    public bool HasHeader { get; private set; }

    /// <summary>True if a usable frame-time column was found in the header.</summary>
    public bool CanProduceFrameTimes => _frameTimeIdx >= 0;

    public void SetHeader(string headerLine)
    {
        var cols = SplitCsv(headerLine);
        _columnCount = cols.Length;
        _appIdx = FindColumn(cols, AppNames);
        _pidIdx = FindColumn(cols, PidNames);
        _timeIdx = FindColumn(cols, TimeNames);
        _frameTimeIdx = FindColumn(cols, FrameTimeNames);
        _droppedIdx = FindColumn(cols, DroppedNames);
        HasHeader = true;
    }

    /// <summary>
    /// Parses one line. If the header hasn't been set yet and this line looks
    /// like a header (contains "Application"), it is consumed as the header and
    /// the method returns false (no sample produced).
    /// </summary>
    /// <param name="line">A raw CSV line.</param>
    /// <param name="sample">The parsed frame, when the method returns true.</param>
    /// <param name="application">The application/process name for this row, if available.</param>
    public bool TryParseLine(string line, out FrameSample? sample, out string? application)
    {
        sample = null;
        application = null;

        if (string.IsNullOrWhiteSpace(line))
            return false;

        if (!HasHeader)
        {
            if (LooksLikeHeader(line))
            {
                SetHeader(line);
                return false;
            }
            // No header yet and this isn't one — we can't reliably map columns.
            return false;
        }

        // A repeated header can appear if PresentMon restarts its session.
        if (LooksLikeHeader(line))
        {
            SetHeader(line);
            return false;
        }

        var cols = SplitCsv(line);
        if (cols.Length < _columnCount)
            return false;

        if (_appIdx >= 0 && _appIdx < cols.Length)
            application = cols[_appIdx];

        if (_frameTimeIdx < 0 || _frameTimeIdx >= cols.Length)
            return false;

        if (!TryParseDouble(cols[_frameTimeIdx], out double frameTime))
            return false;

        double time = 0;
        if (_timeIdx >= 0 && _timeIdx < cols.Length)
            TryParseDouble(cols[_timeIdx], out time);

        bool dropped = false;
        if (_droppedIdx >= 0 && _droppedIdx < cols.Length)
            dropped = ParseDropped(cols[_droppedIdx]);

        sample = new FrameSample
        {
            TimeInSeconds = time,
            FrameTimeMs = frameTime,
            Dropped = dropped,
        };
        return true;
    }

    private static bool LooksLikeHeader(string line)
        => line.StartsWith("Application", StringComparison.OrdinalIgnoreCase);

    private static int FindColumn(string[] cols, string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            for (int i = 0; i < cols.Length; i++)
            {
                if (string.Equals(cols[i].Trim(), candidate, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
        }
        return -1;
    }

    private static bool ParseDropped(string value)
    {
        value = value.Trim();
        if (value.Length == 0) return false;
        if (value == "1") return true;
        if (value == "0") return false;
        return bool.TryParse(value, out var b) && b;
    }

    private static bool TryParseDouble(string value, out double result)
        => double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result);

    /// <summary>
    /// Minimal CSV field splitter that honours double-quoted fields
    /// (PresentMon rarely quotes, but application names can contain commas).
    /// </summary>
    private static string[] SplitCsv(string line)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                    else inQuotes = false;
                }
                else current.Append(c);
            }
            else
            {
                if (c == '"') inQuotes = true;
                else if (c == ',') { result.Add(current.ToString()); current.Clear(); }
                else current.Append(c);
            }
        }
        result.Add(current.ToString());
        return result.ToArray();
    }
}
