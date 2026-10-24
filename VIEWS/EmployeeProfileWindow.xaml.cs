using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ARISHrms.Data;
using ARISHrms.Models;
using ARISHrms.Services;
using Microsoft.Data.Sqlite;
using Dapper;

namespace ARISHrms.Views
{
    /// <summary>
    /// The 360 degree employee summary (opened by double-clicking an employee). Personal details come from this
    /// app, loans and advances from EasyPay's monsum.DBF, and leave history from the village-visit tracker.
    /// </summary>
    public partial class EmployeeProfileWindow : Window
    {
        private readonly Employee _employee;
        private SummaryFinancials _financials;
        private string _financialNote = "";

        public EmployeeProfileWindow(Employee employee)
        {
            InitializeComponent();
            _employee = employee;

            ShowPersonalDetails();
            LoadFinancials();
            LoadLeaves();
            LoadAdvanceTaken();
            LoadAbsences();

            // remarks can be added right here, and are hidden from the print-out's input area
            TrackRecord.ShowEmployee(_employee.EmpCode);
            TrackRecord.ShowLeaveMonths = false;     // the 360 page has its own leave section just above
        }

        private void LoadAbsences()
        {
            LeaveMonths.Show(_employee.EmpCode, _employee.DisplayName);
            LeaveMonths.Changed += (s, e) => TrackRecord.ShowEmployee(_employee.EmpCode);
        }

        private void LoadAdvanceTaken()
        {
            try
            {
                var ticks = AdvanceTakenStore.ForEmployee(_employee.EmpCode);
                IcTakenDots.ItemsSource = ARISHrms.Services.TrackRecord.AdvanceTakenDots(ticks, DateTime.Today);
                TxtTakenHealth.Text = ARISHrms.Services.TrackRecord.AdvanceTakenText(ticks, DateTime.Today);
                TxtLastAdvance.Text = ticks.Count == 0
                    ? "Advance taken: none ticked yet (tick them in the Advance Ledger)"
                    : $"Last advance taken on {ticks.Max(t => t.TakenDate):dd-MMM-yyyy}   ({ticks.Count} recorded in total)";
            }
            catch (Exception ex)
            {
                TxtTakenHealth.Text = "Data Error";
                TxtLastAdvance.Text = "Could not read advance ticks: " + ex.Message;
            }
        }

        private static string OrDash(string s) => string.IsNullOrWhiteSpace(s) ? "-" : s;

        private void ShowPersonalDetails()
        {
            TxtFullName.Text = OrDash(_employee.FullName);
            TxtCodes.Text = $"{_employee.EmpCode} / {OrDash(_employee.CardNo)}";

            string dept = _employee.DepartmentName ?? "";
            string desig = _employee.Designation ?? "";
            string combined = $"{dept} - {desig}".Trim(' ', '-');
            TxtDept.Text = OrDash(combined);

            TxtPhone.Text = OrDash(_employee.Phone);
            TxtAadhar.Text = OrDash(_employee.AadharNo);
            TxtVillage.Text = OrDash(_employee.NativeVillage);

            bool left = string.Equals(_employee.Status?.Trim(), "N", StringComparison.OrdinalIgnoreCase);
            TxtStatus.Text = left
                ? (string.IsNullOrWhiteSpace(_employee.LeavingDisplay) ? "Inactive / Left" : $"Inactive / Left (on {_employee.LeavingDisplay})")
                : "Active";
        }

        private void LoadFinancials()
        {
            string path = DbfSource.Find("monsum");
            if (path == null)
            {
                TxtTotalLoan.Text = TxtTotalAdvance.Text = "-";
                TxtLoanHealth.Text = TxtAdvanceHealth.Text = "-";
                TxtDataNote.Text = "Loan and advance figures: " + DbfSource.NotFoundMessage("monsum.DBF");
                _financialNote = TxtDataNote.Text;
                return;
            }

            try
            {
                var summary = MonsumData.Load(path).BuildSummary(_employee.EmpCode, AdvanceLimitStore.Get(_employee.EmpCode));
                _financials = summary;

                TxtTotalLoan.Text = $"₹ {summary.TotalLoan:N2}";
                TxtLoanHealth.Text = summary.LoanHealthText;
                IcLoanDots.ItemsSource = summary.LoanDots;

                TxtTotalAdvance.Text = $"₹ {summary.TotalAdvance:N2}";
                TxtAdvanceHealth.Text = summary.AdvanceHealthText;
                IcAdvanceDots.ItemsSource = summary.AdvanceDots;
            }
            catch (Exception ex)
            {
                TxtTotalLoan.Text = TxtTotalAdvance.Text = "Data Error";
                TxtDataNote.Text = "Could not read monsum.DBF: " + ex.Message;
                _financialNote = TxtDataNote.Text;
            }
        }

        private void LoadLeaves()
        {
            try
            {
                var visits = VillageVisitStore.ForEmployee(_employee.EmpCode);

                // every visit, newest first - nothing is hidden
                LeaveGrid.ItemsSource = visits.OrderByDescending(v => v.DepartureDate).ThenByDescending(v => v.Id).ToList();
                TxtLeaveCount.Text = visits.Count == 0 ? "" : $"{visits.Count} visit(s)";

                var reliability = VillageVisitStore.Reliability(visits, DateTime.Today);
                TxtLeaveHealth.Text = reliability.Text;
                IcLeaveDots.ItemsSource = reliability.Dots;
            }
            catch (Exception ex)
            {
                TxtLeaveHealth.Text = "Data Error";
                TxtDataNote.Text = (TxtDataNote.Text + "  Could not read leave history: " + ex.Message).Trim();
            }
        }

        private void BtnEdit_Click(object sender, RoutedEventArgs e)
        {
            var edit = new EditEmployeeWindow(_employee) { Owner = this };
            if (edit.ShowDialog() == true)
                ShowPersonalDetails();
        }

        private void BtnPrint_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var visits = VillageVisitStore.ForEmployee(_employee.EmpCode);
                var header = new EmployeeHeader
                {
                    Name = TxtFullName.Text, Codes = TxtCodes.Text, Department = TxtDept.Text, Phone = TxtPhone.Text,
                    Aadhar = TxtAadhar.Text, Village = TxtVillage.Text, Status = TxtStatus.Text
                };

                var report = ReportBuilders.EmployeeSummary(
                    header, _financials, _financialNote,
                    AdvanceTakenStore.ForEmployee(_employee.EmpCode),
                    RemarkStore.ForEmployee(_employee.EmpCode),
                    visits,
                    VillageVisitStore.Reliability(visits, DateTime.Today),
                    DateTime.Today,
                    AttendanceAbsences.Summary(_employee.EmpCode, DateTime.Today));

                ReportPrinter.Print(report, this);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Could not prepare the report: {ex.Message}", "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
