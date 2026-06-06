using ClosedXML.Excel;
using RigMetrics.Core.Export;
using RigMetrics.Core.Models;

namespace RigMetrics.App.Services;

/// <summary>
/// Writes the results table to a real Excel .xlsx workbook (opens directly in
/// Excel or Google Sheets) using the shared <see cref="ResultsExporter.Columns"/>.
/// </summary>
public static class ResultsXlsxExporter
{
    public static void Export(IEnumerable<SessionSummaryRow> rows, string path)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Results");

        var columns = ResultsExporter.Columns;

        // Header row.
        for (int c = 0; c < columns.Count; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = columns[c].Header;
            cell.Style.Font.Bold = true;
        }

        // Data rows.
        int r = 2;
        foreach (var row in rows)
        {
            for (int c = 0; c < columns.Count; c++)
            {
                var column = columns[c];
                var value = ResultsExporter.GetValue(row, column);
                var cell = ws.Cell(r, c + 1);

                switch (value)
                {
                    case null:
                        break;
                    case double d:
                        cell.Value = d;
                        if (column.Format is not null) cell.Style.NumberFormat.Format = column.Format;
                        break;
                    case int i:
                        cell.Value = i;
                        break;
                    default:
                        cell.Value = value.ToString();
                        break;
                }
            }
            r++;
        }

        ws.SheetView.FreezeRows(1);
        try
        {
            // Cosmetic only; skip if font metrics aren't available.
            ws.Columns().AdjustToContents();
        }
        catch
        {
            // Leave default column widths.
        }
        workbook.SaveAs(path);
    }
}
