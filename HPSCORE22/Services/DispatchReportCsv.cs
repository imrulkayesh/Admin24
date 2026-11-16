// Services/DispatchReportCsv.cs
// Download of a dispatch report (old btnExport -> dalCommon.ExportToExcel of the panel).
// CSV (UTF-8 with BOM, opens in Excel). Not EPPlus: the project's EPPlus 8 refuses the old LicenseContext setting
// (LicenseContextPropertyObsoleteException) and needs a licence choice first.
// A text that starts with = + - @ followed by a non digit gets a leading ' so Excel does not run it as a formula.
using AlphaCare.Models;
using System.Globalization;
using System.Text;

namespace AlphaCare.Services
{
    public static class DispatchReportCsv
    {
        public static byte[] Build(string title, List<DispatchReportColumn> columns, List<IDictionary<string, object?>> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine(Cell(title));
            sb.AppendLine();
            sb.AppendLine(string.Join(",", columns.Select(c => Cell(c.Header))));
            foreach (var row in rows)
            {
                sb.AppendLine(string.Join(",", columns.Select(c =>
                {
                    row.TryGetValue(c.Key, out var v);
                    return Cell(v switch
                    {
                        null => "",
                        DateTime dt => dt.ToString("dd-MMM-yyyy hh:mm tt", CultureInfo.InvariantCulture),
                        decimal or double or float or int or long or short => Convert.ToDecimal(v).ToString(CultureInfo.InvariantCulture),
                        _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? ""
                    });
                })));
            }
            var bom = Encoding.UTF8.GetPreamble();
            var body = Encoding.UTF8.GetBytes(sb.ToString());
            return bom.Concat(body).ToArray();
        }

        private static string Cell(string s)
        {
            if (s.Length > 1 && "=+-@".Contains(s[0]) && !char.IsDigit(s[1])) s = "'" + s;
            else if (s.Length == 1 && "=+-@".Contains(s[0])) s = "'" + s;
            bool quote = s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
            s = s.Replace("\"", "\"\"");
            return quote ? "\"" + s + "\"" : s;
        }
    }
}
