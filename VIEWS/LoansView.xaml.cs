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
                LoanGrid.ItemsSource = loans;

                if (loans.Count == 0)
                {
                    TxtInfo.Text = $"No active loans for employee {empCode}";
                    TxtTotalOutstanding.Text = "₹ 0";
                    return;
                }

                // Calculate total outstanding
                decimal totalOutstanding = 0;
                foreach (var loan in loans)
                {
                    decimal outstanding = LoanService.GetOutstandingBalance(loan.Id, empCode);
                    totalOutstanding += outstanding;
                }

                TxtInfo.Text = $"Showing {loans.Count} active loan(s)";
                TxtTotalOutstanding.Text = $"₹ {totalOutstanding:N2}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading loan data: {ex.Message}", "Error");
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

                    // Headers
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

                    // Format as table
                    var table = worksheet.Range($"A1:H{row - 1}").CreateTable();
                    table.Theme = XLTableTheme.TableStyleMedium2;

                    // Adjust column widths
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

                // For now, show a message - PDF export would require additional library
                string pdfContent = $"Loan Report for Employee: {_selectedEmpCode}\n";
                pdfContent += $"Generated: {DateTime.Now:dd-MM-yyyy HH:mm}\n\n";

                foreach (var loan in loans)
                {
                    decimal outstanding = LoanService.GetOutstandingBalance(loan.Id, _selectedEmpCode);
                    pdfContent += $"Loan ID: {loan.Id}\n";
                    pdfContent += $"Amount: ₹{loan.LoanAmount}\n";
                    pdfContent += $"EMI: ₹{loan.MonthlyEMI}\n";
                    pdfContent += $"Outstanding: ₹{outstanding}\n";
                    pdfContent += $"Status: {loan.Status}\n\n";
                }

                // Save as text for demo (would use iTextSharp or similar for PDF in production)
                string fileName = $"Loan_Report_{_selectedEmpCode}_{DateTime.Now:yyyyMMdd}.txt";
                System.IO.File.WriteAllText(fileName, pdfContent);

                MessageBox.Show($"Loan report saved to {fileName}", "Success");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving report: {ex.Message}", "Error");
            }
        }

        private void BtnCreateLoan_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedEmpCode))
            {
                MessageBox.Show("Please select an employee first", "Warning");
                return;
            }

            // Open a dialog to create a new loan
            try
            {
                decimal loanAmount = 100000; // Default
                int tenureMonths = 24;
                decimal interestRate = 12; // 12% p.a.

                int loanId = LoanService.CreateLoan(_selectedEmpCode, DateTime.Now, loanAmount, tenureMonths, interestRate);
                MessageBox.Show($"Loan created successfully (ID: {loanId})", "Success");
                LoadLoanData(_selectedEmpCode);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error creating loan: {ex.Message}", "Error");
            }
        }
    }
}