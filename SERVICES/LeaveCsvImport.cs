using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Data.Sqlite;
using Dapper;
using ARISHrms.Data;

namespace ARISHrms.Services
{
    /// <summary>One village visit read from the CSV.</summary>
    public class LeaveCsvRow
    {
        public int LineNumber;                 // 1-based line in the file
        public string EmpCode = "";
        public DateTime Departure;
        public DateTime ExpectedReturn;
        public DateTime? ActualReturn;
        public int LateDays;
        public string Notes = "";
    }

    public class LeaveCsvResult
    {
        public List<LeaveCsvRow> Rows = new List<LeaveCsvRow>();
        public int Skipped;                    // rows with a card number but a blank/unreadable departure or return date
        public List<string> SkippedLines = new List<string>();   // "line 12: card 45 - departure '31-02-2024' not a date"
        public string ColumnInfo = "";

        // filled by the importer
        public int Imported;
        public int Duplicates;                 // already in the database - not added again
        public List<string> UnknownEmployees = new List<string>();   // imported, but the code is not in Employee Master
    }

    /// <summary>
    /// Imports old village-visit / long-leave data from a CSV, using the same rules as the old ARIS Payroll
    /// Companion's "Import Legacy" button. Header row (within the first 10 lines) is found by CARD / DEPARTURE;
    /// columns are mapped by name, otherwise: card, departure, expected return, actual return, notes.
    /// </summary>
    public static class LeaveCsvImport
    {
        private static readonly string[] DateFormats =
        {
            "dd-MM-yyyy", "d-M-yyyy", "dd/MM/yyyy", "M/d/yyyy",
            "dd-MM-yy", "d-M-yy", "yyyy-MM-dd"
        };

        public static DateTime? ParseDate(string d)
        {
            if (string.IsNullOrWhiteSpace(d)) return null;
            return DateTime.TryParseExact(d.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
                ? dt : (DateTime?)null;
        }

        /// <summary>
        /// Splits a CSV line. Plain commas work exactly like the old app; in addition a value in double quotes may
        /// contain commas (Excel writes notes such as "back late, called" that way), which the old app split wrongly.
        /// </summary>
        public static string[] SplitLine(string line)
        {
            if (line.IndexOf('"') < 0) return line.Split(',');

            var cols = new List<string>();
            var cur = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                    else if (c == '"') quoted = false;
                    else cur.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { cols.Add(cur.ToString()); cur.Clear(); }
                else cur.Append(c);
            }
            cols.Add(cur.ToString());
            return cols.ToArray();
        }

        public static LeaveCsvResult Parse(string[] lines, DateTime today)
        {
            var result = new LeaveCsvResult();
            int cardIdx = 0, depIdx = 1, expIdx = 2, actIdx = 3, noteIdx = 4;
            int startRow = 1;
            bool headerFound = false;

            // Header scanner - same order of checks as the old app ("ACTUAL" before "RETURN")
            for (int i = 0; i < Math.Min(10, lines.Length); i++)
            {
                string upper = lines[i].ToUpperInvariant();
                if (upper.Contains("CARD") || upper.Contains("DEPARTURE"))
                {
                    var headers = SplitLine(lines[i]);
                    for (int c = 0; c < headers.Length; c++)
                    {
                        string h = headers[c].ToUpperInvariant().Trim();
                        if (h.Contains("CARD")) cardIdx = c;
                        else if (h.Contains("DEPARTURE")) depIdx = c;
                        else if (h.Contains("ACTUAL") || h == "DAYS" || h == "DAY") actIdx = c;
                        else if (h.Contains("EXPECTED") || h.Contains("RETURN")) expIdx = c;
                        else if (h.Contains("REMARK") || h.Contains("NOTE")) noteIdx = c;
                    }
                    startRow = i + 1;
                    headerFound = true;
                    break;
                }
            }

            result.ColumnInfo = headerFound
                ? $"Header found on line {startRow}: card = column {cardIdx + 1}, departure = {depIdx + 1}, expected return = {expIdx + 1}, actual return = {actIdx + 1}, notes = {noteIdx + 1}"
                : "No header row found - used columns: card, departure, expected return, actual return, notes (starting on line 2)";

            for (int i = startRow; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;

                string[] cols = SplitLine(line);
                if (cols.Length <= Math.Max(cardIdx, Math.Max(depIdx, expIdx))) continue;   // too few columns

                string rawCard = cols[cardIdx].Trim();
                if (string.IsNullOrWhiteSpace(rawCard) || !int.TryParse(rawCard, out _)) continue;   // titles / totals / blank rows

                string empCode = rawCard.PadLeft(4, '0');

                DateTime? dep = ParseDate(cols[depIdx]);
                DateTime? exp = ParseDate(cols[expIdx]);
                DateTime? act = null;
                if (cols.Length > actIdx && actIdx != expIdx) act = ParseDate(cols[actIdx]);
                string notes = cols.Length > noteIdx ? cols[noteIdx].Trim() : "";

                if (!dep.HasValue || !exp.HasValue)
                {
                    result.Skipped++;
                    string why = !dep.HasValue
                        ? $"departure '{cols[depIdx].Trim()}'"
                        : $"expected return '{cols[expIdx].Trim()}'";
                    result.SkippedLines.Add($"line {i + 1}: card {rawCard} - {why} is blank or not a date");
                    continue;
                }

                int lateDays = 0;
                if (act.HasValue)
                {
                    if ((act.Value - exp.Value).Days > 0) lateDays = (act.Value - exp.Value).Days;
                }
                else if (exp.Value < today)
                {
                    lateDays = (today - exp.Value).Days;
                }

                result.Rows.Add(new LeaveCsvRow
                {
                    LineNumber = i + 1,
                    EmpCode = empCode,
                    Departure = dep.Value,
                    ExpectedReturn = exp.Value,
                    ActualReturn = act,
                    LateDays = lateDays,
                    Notes = notes
                });
            }

            return result;
        }

        /// <summary>Reads the file even while Excel has it open.</summary>
        public static string[] ReadLines(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var lines = new List<string>();
            string l;
            while ((l = sr.ReadLine()) != null) lines.Add(l);
            return lines.ToArray();
        }

        /// <summary>
        /// Saves the parsed rows. A row that is already in the database (same employee, departure and expected
        /// return) is not added again, so importing the same file twice does not double the history.
        /// </summary>
        public static void Save(LeaveCsvResult parsed)
        {
            using var c = new SqliteConnection(DatabaseInitializer.ConnectionString);
            c.Open();
            using var t = c.BeginTransaction();

            var known = new HashSet<string>(c.Query<string>("SELECT EmpCode FROM Employees", transaction: t));

            foreach (var r in parsed.Rows)
            {
                bool exists = c.QuerySingle<int>(
                    @"SELECT COUNT(*) FROM VillageVisits
                      WHERE EmpCode = @code AND date(DepartureDate) = date(@dep) AND date(ExpectedReturn) = date(@exp)",
                    new { code = r.EmpCode, dep = r.Departure.ToString("yyyy-MM-dd"), exp = r.ExpectedReturn.ToString("yyyy-MM-dd") }, t) > 0;

                if (exists) { parsed.Duplicates++; continue; }

                c.Execute(
                    @"INSERT INTO VillageVisits (EmpCode, DepartureDate, ExpectedReturn, ActualReturn, LateDays, LatePenalty, ReasonNotes, ApprovedBy, Status)
                      VALUES (@code, @dep, @exp, @act, @late, 0, @notes, 'CSV import', @status)",
                    new
                    {
                        code = r.EmpCode,
                        dep = r.Departure.ToString("yyyy-MM-dd"),
                        exp = r.ExpectedReturn.ToString("yyyy-MM-dd"),
                        act = r.ActualReturn?.ToString("yyyy-MM-dd"),
                        late = r.LateDays,
                        notes = r.Notes,
                        status = r.ActualReturn.HasValue ? "Returned" : "On Leave"
                    }, t);
                parsed.Imported++;

                if (!known.Contains(r.EmpCode) && !parsed.UnknownEmployees.Contains(r.EmpCode))
                    parsed.UnknownEmployees.Add(r.EmpCode);
            }

            t.Commit();
        }
    }
}
