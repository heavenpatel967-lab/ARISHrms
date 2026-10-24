using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ARISHrms.Services
{
    /// <summary>One employee-month row of EasyPay's monsum.DBF (only the columns the reports need).</summary>
    public class MonsumRow
    {
        public string EmpCode = "";
        public string CardNo = "";
        public string Month = "";        // YRMONTH, or PAY_MONTH when YRMONTH is blank
        public string YrMonthRaw = "";   // YRMONTH exactly as stored (the old dashboard orders by this only)
        public double EmiDeducted;       // LOAN_DED
        public double PaidLoan;          // PAID_LOAN  (total loan given, cumulative)
        public double LoanRecovered;     // LOAN_REC   (total recovered, cumulative)
        public double Advance;           // ADVANCE
    }

    public class LoanLedgerItem
    {
        public string MonthYear { get; set; } = "";
        public double? PendingLoanAmount { get; set; }
        public double? EmiDeducted { get; set; }
    }

    public class HealthDot
    {
        public string Month { get; set; } = "";
        public bool Good { get; set; }
    }

    public class LoanSummary
    {
        public List<LoanLedgerItem> Ledger { get; } = new List<LoanLedgerItem>();
        public List<HealthDot> Last6 { get; } = new List<HealthDot>();
        public double TotalApproved;        // latest PAID_LOAN
        public double TotalRecovered;       // latest LOAN_REC
        public double Outstanding;          // approved - recovered
        public double TotalEmiDeducted;     // sum of LOAN_DED over the months shown
        public int MonthsTotal, MonthsPaid;
    }

    public class AdvanceItem
    {
        public string MonthYear { get; set; } = "";
        public double AdvanceAmount { get; set; }
        public string StatusColor { get; set; } = "#27AE60";
    }

    public class AdvanceSummary
    {
        public List<AdvanceItem> Items { get; } = new List<AdvanceItem>();
        public double Total;
        public int SafeCount;
        public double Limit;
    }

    public class FactoryTotals
    {
        public int Headcount;                 // distinct EMPCD in monsum
        public int EmployeesWithLoans;        // latest outstanding > 0
        public double PendingLoans;           // sum of latest (PAID_LOAN - LOAN_REC) where > 0
        public double TotalAdvances;          // sum of ADVANCE over the whole file
        public string LatestMonth = "";
    }

    /// <summary>
    /// Reads loans and advances from EasyPay's monsum.DBF exactly like the old ARIS Payroll Companion:
    /// nothing is typed in by hand, the payroll software's own monthly output is the source of truth.
    /// </summary>
    public class MonsumData
    {
        public string FilePath { get; }
        public List<MonsumRow> Rows { get; }
        private readonly Dictionary<long, List<MonsumRow>> _byCode = new Dictionary<long, List<MonsumRow>>();
        private readonly Dictionary<long, List<MonsumRow>> _byCard = new Dictionary<long, List<MonsumRow>>();

        private static readonly string[] Columns =
            { "EMPCD", "CARDNO", "YRMONTH", "PAY_MONTH", "LOAN_DED", "PAID_LOAN", "LOAN_REC", "ADVANCE" };

        // ---------------- loading (cached until the file changes) ----------------
        private static MonsumData _cache;
        private static DateTime _cacheStamp;
        private static long _cacheLength;

        public static MonsumData Load(string path)
        {
            var info = new FileInfo(path);
            if (_cache != null && _cache.FilePath == path && _cacheStamp == info.LastWriteTimeUtc && _cacheLength == info.Length)
                return _cache;

            var rows = new List<MonsumRow>();
            DbfReader.ForEachRow(path, Columns, r =>
            {
                string Get(string k) => r.TryGetValue(k, out var v) ? v : "";

                string month = Get("YRMONTH").Trim();
                if (month.Length == 0) month = Get("PAY_MONTH").Trim();

                rows.Add(new MonsumRow
                {
                    EmpCode = Get("EMPCD").Trim(),
                    CardNo = Get("CARDNO").Trim(),
                    Month = month,
                    YrMonthRaw = Get("YRMONTH").Trim(),
                    EmiDeducted = ParseAmount(Get("LOAN_DED")),
                    PaidLoan = ParseAmount(Get("PAID_LOAN")),
                    LoanRecovered = ParseAmount(Get("LOAN_REC")),
                    Advance = ParseAmount(Get("ADVANCE"))
                });
                return true;
            });

            _cache = new MonsumData(path, rows);
            _cacheStamp = info.LastWriteTimeUtc;
            _cacheLength = info.Length;
            return _cache;
        }

        public MonsumData(string path, List<MonsumRow> rows)
        {
            FilePath = path;
            Rows = rows;
            foreach (var r in rows)
            {
                AddIndex(_byCode, r.EmpCode, r);   // employee code is only compared with EMPCD
                AddIndex(_byCard, r.CardNo, r);    // card number is only compared with CARDNO
            }
        }

        private static void AddIndex(Dictionary<long, List<MonsumRow>> index, string code, MonsumRow row)
        {
            long? n = DigitsToNumber(code);
            if (!n.HasValue) return;      // old app: a code without digits never matches anything
            if (!index.TryGetValue(n.Value, out var l)) index[n.Value] = l = new List<MonsumRow>();
            l.Add(row);
        }

        // ---------------- helpers (same behaviour as the old app) ----------------
        public static double ParseAmount(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            s = s.Trim().Replace(",", "");
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
        }

        /// <summary>Old app's CodesMatch: compare only the digits, as numbers ("0007" == "7").</summary>
        private static long? DigitsToNumber(string s)
        {
            string digits = new string((s ?? "").Where(char.IsDigit).ToArray());
            if (digits.Length == 0) return null;
            return long.TryParse(digits, out var n) ? n : (long?)null;
        }

        public static bool CodesMatch(string a, string b)
        {
            long? na = DigitsToNumber(a), nb = DigitsToNumber(b);
            if (!na.HasValue || !nb.HasValue) return false;
            return na.Value == nb.Value;
        }

        /// <summary>All monsum rows for one employee, matched by code or (when given) card number.</summary>
        public List<MonsumRow> RowsFor(string empCode, string cardNo)
        {
            var seen = new HashSet<MonsumRow>();

            void Collect(Dictionary<long, List<MonsumRow>> index, string code)
            {
                long? n = DigitsToNumber(code);
                if (n.HasValue && index.TryGetValue(n.Value, out var l))
                    foreach (var r in l) seen.Add(r);
            }

            Collect(_byCode, empCode);
            Collect(_byCard, cardNo);

            // keep file order, like the old app's row loop
            return seen.Count == 0 ? new List<MonsumRow>() : Rows.Where(seen.Contains).ToList();
        }

        /// <summary>One row per month (the last row in the file wins), oldest month first.</summary>
        private static List<MonsumRow> ByMonth(IEnumerable<MonsumRow> rows) =>
            rows.Where(r => !string.IsNullOrEmpty(r.Month))
                .GroupBy(r => r.Month)
                .Select(g => g.Last())
                .OrderBy(r => r.Month, StringComparer.Ordinal)
                .ToList();

        // ---------------- loans (LoanReportWindow rules) ----------------
        public LoanSummary BuildLoanSummary(string empCode, string cardNo)
        {
            var months = ByMonth(RowsFor(empCode, cardNo));
            var sum = new LoanSummary();

            foreach (var m in months)
            {
                bool hasPending = m.PaidLoan > 0 || m.LoanRecovered > 0;
                bool paidEmi = m.EmiDeducted > 0;
                if (!hasPending && !paidEmi) continue;

                sum.MonthsTotal++;
                if (paidEmi) { sum.MonthsPaid++; sum.TotalEmiDeducted += m.EmiDeducted; }

                sum.Ledger.Add(new LoanLedgerItem
                {
                    MonthYear = m.Month,
                    PendingLoanAmount = hasPending ? m.PaidLoan - m.LoanRecovered : (double?)null,
                    EmiDeducted = paidEmi ? m.EmiDeducted : (double?)null
                });
            }

            foreach (var m in months.Where(x => x.PaidLoan > 0 || x.LoanRecovered > 0 || x.EmiDeducted > 0).TakeLast(6))
                sum.Last6.Add(new HealthDot { Month = m.Month, Good = m.EmiDeducted > 0 });

            var last = months.LastOrDefault(x => x.PaidLoan > 0 || x.LoanRecovered > 0);
            if (last != null)
            {
                sum.TotalApproved = last.PaidLoan;
                sum.TotalRecovered = last.LoanRecovered;
            }
            sum.Outstanding = sum.TotalApproved - sum.TotalRecovered;
            return sum;
        }

        // ---------------- advances (AdvanceReportWindow rules) ----------------
        public AdvanceSummary BuildAdvanceSummary(string empCode, string cardNo, double limit)
        {
            // old app: Distinct() on (month, amount), ordered by month, zero amounts are skipped
            var distinct = RowsFor(empCode, cardNo)
                .Select(r => (month: r.Month, amt: r.Advance))
                .Distinct()
                .OrderBy(x => x.month, StringComparer.Ordinal)
                .ToList();

            var sum = new AdvanceSummary { Limit = limit };
            foreach (var m in distinct)
            {
                if (m.amt == 0) continue;
                sum.Total += m.amt;
                bool high = limit > 0 && m.amt > limit;
                if (!high) sum.SafeCount++;
                sum.Items.Add(new AdvanceItem
                {
                    MonthYear = string.IsNullOrEmpty(m.month) ? "Advance Taken" : m.month,
                    AdvanceAmount = m.amt,
                    StatusColor = high ? "#E74C3C" : "#27AE60"
                });
            }
            return sum;
        }

        // ---------------- factory-wide numbers (DashboardWindow rules) ----------------
        public FactoryTotals BuildFactoryTotals()
        {
            var t = new FactoryTotals();

            t.Headcount = Rows.Select(r => r.EmpCode).Where(c => c.Length > 0).Distinct().Count();
            t.TotalAdvances = Rows.Sum(r => r.Advance);
            t.LatestMonth = Rows.Select(r => r.Month).Where(m => m.Length > 0).OrderBy(m => m, StringComparer.Ordinal).LastOrDefault() ?? "";

            // latest month's row per employee
            foreach (var g in Rows.Where(r => r.EmpCode.Length > 0).GroupBy(r => r.EmpCode))
            {
                var latest = g.OrderBy(r => r.YrMonthRaw, StringComparer.Ordinal).Last();
                double outstanding = latest.PaidLoan - latest.LoanRecovered;
                if (outstanding > 0)
                {
                    t.PendingLoans += outstanding;
                    t.EmployeesWithLoans++;
                }
            }
            return t;
        }
    }
}
