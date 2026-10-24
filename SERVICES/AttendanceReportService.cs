using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ARISHrms.Services
{
    /// <summary>One employee-day from MONDATA.DBF (the daily attendance file written by EasyPay).</summary>
    public class DayRecord
    {
        public string EmpCode = "";
        public DateTime Date;
        public double InTime;        // IN1, written as hours.minutes e.g. 8.45 = 08:45
        public double OutTime;       // OUT1
        public bool HasOutColumn;
        public bool IsAbsent;        // ABS >= 1
        public string WeekOffDay = "";   // OFF1
    }

    public class DailyReportRow
    {
        public string EmployeeCode { get; set; } = "";
        public string CardNumber { get; set; } = "";
        public string EmployeeName { get; set; } = "";
        public string Department { get; set; } = "";
        public string PunchTimeStr { get; set; } = "";
        public string Status { get; set; } = "";
        public double PunchValue { get; set; }
    }

    public class HistoryRow
    {
        public string DateStr { get; set; } = "";
        public DateTime SortDate { get; set; }
        public string InTime { get; set; } = "";
        public string OutTime { get; set; } = "";
        public string Status { get; set; } = "";
        public string StatusColor { get; set; } = "";
    }

    /// <summary>
    /// Late / Absent reports and per-employee history from MONDATA.DBF, using the same rules as the old
    /// ARIS Payroll Companion.
    /// </summary>
    public static class AttendanceReportService
    {
        /// <summary>A punch after 08:32 counts as late.</summary>
        public const double LateAfter = 8.32;

        private static readonly string[] DateFormats =
            { "yyyyMMdd", "dd-MM-yyyy", "MM/dd/yyyy", "yyyy-MM-dd", "dd/MM/yyyy" };

        private static readonly string[] Columns = { "EMPCD", "DATE", "IN1", "OUT1", "ABS", "OFF1" };

        public static bool TryParseDate(string raw, out DateTime date) =>
            DateTime.TryParseExact((raw ?? "").Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

        private static double D(string s) =>
            double.TryParse((s ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;

        /// <summary>
        /// Streams MONDATA and returns the records that pass the filter. Throws if the file has no EMPCD/DATE column.
        /// </summary>
        public static List<DayRecord> Read(string path, Func<string, DateTime, bool> keep)
        {
            var result = new List<DayRecord>();
            bool hasOut = false;

            var fields = DbfReader.ForEachRow(path, Columns, r =>
            {
                string rawDate = r.TryGetValue("DATE", out var d) ? d : "";
                if (!TryParseDate(rawDate, out var date)) return true;

                string emp = r.TryGetValue("EMPCD", out var e) ? e.Trim() : "";
                if (!keep(emp, date)) return true;

                result.Add(new DayRecord
                {
                    EmpCode = emp,
                    Date = date,
                    InTime = D(r.TryGetValue("IN1", out var i) ? i : ""),
                    OutTime = D(r.TryGetValue("OUT1", out var o) ? o : ""),
                    IsAbsent = D(r.TryGetValue("ABS", out var a) ? a : "") >= 1.0,
                    WeekOffDay = (r.TryGetValue("OFF1", out var w) ? w : "").Trim()
                });
                return true;
            });

            bool Has(string n) => fields.Any(f => string.Equals(f.Name, n, StringComparison.OrdinalIgnoreCase));
            if (!Has("EMPCD") || !Has("DATE"))
                throw new InvalidDataException("This does not look like MONDATA.DBF - it has no EMPCD / DATE columns.");

            hasOut = Has("OUT1");
            foreach (var rec in result) rec.HasOutColumn = hasOut;
            return result;
        }

        // ---------------- daily Late / Absent ----------------
        public static (List<DayRecord> late, List<DayRecord> absent) BuildDaily(string path, DateTime day)
        {
            var records = Read(path, (emp, date) => date.Date == day.Date);
            bool isWednesday = day.DayOfWeek == DayOfWeek.Wednesday;

            var late = records.Where(r => r.InTime > LateAfter && !r.IsAbsent).ToList();

            var absent = new List<DayRecord>();
            foreach (var r in records.Where(x => x.IsAbsent))
            {
                // Wednesday week-off is not an absence
                if (isWednesday && r.WeekOffDay.ToUpperInvariant().Contains("WED")) continue;
                absent.Add(r);
            }
            return (late, absent);
        }

        // ---------------- employee history ----------------
        public static List<HistoryRow> BuildHistory(string path, string empCode, DateTime start, DateTime end)
        {
            string e1 = (empCode ?? "").Trim();
            string e2 = e1.PadLeft(4, '0');

            var records = Read(path, (emp, date) =>
                (emp == e1 || emp == e2) && date.Date >= start.Date && date.Date <= end.Date);

            var rows = new List<HistoryRow>();
            foreach (var r in records)
            {
                string status = "Present", color = "#27AE60";

                if (r.IsAbsent)
                {
                    if (r.Date.DayOfWeek == DayOfWeek.Wednesday && r.WeekOffDay.ToUpperInvariant().Contains("WED"))
                    { status = "Week Off"; color = "#8E44AD"; }
                    else
                    { status = "Absent"; color = "#E74C3C"; }
                }
                else if (r.InTime > LateAfter)
                { status = "Late"; color = "#F39C12"; }

                rows.Add(new HistoryRow
                {
                    DateStr = r.Date.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture),
                    SortDate = r.Date,
                    InTime = r.InTime > 0 ? r.InTime.ToString("0.00", CultureInfo.InvariantCulture) : "-",
                    OutTime = r.OutTime > 0 ? r.OutTime.ToString("0.00", CultureInfo.InvariantCulture) : "-",
                    Status = status,
                    StatusColor = color
                });
            }
            return rows.OrderBy(x => x.SortDate).ToList();
        }
    }

    /// <summary>Department / category code to name tables, taken from the old Payroll Companion.</summary>
    public static class LookupNames
    {
        public static string Department(string code)
        {
            string c = code?.Trim().TrimStart('0');
            switch (c)
            {
                case "1": return "OFFICE";
                case "2": return "CASTING";
                case "3": return "MACHINE";
                case "4": return "BUFF & AMRI";
                case "5": return "CHROM";
                case "6": return "FITTING / OTHER";
                case "7": return "OTHER NEW";
                default: return code;
            }
        }

        public static string Category(string code)
        {
            string c = code?.Trim().TrimStart('0');
            switch (c)
            {
                case "1": return "STAFF";
                case "2": return "CASTING_10";
                case "3": return "WORKER";
                case "4": return "MONTHLY_8";
                case "5": return "GENERAL / PIC RATE";
                case "6": return "GENERAL ODD";
                case "7": return "MONTHLY_10";
                case "8": return "TRANSPORT";
                case "10": return "PLUMBER";
                case "11": return "PIC RATE GENERAL";
                case "12": return "WORKER_9";
                default: return code;
            }
        }
    }
}
