using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// Advance history for one employee, read from EasyPay's monsum.DBF (ADVANCE column).
    /// The monthly limit is set per employee here (default 5000); a month above it is flagged red.
    /// </summary>
    public partial class AdvancesView : UserControl
    {
        private List<Employee> _employees = new List<Employee>();
        private MonsumData _monsum;
        private Employee _selected;
        private AdvanceSummary _summary;
        private bool _loading = true;

        public AdvancesView()
        {
            InitializeComponent();
            LoadEmployeeList();
            _loading = false;
            if (_selected != null) LoadAdvanceData();
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
            if (_selected == null || _loading) return;
            LoadAdvanceData();
        }

        private double CurrentLimit()
        {
            return double.TryParse(TxtMonthlyLimit.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
        }

        private void LoadAdvanceData()
        {
            // show this employee's saved limit without writing it back
            bool wasLoading = _loading;
            _loading = true;
            TxtMonthlyLimit.Text = AdvanceLimitStore.Get(_selected.EmpCode).ToString("0", CultureInfo.InvariantCulture);
            _loading = wasLoading;

            Refresh();
        }

        private void Refresh()
        {
            AdvanceGrid.ItemsSource = null;
            TrendDotsContainer.Children.Clear();
            TxtTotalAdvances.Text = "₹ 0.00";
            TxtHealth.Text = "0 / 0 Safe";
            TxtLifetimeAdvance.Text = "Total Lifetime Advance: ₹ 0.00";
            _summary = null;

            if (_selected == null || !EnsureMonsum()) return;

            _summary = _monsum.BuildAdvanceSummary(_selected.EmpCode, _selected.CardNo, CurrentLimit());

            AdvanceGrid.ItemsSource = _summary.Items;
            TxtTotalAdvances.Text = $"₹ {_summary.Total:N2}";
            TxtLifetimeAdvance.Text = $"Total Lifetime Advance: ₹ {_summary.Total:N2}";
            TxtHealth.Text = $"{_summary.SafeCount} / {_summary.Items.Count} Safe";

            foreach (var item in _summary.Items.Skip(Math.Max(0, _summary.Items.Count - 6)))
            {
                TrendDotsContainer.Children.Add(new Ellipse
                {
                    Width = 14,
                    Height = 14,
                    Margin = new Thickness(0, 0, 6, 0),
                    Fill = (SolidColorBrush)new BrushConverter().ConvertFromString(item.StatusColor),
                    ToolTip = $"{item.MonthYear}: ₹ {item.AdvanceAmount:N0}"
                });
            }

            TxtInfo.Text = _summary.Items.Count == 0
                ? "No advances in monsum.DBF for this employee."
                : "Data from monsum.DBF  |  red = above this employee's monthly limit";
        }

        private void TxtMonthlyLimit_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading || _selected == null) return;

            // save as you type, like the old app, then recolor the months
            if (double.TryParse(TxtMonthlyLimit.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var limit) && limit >= 0)
            {
                try { AdvanceLimitStore.Set(_selected.EmpCode, limit); } catch { }
            }
            Refresh();
        }

        private void BtnImportLimits_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Select Advance Limits CSV", Filter = "CSV Files (*.csv)|*.csv" };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var result = AdvanceLimitStore.ImportCsv(dlg.FileName);

                if (result.Updated == 0)
                {
                    MessageBox.Show("No records could be imported. Please make sure the file is a CSV with 'Card No' and 'ADV' headers.",
                        "Import Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                string msg = $"Updated {result.Updated} employee advance limit(s).";
                if (result.Skipped > 0) msg += $"\n\nSkipped {result.Skipped} row(s) (the ADV limit was empty or text).";
                MessageBox.Show(msg, "Import Complete", MessageBoxButton.OK,
                    result.Skipped > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);

                if (_selected != null) LoadAdvanceData();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not import the CSV: {ex.Message}", "Error");
            }
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
                    FileName = $"AdvanceLedger_{_selected.EmpCode}.xlsx"
                };
                if (dlg.ShowDialog() != true) return;

                using (var wb = new XLWorkbook())
                {
                    var ws = wb.Worksheets.Add("Advance Report");
                    ws.Cell("A1").Value = $"Advance Ledger - {_selected.DisplayName}";
                    ws.Cell("A1").Style.Font.SetBold();

                    ws.Cell("A3").Value = "Total Advances Taken"; ws.Cell("B3").Value = _summary.Total;
                    ws.Cell("A4").Value = "Monthly Limit";        ws.Cell("B4").Value = _summary.Limit;

                    ws.Cell("A6").Value = "Status";
                    ws.Cell("B6").Value = "Month/Year";
                    ws.Cell("C6").Value = "Advance Amount";
                    ws.Range("A6:C6").Style.Font.SetBold();

                    int row = 7;
                    foreach (var item in _summary.Items)
                    {
                        bool safe = item.StatusColor == "#27AE60";
                        ws.Cell(row, 1).Value = safe ? "Safe" : "High";
                        ws.Cell(row, 2).Value = item.MonthYear;
                        ws.Cell(row, 3).Value = item.AdvanceAmount;
                        if (!safe)
                        {
                            ws.Cell(row, 1).Style.Font.FontColor = XLColor.Red;
                            ws.Cell(row, 3).Style.Font.FontColor = XLColor.Red;
                        }
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
            var print = new PrintDialog();
            if (print.ShowDialog() == true)
                print.PrintVisual(this, $"Advance Ledger - {_selected?.EmpCode}");
        }
    }
}
