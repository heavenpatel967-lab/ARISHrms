using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Data.Sqlite;
using Dapper;
using ARISHrms.Data;
using ARISHrms.Models;

namespace ARISHrms.Services
{
    // =====================================================================================
    //  Monthly attendance summary for ONE employee, from the punches imported in
    //  Attendance & Punches (.ddd). Pure calculation first (tested), database access below.
    // =====================================================================================

    /// <summary>Shift start / end - the same times the Auto-Fill OUTs engine uses.</summary>
    public class ShiftTimes
    {
        public string Code = "";
        public TimeSpan Start;
        public TimeSpan End;
        public bool IsDefault;

        public string Label =>
            $"Shift {(string.IsNullOrEmpty(Code) ? "-" : Code)}{(IsDefault ? " (standard)" : "")}: {Fmt(Start)}–{Fmt(End)}";

        private static string Fmt(TimeSpan t) => $"{(int)t.TotalHours:00}:{t.Minutes:00}";

        public static ShiftTimes For(string shift)
        {
            string s = (shift ?? "").Trim().ToUpperInvariant();
            // Auto-Fill OUTs: end 17:00, shift B 19:00, shifts D and E 18:00; start 08:30, shift E 09:00
            int endHour = 17;
            if (s == "B") endHour = 19;
            else if (s == "D" || s == "E") endHour = 18;
            var start = s == "E" ? new TimeSpan(9, 0, 0) : new TimeSpan(8, 30, 0);
            return new ShiftTimes
            {
                Code = s,
                Start = start,
                End = new TimeSpan(endHour, 0, 0),
                IsDefault = s != "B" && s != "D" && s != "E"
            };
        }
    }

    public enum OtRounding { Exact = 0, Down15 = 1, Down30 = 2 }

    public class AttendanceDay
    {
        public DateTime Date { get; set; }
        public string Status { get; set; } = "";
        public string StatusColor { get; set; } = "#0F172A";
        public List<DateTime> Punches { get; set; } = new List<DateTime>();
        public int WorkedMinutes { get; set; }
        public int LateMinutes { get; set; }
        public int EarlyOutMinutes { get; set; }
        public int OtMinutes { get; set; }
        public bool Counted { get; set; }          // inside the imported data range and not in the future
        public bool IsPresent { get; set; }
        public bool IsWeekOffDay { get; set; }
        public bool MissingPunch { get; set; }
        public string Note { get; set; } = "";

        public string DateDisplay => Date.ToString("dd-MM-yyyy  ddd", CultureInfo.InvariantCulture);
        private string P(int i) => i < Punches.Count ? Punches[i].ToString("HH:mm", CultureInfo.InvariantCulture) : "";
        public string In => P(0);
        public string LOut => Punches.Count == 2 ? "" : P(1);
        public string LIn => Punches.Count == 2 ? "" : P(2);
        public string Out => Punches.Count == 2 ? P(1) : P(3);
        public string Extra => Punches.Count > 4 ? string.Join(" ", Punches.Skip(4).Select(p => p.ToString("HH:mm", CultureInfo.InvariantCulture))) : "";
        public string WorkedDisplay => IsPresent ? MonthlyAttendance.Hm(WorkedMinutes) : "";
        public string LateDisplay => LateMinutes > 0 ? $"{LateMinutes} min" : "";
        public string OtDisplay => OtMinutes > 0 ? MonthlyAttendance.Hm(OtMinutes) : "";
        public string EarlyDisplay => EarlyOutMinutes > 0 ? $"{EarlyOutMinutes} min" : "";
    }

    public class MonthlyAttendanceSummary
    {
        public DateTime Month;
        public ShiftTimes Shift;
        public DayOfWeek? WeekOff;
        public OtRounding Rounding;
        public DateTime? DataFrom, DataTo;           // imported punch range (all employees)
        public List<AttendanceDay> Days = new List<AttendanceDay>();

        public int CountedDays => Days.Count(d => d.Counted);
        public int Present => Days.Count(d => d.IsPresent);
        public int MissingPunchDays => Days.Count(d => d.MissingPunch);
        public int WeekOffs => Days.Count(d => d.Counted && d.IsWeekOffDay && !d.IsPresent);
        public int WorkedOnWeekOff => Days.Count(d => d.IsWeekOffDay && d.IsPresent);
        public int WorkedOnWeekOffMinutes => Days.Where(d => d.IsWeekOffDay && d.IsPresent).Sum(d => d.WorkedMinutes);
        public int Absent => Days.Count(d => d.Status == "Absent");
        public int AbsentUninformed => Days.Count(d => d.Status == "Absent" && d.Note == "Without informing");
        public int AbsentInformed => Absent - AbsentUninformed;
        public int LateDays => Days.Count(d => d.LateMinutes > 0);
        public int LateMinutes => Days.Sum(d => d.LateMinutes);
        public int EarlyOutDays => Days.Count(d => d.EarlyOutMinutes > 0);
        public int OtDays => Days.Count(d => d.OtMinutes > 0);
        public int OtMinutes => Days.Sum(d => d.OtMinutes);
        public int WorkedMinutes => Days.Sum(d => d.WorkedMinutes);
        public int NotImportedDays => Days.Count(d => d.Status == "No data");

        public string RuleText =>
            $"{Shift.Label}   ·   late = first punch after {MonthlyAttendance.Clock(Shift.Start.Add(TimeSpan.FromMinutes(MonthlyAttendance.LateGraceMinutes)))}" +
            $"   ·   overtime = last punch after {MonthlyAttendance.Clock(Shift.End)}" +
            (Rounding == OtRounding.Down15 ? " (rounded down to 15 min)" : Rounding == OtRounding.Down30 ? " (rounded down to 30 min)" : " (exact minutes)") +
            $"   ·   week off: {(WeekOff.HasValue ? WeekOff.Value.ToString() : "none")}";
    }

    public static class MonthlyAttendance
    {
        /// <summary>Punches within 5 minutes of each other are one swipe (same rule as Auto-Fill OUTs).</summary>
        public const int DuplicateMinutes = 5;
        /// <summary>08:30 start + 2 min grace = late after 08:32 (same rule as the Late report).</summary>
        public const int LateGraceMinutes = 2;

        public static string Hm(int minutes) => $"{minutes / 60}:{minutes % 60:00}";
        public static string Clock(TimeSpan t) => $"{(int)t.TotalHours:00}:{t.Minutes:00}";

        private static List<DateTime> CleanPunches(IEnumerable<DailyAttendance> rows)
        {
            var raw = new List<DateTime>();
            foreach (var r in rows)
                foreach (var t in new[] { r.PunchIn, r.LunchOut, r.LunchIn, r.PunchOut, r.Punch5, r.Punch6, r.Punch7, r.Punch8 })
                    if (t.HasValue) raw.Add(t.Value);
            raw.Sort();

            var p = new List<DateTime>();
            foreach (var t in raw)
                if (p.Count == 0 || (t - p[p.Count - 1]).TotalMinutes > DuplicateMinutes) p.Add(t);
            return p;
        }

        private static int Round(int minutes, OtRounding r) =>
            r == OtRounding.Down15 ? minutes - minutes % 15 : r == OtRounding.Down30 ? minutes - minutes % 30 : minutes;

        /// <param name="rows">this employee's DailyAttendance rows (any dates - only the month is used)</param>
        /// <param name="dataFrom">first imported date (any employee); days before it have no data</param>
        /// <param name="dataTo">last imported date (any employee); days after it have no data</param>
        public static MonthlyAttendanceSummary Build(DateTime month, IEnumerable<DailyAttendance> rows, ShiftTimes shift,
            DayOfWeek? weekOff, DateTime? dataFrom, DateTime? dataTo, DateTime today,
            IEnumerable<AbsenceMark> marks, OtRounding rounding)
        {
            var first = new DateTime(month.Year, month.Month, 1);
            var next = first.AddMonths(1);
            var byDate = rows.Where(r => r.WorkDate.Date >= first && r.WorkDate.Date < next)
                             .GroupBy(r => r.WorkDate.Date)
                             .ToDictionary(g => g.Key, g => CleanPunches(g));
            var markByDate = (marks ?? Enumerable.Empty<AbsenceMark>())
                             .GroupBy(m => m.AbsenceDate.Date).ToDictionary(g => g.Key, g => g.OrderBy(x => x.Id).Last());

            var s = new MonthlyAttendanceSummary { Month = first, Shift = shift, WeekOff = weekOff, Rounding = rounding, DataFrom = dataFrom, DataTo = dataTo };

            for (var d = first; d < next; d = d.AddDays(1))
            {
                var day = new AttendanceDay { Date = d, IsWeekOffDay = weekOff.HasValue && d.DayOfWeek == weekOff.Value };
                byDate.TryGetValue(d, out var p);
                p = p ?? new List<DateTime>();
                day.Punches = p;

                bool inData = dataFrom.HasValue && dataTo.HasValue && d >= dataFrom.Value.Date && d <= dataTo.Value.Date;

                if (p.Count > 0)
                {
                    day.Counted = true;
                    day.IsPresent = true;
                    day.MissingPunch = p.Count % 2 != 0;

                    // worked time = sum of complete in/out pairs (lunch between pairs is not counted)
                    for (int i = 0; i + 1 < p.Count; i += 2)
                        day.WorkedMinutes += (int)Math.Floor((p[i + 1] - p[i]).TotalMinutes);

                    // late: first punch is a morning "in" (before 11:00, same split as Auto-Fill) after start + grace
                    var firstPunch = p[0];
                    if (firstPunch.TimeOfDay < new TimeSpan(11, 0, 0))
                    {
                        int late = (int)Math.Floor((firstPunch.TimeOfDay - shift.Start).TotalMinutes);
                        if (late > LateGraceMinutes) day.LateMinutes = late;
                    }

                    // overtime / early out only when the day is complete (even number of punches)
                    if (!day.MissingPunch)
                    {
                        var last = p[p.Count - 1];
                        var endAt = d.Add(shift.End);
                        int diff = (int)Math.Floor((last - endAt).TotalMinutes);
                        if (diff > 0) day.OtMinutes = Round(diff, rounding);
                        else if (diff < 0) day.EarlyOutMinutes = -diff;
                    }

                    if (day.IsWeekOffDay) { day.Status = "Worked on week off"; day.StatusColor = "#0E7490"; }
                    else if (day.MissingPunch) { day.Status = "Present (missing punch)"; day.StatusColor = "#D97706"; }
                    else { day.Status = "Present"; day.StatusColor = "#16A34A"; }
                    if (day.MissingPunch && day.IsWeekOffDay) day.Note = "missing punch";
                }
                else if (d.Date > today.Date)
                {
                    day.Status = "—"; day.StatusColor = "#CBD5E1";
                }
                else if (!inData)
                {
                    day.Status = "No data"; day.StatusColor = "#94A3B8"; day.Note = "punches not imported for this date";
                }
                else if (day.IsWeekOffDay)
                {
                    day.Counted = true;
                    day.Status = "Week off"; day.StatusColor = "#7C3AED";
                }
                else
                {
                    day.Counted = true;
                    day.Status = "Absent"; day.StatusColor = "#DC2626";
                    bool informed = !markByDate.TryGetValue(d, out var mk) || mk.Informed;
                    day.Note = informed ? "Informed" : "Without informing";
                }

                s.Days.Add(day);
            }
            return s;
        }

        /// <summary>The same numbers as a printable report (every day of the month).</summary>
        public static Report ToReport(MonthlyAttendanceSummary s, string employee)
        {
            var r = new Report { Title = "MONTHLY ATTENDANCE SUMMARY", Subtitle = $"{employee}  -  {s.Month:MMMM yyyy}" };
            ReportCell C(string t, string color = null, bool bold = false) => new ReportCell(t, color, bold);

            r.Blocks.Add(new ReportHeading { Text = "SUMMARY" });
            r.Blocks.Add(new ReportFields
            {
                Items =
                {
                    ("Present days", C($"{s.Present}" + (s.MissingPunchDays > 0 ? $"   ({s.MissingPunchDays} with a missing punch)" : ""), "#16A34A", true)),
                    ("Week offs", C($"{s.WeekOffs}" + (s.WorkedOnWeekOff > 0 ? $"   (+ worked on {s.WorkedOnWeekOff} week off day(s), {Hm(s.WorkedOnWeekOffMinutes)} h)" : ""), "#7C3AED", true)),
                    ("Absent days", C($"{s.Absent}   (informed {s.AbsentInformed}, without informing {s.AbsentUninformed})", s.Absent > 0 ? "#DC2626" : null, true)),
                    ("Overtime", C($"{Hm(s.OtMinutes)} h on {s.OtDays} day(s)", null, true)),
                    ("Worked hours", C($"{Hm(s.WorkedMinutes)} h")),
                    ("Late", C($"{s.LateDays} day(s), {s.LateMinutes} min in total")),
                    ("Early out", C($"{s.EarlyOutDays} day(s)")),
                    ("Days counted", C($"{s.CountedDays}" + (s.NotImportedDays > 0 ? $"   ({s.NotImportedDays} day(s) without imported punches are not counted)" : "")))
                }
            });
            r.Blocks.Add(new ReportNote { Text = s.RuleText });

            var t = new ReportTable
            {
                Headers = { "Date", "Status", "In", "L-Out", "L-In", "Out", "Extra", "Worked", "Late", "OT", "Note" },
                Widths = { 1.7, 1.9, 0.7, 0.7, 0.7, 0.7, 1.1, 0.8, 0.8, 0.7, 1.4 },
                EmptyText = "No days."
            };
            foreach (var d in s.Days)
            {
                t.Rows.Add(new List<ReportCell>
                {
                    C(d.DateDisplay), C(d.Status, d.StatusColor, true), C(d.In), C(d.LOut), C(d.LIn), C(d.Out), C(d.Extra),
                    C(d.WorkedDisplay), C(d.LateDisplay, d.LateMinutes > 0 ? "#D97706" : null), C(d.OtDisplay, d.OtMinutes > 0 ? "#0E7490" : null, d.OtMinutes > 0),
                    C(d.Note, d.Note == "Without informing" ? "#DC2626" : null)
                });
                t.RowBackgrounds.Add(d.Status == "Absent" ? "#FEF2F2" : d.Status == "Week off" ? "#F5F3FF" : null);
            }
            r.Blocks.Add(new ReportHeading { Text = "DAY BY DAY" });
            r.Blocks.Add(t);
            return r;
        }
    }

    // =====================================================================================
    //  Database side
    // =====================================================================================

    public static class MonthlyAttendanceData
    {
        public static List<DailyAttendance> Load(string empCode, DateTime month)
        {
            var first = new DateTime(month.Year, month.Month, 1);
            using var c = new SqliteConnection(DatabaseInitializer.ConnectionString);
            c.Open();
            return c.Query<DailyAttendance>(
                @"SELECT * FROM DailyAttendance
                  WHERE EmpCode = @code AND date(WorkDate) >= date(@from) AND date(WorkDate) < date(@to)",
                new { code = EmployeeLookup.Key(empCode), from = first.ToString("yyyy-MM-dd"), to = first.AddMonths(1).ToString("yyyy-MM-dd") }).ToList();
        }

        /// <summary>First and last date that has imported punches (any employee).</summary>
        public static (DateTime? from, DateTime? to) DataRange()
        {
            using var c = new SqliteConnection(DatabaseInitializer.ConnectionString);
            c.Open();
            string a = c.QueryFirstOrDefault<string>("SELECT MIN(date(WorkDate)) FROM DailyAttendance");
            string b = c.QueryFirstOrDefault<string>("SELECT MAX(date(WorkDate)) FROM DailyAttendance");
            DateTime? P(string x) => DateTime.TryParseExact(x ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : (DateTime?)null;
            return (P(a), P(b));
        }

        /// <summary>Month of the employee's latest imported punch (so the window opens on real data).</summary>
        public static DateTime? LatestMonth(string empCode)
        {
            using var c = new SqliteConnection(DatabaseInitializer.ConnectionString);
            c.Open();
            string x = c.QueryFirstOrDefault<string>("SELECT MAX(date(WorkDate)) FROM DailyAttendance WHERE EmpCode = @code",
                new { code = EmployeeLookup.Key(empCode) });
            return DateTime.TryParseExact(x ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : (DateTime?)null;
        }

        /// <summary>Shift from Employee Master (the same value the Attendance grid shows).</summary>
        public static string ShiftOf(string empCode)
        {
            string key = EmployeeLookup.Key(empCode);
            var e = EmployeeLookup.LoadAll().FirstOrDefault(x => EmployeeLookup.Key(x.EmpCode) == key);
            return (e?.Shift ?? "").Trim();
        }

        /// <summary>Employees from Employee Master plus any code that only appears in imported punches.</summary>
        public static List<Employee> Employees()
        {
            var list = EmployeeLookup.LoadAll();
            var known = new HashSet<string>(list.Select(e => EmployeeLookup.Key(e.EmpCode)));
            using var c = new SqliteConnection(DatabaseInitializer.ConnectionString);
            c.Open();
            foreach (var code in c.Query<string>("SELECT DISTINCT EmpCode FROM DailyAttendance"))
                if (!string.IsNullOrWhiteSpace(code) && known.Add(EmployeeLookup.Key(code)))
                    list.Add(new Employee { EmpCode = code, FullName = "(not in Employee Master)" });
            return list.OrderBy(e => long.TryParse(e.EmpCode, out var n) ? n : long.MaxValue).ThenBy(e => e.EmpCode).ToList();
        }

        // ---------------- settings ----------------
        private static string Get(string key)
        {
            try
            {
                using var c = new SqliteConnection(DatabaseInitializer.ConnectionString);
                c.Open();
                return c.QueryFirstOrDefault<string>("SELECT SettingValue FROM AppSettings WHERE SettingKey = @k", new { k = key });
            }
            catch { return null; }
        }

        private static void Set(string key, string value)
        {
            try
            {
                using var c = new SqliteConnection(DatabaseInitializer.ConnectionString);
                c.Open();
                c.Execute("INSERT OR REPLACE INTO AppSettings (SettingKey, SettingValue) VALUES (@k, @v)", new { k = key, v = value });
            }
            catch { }
        }

        public static OtRounding GetRounding() => int.TryParse(Get("OtRounding"), out var r) && r >= 0 && r <= 2 ? (OtRounding)r : OtRounding.Exact;
        public static void SaveRounding(OtRounding r) => Set("OtRounding", ((int)r).ToString());

        public static DayOfWeek? ParseDay(string text)
        {
            string t = (text ?? "").Trim().ToUpperInvariant();
            if (t.Length < 3) return null;
            foreach (DayOfWeek d in Enum.GetValues(typeof(DayOfWeek)))
                if (d.ToString().ToUpperInvariant().StartsWith(t.Substring(0, 3))) return d;
            return null;
        }

        /// <summary>
        /// Week-off day for the employee: the choice saved in this window, else the most common OFF1 value in
        /// MONDATA.DBF, else Wednesday (the factory rule the old app used). source says which one.
        /// </summary>
        public static DayOfWeek? GetWeekOff(string empCode, out string source)
        {
            string key = EmployeeLookup.Key(empCode);
            string saved = Get("WeekOff_" + key);
            if (saved == "None") { source = "chosen here"; return null; }
            var savedDay = ParseDay(saved);
            if (savedDay.HasValue) { source = "chosen here"; return savedDay; }

            var fromMon = WeekOffFromMonData(key);
            if (fromMon.HasValue) { source = "from MONDATA.DBF"; return fromMon; }

            source = "standard";
            return DayOfWeek.Wednesday;
        }

        public static void SaveWeekOff(string empCode, DayOfWeek? day) =>
            Set("WeekOff_" + EmployeeLookup.Key(empCode), day.HasValue ? day.Value.ToString() : "None");

        private static readonly Dictionary<string, DayOfWeek?> _monCache = new Dictionary<string, DayOfWeek?>();
        private static string _monStamp = "";

        private static DayOfWeek? WeekOffFromMonData(string key)
        {
            try
            {
                string path = DbfSource.GetMonDataPath();
                if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path)) return null;
                var info = new System.IO.FileInfo(path);
                string stamp = path + "|" + info.LastWriteTimeUtc.Ticks + "|" + info.Length;
                if (stamp != _monStamp) { _monCache.Clear(); _monStamp = stamp; }
                if (_monCache.TryGetValue(key, out var cached)) return cached;

                var counts = new Dictionary<DayOfWeek, int>();
                foreach (var r in AttendanceReportService.Read(path, (emp, d) => EmployeeLookup.Key(emp) == key))
                {
                    var d = ParseDay(r.WeekOffDay);
                    if (d.HasValue) counts[d.Value] = counts.TryGetValue(d.Value, out var n) ? n + 1 : 1;
                }
                DayOfWeek? result = counts.Count == 0 ? (DayOfWeek?)null : counts.OrderByDescending(kv => kv.Value).First().Key;
                _monCache[key] = result;
                return result;
            }
            catch { return null; }
        }
    }
}
