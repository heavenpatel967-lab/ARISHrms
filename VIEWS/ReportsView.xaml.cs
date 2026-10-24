using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ARISHrms.Models;
using ARISHrms.Services;
using ClosedXML.Excel;

namespace ARISHrms.Views
{
    /// <summary>Daily Late/Absent and Employee History reports, from EasyPay's MONDATA.DBF.</summary>
    public partial class ReportsView : UserControl
    {
        private readonly List<DailyReportRow> _late = new List<DailyReportRow>();
        private readonly List<DailyReportRow> _absent = new List<DailyReportRow>();
        private Dictionary<string, Employee> _employees = new Dictionary<string, Employee>();
        private List<Employee> _employeeList = new List<Employee>();
        private List<HistoryRow> _history = new List<HistoryRow>();
        private string _historyEmp;

        public ReportsView()
        {
            InitializeComponent();

            DpReportDate.SelectedDate = DateTime.Today;
            DpStart.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            DpEnd.SelectedDate = DateTime.Today;

            TxtMonDataPath.Text = DbfSource.GetMonDataPath() ?? "";

            try
            {
                var all = EmployeeLookup.LoadAll();
                _employeeList = all;
                _employees = EmployeeLookup.LoadByKey();
                CmbEmployee.ItemsSource = all;
                if (all.Count > 0) CmbEmployee.SelectedIndex = 0;

                // search by card number / code / name
                EmployeeSearchBinder.Attach(CmbEmployee, TxtEmpSearch, TxtSearchHint, _employeeList, () =>
                {
                    HistoryGrid.ItemsSource = null;
                    _history = new List<HistoryRow>();
                    _historyEmp = null;
                    HistoryLeaveMonths.Show(null, null);
                    TxtHistoryInfo.Text = "No employee matches your search.";
                });
                // editing leaves from the summary refreshes the history grid
                HistoryLeaveMonths.Changed += (sender, e) => ShowHistory();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading employees: {ex.Message}", "Error");
            }

            if (string.IsNullOrEmpty(TxtMonDataPath.Text))
                TxtDailyInfo.Text = "MONDATA.DBF not found yet. Click Browse, or pick your DBFS folder in Employee Master first.";
        }

        // ---------- shared ----------
        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Select MONDATA.DBF", Filter = "DBF Files (*.dbf)|*.dbf" };
            string folder = DbfSource.Folder;
            if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder)) dlg.InitialDirectory = folder;

            if (dlg.ShowDialog() == true)
            {
                TxtMonDataPath.Text = dlg.FileName;
                DbfSource.SaveMonDataPath(dlg.FileName);
            }
        }

        private string RequireMonData()
        {
            string path = TxtMonDataPath.Text.Trim();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                MessageBox.Show("Please select a valid MONDATA.DBF file.", "Missing File", MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }
            DbfSource.SaveMonDataPath(path);
            return path;
        }

        // ---------- daily ----------
        private DailyReportRow ToRow(DayRecord r, string status, string punch)
        {
            _employees.TryGetValue(EmployeeLookup.Key(r.EmpCode), out var emp);
            return new DailyReportRow
            {
                EmployeeCode = r.EmpCode,
                CardNumber = string.IsNullOrWhiteSpace(emp?.CardNo) ? r.EmpCode : emp.CardNo,
                EmployeeName = emp?.FullName is string n && n.Length > 0 ? n : "Unknown / Not Synced",
                Department = emp?.DepartmentName ?? "",
                PunchTimeStr = punch,
                PunchValue = r.InTime,
                Status = status
            };
        }

        private void BtnRunDaily_Click(object sender, RoutedEventArgs e)
        {
            string path = RequireMonData();
            if (path == null || DpReportDate.SelectedDate == null) return;
            DateTime day = DpReportDate.SelectedDate.Value;

            try
            {
                Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
                var (late, absent) = AttendanceReportService.BuildDaily(path, day);

                _late.Clear();
                _absent.Clear();
                _late.AddRange(late.Select(r => ToRow(r, "Late", r.InTime.ToString("0.00", CultureInfo.InvariantCulture))));
                _absent.AddRange(absent.Select(r => ToRow(r, "Absent", "-")));
                Mouse.OverrideCursor = null;

                if (late.Count == 0 && absent.Count == 0)
                {
                    // distinguish "nobody late/absent" from "no data for that day"
                    var any = AttendanceReportService.Read(path, (emp, d) => d.Date == day.Date);
                    if (any.Count == 0)
                    {
                        ApplyDailyFilter();
                        TxtDailyInfo.Text = $"No attendance data found for {day:dd-MMM-yyyy}.";
                        MessageBox.Show($"No attendance data found for {day:dd-MMM-yyyy}.", "No Data Found", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                }

                ApplyDailyFilter();
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                MessageBox.Show($"Could not read MONDATA: {ex.Message}", "Error");
            }
        }

        private void TxtDailySearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyDailyFilter();

        private void ApplyDailyFilter()
        {
            if (LateGrid == null || AbsentGrid == null) return;
            string s = (TxtDailySearch.Text ?? "").Trim().ToLowerInvariant();

            bool Match(DailyReportRow x) => s.Length == 0 ||
                x.CardNumber.ToLowerInvariant().Contains(s) || x.EmployeeCode.ToLowerInvariant().Contains(s) || x.EmployeeName.ToLowerInvariant().Contains(s);

            var late = _late.Where(Match).OrderByDescending(x => x.PunchValue).ToList();
            var absent = _absent.Where(Match).OrderBy(x => x.CardNumber, StringComparer.Ordinal).ToList();

            LateGrid.ItemsSource = late;
            AbsentGrid.ItemsSource = absent;
            TxtDailyInfo.Text = $"Late: {late.Count}    Absent: {absent.Count}";
        }

        private void Export(IEnumerable<DailyReportRow> rows, string name, bool withPunch)
        {
            var list = rows.ToList();
            if (list.Count == 0) { MessageBox.Show("Nothing to export.", "Export"); return; }

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Excel Workbook|*.xlsx",
                FileName = $"{name}_{(DpReportDate.SelectedDate ?? DateTime.Today):dd-MMM-yyyy}.xlsx"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                using (var wb = new XLWorkbook())
                {
                    var ws = wb.Worksheets.Add(name);
                    string[] heads = withPunch
                        ? new[] { "Card No", "Emp Code", "Name", "Department", "Punch Time" }
                        : new[] { "Card No", "Emp Code", "Name", "Department", "Status" };
                    for (int i = 0; i < heads.Length; i++) { ws.Cell(1, i + 1).Value = heads[i]; ws.Cell(1, i + 1).Style.Font.SetBold(); }

                    int r = 2;
                    foreach (var x in list)
                    {
                        ws.Cell(r, 1).Value = x.CardNumber;
                        ws.Cell(r, 2).Value = x.EmployeeCode;
                        ws.Cell(r, 3).Value = x.EmployeeName;
                        ws.Cell(r, 4).Value = x.Department;
                        ws.Cell(r, 5).Value = withPunch ? x.PunchTimeStr : x.Status;
                        r++;
                    }
                    ws.Columns().AdjustToContents();
                    wb.SaveAs(dlg.FileName);
                }
                MessageBox.Show("Excel report generated successfully!", "Success");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Export Error");
            }
        }

        private void BtnExportLate_Click(object sender, RoutedEventArgs e) => Export(LateGrid.ItemsSource as IEnumerable<DailyReportRow> ?? new List<DailyReportRow>(), "LatePunching", true);
        private void BtnExportAbsent_Click(object sender, RoutedEventArgs e) => Export(AbsentGrid.ItemsSource as IEnumerable<DailyReportRow> ?? new List<DailyReportRow>(), "Absentees", false);

        // ---------- history ----------
        private void BtnRunHistory_Click(object sender, RoutedEventArgs e)
        {
            string path = RequireMonData();
            if (path == null) return;

            if (DpStart.SelectedDate == null || DpEnd.SelectedDate == null)
            {
                MessageBox.Show("Please choose a From and To date.", "Missing dates");
                return;
            }

            string code = CmbEmployee.SelectedValue?.ToString();
            if (string.IsNullOrWhiteSpace(code))
            {
                MessageBox.Show("Please choose an employee.", "Missing employee");
                return;
            }

            try
            {
                Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
                var rows = AttendanceReportService.BuildHistory(path, code, DpStart.SelectedDate.Value, DpEnd.SelectedDate.Value);
                Mouse.OverrideCursor = null;

                _history = rows;
                _historyEmp = code;
                ShowHistory();
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                MessageBox.Show($"Could not read MONDATA: {ex.Message}", "Error");
            }
        }

        private void ShowHistory()
        {
            if (_historyEmp == null) return;
            List<AbsenceMark> marks;
            try { marks = AbsenceStore.ForEmployee(_historyEmp); }
            catch (Exception ex) { marks = new List<AbsenceMark>(); TxtHistoryInfo.Text = "Could not read leave types: " + ex.Message; }

            AbsenceLogic.Apply(_history, marks);
            HistoryGrid.ItemsSource = null;
            HistoryGrid.ItemsSource = _history;

            var emp = _employeeList.FirstOrDefault(x => x.EmpCode == _historyEmp);
            HistoryLeaveMonths.Show(_historyEmp, emp?.DisplayName ?? _historyEmp);

            if (_history.Count == 0)
            {
                TxtHistoryInfo.Text = "No attendance data found for this employee in this date range.";
                return;
            }

            int absent = _history.Count(r => r.IsAbsent);
            int without = _history.Count(r => r.IsAbsent && r.Uninformed);
            TxtHistoryInfo.Text =
                $"Present {_history.Count(r => r.Status == "Present")}   Late {_history.Count(r => r.Status == "Late")}   " +
                $"Absent {absent}  (informed {absent - without}, without informing {without})   Week Off {_history.Count(r => r.Status == "Week Off")}";
        }

        private void BtnExportHistory_Click(object sender, RoutedEventArgs e)
        {
            var data = HistoryGrid.ItemsSource as List<HistoryRow>;
            if (data == null || data.Count == 0) { MessageBox.Show("Nothing to export.", "Export"); return; }

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Excel Workbook|*.xlsx",
                FileName = $"History_{CmbEmployee.SelectedValue}_{DpStart.SelectedDate:ddMMM}_to_{DpEnd.SelectedDate:ddMMM}.xlsx"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                using (var wb = new XLWorkbook())
                {
                    var ws = wb.Worksheets.Add("History Report");
                    string[] heads = { "Date", "In Time", "Out Time", "Status", "Leave Type", "Note" };
                    for (int i = 0; i < heads.Length; i++) { ws.Cell(1, i + 1).Value = heads[i]; ws.Cell(1, i + 1).Style.Font.SetBold(); }
                    int r = 2;
                    foreach (var x in data)
                    {
                        ws.Cell(r, 1).Value = x.DateStr;
                        ws.Cell(r, 2).Value = x.InTime;
                        ws.Cell(r, 3).Value = x.OutTime;
                        ws.Cell(r, 4).Value = x.Status;
                        ws.Cell(r, 5).Value = x.LeaveType;
                        ws.Cell(r, 6).Value = x.LeaveNote;
                        if (x.Uninformed) ws.Cell(r, 5).Style.Font.FontColor = XLColor.Red;
                        r++;
                    }
                    ws.Columns().AdjustToContents();
                    wb.SaveAs(dlg.FileName);
                }
                MessageBox.Show("Excel report generated successfully!", "Success");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Export Error");
            }
        }
    }
}
