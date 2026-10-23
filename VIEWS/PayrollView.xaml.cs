using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using ARISHrms.Services;
using ClosedXML.Excel;
using System.Linq;

namespace ARISHrms.Views
{
    public partial class PayrollView : UserControl
    {
        private string _selectedMonthYear;

        public PayrollView()
        {
            InitializeComponent();
            DtpMonth.SelectedDate = DateTime.Now;
        }

        private void DtpMonth_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DtpMonth.SelectedDate.HasValue)
            {
                _selectedMonthYear = DtpMonth.SelectedDate.Value.ToString("yyyy-MM");
                LoadPayrollData(_selectedMonthYear);
            }
        }

        private void BtnGeneratePayroll_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedMonthYear))
            {
                MessageBox.Show("Please select a month first", "Warning");
                return;
            }

            try
            {
                var slips = PayrollEngine.GenerateMonthlyPayroll(_selectedMonthYear);
                MessageBox.Show($"Payroll generated for {slips.Count} employees", "Success");
                LoadPayrollData(_selectedMonthYear);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error generating payroll: {ex.Message}", "Error");
            }
        }

        private void LoadPayrollData(string monthYear)
        {
            try
            {
                var slips = PayrollEngine.GetPayrollRegister(monthYear);
                PayrollGrid.ItemsSource = slips;

                if (slips.Count == 0)
                {
                    TxtInfo.Text = $"No payroll data for {monthYear}. Click 'Generate Payroll' to create salary slips.";
                    TxtEmployeeCount.Text = "0";
                    TxtTotalGross.Text = "₹ 0";
                    TxtTotalDeductions.Text = "₹ 0";
                    TxtTotalNet.Text = "₹ 0";
                    return;
                }

                // Calculate totals
                decimal totalGross = slips.Sum(s => s.Gross);
                decimal totalDeductions = slips.Sum(s => s.TotalDeduction);
                decimal totalNet = slips.Sum(s => s.NetSalary);

                TxtEmployeeCount.Text = slips.Count.ToString();
                TxtTotalGross.Text = $"₹ {totalGross:N0}";
                TxtTotalDeductions.Text = $"₹ {totalDeductions:N0}";
                TxtTotalNet.Text = $"₹ {totalNet:N0}";

                TxtInfo.Text = $"Payroll for {monthYear}: {slips.Count} employees processed";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading payroll data: {ex.Message}", "Error");
            }
        }

        private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedMonthYear))
            {
                MessageBox.Show("Please select a month first", "Warning");
                return;
            }

            try
            {
                var slips = PayrollEngine.GetPayrollRegister(_selectedMonthYear);

                if (slips.Count == 0)
                {
                    MessageBox.Show("No payroll data to export", "Warning");
                    return;
                }

                using (var workbook = new XLWorkbook())
                {
                    var worksheet = workbook.Worksheets.Add("Payroll Register");

                    // Headers
                    worksheet.Cell("A1").Value = "Employee Code";
                    worksheet.Cell("B1").Value = "Basic Salary";
                    worksheet.Cell("C1").Value = "Dearness";
                    worksheet.Cell("D1").Value = "Gross";
                    worksheet.Cell("E1").Value = "Loan EMI";
                    worksheet.Cell("F1").Value = "Advance Recovery";
                    worksheet.Cell("G1").Value = "Late Penalty";
                    worksheet.Cell("H1").Value = "PF";
                    worksheet.Cell("I1").Value = "Tax";
                    worksheet.Cell("J1").Value = "Total Deduction";
                    worksheet.Cell("K1").Value = "Net Salary";

                    int row = 2;
                    decimal totalGross = 0, totalDeductions = 0, totalNet = 0;

                    foreach (var slip in slips)
                    {
                        worksheet.Cell($"A{row}").Value = slip.EmpCode;
                        worksheet.Cell($"B{row}").Value = slip.BasicSalary;
                        worksheet.Cell($"C{row}").Value = slip.Dearness;
                        worksheet.Cell($"D{row}").Value = slip.Gross;
                        worksheet.Cell($"E{row}").Value = slip.LoanEMI;
                        worksheet.Cell($"F{row}").Value = slip.AdvanceRecovery;
                        worksheet.Cell($"G{row}").Value = slip.LatePenalty;
                        worksheet.Cell($"H{row}").Value = slip.PF;
                        worksheet.Cell($"I{row}").Value = slip.TaxDeduction;
                        worksheet.Cell($"J{row}").Value = slip.TotalDeduction;
                        worksheet.Cell($"K{row}").Value = slip.NetSalary;

                        totalGross += slip.Gross;
                        totalDeductions += slip.TotalDeduction;
                        totalNet += slip.NetSalary;

                        row++;
                    }

                    // Totals row
                    worksheet.Cell($"A{row}").Value = "TOTAL";
                    worksheet.Cell($"D{row}").Value = totalGross;
                    worksheet.Cell($"J{row}").Value = totalDeductions;
                    worksheet.Cell($"K{row}").Value = totalNet;

                    // Format as table
                    var table = worksheet.Range($"A1:K{row}").CreateTable();
                    table.Theme = XLTableTheme.TableStyleMedium2;

                    // Adjust column widths
                    worksheet.Columns().AdjustToContents();

                    string fileName = $"Payroll_{_selectedMonthYear}_{DateTime.Now:yyyyMMdd}.xlsx";
                    workbook.SaveAs(fileName);

                    MessageBox.Show($"Payroll exported to {fileName}", "Success");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting to Excel: {ex.Message}", "Error");
            }
        }

        private void BtnExportPdf_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedMonthYear))
            {
                MessageBox.Show("Please select a month first", "Warning");
                return;
            }

            try
            {
                var slips = PayrollEngine.GetPayrollRegister(_selectedMonthYear);

                if (slips.Count == 0)
                {
                    MessageBox.Show("No payroll data to export", "Warning");
                    return;
                }

                // For now, export as text (PDF would require iTextSharp library)
                string pdfContent = $"PAYROLL REGISTER - {_selectedMonthYear}\n";
                pdfContent += $"Generated: {DateTime.Now:dd-MM-yyyy HH:mm}\n\n";
                pdfContent += $"{"EmpCode",-12} {"Gross",-15} {"Deductions",-15} {"NetSalary",-15}\n";
                pdfContent += new string('-', 60) + "\n";

                decimal totalGross = 0, totalDeductions = 0, totalNet = 0;

                foreach (var slip in slips)
                {
                    pdfContent += $"{slip.EmpCode,-12} {slip.Gross,-15:N0} {slip.TotalDeduction,-15:N0} {slip.NetSalary,-15:N0}\n";
                    totalGross += slip.Gross;
                    totalDeductions += slip.TotalDeduction;
                    totalNet += slip.NetSalary;
                }

                pdfContent += new string('-', 60) + "\n";
                pdfContent += $"{"TOTAL",-12} {totalGross,-15:N0} {totalDeductions,-15:N0} {totalNet,-15:N0}\n";

                string fileName = $"Payroll_{_selectedMonthYear}_{DateTime.Now:yyyyMMdd}.txt";
                System.IO.File.WriteAllText(fileName, pdfContent);

                MessageBox.Show($"Payroll report saved to {fileName}", "Success");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving report: {ex.Message}", "Error");
            }
        }
    }
}
