using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Data.Sqlite;
using Dapper;
using ARISHrms.Data;

namespace ARISHrms.Services
{
    // =====================================================================================
    //  Advance "taken" ticks - kept inside ARIS HRMS. EasyPay's monsum.DBF only has one
    //  ADVANCE amount per month; this records WHEN the money was actually handed over
    //  (any day, including days outside the normal advance date).
    // =====================================================================================

    public class AdvanceTakenEntry
    {
        public int Id { get; set; }
        public string EmpCode { get; set; } = "";
        public DateTime TakenDate { get; set; }
        public string MonthKey { get; set; } = "";      // yyyyMM the tick belongs to (normally the month of TakenDate)
        public double? Amount { get; set; }               // optional - only for advances not in monsum
        public string Note { get; set; } = "";
        public DateTime CreatedAt { get; set; }

        public string DateDisplay => TakenDate.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture);
        public string MonthDisplay => MonthKey != null && MonthKey.Length == 6 ? $"{MonthKey.Substring(0, 4)}-{MonthKey.Substring(4, 2)}" : "";
        public string AmountDisplay => Amount.HasValue ? $"₹ {Amount.Value:N2}" : "(monsum amount)";
    }

    /// <summary>All village visits of one employee, oldest first - no limit.</summary>
    public static class VillageVisitStore
    {
        public static List<VillageVisit> ForEmployee(string empCode)
        {
            using var c = new SqliteConnection(DatabaseInitializer.ConnectionString);
            c.Open();
            return c.Query<VillageVisit>(
                "SELECT * FROM VillageVisits WHERE EmpCode = @code ORDER BY Id ASC", new { code = empCode }).ToList();
        }

        public static LeaveReliabilityResult Reliability(IEnumerable<VillageVisit> visits, DateTime today) =>
            LeaveReliability.Build(
                visits.Select(v => new LeaveInput
                {
                    Departure = v.DepartureDate,
                    ExpectedReturn = v.ExpectedReturn,
                    ActualReturn = v.ActualReturn,
                    LateDays = v.LateDays
                }).ToList(),
                today);
    }

    public static class AdvanceTakenStore
    {
        /// <summary>Month key used everywhere: yyyyMM (monsum YRMONTH format).</summary>
        public static string MonthKeyOf(DateTime d) => d.ToString("yyyyMM", CultureInfo.InvariantCulture);

        /// <summary>Turns monsum month labels such as 202607, 2026-07 or 07/2026 into yyyyMM; "" if unreadable.</summary>
        public static string NormalizeMonth(string label)
        {
            string digits = new string((label ?? "").Where(char.IsDigit).ToArray());
            if (digits.Length != 6) return "";
            int a = int.Parse(digits.Substring(0, 4)), m2 = int.Parse(digits.Substring(4, 2));
            if (a >= 1990 && a <= 2100 && m2 >= 1 && m2 <= 12) return digits;                      // yyyyMM
            int m1 = int.Parse(digits.Substring(0, 2)), y = int.Parse(digits.Substring(2, 4));
            if (y >= 1990 && y <= 2100 && m1 >= 1 && m1 <= 12) return $"{y:D4}{m1:D2}";             // MMyyyy
            return "";
        }

        public static List<AdvanceTakenEntry> ForEmployee(string empCode)
        {
            using var c = new SqliteConnection(DatabaseInitializer.ConnectionString);
            c.Open();
            return c.Query<AdvanceTakenEntry>(
                "SELECT * FROM AdvanceTaken WHERE EmpCode = @k ORDER BY TakenDate, Id",
                new { k = EmployeeLookup.Key(empCode) }).ToList();
        }

        public static int Add(string empCode, DateTime takenDate, string monthKey, double? amount, string note)
        {
            using var c = new SqliteConnection(DatabaseInitializer.ConnectionString);
            c.Open();
            return c.QuerySingle<int>(
                @"INSERT INTO AdvanceTaken (EmpCode, TakenDate, MonthKey, Amount, Note, CreatedAt)
                  VALUES (@k, @d, @m, @a, @n, @now); SELECT last_insert_rowid();",
                new
                {
                    k = EmployeeLookup.Key(empCode),
                    d = takenDate.Date,
                    m = string.IsNullOrEmpty(monthKey) ? MonthKeyOf(takenDate) : monthKey,
                    a = amount,
                    n = (note ?? "").Trim(),
                    now = DateTime.Now
                });
        }

        public static void UpdateDate(int id, DateTime takenDate)
        {
            using var c = new SqliteConnection(DatabaseInitializer.ConnectionString);
            c.Open();
            c.Execute("UPDATE AdvanceTaken SET TakenDate = @d WHERE Id = @id", new { d = takenDate.Date, id });
        }

        public static void Delete(int id)
        {
            using var c = new SqliteConnection(DatabaseInitializer.ConnectionString);
            c.Open();
            c.Execute("DELETE FROM AdvanceTaken WHERE Id = @id", new { id });
        }
    }

    // =====================================================================================
    //  Good / bad remarks with a date - shown when deciding on extra advances or loans
    // =====================================================================================

    public class EmployeeRemark
    {
        public int Id { get; set; }
        public string EmpCode { get; set; } = "";
        public DateTime RemarkDate { get; set; }
        public string Kind { get; set; } = "GOOD";        // GOOD or BAD
        public string Text { get; set; } = "";
        public DateTime CreatedAt { get; set; }

        public bool IsBad => string.Equals(Kind, "BAD", StringComparison.OrdinalIgnoreCase);
        public string KindLabel => IsBad ? "BAD" : "GOOD";
        public string Color => IsBad ? "#E74C3C" : "#27AE60";
        public string Background => IsBad ? "#FDECEA" : "#E9F7EF";
        public string DateDisplay => RemarkDate.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture);
    }

    public static class RemarkStore
    {
        public static List<EmployeeRemark> ForEmployee(string empCode)
        {
            using var c = new SqliteConnection(DatabaseInitializer.ConnectionString);
            c.Open();
            return c.Query<EmployeeRemark>(
                "SELECT * FROM EmployeeRemarks WHERE EmpCode = @k ORDER BY RemarkDate DESC, Id DESC",
                new { k = EmployeeLookup.Key(empCode) }).ToList();
        }

        public static int Add(string empCode, DateTime date, bool bad, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Please write the remark.");
            using var c = new SqliteConnection(DatabaseInitializer.ConnectionString);
            c.Open();
            return c.QuerySingle<int>(
                @"INSERT INTO EmployeeRemarks (EmpCode, RemarkDate, Kind, Text, CreatedAt)
                  VALUES (@k, @d, @kind, @t, @now); SELECT last_insert_rowid();",
                new { k = EmployeeLookup.Key(empCode), d = date.Date, kind = bad ? "BAD" : "GOOD", t = text.Trim(), now = DateTime.Now });
        }

        public static void Delete(int id)
        {
            using var c = new SqliteConnection(DatabaseInitializer.ConnectionString);
            c.Open();
            c.Execute("DELETE FROM EmployeeRemarks WHERE Id = @id", new { id });
        }
    }

    // =====================================================================================
    //  Pure logic (no database) - tested separately
    // =====================================================================================

    public class TrackRecordVerdict
    {
        public string Text = "";
        public string Color = "#64748B";
        public string Detail = "";
    }

    public static class TrackRecord
    {
        /// <summary>
        /// Last 6 months (ending with the current month) - one dot per month:
        /// green = an advance was ticked as taken that month, grey = nothing recorded.
        /// </summary>
        public static List<SummaryDot> AdvanceTakenDots(IEnumerable<AdvanceTakenEntry> entries, DateTime today)
        {
            var byMonth = entries.GroupBy(e => string.IsNullOrEmpty(e.MonthKey) ? AdvanceTakenStore.MonthKeyOf(e.TakenDate) : e.MonthKey)
                                 .ToDictionary(g => g.Key, g => g.OrderBy(x => x.TakenDate).ToList());
            var dots = new List<SummaryDot>();
            var first = new DateTime(today.Year, today.Month, 1).AddMonths(-5);

            for (int i = 0; i < 6; i++)
            {
                var m = first.AddMonths(i);
                string key = AdvanceTakenStore.MonthKeyOf(m);
                string label = m.ToString("MMM-yyyy", CultureInfo.InvariantCulture);

                if (byMonth.TryGetValue(key, out var list))
                {
                    string dates = string.Join(", ", list.Select(x => x.TakenDate.ToString("dd-MMM", CultureInfo.InvariantCulture)));
                    dots.Add(new SummaryDot { Month = $"{label}: taken on {dates}", Color = "#27AE60" });
                }
                else
                {
                    dots.Add(new SummaryDot { Month = $"{label}: no advance recorded", Color = "#CBD5E1" });
                }
            }
            return dots;
        }

        public static string AdvanceTakenText(IEnumerable<AdvanceTakenEntry> entries, DateTime today)
        {
            int taken = AdvanceTakenDots(entries, today).Count(d => d.Color == "#27AE60");
            return $"{taken} / 6 Months";
        }

        /// <summary>
        /// One-line overall verdict to help decide on extra advances or loans.
        /// Based only on facts already on screen: remarks (good / bad) and the village-leave record.
        /// It is a summary for the person deciding - it does not decide anything by itself.
        /// </summary>
        public static TrackRecordVerdict Verdict(IList<EmployeeRemark> remarks, LeaveReliabilityResult leave) =>
            Verdict(remarks, leave, null);

        /// <summary>Same, also weighing leaves taken without informing (from the attendance history).</summary>
        public static TrackRecordVerdict Verdict(IList<EmployeeRemark> remarks, LeaveReliabilityResult leave, AbsenceSummary absences)
        {
            var v = VerdictCore(remarks, leave);
            if (absences == null || absences.Informed + absences.Uninformed == 0) return v;

            v.Detail += $"   |   absences: {absences.Text}";
            if (v.Text == "No record yet") { v.Text = "✔ GOOD RECORD"; v.Color = "#27AE60"; }

            if (absences.Uninformed >= 3) { v.Text = "⚠ CAUTION"; v.Color = "#E74C3C"; }
            else if (absences.Uninformed > 0 && v.Text == "✔ GOOD RECORD") { v.Text = "MIXED"; v.Color = "#F39C12"; }
            return v;
        }

        private static TrackRecordVerdict VerdictCore(IList<EmployeeRemark> remarks, LeaveReliabilityResult leave)
        {
            int good = remarks.Count(r => !r.IsBad);
            int bad = remarks.Count(r => r.IsBad);

            int leaveTotal = leave?.Dots.Count ?? 0;
            int leaveRed = leave?.Dots.Count(d => d.Color == "#FF5252") ?? 0;         // more than 7 days late / not back
            int leaveYellow = leave?.Dots.Count(d => d.Color == "#F1C40F") ?? 0;      // a little late

            var v = new TrackRecordVerdict();
            var parts = new List<string> { $"{good} good / {bad} bad remark(s)" };
            if (leaveTotal > 0) parts.Add($"village leave: {leave.Text}");
            v.Detail = string.Join("   |   ", parts);

            var latest = remarks.OrderByDescending(r => r.RemarkDate).ThenByDescending(r => r.Id).FirstOrDefault();

            if (bad == 0 && good == 0 && leaveTotal == 0)
            {
                v.Text = "No record yet";
                v.Color = "#64748B";
            }
            else if (bad > good || leaveRed >= 2 || (latest != null && latest.IsBad && bad >= good))
            {
                v.Text = "⚠ CAUTION";
                v.Color = "#E74C3C";
            }
            else if (bad > 0 || leaveRed == 1 || leaveYellow > 0)
            {
                v.Text = "MIXED";
                v.Color = "#F39C12";
            }
            else
            {
                v.Text = "✔ GOOD RECORD";
                v.Color = "#27AE60";
            }
            return v;
        }
    }
}

namespace ARISHrms.Services
{
    /// <summary>One row of the Advance Ledger: a monsum month and/or a ticked "taken" record.</summary>
    public class AdvanceLedgerRow
    {
        public string MonthYear { get; set; } = "";           // label shown (monsum label, or yyyy-MM for added dates)
        public string MonthKey { get; set; } = "";            // yyyyMM, "" when the monsum label isn't a month
        public double? AdvanceAmount { get; set; }            // from monsum (or from the added record)
        public string StatusColor { get; set; } = "#CBD5E1";  // limit colour from monsum; grey for added-only rows
        public int? TakenId { get; set; }                     // AdvanceTaken.Id when ticked
        public DateTime? TakenDate { get; set; }
        public string Note { get; set; } = "";
        public bool FromMonsum { get; set; }

        public bool IsTaken => TakenId.HasValue;
        public bool CanEditDate => IsTaken;
        public string AmountDisplay => AdvanceAmount.HasValue ? $"₹ {AdvanceAmount.Value:N2}" : "-";
    }

    public static class AdvanceLedgerBuilder
    {
        /// <summary>
        /// Combines the monsum advance months with the ticks recorded in this app.
        /// - Each monsum month gets the FIRST tick of that month (the date column shows its date).
        /// - Further ticks in the same month, and ticks for months monsum doesn't have, become their own rows
        ///   (an advance on a non-regular date). Rows are ordered by month, then by date.
        /// </summary>
        public static List<AdvanceLedgerRow> Build(IEnumerable<AdvanceItem> monsumItems, IEnumerable<AdvanceTakenEntry> ticks)
        {
            var rows = new List<AdvanceLedgerRow>();
            var byMonth = ticks.GroupBy(t => string.IsNullOrEmpty(t.MonthKey) ? AdvanceTakenStore.MonthKeyOf(t.TakenDate) : t.MonthKey)
                               .ToDictionary(g => g.Key, g => new Queue<AdvanceTakenEntry>(g.OrderBy(x => x.TakenDate).ThenBy(x => x.Id)));

            foreach (var m in monsumItems)
            {
                string key = AdvanceTakenStore.NormalizeMonth(m.MonthYear);
                var row = new AdvanceLedgerRow
                {
                    MonthYear = m.MonthYear,
                    MonthKey = key,
                    AdvanceAmount = m.AdvanceAmount,
                    StatusColor = m.StatusColor,
                    FromMonsum = true
                };
                if (key.Length > 0 && byMonth.TryGetValue(key, out var q) && q.Count > 0)
                {
                    var t = q.Dequeue();
                    row.TakenId = t.Id; row.TakenDate = t.TakenDate; row.Note = t.Note ?? "";
                }
                rows.Add(row);
            }

            // ticks not attached to a monsum month: extra advances / months not in monsum yet
            foreach (var kv in byMonth)
            {
                while (kv.Value.Count > 0)
                {
                    var t = kv.Value.Dequeue();
                    rows.Add(new AdvanceLedgerRow
                    {
                        MonthYear = $"{kv.Key.Substring(0, 4)}-{kv.Key.Substring(4, 2)}",
                        MonthKey = kv.Key,
                        AdvanceAmount = t.Amount,
                        StatusColor = "#0EA5E9",              // blue = recorded in this app only
                        TakenId = t.Id,
                        TakenDate = t.TakenDate,
                        Note = string.IsNullOrWhiteSpace(t.Note) ? "Extra advance (added here)" : t.Note
                    });
                }
            }

            return rows
                .OrderBy(r => r.MonthKey.Length == 0 ? "" : r.MonthKey, StringComparer.Ordinal)
                .ThenBy(r => r.FromMonsum ? 0 : 1)
                .ThenBy(r => r.TakenDate ?? DateTime.MinValue)
                .ToList();
        }
    }
}
