using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
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

        private void LoadAdvanceData(string empCode)
        {
            try
            {
                var advances = AdvanceService.GetEmployeeAdvances(empCode, DateTime.Now.Year);
                AdvanceGrid.ItemsSource = advances;

                if (advances.Count == 0)
                {
                    TxtInfo.Text = $"No advances for employee {empCode} this year";
                    TxtTotalPending.Text = "₹ 0";
                    TxtRemainingQuota.Text = "₹ 0";
                    return;
                }

                // Calculate totals
                decimal totalAdvanced = advances.Sum(a => a.AdvanceAmount);
                decimal totalPending = AdvanceService.GetTotalPendingAdvance(empCode);
                
                // Get employee's basic salary (from database)
                decimal basicSalary = 50000; // Default - should come from employee master
                var (isOverLimit, availableAmount) = AdvanceService.CheckAdvanceLimit(empCode, 0, basicSalary);

                TxtInfo.Text = $"Showing {advances.Count} advance(s) - {advances.Count(a => !a.IsRecovered)} pending recovery";
                TxtTotalPending.Text = $"₹ {totalPending:N2}";
                TxtRemainingQuota.Text = $"₹ {Math.Max(0, availableAmount):N2}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading advance data: {ex.Message}", "Error");
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

                    // Headers
                    worksheet.Cell("A1").Value = "Month";
                    worksheet.Cell("B1").Value = "Advance Amount";
                    worksheet.Cell("C1").Value = "Recovery Amount";
                    worksheet.Cell("D1").Value = "Pending";
                    worksheet.Cell("E1").Value = "Is Recovered";
                    worksheet.Cell("F1").Value = "Over Limit";

                    int row = 2;
                    foreach (var advance in advances)
                    {
                        decimal pending = advance.AdvanceAmount - advance.RecoveryAmount;

                        worksheet.Cell($"A{row}").Value = advance.MonthYear;
                        worksheet.Cell($"B{row}").Value = advance.AdvanceAmount;
                        worksheet.Cell($"C{row}").Value = advance.RecoveryAmount;
                        worksheet.Cell($"D{row}").Value = pending;
                        worksheet.Cell($"E{row}").Value = advance.IsRecovered ? "Yes" : "No";
                        worksheet.Cell($"F{row}").Value = advance.IsOverLimit ? "Yes" : "No";

                        row++;
                    }

                    // Format as table
                    var table = worksheet.Range($"A1:F{row - 1}").CreateTable();
                    table.Theme = XLTableTheme.TableStyleMedium2;

                    // Adjust column widths
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

                string pdfContent = $"Advance Report for Employee: {_selectedEmpCode}\n";
                pdfContent += $"Generated: {DateTime.Now:dd-MM-yyyy HH:mm}\n\n";

                foreach (var advance in advances)
                {
                    decimal pending = advance.AdvanceAmount - advance.RecoveryAmount;
                    pdfContent += $"Month: {advance.MonthYear}\n";
                    pdfContent += $"Amount: ₹{advance.AdvanceAmount}\n";
                    pdfContent += $"Recovered: ₹{advance.RecoveryAmount}\n";
                    pdfContent += $"Pending: ₹{pending}\n";
                    pdfContent += $"Status: {(advance.IsRecovered ? "Recovered" : "Pending")}\n\n";
                }

                string fileName = $"Advance_Report_{_selectedEmpCode}_{DateTime.Now:yyyyMMdd}.txt";
                System.IO.File.WriteAllText(fileName, pdfContent);

                MessageBox.Show($"Advance report saved to {fileName}", "Success");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving report: {ex.Message}", "Error");
            }
        }

        private void BtnRequestAdvance_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedEmpCode))
            {
                MessageBox.Show("Please select an employee first", "Warning");
                return;
            }

            try
            {
                decimal advanceAmount = 5000; // Default - would get from input dialog
                decimal basicSalary = 50000; // Default

                int advanceId = AdvanceService.RequestAdvance(_selectedEmpCode, advanceAmount, basicSalary);
                MessageBox.Show($"Advance requested successfully (ID: {advanceId})", "Success");
                LoadAdvanceData(_selectedEmpCode);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error requesting advance: {ex.Message}", "Error");
            }
        }
    }
}