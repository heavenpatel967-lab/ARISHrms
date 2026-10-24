using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Data.Sqlite;
using Dapper;
using ARISHrms.Data;

namespace ARISHrms.Services
{
    /// <summary>
    /// How an absence was taken. Saved per employee per date inside ARIS HRMS.
    /// An absent day that has not been marked counts as INFORMED (the default).
    /// </summary>
    public class AbsenceMark
    {
        public int Id { get; set; }
        public string EmpCode { get; set; } = "";
        public DateTime AbsenceDate { get; set; }
        public bool Informed { get; set; } = true;
        public string Note { get; set; } = "";
        public DateTime UpdatedAt { get; set; }

        public string DateDisplay => AbsenceDate.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture);
        public string KindLabel => Informed ? "Informed (permission)" : "Without informing";
        public string Color => Informed ? "#27AE60" : "#E74C3C";
    }

    public class AbsenceSummary
    {
        public int Informed;
        public int Uninformed;
        public string Text => Informed + Uninformed == 0 ? "No leaves marked" : $"{Informed} informed / {Uninformed} without informing";
        /// <summary>Last 6 marked absences, oldest first: green = informed, red = without informing.</summary>
        public List<SummaryDot> Dots = new List<SummaryDot>();
    }

    public static class AbsenceStore
    {
        public static List<AbsenceMark> ForEmployee(string empCode)
        {
            using var c = new SqliteConnection(DatabaseInitializer.ConnectionString);
            c.Open();
            return c.Query<AbsenceMark>(
                "SELECT * FROM AbsenceMarks WHERE EmpCode = @k ORDER BY AbsenceDate, Id",
                new { k = EmployeeLookup.Key(empCode) }).ToList();
        }

        /// <summary>Mark (or re-mark) one date. One mark per employee per date.</summary>
        public static void Set(string empCode, DateTime date, bool informed, string note)
        {
            using var c = new SqliteConnection(DatabaseInitializer.ConnectionString);
            c.Open();
            c.Execute(
                @"INSERT INTO AbsenceMarks (EmpCode, AbsenceDate, Informed, Note, UpdatedAt)
                  VALUES (@k, @d, @i, @n, @now)
                  ON CONFLICT(EmpCode, AbsenceDate) DO UPDATE SET
                      Informed = excluded.Informed, Note = excluded.Note, UpdatedAt = excluded.UpdatedAt;",
                new { k = EmployeeLookup.Key(empCode), d = date.Date.ToString("yyyy-MM-dd"), i = informed ? 1 : 0, n = (note ?? "").Trim(), now = DateTime.Now });
        }

        public static void Delete(string empCode, DateTime date)
        {
            using var c = new SqliteConnection(DatabaseInitializer.ConnectionString);
            c.Open();
            c.Execute("DELETE FROM AbsenceMarks WHERE EmpCode = @k AND AbsenceDate = @d",
                new { k = EmployeeLookup.Key(empCode), d = date.Date.ToString("yyyy-MM-dd") });
        }
    }

    public static class AbsenceLogic
    {
        /// <summary>
        /// Puts the saved marks onto the attendance rows. Every "Absent" row gets a leave type:
        /// the saved mark, or Informed when nothing was saved. Other rows are untouched.
        /// </summary>
        public static void Apply(IEnumerable<HistoryRow> rows, IEnumerable<AbsenceMark> marks)
        {
            var byDate = marks.GroupBy(m => m.AbsenceDate.Date).ToDictionary(g => g.Key, g => g.Last());
            foreach (var r in rows)
            {
                if (r.Status != "Absent") { r.LeaveType = ""; r.Uninformed = false; continue; }
                bool informed = !byDate.TryGetValue(r.SortDate.Date, out var m) || m.Informed;
                r.Uninformed = !informed;
                r.LeaveNote = m?.Note ?? "";
                r.LeaveType = informed ? "Informed" : "Without informing";
            }
        }

        /// <summary>Totals for the 360 report and the track record - counts only what was saved.</summary>
        public static AbsenceSummary Summarize(IEnumerable<AbsenceMark> marks)
        {
            var list = marks.OrderBy(m => m.AbsenceDate).ThenBy(m => m.Id).ToList();
            var s = new AbsenceSummary
            {
                Informed = list.Count(m => m.Informed),
                Uninformed = list.Count(m => !m.Informed)
            };
            foreach (var m in list.Skip(Math.Max(0, list.Count - 6)))
                s.Dots.Add(new SummaryDot { Month = $"{m.DateDisplay}: {m.KindLabel}", Color = m.Color });
            return s;
        }
    }
}

namespace ARISHrms.Services
{
    /// <summary>One month of the leave summary: "Jul-2026  3 informed / 5 without informing".</summary>
    public class MonthLeave
    {
        public DateTime Month { get; set; }                   // first day of the month
        public int Informed { get; set; }
        public int Uninformed { get; set; }

        public string Label => Month.ToString("MMM-yyyy", CultureInfo.InvariantCulture);
        public int Total => Informed + Uninformed;
        public string Text => Total == 0 ? "no leave" : $"{Informed} informed / {Uninformed} without informing";
        public string InformedText => Informed.ToString();
        public string UninformedText => Uninformed.ToString();
        public string UninformedColor => Uninformed > 0 ? "#E74C3C" : "#94A3B8";
        public string InformedColor => Informed > 0 ? "#27AE60" : "#94A3B8";
    }

    public class MonthlyLeaveSummary
    {
        public List<MonthLeave> Months = new List<MonthLeave>();   // newest month first
        public bool FromAttendance;                                // true = absent days counted from MONDATA
        public string Source = "";
        public int Informed => Months.Sum(m => m.Informed);
        public int Uninformed => Months.Sum(m => m.Uninformed);
        public string Text => Informed + Uninformed == 0 ? "No leave in the last 6 months" : $"Last 6 months: {Informed} informed / {Uninformed} without informing";

        /// <summary>Same totals in the shape the track-record verdict uses.</summary>
        public AbsenceSummary AsSummary() => new AbsenceSummary { Informed = Informed, Uninformed = Uninformed };
    }

    public static class MonthlyLeave
    {
        /// <summary>
        /// Last 6 months (ending this month), newest first.
        /// Every absent day from the attendance file counts as INFORMED unless it was marked "without informing".
        /// Dates that were marked but are not absent in the attendance file (e.g. added by hand) count as marked.
        /// When the attendance file is not available, only the marked dates are counted.
        /// </summary>
        public static MonthlyLeaveSummary Build(IEnumerable<DateTime> absentDaysFromAttendance, IEnumerable<AbsenceMark> marks, DateTime today)
        {
            var first = new DateTime(today.Year, today.Month, 1).AddMonths(-5);
            var last = new DateTime(today.Year, today.Month, 1).AddMonths(1);       // exclusive

            var markByDate = marks.GroupBy(m => m.AbsenceDate.Date).ToDictionary(g => g.Key, g => g.OrderBy(x => x.Id).Last());

            // the set of leave days = absent days in the attendance file + every marked date
            var days = new HashSet<DateTime>();
            bool fromAtt = absentDaysFromAttendance != null;
            if (fromAtt) foreach (var d in absentDaysFromAttendance) days.Add(d.Date);
            foreach (var d in markByDate.Keys) days.Add(d);

            var summary = new MonthlyLeaveSummary { FromAttendance = fromAtt };
            var byMonth = new Dictionary<DateTime, MonthLeave>();
            for (int i = 0; i < 6; i++)
            {
                var m = first.AddMonths(i);
                byMonth[m] = new MonthLeave { Month = m };
            }

            foreach (var d in days)
            {
                if (d < first || d >= last) continue;
                var key = new DateTime(d.Year, d.Month, 1);
                bool informed = !markByDate.TryGetValue(d, out var mk) || mk.Informed;
                if (informed) byMonth[key].Informed++; else byMonth[key].Uninformed++;
            }

            summary.Months = byMonth.Values.OrderByDescending(m => m.Month).ToList();
            return summary;
        }
    }

    /// <summary>
    /// Absent days per employee from MONDATA.DBF, read once and kept until the file changes - so switching
    /// employees on the Loan / Advance / 360 screens does not re-read a large file every time.
    /// </summary>
    public static class AttendanceAbsences
    {
        private static string _path;
        private static DateTime _stamp;
        private static long _length;
        private static Dictionary<string, List<DateTime>> _byEmp;

        /// <summary>Absent days for the employee, or null when MONDATA is not available.</summary>
        public static List<DateTime> For(string empCode, out string source)
        {
            source = "";
            string path = DbfSource.GetMonDataPath();
            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path)) return null;

            try
            {
                var info = new System.IO.FileInfo(path);
                if (_byEmp == null || _path != path || _stamp != info.LastWriteTimeUtc || _length != info.Length)
                {
                    var map = new Dictionary<string, List<DateTime>>();
                    foreach (var r in AttendanceReportService.Read(path, (emp, date) => true))
                    {
                        if (!r.IsAbsent) continue;
                        // Wednesday week-off is not a leave (same rule as the history report)
                        if (r.Date.DayOfWeek == DayOfWeek.Wednesday && r.WeekOffDay.ToUpperInvariant().Contains("WED")) continue;
                        string key = EmployeeLookup.Key(r.EmpCode);
                        if (!map.TryGetValue(key, out var list)) map[key] = list = new List<DateTime>();
                        list.Add(r.Date.Date);
                    }
                    _byEmp = map; _path = path; _stamp = info.LastWriteTimeUtc; _length = info.Length;
                }

                source = System.IO.Path.GetFileName(path);
                return _byEmp.TryGetValue(EmployeeLookup.Key(empCode), out var days) ? days : new List<DateTime>();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Monthly informed / without-informing summary for one employee (last 6 months).</summary>
        public static MonthlyLeaveSummary Summary(string empCode, DateTime today)
        {
            var absent = For(empCode, out string source);
            var s = MonthlyLeave.Build(absent, AbsenceStore.ForEmployee(empCode), today);
            s.Source = absent == null ? "marked days only (MONDATA.DBF not found)" : $"absent days from {source}";
            return s;
        }
    }
}
