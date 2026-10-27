using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClosedXML.Excel;
using ARISHrms.Models;
using ARISHrms.Services;

namespace ARISHrms.Views
{
    /// <summary>
    /// One employee, one month: present days, week offs, absent, overtime, late, early out - counted from the
    /// punches imported in Attendance &amp; Punches. Read only: nothing here changes the punches.
    /// </summary>
    public partial class MonthlyAttendanceWindow : Window
    {
        private static readonly string[] WeekOffChoices = { "None", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };

        private readonly List<Employee> _employees = new List<Employee>();
        private Employee _emp;
        private DateTime _month;
        private bool _monthKnown;
        private bool _loading = true;
        private DateTime? _dataFrom, _dataTo;
        private MonthlyAttendanceSummary _summary;

        /// <param name="employee">code, card number or name to open on (may be empty)</param>
        /// <param name="month">month to open on; null = the employee's latest month with punches</param>
        public MonthlyAttendanceWindow(string employee, DateTime? month)
        {
            InitializeComponent();

            foreach (var d in WeekOffChoices) CmbWeekOff.Items.Add(d);
            CmbRounding.SelectedIndex = (int)MonthlyAttendanceData.GetRounding();

            try
            {
                _employees = MonthlyAttendanceData.Employees();
                (_dataFrom, _dataTo) = MonthlyAttendanceData.DataRange();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not read attendance data: " + ex.Message, "Monthly Summary", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            TxtDataRange.Text = _dataFrom.HasValue
                ? $"Punches imported: {_dataFrom:dd-MM-yyyy} to {_dataTo:dd-MM-yyyy}. Days outside this range show \"No data\" and are not counted."
                : "No punches imported yet - use \"1. Import (.ddd)\" in Attendance & Punches first.";

            if (month.HasValue) { _month = new DateTime(month.Value.Year, month.Value.Month, 1); _monthKnown = true; }

            EmployeeSearchBinder.Attach(CmbEmployee, TxtSearch, TxtSearchHint, _employees, () => { _emp = null; Refresh(); });
            CmbEmployee.ItemsSource = _employees;

            Employee start = null;
            if (!string.IsNullOrWhiteSpace(employee))
            {
                string key = EmployeeLookup.Key(employee);
                start = _employees.FirstOrDefault(e => EmployeeLookup.Key(e.EmpCode) == key)
                        ?? EmployeeFilter.Filter(_employees, employee).FirstOrDefault();
            }
            _loading = false;
            CmbEmployee.SelectedItem = start ?? _employees.FirstOrDefault();
            if (CmbEmployee.SelectedItem == null) Refresh();
        }

        // ------------------------------------------------------------------ selection

        private void CmbEmployee_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            var emp = CmbEmployee.SelectedItem as Employee;
            if (emp == null) return;          // list re-filtered by the search box; the binder picks the next one
            if (_emp != null && EmployeeLookup.Key(_emp.EmpCode) == EmployeeLookup.Key(emp.EmpCode)) return;
            _emp = emp;

            if (!_monthKnown)
            {
                DateTime? latest = null;
                try { latest = MonthlyAttendanceData.LatestMonth(emp.EmpCode); } catch { }
                var m = latest ?? _dataTo ?? DateTime.Today;
                _month = new DateTime(m.Year, m.Month, 1);
                _monthKnown = true;      // from now on the month stays when another employee is picked
            }

            _loading = true;
            var wo = MonthlyAttendanceData.GetWeekOff(emp.EmpCode, out string source);
            CmbWeekOff.SelectedItem = wo.HasValue ? wo.Value.ToString() : "None";
            TxtWeekOffSource.Text = "(" + source + ")";
            _loading = false;

            Refresh();
        }

        private void CmbWeekOff_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || _emp == null) return;
            MonthlyAttendanceData.SaveWeekOff(_emp.EmpCode, SelectedWeekOff());
            TxtWeekOffSource.Text = "(chosen here)";
            Refresh();
        }

        private void CmbRounding_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            MonthlyAttendanceData.SaveRounding(SelectedRounding());
            Refresh();
        }

        private DayOfWeek? SelectedWeekOff()
        {
            string s = CmbWeekOff.SelectedItem as string;
            return string.IsNullOrEmpty(s) || s == "None" ? (DayOfWeek?)null : MonthlyAttendanceData.ParseDay(s);
        }

        private OtRounding SelectedRounding() =>
            CmbRounding.SelectedIndex >= 0 && CmbRounding.SelectedIndex <= 3 ? (OtRounding)CmbRounding.SelectedIndex : OtRounding.FullHour10;

        private void BtnPrev_Click(object sender, RoutedEventArgs e) { _month = _month.AddMonths(-1); _monthKnown = true; Refresh(); }
        private void BtnNext_Click(object sender, RoutedEventArgs e) { _month = _month.AddMonths(1); _monthKnown = true; Refresh(); }
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        // ------------------------------------------------------------------ calculation + display

        private void Refresh()
        {
            if (_month == default) _month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            TxtMonth.Text = _month.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
            CardsPanel.Children.Clear();

            if (_emp == null)
            {
                _summary = null;
                DaysGrid.ItemsSource = null;
                TxtShift.Text = "";
                TxtRules.Text = "Select an employee.";
                return;
            }

            try
            {
                var rows = MonthlyAttendanceData.Load(_emp.EmpCode, _month);
                var shift = ShiftTimes.For(MonthlyAttendanceData.ShiftOf(_emp.EmpCode));
                List<AbsenceMark> marks;
                try { marks = AbsenceStore.ForEmployee(_emp.EmpCode); } catch { marks = new List<AbsenceMark>(); }

                _summary = MonthlyAttendance.Build(_month, rows, shift, SelectedWeekOff(), _dataFrom, _dataTo, DateTime.Today, marks, SelectedRounding());
            }
            catch (Exception ex)
            {
                _summary = null;
                DaysGrid.ItemsSource = null;
                TxtRules.Text = "Could not build the summary: " + ex.Message;
                return;
            }

            var s = _summary;
            TxtShift.Text = s.Shift.Label;
            TxtRules.Text = "Rules: " + s.RuleText.Replace(s.Shift.Label + "   ·   ", "") +
                            "   ·   absent days count as Informed unless marked Without informing in Leaves.";
            DaysGrid.ItemsSource = s.Days;

            AddCard("Present", s.Present.ToString(), "#16A34A",
                s.WorkedOnWeekOff > 0 ? $"incl. {s.WorkedOnWeekOff} on week off" : "days with punches");
            AddCard("Week offs", s.WeekOffs.ToString(), "#7C3AED",
                s.WorkedOnWeekOff > 0 ? $"+ worked {s.WorkedOnWeekOff} (all OT)" : "not worked");
            AddCard("Absent", s.Absent.ToString(), s.Absent > 0 ? "#DC2626" : "#0F172A",
                $"informed {s.AbsentInformed} · without {s.AbsentUninformed}");
            AddCard("Overtime", MonthlyAttendance.Hm(s.OtMinutes) + " h", "#0E7490",
                $"on {s.OtDays} day(s)" + (s.WorkedOnWeekOff > 0 ? " incl. week off" : ""));
            AddCard("Worked", MonthlyAttendance.Hm(s.WorkedMinutes) + " h", "#0F172A", "in - out pairs");
            AddCard("Late", s.LateDays.ToString(), s.LateDays > 0 ? "#D97706" : "#0F172A", $"{s.LateMinutes} min in total");
            AddCard("Early out", s.EarlyOutDays.ToString(), s.EarlyOutDays > 0 ? "#D97706" : "#0F172A", $"left before {MonthlyAttendance.Clock(s.Shift.End)}");
            AddCard("Missing punch", s.MissingPunchDays.ToString(), s.MissingPunchDays > 0 ? "#D97706" : "#0F172A", "odd punches, no OT");
            AddCard("Days counted", s.CountedDays.ToString(), "#475569",
                s.NotImportedDays > 0 ? $"{s.NotImportedDays} day(s) no data" : "whole month");
        }

        private static Brush Hex(string color) => (Brush)new BrushConverter().ConvertFromString(color);

        private void AddCard(string title, string value, string color, string sub)
        {
            var brush = Hex(color);
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = title.ToUpperInvariant(), FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Hex("#64748B") });
            panel.Children.Add(new TextBlock { Text = value, FontSize = 22, FontWeight = FontWeights.Bold, Foreground = brush, Margin = new Thickness(0, 2, 0, 0) });
            panel.Children.Add(new TextBlock { Text = sub, FontSize = 10, Foreground = Hex("#94A3B8"), TextTrimming = TextTrimming.CharacterEllipsis });
            CardsPanel.Children.Add(new System.Windows.Controls.Border
            {
                Child = panel,
                Width = 112,
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 8, 8),
                Background = Brushes.White,
                BorderBrush = Hex("#E2E8F0"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6)
            });
        }

        // ------------------------------------------------------------------ output

        private string EmployeeTitle() => _emp == null ? "" : _emp.DisplayName;

        private void BtnPrint_Click(object sender, RoutedEventArgs e)
        {
            if (_summary == null) { MessageBox.Show("Select an employee first.", "Print"); return; }
            try { ReportPrinter.Print(MonthlyAttendance.ToReport(_summary, EmployeeTitle()), this); }
            catch (Exception ex) { MessageBox.Show("Print failed: " + ex.Message, "Print", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            if (_summary == null) { MessageBox.Show("Select an employee first.", "Export"); return; }
            var s = _summary;
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Excel Workbook|*.xlsx",
                FileName = $"Attendance_{EmployeeLookup.Key(_emp.EmpCode)}_{s.Month:yyyy-MM}.xlsx"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                using (var wb = new XLWorkbook())
                {
                    var ws = wb.Worksheets.Add("Monthly Attendance");
                    ws.Cell(1, 1).Value = $"Monthly Attendance - {EmployeeTitle()} - {s.Month:MMMM yyyy}";
                    ws.Cell(1, 1).Style.Font.SetBold();
                    ws.Cell(2, 1).Value = s.RuleText;

                    int r = 4;
                    void Line(string label, string value) { ws.Cell(r, 1).Value = label; ws.Cell(r, 2).Value = value; ws.Cell(r, 1).Style.Font.SetBold(); r++; }
                    Line("Present days", $"{s.Present}" + (s.WorkedOnWeekOff > 0 ? $" (incl. {s.WorkedOnWeekOff} on week off)" : ""));
                    Line("Week offs", $"{s.WeekOffs}");
                    Line("Absent days", $"{s.Absent} (informed {s.AbsentInformed}, without informing {s.AbsentUninformed})");
                    Line("Overtime", $"{MonthlyAttendance.Hm(s.OtMinutes)} h on {s.OtDays} day(s)");
                    Line("Worked hours", $"{MonthlyAttendance.Hm(s.WorkedMinutes)} h");
                    Line("Late", $"{s.LateDays} day(s), {s.LateMinutes} min");
                    Line("Early out", $"{s.EarlyOutDays} day(s)");
                    Line("Missing punch", $"{s.MissingPunchDays} day(s)");
                    Line("Days counted", $"{s.CountedDays}" + (s.NotImportedDays > 0 ? $" ({s.NotImportedDays} day(s) with no imported punches)" : ""));

                    r++;
                    string[] headers = { "Date", "Status", "In", "L-Out", "L-In", "Out", "Extra", "Worked", "Late (min)", "Early out (min)", "OT", "Note" };
                    for (int c = 0; c < headers.Length; c++) ws.Cell(r, c + 1).Value = headers[c];
                    ws.Range(r, 1, r, headers.Length).Style.Font.SetBold();
                    r++;
                    foreach (var d in s.Days)
                    {
                        ws.Cell(r, 1).Value = d.DateDisplay;
                        ws.Cell(r, 2).Value = d.Status;
                        ws.Cell(r, 3).Value = d.In;
                        ws.Cell(r, 4).Value = d.LOut;
                        ws.Cell(r, 5).Value = d.LIn;
                        ws.Cell(r, 6).Value = d.Out;
                        ws.Cell(r, 7).Value = d.Extra;
                        ws.Cell(r, 8).Value = d.WorkedDisplay;
                        if (d.LateMinutes > 0) ws.Cell(r, 9).Value = d.LateMinutes;
                        if (d.EarlyOutMinutes > 0) ws.Cell(r, 10).Value = d.EarlyOutMinutes;
                        ws.Cell(r, 11).Value = d.OtDisplay;
                        ws.Cell(r, 12).Value = d.Note;
                        if (d.Status == "Absent") ws.Range(r, 1, r, headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#FEF2F2");
                        else if (d.Status == "Week off") ws.Range(r, 1, r, headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F3FF");
                        r++;
                    }
                    ws.Columns().AdjustToContents();
                    wb.SaveAs(dlg.FileName);
                }
                MessageBox.Show("Saved:\n" + dlg.FileName, "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) { MessageBox.Show("Export failed: " + ex.Message, "Export", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
    }
}
