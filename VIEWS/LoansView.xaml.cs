using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ARISHrms.Models;
using ARISHrms.Services;
using ClosedXML.Excel;

namespace ARISHrms.Views
{
    /// <summary>
    /// Loan ledger for one employee. Nothing is typed in here: the figures come from EasyPay's
    /// monsum.DBF (PAID_LOAN, LOAN_REC, LOAN_DED), exactly like the old ARIS Payroll Companion.
    /// </summary>
    public partial class LoansView : UserControl
    {
        private List<Employee> _employees = new List<Employee>();
        private MonsumData _monsum;
        private Employee _selected;
        private LoanSummary _summary;

        public LoansView()
        {
            InitializeComponent();
            LoadEmployeeList();
        }

        private void LoadEmployeeList()
        {
            try
            {
                _employees = EmployeeLookup.LoadAll();
                CmbSelectEmployee.ItemsSource = _employees;
                CmbSelectEmployee.DisplayMemberPath = "DisplayName";
                CmbSelectEmployee.SelectedValuePath = "EmpCode";

                if (_employees.Count == 0)
                {
                    TxtInfo.Text = "No employees yet. Use Employee Master -> Sync DBFS Folder.";
                    return;
                }

                CmbSelectEmployee.SelectedIndex = 0;

                // search by card number / code / name
                EmployeeSearchBinder.Attach(CmbSelectEmployee, TxtEmpSearch, TxtSearchHint, _employees, () =>
                {
                    _selected = null;
                    ClearScreen();
                    TrackRecord.ShowEmployee(null);
                    TxtInfo.Text = "No employee matches your search.";
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading employees: {ex.Message}", "Error");
            }
        }

        private bool EnsureMonsum()
        {
            string path = DbfSource.Find("monsum");
            if (path == null)
            {
                TxtInfo.Text = "monsum.DBF not found. " + DbfSource.NotFoundHelp.Replace("\n\n", " ").Replace("\n", " ");
                return false;
            }

            try
            {
                _monsum = MonsumData.Load(path);
                return true;
            }
            catch (Exception ex)
            {
                TxtInfo.Text = "Could not read monsum.DBF: " + ex.Message;
                return false;
            }
        }

        private void CmbSelectEmployee_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selected = CmbSelectEmployee.SelectedItem as Employee;
            if (_selected == null) return;
            LoadLoanData();
        }

        private void LoadLoanData()
        {
            ClearScreen();
            TrackRecord.ShowEmployee(_selected?.EmpCode);
            if (!EnsureMonsum()) return;

            _summary = _monsum.BuildLoanSummary(_selected.EmpCode, _selected.CardNo);

            LoanGrid.ItemsSource = _summary.Ledger;
            TxtTotalApproved.Text = $"₹ {_summary.TotalApproved:N2}";
            TxtTotalEmi.Text = $"₹ {_summary.TotalRecovered:N2}";
            TxtOutstanding.Text = $"₹ {_summary.Outstanding:N2}";
            TxtHealth.Text = $"{_summary.MonthsPaid} / {_summary.MonthsTotal} Paid";

            HealthBarContainer.Children.Clear();
            foreach (var dot in _summary.Last6)
            {
                HealthBarContainer.Children.Add(new Rectangle
                {
                    Width = 18,
                    Height = 8,
                    Margin = new Thickness(0, 0, 4, 0),
                    RadiusX = 2,
                    RadiusY = 2,
                    Fill = new SolidColorBrush(dot.Good ? Color.FromRgb(0x27, 0xAE, 0x60) : Color.FromRgb(0xE7, 0x4C, 0x3C)),
                    ToolTip = $"{dot.Month}: {(dot.Good ? "EMI deducted" : "EMI missed")}"
                });
            }

            TxtInfo.Text = _summary.Ledger.Count == 0
                ? "No loan activity in monsum.DBF for this employee."
                : $"Data from monsum.DBF  |  Total EMI deducted: ₹ {_summary.TotalEmiDeducted:N2}";
        }

        private void ClearScreen()
        {
            LoanGrid.ItemsSource = null;
            TxtTotalApproved.Text = TxtTotalEmi.Text = TxtOutstanding.Text = "₹ 0.00";
            TxtHealth.Text = "0 / 0 Paid";
            HealthBarContainer.Children.Clear();
            _summary = null;
        }

        private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null || _summary == null)
            {
                MessageBox.Show("Please select an employee first", "Warning");
                return;
            }

            try
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "Excel Workbook|*.xlsx",
                    FileName = $"LoanLedger_{_selected.EmpCode}.xlsx"
                };
                if (dlg.ShowDialog() != true) return;

                using (var wb = new XLWorkbook())
                {
                    var ws = wb.Worksheets.Add("Loan Ledger");
                    ws.Cell("A1").Value = $"Loan Ledger - {_selected.DisplayName}";
                    ws.Cell("A1").Style.Font.SetBold();

                    ws.Cell("A3").Value = "Total Loan Given";   ws.Cell("B3").Value = _summary.TotalApproved;
                    ws.Cell("A4").Value = "Recovered So Far";   ws.Cell("B4").Value = _summary.TotalRecovered;
                    ws.Cell("A5").Value = "Outstanding";        ws.Cell("B5").Value = _summary.Outstanding;
                    ws.Cell("A6").Value = "Total EMI Deducted"; ws.Cell("B6").Value = _summary.TotalEmiDeducted;

                    ws.Cell("A8").Value = "Month / Year";
                    ws.Cell("B8").Value = "Pending Loan Amount";
                    ws.Cell("C8").Value = "EMI Deducted";
                    ws.Range("A8:C8").Style.Font.SetBold();

                    int row = 9;
                    foreach (var item in _summary.Ledger)
                    {
                        ws.Cell(row, 1).Value = item.MonthYear;
                        if (item.PendingLoanAmount.HasValue) ws.Cell(row, 2).Value = item.PendingLoanAmount.Value; else ws.Cell(row, 2).Value = "-";
                        if (item.EmiDeducted.HasValue) ws.Cell(row, 3).Value = item.EmiDeducted.Value; else ws.Cell(row, 3).Value = "-";
                        row++;
                    }

                    ws.Columns().AdjustToContents();
                    wb.SaveAs(dlg.FileName);
                }

                MessageBox.Show("Excel report generated successfully!", "Success");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting to Excel: {ex.Message}", "Error");
            }
        }

        private void BtnSavePdf_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null)
            {
                MessageBox.Show("Please select an employee first", "Warning");
                return;
            }

            try
            {
                var visits = VillageVisitStore.ForEmployee(_selected.EmpCode);
                var report = ReportBuilders.LoanLedger(
                    _selected.DisplayName, _summary, TxtInfo.Text,
                    RemarkStore.ForEmployee(_selected.EmpCode),
                    visits,
                    VillageVisitStore.Reliability(visits, DateTime.Today));
                ReportPrinter.Print(report, Window.GetWindow(this));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not prepare the report: {ex.Message}", "Print Error");
            }
        }
    }
}
