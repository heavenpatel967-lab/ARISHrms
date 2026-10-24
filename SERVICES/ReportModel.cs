using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ARISHrms.Services
{
    // =====================================================================================
    //  A printable report described as plain data. VIEWS/ReportPrinter turns it into pages;
    //  the printer adds as many pages as the content needs, so no row is ever cut off.
    // =====================================================================================

    public class ReportCell
    {
        public string Text = "";
        public string Color;          // text colour (#RRGGBB) or null
        public bool Bold;

        public ReportCell() { }
        public ReportCell(string text, string color = null, bool bold = false)
        {
            Text = text ?? "";
            Color = color;
            Bold = bold;
        }
    }

    public abstract class ReportBlock { }

    public class ReportHeading : ReportBlock { public string Text = ""; }

    public class ReportFields : ReportBlock
    {
        public List<(string Label, ReportCell Value)> Items = new List<(string, ReportCell)>();
    }

    public class ReportNote : ReportBlock
    {
        public string Text = "";
        public string Color = "#555555";
    }

    public class ReportDots : ReportBlock
    {
        public string Label = "";
        public string Text = "";
        public List<SummaryDot> Dots = new List<SummaryDot>();
    }

    public class ReportTable : ReportBlock
    {
        public List<string> Headers = new List<string>();
        public List<double> Widths = new List<double>();          // relative column widths
        public List<List<ReportCell>> Rows = new List<List<ReportCell>>();
        public List<string> RowBackgrounds = new List<string>();  // optional, one per row (null = none)
        public string EmptyText = "No records.";
    }

    public class Report
    {
        public string Company = "ARIS BATHWARES & SANITARYWARES";
        public string Title = "";
        public string Subtitle = "";
        public DateTime Printed = DateTime.Now;
        public List<ReportBlock> Blocks = new List<ReportBlock>();

        /// <summary>Total table rows in the report - printed at the end so you can see nothing was dropped.</summary>
        public int RowCount => Blocks.OfType<ReportTable>().Sum(t => t.Rows.Count);
    }

    /// <summary>Header details shown at the top of an employee report.</summary>
    public class EmployeeHeader
    {
        public string Name = "", Codes = "", Department = "", Phone = "", Aadhar = "", Village = "", Status = "";
    }

    // =====================================================================================
    //  Builders - every list is copied in full (no Take / no limits)
    // =====================================================================================

    public static class ReportBuilders
    {
        private static ReportCell C(string text, string color = null, bool bold = false) => new ReportCell(text, color, bold);
        private static string D(DateTime d) => d.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture);
        private static string Money(double v) => $"₹ {v:N2}";

        // ---------------- shared sections ----------------

        public static ReportTable RemarksTable(IEnumerable<EmployeeRemark> remarks)
        {
            var t = new ReportTable
            {
                Headers = { "Date", "Type", "Remark" },
                Widths = { 1.3, 0.8, 6 },
                EmptyText = "No remarks."
            };
            foreach (var m in remarks.OrderByDescending(x => x.RemarkDate).ThenByDescending(x => x.Id))
            {
                t.Rows.Add(new List<ReportCell> { C(m.DateDisplay), C(m.KindLabel, m.Color, true), C(m.Text) });
                t.RowBackgrounds.Add(m.Background);
            }
            return t;
        }

        public static ReportTable VillageVisitTable(IEnumerable<VillageVisit> visits)
        {
            var t = new ReportTable
            {
                Headers = { "Departure", "Expected Return", "Actual Return", "Late Days", "Reason / Notes" },
                Widths = { 1.3, 1.4, 1.3, 0.9, 4 },
                EmptyText = "No village visits recorded."
            };
            foreach (var v in visits.OrderByDescending(x => x.DepartureDate).ThenByDescending(x => x.Id))
            {
                t.Rows.Add(new List<ReportCell>
                {
                    C(D(v.DepartureDate)),
                    C(D(v.ExpectedReturn)),
                    v.ActualReturn.HasValue ? C(D(v.ActualReturn.Value)) : C("Not back yet", "#E74C3C", true),
                    v.LateDays > 0 ? C(v.LateDays.ToString(), "#E74C3C", true) : C(v.LateDays.ToString(), "#27AE60"),
                    C(v.ReasonNotes)
                });
                t.RowBackgrounds.Add(null);
            }
            return t;
        }

        public static ReportTable AdvanceTakenTable(IEnumerable<AdvanceTakenEntry> ticks)
        {
            var t = new ReportTable
            {
                Headers = { "Date Taken", "For Month", "Amount", "Note" },
                Widths = { 1.3, 1.1, 1.3, 4 },
                EmptyText = "No advance ticked as taken yet."
            };
            foreach (var a in ticks.OrderBy(x => x.TakenDate).ThenBy(x => x.Id))
            {
                t.Rows.Add(new List<ReportCell> { C(a.DateDisplay), C(a.MonthDisplay), C(a.AmountDisplay), C(a.Note) });
                t.RowBackgrounds.Add(null);
            }
            return t;
        }

        public static void AddTrackRecord(Report r, IList<EmployeeRemark> remarks, IList<VillageVisit> visits, LeaveReliabilityResult leave)
        {
            var verdict = TrackRecord.Verdict(remarks, leave);
            r.Blocks.Add(new ReportHeading { Text = "REMARKS & TRACK RECORD" });
            r.Blocks.Add(new ReportFields { Items = { ("Overall", C(verdict.Text, verdict.Color, true)), ("Summary", C(verdict.Detail)) } });
            r.Blocks.Add(new ReportDots { Label = "Village leave record (last 6)", Text = leave.Text, Dots = leave.Dots });
            r.Blocks.Add(RemarksTable(remarks));

            r.Blocks.Add(new ReportHeading { Text = "VILLAGE VISIT / LONG LEAVE HISTORY" });
            r.Blocks.Add(VillageVisitTable(visits));
        }

        private static void AddEmployeeHeader(Report r, EmployeeHeader h)
        {
            r.Blocks.Add(new ReportHeading { Text = "PERSONAL & CAREER DETAILS" });
            r.Blocks.Add(new ReportFields
            {
                Items =
                {
                    ("Employee Name", C(h.Name, null, true)),
                    ("Emp Code / Card No", C(h.Codes)),
                    ("Department", C(h.Department)),
                    ("Phone Number", C(h.Phone)),
                    ("Aadhar Number", C(h.Aadhar)),
                    ("Native Village", C(h.Village)),
                    ("Status", C(h.Status, null, true))
                }
            });
        }

        // ---------------- 360 summary ----------------

        public static Report EmployeeSummary(EmployeeHeader h, SummaryFinancials fin, string financialNote,
            IList<AdvanceTakenEntry> ticks, IList<EmployeeRemark> remarks, IList<VillageVisit> visits,
            LeaveReliabilityResult leave, DateTime today)
        {
            var r = new Report { Title = "360° EMPLOYEE SUMMARY REPORT", Subtitle = $"{h.Name}  ({h.Codes})" };
            AddEmployeeHeader(r, h);

            r.Blocks.Add(new ReportHeading { Text = "FINANCIAL SUMMARY" });
            if (fin != null)
            {
                r.Blocks.Add(new ReportFields
                {
                    Items =
                    {
                        ("Total Outstanding Loan", C(Money(fin.TotalLoan), "#C0392B", true)),
                        ("Total Advance Taken", C(Money(fin.TotalAdvance), "#E67E22", true))
                    }
                });
                r.Blocks.Add(new ReportDots { Label = "Loan Health", Text = fin.LoanHealthText, Dots = fin.LoanDots });
                r.Blocks.Add(new ReportDots { Label = "Advance Health", Text = fin.AdvanceHealthText, Dots = fin.AdvanceDots });
            }
            if (!string.IsNullOrWhiteSpace(financialNote)) r.Blocks.Add(new ReportNote { Text = financialNote, Color = "#C0392B" });
            r.Blocks.Add(new ReportDots { Label = "Leave Reliability", Text = leave.Text, Dots = leave.Dots });
            r.Blocks.Add(new ReportDots
            {
                Label = "Advance Taken (last 6 months)",
                Text = TrackRecord.AdvanceTakenText(ticks, today),
                Dots = TrackRecord.AdvanceTakenDots(ticks, today)
            });

            r.Blocks.Add(new ReportHeading { Text = "ADVANCE TAKEN HISTORY (all dates)" });
            r.Blocks.Add(AdvanceTakenTable(ticks));

            AddTrackRecord(r, remarks, visits, leave);
            return r;
        }

        // ---------------- loan ledger ----------------

        public static Report LoanLedger(string employee, LoanSummary s, string note,
            IList<EmployeeRemark> remarks, IList<VillageVisit> visits, LeaveReliabilityResult leave)
        {
            var r = new Report { Title = "LOAN LEDGER", Subtitle = employee };

            r.Blocks.Add(new ReportHeading { Text = "SUMMARY" });
            if (s != null)
            {
                r.Blocks.Add(new ReportFields
                {
                    Items =
                    {
                        ("Total Loan Given", C(Money(s.TotalApproved), null, true)),
                        ("Recovered So Far", C(Money(s.TotalRecovered))),
                        ("Current Outstanding Loan", C(Money(s.Outstanding), "#C0392B", true)),
                        ("Total EMI Deducted", C(Money(s.TotalEmiDeducted))),
                        ("Repayment Health", C($"{s.MonthsPaid} / {s.MonthsTotal} Paid", null, true))
                    }
                });
                r.Blocks.Add(new ReportDots
                {
                    Label = "Last 6 months",
                    Text = "",
                    Dots = s.Last6.Select(d => new SummaryDot { Month = d.Month, Color = d.Good ? "#27AE60" : "#E74C3C" }).ToList()
                });

                var t = new ReportTable
                {
                    Headers = { "Month / Year", "Pending Loan Amount", "EMI Deducted" },
                    Widths = { 1.5, 2, 2 },
                    EmptyText = "No loan activity."
                };
                foreach (var row in s.Ledger)
                {
                    t.Rows.Add(new List<ReportCell>
                    {
                        C(row.MonthYear),
                        C(row.PendingLoanAmount.HasValue ? Money(row.PendingLoanAmount.Value) : "-"),
                        row.EmiDeducted.HasValue ? C(Money(row.EmiDeducted.Value), "#C0392B", true) : C("-")
                    });
                    t.RowBackgrounds.Add(null);
                }
                r.Blocks.Add(new ReportHeading { Text = "MONTHLY LOAN & DEDUCTION TIMELINE" });
                r.Blocks.Add(t);
            }
            if (!string.IsNullOrWhiteSpace(note)) r.Blocks.Add(new ReportNote { Text = note });

            AddTrackRecord(r, remarks, visits, leave);
            return r;
        }

        // ---------------- advance ledger ----------------

        public static Report AdvanceLedger(string employee, AdvanceSummary s, double limit, IList<AdvanceLedgerRow> rows,
            IList<AdvanceTakenEntry> ticks, IList<EmployeeRemark> remarks, IList<VillageVisit> visits,
            LeaveReliabilityResult leave, DateTime today)
        {
            var r = new Report { Title = "ADVANCE LEDGER", Subtitle = employee };

            r.Blocks.Add(new ReportHeading { Text = "SUMMARY" });
            var fields = new ReportFields();
            if (s != null)
            {
                fields.Items.Add(("Total Advances Taken", C(Money(s.Total), null, true)));
                fields.Items.Add(("Advance Health (limit)", C($"{s.SafeCount} / {s.Items.Count} Safe", null, true)));
            }
            fields.Items.Add(("Monthly Limit", C(Money(limit))));
            r.Blocks.Add(fields);
            r.Blocks.Add(new ReportDots
            {
                Label = "Advance Taken (last 6 months)",
                Text = TrackRecord.AdvanceTakenText(ticks, today),
                Dots = TrackRecord.AdvanceTakenDots(ticks, today)
            });

            var t = new ReportTable
            {
                Headers = { "Status", "Month / Year", "Advance Amount", "Taken", "Taken On", "Note" },
                Widths = { 1, 1.2, 1.5, 0.7, 1.3, 3 },
                EmptyText = "No advances."
            };
            foreach (var row in rows)
            {
                string status = !row.FromMonsum ? "Added here" : row.StatusColor == "#E74C3C" ? "High" : "Safe";
                string color = !row.FromMonsum ? "#0EA5E9" : row.StatusColor == "#E74C3C" ? "#E74C3C" : "#27AE60";
                t.Rows.Add(new List<ReportCell>
                {
                    C(status, color, true),
                    C(row.MonthYear),
                    C(row.AmountDisplay, row.StatusColor == "#E74C3C" ? "#E74C3C" : null),
                    C(row.IsTaken ? "✔" : "", "#27AE60", true),
                    C(row.TakenDate.HasValue ? D(row.TakenDate.Value) : ""),
                    C(row.Note)
                });
                t.RowBackgrounds.Add(null);
            }
            r.Blocks.Add(new ReportHeading { Text = "MONTHLY ADVANCES" });
            r.Blocks.Add(t);

            AddTrackRecord(r, remarks, visits, leave);
            return r;
        }
    }
}
