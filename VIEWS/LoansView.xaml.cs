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
    public partial class LoansView : UserControl
    {
        private List<Employee> _employees = new List<Employee>();
        private string _selectedEmpCode;

        public LoansView()
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
            LoadLoanData(_selectedEmpCode);
        }

        private void LoadLoanData(string empCode)
        {
            try
            {
                var loans = LoanService.GetActiveLoansByEmployee(empCode);
                var ledgers = LoanService.GetLedgersForEmployee(empCode);

                LoanGrid.ItemsSource = ledgers;

                decimal totalApproved = loans.Sum(l => l.LoanAmount);
                decimal totalEmi = ledgers.Sum(l => l.EmiDeducted ?? 0);
                decimal outstanding = loans.Sum(l => LoanService.GetOutstandingBalance(l.Id, empCode));

                TxtTotalApproved.Text = $"₹ {totalApproved:N2}";
                TxtTotalEmi.Text = $"₹ {totalEmi:N2}";
                TxtOutstanding.Text = $"₹ {outstanding:N2}";

                // Repayment health: months fully paid vs. total scheduled months
                int totalMonths = ledgers.Count;
                int paidMonths = ledgers.Count(l => (l.EmiDeducted ?? 0) >= l.MonthlyEMI && l.MonthlyEMI > 0);
                TxtHealth.Text = $"{paidMonths} / {totalMonths} Paid";

                HealthBarContainer.Children.Clear();
                for (int i = 0; i < Math.Min(totalMonths, 36); i++)
                {
                    bool paid = (ledgers[i].EmiDeducted ?? 0) >= ledgers[i].MonthlyEMI;
                    HealthBarContainer.Children.Add(new Rectangle
                    {
                        Width = 6,
                        Height = 10,
                        Margin = new Thickness(0, 0, 2, 0),
                        Fill = new SolidColorBrush(paid ? Color.FromRgb(0x10, 0xB9, 0x81) : Color.FromRgb(0x47, 0x55, 0x69))
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading loan data: {ex.Message}", "Error");
            }
        }

        private void BtnCreateLoan_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedEmpCode))
            {
                MessageBox.Show("Please select an employee first", "Warning");
                return;
            }

            if (!decimal.TryParse(TxtLoanAmount.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
            {
                MessageBox.Show("Please enter a valid loan amount", "Warning");
                return;
            }

            if (!int.TryParse(TxtLoanTenure.Text, out var tenure) || tenure <= 0)
            {
                MessageBox.Show("Please enter a valid number of months", "Warning");
                return;
            }

            if (!decimal.TryParse(TxtLoanRate.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var rate) || rate < 0)
            {
                MessageBox.Show("Please enter a valid interest rate (0 or more)", "Warning");
                return;
            }

            try
            {
                decimal emi = LoanService.CalculateEMI(amount, rate, tenure);
                LoanService.CreateLoan(_selectedEmpCode, DateTime.Now, amount, tenure, rate);

                MessageBox.Show($"Loan created. Monthly EMI: ₹ {emi:N2}", "Success");

                TxtLoanAmount.Clear();
                TxtLoanTenure.Clear();
                LoadLoanData(_selectedEmpCode);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error creating loan: {ex.Message}", "Error");
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
                var loans = LoanService.GetActiveLoansByEmployee(_selectedEmpCode);

                using (var workbook = new XLWorkbook())
                {
                    var worksheet = workbook.Worksheets.Add("Loans");

                    worksheet.Cell("A1").Value = "Loan ID";
                    worksheet.Cell("B1").Value = "Loan Date";
                    worksheet.Cell("C1").Value = "Loan Amount";
                    worksheet.Cell("D1").Value = "Tenure (Months)";
                    worksheet.Cell("E1").Value = "Interest Rate %";
                    worksheet.Cell("F1").Value = "Monthly EMI";
                    worksheet.Cell("G1").Value = "Outstanding Balance";
                    worksheet.Cell("H1").Value = "Status";

                    int row = 2;
                    foreach (var loan in loans)
                    {
                        decimal outstanding = LoanService.GetOutstandingBalance(loan.Id, _selectedEmpCode);

                        worksheet.Cell($"A{row}").Value = loan.Id;
                        worksheet.Cell($"B{row}").Value = loan.LoanDate.ToString("dd-MM-yyyy");
                        worksheet.Cell($"C{row}").Value = loan.LoanAmount;
                        worksheet.Cell($"D{row}").Value = loan.TenureMonths;
                        worksheet.Cell($"E{row}").Value = loan.InterestRate;
                        worksheet.Cell($"F{row}").Value = loan.MonthlyEMI;
                        worksheet.Cell($"G{row}").Value = outstanding;
                        worksheet.Cell($"H{row}").Value = loan.Status;
                        row++;
                    }

                    if (row > 2)
                    {
                        var table = worksheet.Range($"A1:H{row - 1}").CreateTable();
                        table.Theme = XLTableTheme.TableStyleMedium2;
                    }

                    worksheet.Columns().AdjustToContents();

                    string fileName = $"Loans_{_selectedEmpCode}_{DateTime.Now:yyyyMMdd}.xlsx";
                    workbook.SaveAs(fileName);

                    MessageBox.Show($"Loan data exported to {fileName}", "Success");
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
                var loans = LoanService.GetActiveLoansByEmployee(_selectedEmpCode);

                string content = $"Loan Report for Employee: {_selectedEmpCode}\n";
                content += $"Generated: {DateTime.Now:dd-MM-yyyy HH:mm}\n\n";

                foreach (var loan in loans)
                {
                    decimal outstanding = LoanService.GetOutstandingBalance(loan.Id, _selectedEmpCode);
                    content += $"Loan ID: {loan.Id}\n";
                    content += $"Amount: ₹{loan.LoanAmount}\n";
                    content += $"EMI: ₹{loan.MonthlyEMI}\n";
                    content += $"Outstanding: ₹{outstanding}\n";
                    content += $"Status: {loan.Status}\n\n";
                }

                // Plain-text report for now; real PDF output would need a PDF library
                string fileName = $"Loan_Report_{_selectedEmpCode}_{DateTime.Now:yyyyMMdd}.txt";
                System.IO.File.WriteAllText(fileName, content);

                MessageBox.Show($"Loan report saved to {fileName} (text format, not PDF)", "Success");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving report: {ex.Message}", "Error");
            }
        }
    }
}
