using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ARISHrms.Data;
using ARISHrms.Models;
using ARISHrms.Services;
using Microsoft.Data.Sqlite;
using Dapper;
using ClosedXML.Excel;
using System.Linq;

namespace ARISHrms.Views
{
    public partial class AdvancesView : UserControl
    {
        // NOTE: The Employees table has no salary column yet. PayrollEngine uses the same
        // placeholder, so keep them in sync until a real BasicSalary field is added.
        private const decimal PlaceholderBasicSalary = 50000;

        private List<Employee> _employees = new List<Employee>();
        private string _selectedEmpCode;

        public AdvancesView()
        {
            InitializeComponent();
            LoadEmployeeList();
        }

        private void LoadEmployeeList()
        {
            try
            {
                using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
                {
                    connection.Open();
                    _employees = connection.Query<Employee>("SELECT * FROM Employees WHERE Status = 'Y' ORDER BY FullName").ToList();
                }

                CmbSelectEmployee.ItemsSource = _employees;
                CmbSelectEmployee.DisplayMemberPath = "FullName";
                CmbSelectEmployee.SelectedValuePath = "EmpCode";

                if (_employees.Count > 0)
                    CmbSelectEmployee.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading employees: {ex.Message}", "Error");
            }
        }

        private void CmbSelectEmployee_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbSelectEmployee.SelectedValue == null)
                return;

            _selectedEmpCode = CmbSelectEmployee.SelectedValue.ToString();
            LoadAdvanceData(_selectedEmpCode);
        }

        private decimal GetMonthlyLimit()
        {
            return decimal.TryParse(TxtMonthlyLimit.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var limit) && limit > 0
                ? limit
                : 5000;
        }

        private void LoadAdvanceData(string empCode)
        {
            try
            {
                var advances = AdvanceService.GetEmployeeAdvances(empCode, DateTime.Now.Year);
                AdvanceGrid.ItemsSource = advances;

                decimal totalAdvances = advances.Sum(a => a.AdvanceAmount);
                TxtTotalAdvances.Text = $"₹ {totalAdvances:N2}";
                TxtLifetimeAdvance.Text = $"Total Lifetime Advance: ₹ {GetLifetimeAdvance(empCode):N2}";

                // Health: how many of the last 6 months stayed within the monthly limit
                decimal limit = GetMonthlyLimit();
                TrendDotsContainer.Children.Clear();

                int safeMonths = 0;
                for (int i = 5; i >= 0; i--)
                {
                    string monthKey = DateTime.Now.AddMonths(-i).ToString("yyyy-MM");
                    decimal monthTotal = GetMonthTotal(empCode, monthKey);
                    bool safe = monthTotal <= limit;
                    if (safe) safeMonths++;

                    TrendDotsContainer.Children.Add(new Ellipse
                    {
                        Width = 12,
                        Height = 12,
                        Margin = new Thickness(0, 0, 6, 0),
                        Fill = new SolidColorBrush(safe ? Color.FromRgb(0x10, 0xB9, 0x81) : Color.FromRgb(0xEF, 0x44, 0x44)),
                        ToolTip = $"{monthKey}: ₹ {monthTotal:N0}"
                    });
                }

                TxtHealth.Text = $"{safeMonths} / 6 Safe";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading advance data: {ex.Message}", "Error");
            }
        }

        private static decimal GetMonthTotal(string empCode, string monthYear)
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();
                return connection.QuerySingleOrDefault<decimal?>(
                    "SELECT SUM(AdvanceAmount) FROM AdvanceLedgers WHERE EmpCode = @EmpCode AND MonthYear = @MonthYear",
                    new { EmpCode = empCode, MonthYear = monthYear }) ?? 0;
            }
        }

        private static decimal GetLifetimeAdvance(string empCode)
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();
                return connection.QuerySingleOrDefault<decimal?>(
                    "SELECT SUM(AdvanceAmount) FROM AdvanceLedgers WHERE EmpCode = @EmpCode",
                    new { EmpCode = empCode }) ?? 0;
            }
        }

        private void BtnRequestAdvance_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedEmpCode))
            {
                MessageBox.Show("Please select an employee first", "Warning");
                return;
            }

            if (!decimal.TryParse(TxtAdvanceAmount.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
            {
                MessageBox.Show("Please enter a valid advance amount", "Warning");
                return;
            }

            try
            {
                var (isOverLimit, available) = AdvanceService.CheckAdvanceLimit(_selectedEmpCode, amount, PlaceholderBasicSalary);

                if (isOverLimit)
                {
                    var answer = MessageBox.Show(
                        $"This advance exceeds the allowed limit (available: ₹ {Math.Max(0, available):N2}).\n\nRecord it anyway and flag it as over-limit?",
                        "Over Limit", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                    if (answer != MessageBoxResult.Yes)
                        return;
                }

                AdvanceService.RequestAdvance(_selectedEmpCode, amount, PlaceholderBasicSalary);
                TxtAdvanceAmount.Clear();
                LoadAdvanceData(_selectedEmpCode);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error requesting advance: {ex.Message}", "Error");
            }
        }

        private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedEmpCode))
            {
                MessageBox.Show("Please select an employee first", "Warning");
                return;
            }

            try
            {
                var advances = AdvanceService.GetEmployeeAdvances(_selectedEmpCode, DateTime.Now.Year);

                using (var workbook = new XLWorkbook())
                {
                    var worksheet = workbook.Worksheets.Add("Advances");

                    worksheet.Cell("A1").Value = "Month";
                    worksheet.Cell("B1").Value = "Advance Amount";
                    worksheet.Cell("C1").Value = "Recovery Amount";
                    worksheet.Cell("D1").Value = "Pending";
                    worksheet.Cell("E1").Value = "Is Recovered";
                    worksheet.Cell("F1").Value = "Over Limit";

                    int row = 2;
                    foreach (var advance in advances)
                    {
                        worksheet.Cell($"A{row}").Value = advance.MonthYear;
                        worksheet.Cell($"B{row}").Value = advance.AdvanceAmount;
                        worksheet.Cell($"C{row}").Value = advance.RecoveryAmount;
                        worksheet.Cell($"D{row}").Value = advance.AdvanceAmount - advance.RecoveryAmount;
                        worksheet.Cell($"E{row}").Value = advance.IsRecovered ? "Yes" : "No";
                        worksheet.Cell($"F{row}").Value = advance.IsOverLimit ? "Yes" : "No";
                        row++;
                    }

                    if (row > 2)
                    {
                        var table = worksheet.Range($"A1:F{row - 1}").CreateTable();
                        table.Theme = XLTableTheme.TableStyleMedium2;
                    }

                    worksheet.Columns().AdjustToContents();

                    string fileName = $"Advances_{_selectedEmpCode}_{DateTime.Now:yyyyMMdd}.xlsx";
                    workbook.SaveAs(fileName);

                    MessageBox.Show($"Advance data exported to {fileName}", "Success");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting to Excel: {ex.Message}", "Error");
            }
        }

        private void BtnSavePdf_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedEmpCode))
            {
                MessageBox.Show("Please select an employee first", "Warning");
                return;
            }

            try
            {
                var advances = AdvanceService.GetEmployeeAdvances(_selectedEmpCode, DateTime.Now.Year);

                string content = $"Advance Report for Employee: {_selectedEmpCode}\n";
                content += $"Generated: {DateTime.Now:dd-MM-yyyy HH:mm}\n\n";

                foreach (var advance in advances)
                {
                    decimal pending = advance.AdvanceAmount - advance.RecoveryAmount;
                    content += $"Month: {advance.MonthYear}\n";
                    content += $"Amount: ₹{advance.AdvanceAmount}\n";
                    content += $"Recovered: ₹{advance.RecoveryAmount}\n";
                    content += $"Pending: ₹{pending}\n";
                    content += $"Status: {(advance.IsRecovered ? "Recovered" : "Pending")}\n\n";
                }

                // Plain-text report for now; real PDF output would need a PDF library
                string fileName = $"Advance_Report_{_selectedEmpCode}_{DateTime.Now:yyyyMMdd}.txt";
                System.IO.File.WriteAllText(fileName, content);

                MessageBox.Show($"Advance report saved to {fileName} (text format, not PDF)", "Success");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving report: {ex.Message}", "Error");
            }
        }
    }
}
