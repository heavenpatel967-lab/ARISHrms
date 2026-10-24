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
