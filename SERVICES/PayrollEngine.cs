using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using Dapper;
using ARISHrms.Data;
using ARISHrms.Models;

namespace ARISHrms.Services
{
    public class SalarySlip
    {
        public int Id { get; set; }
        public string EmpCode { get; set; }
        public string MonthYear { get; set; }
        public decimal BasicSalary { get; set; }
        public decimal Dearness { get; set; }
        public decimal OtherAllowances { get; set; }
        public decimal Gross { get; set; }
        public decimal LoanEMI { get; set; } = 0;
        public decimal AdvanceRecovery { get; set; } = 0;
        public decimal LatePenalty { get; set; } = 0;
        public decimal PF { get; set; } = 0;
        public decimal TaxDeduction { get; set; } = 0;
        public decimal TotalDeduction { get; set; }
        public decimal NetSalary { get; set; }
        public DateTime GeneratedDate { get; set; } = DateTime.Now;
    }

    public static class PayrollEngine
    {
        /// <summary>
        /// Calculate complete salary slip for an employee for a given month
        /// </summary>
        public static SalarySlip CalculateSalarySlip(string empCode, string monthYear)
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();

                // Get employee
                var employee = connection.QuerySingleOrDefault<Employee>(
                    "SELECT * FROM Employees WHERE EmpCode = @EmpCode",
                    new { EmpCode = empCode });

                if (employee == null)
                    throw new Exception($"Employee {empCode} not found");

                decimal basicSalary = 50000; // Default - should come from employee master
                decimal dearness = basicSalary * 0.05m; // DA = 5% of basic
                decimal otherAllowances = 0; // Can be configured per company

                // ===== EARNINGS =====
                decimal gross = basicSalary + dearness + otherAllowances;

                // ===== DEDUCTIONS =====

                // 1. Loan EMI
                decimal loanEMI = GetLoanDeductionForMonth(empCode, monthYear);

                // 2. Advance Recovery
                decimal advanceRecovery = GetAdvanceDeductionForMonth(empCode, monthYear);

                // 3. Late Penalty (from village visits)
                decimal latePenalty = GetLatePenaltyForMonth(empCode, monthYear);

                // 4. Provident Fund (PF = 12% of Gross)
                decimal pf = Math.Round(gross * 0.12m, 2);

                // 5. Income Tax (simplified - can be enhanced with tax slab logic)
                decimal taxDeduction = CalculateTaxDeduction(gross, pf);

                decimal totalDeduction = loanEMI + advanceRecovery + latePenalty + pf + taxDeduction;
                decimal netSalary = gross - totalDeduction;

                // Create salary slip object
                var slip = new SalarySlip
                {
                    EmpCode = empCode,
                    MonthYear = monthYear,
                    BasicSalary = basicSalary,
                    Dearness = dearness,
                    OtherAllowances = otherAllowances,
                    Gross = gross,
                    LoanEMI = loanEMI,
                    AdvanceRecovery = advanceRecovery,
                    LatePenalty = latePenalty,
                    PF = pf,
                    TaxDeduction = taxDeduction,
                    TotalDeduction = totalDeduction,
                    NetSalary = Math.Max(0, netSalary),
                    GeneratedDate = DateTime.Now
                };

                return slip;
            }
        }

        /// <summary>
        /// Generate and save salary slip to database
        /// </summary>
        public static int GenerateSalarySlip(string empCode, string monthYear)
        {
            var slip = CalculateSalarySlip(empCode, monthYear);

            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();

                // Check if already exists
                var existing = connection.QuerySingleOrDefault<int?>(
                    "SELECT Id FROM SalarySlips WHERE EmpCode = @EmpCode AND MonthYear = @MonthYear",
                    new { EmpCode = empCode, MonthYear = monthYear });

                if (existing.HasValue)
                {
                    // Update existing
                    string updateSql = @"
                        UPDATE SalarySlips SET
                            BasicSalary = @BasicSalary,
                            Dearness = @Dearness,
                            OtherAllowances = @OtherAllowances,
                            Gross = @Gross,
                            LoanEMI = @LoanEMI,
                            AdvanceRecovery = @AdvanceRecovery,
                            LatePenalty = @LatePenalty,
                            PF = @PF,
                            TaxDeduction = @TaxDeduction,
                            TotalDeduction = @TotalDeduction,
                            NetSalary = @NetSalary,
                            GeneratedDate = @GeneratedDate
                        WHERE EmpCode = @EmpCode AND MonthYear = @MonthYear";

                    connection.Execute(updateSql, slip);
                    return existing.Value;
                }
                else
                {
                    // Insert new
                    string insertSql = @"
                        INSERT INTO SalarySlips (EmpCode, MonthYear, BasicSalary, Dearness, OtherAllowances, Gross, 
                                               LoanEMI, AdvanceRecovery, LatePenalty, PF, TaxDeduction, TotalDeduction, NetSalary, GeneratedDate)
                        VALUES (@EmpCode, @MonthYear, @BasicSalary, @Dearness, @OtherAllowances, @Gross, 
                               @LoanEMI, @AdvanceRecovery, @LatePenalty, @PF, @TaxDeduction, @TotalDeduction, @NetSalary, @GeneratedDate);
                        SELECT last_insert_rowid();";

                    return connection.QuerySingle<int>(insertSql, slip);
                }
            }
        }

        /// <summary>
        /// Generate salary slips for all employees in a month
        /// </summary>
        public static List<SalarySlip> GenerateMonthlyPayroll(string monthYear)
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();

                // Get all active employees
                var employees = connection.Query<Employee>(
                    "SELECT DISTINCT EmpCode FROM Employees WHERE Status = 'Y' ORDER BY EmpCode").ToList();

                var slips = new List<SalarySlip>();

                foreach (var emp in employees)
                {
                    try
                    {
                        int slipId = GenerateSalarySlip(emp.EmpCode, monthYear);
                        var slip = connection.QuerySingle<SalarySlip>(
                            "SELECT * FROM SalarySlips WHERE Id = @Id",
                            new { Id = slipId });
                        slips.Add(slip);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error generating slip for {emp.EmpCode}: {ex.Message}");
                    }
                }

                return slips;
            }
        }

        /// <summary>
        /// Get salary slip for employee in given month
        /// </summary>
        public static SalarySlip GetSalarySlip(string empCode, string monthYear)
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();

                return connection.QuerySingleOrDefault<SalarySlip>(
                    "SELECT * FROM SalarySlips WHERE EmpCode = @EmpCode AND MonthYear = @MonthYear",
                    new { EmpCode = empCode, MonthYear = monthYear });
            }
        }

        /// <summary>
        /// Get payroll register for a month (all employees)
        /// </summary>
        public static List<SalarySlip> GetPayrollRegister(string monthYear)
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();

                return connection.Query<SalarySlip>(
                    "SELECT * FROM SalarySlips WHERE MonthYear = @MonthYear ORDER BY EmpCode",
                    new { MonthYear = monthYear }).ToList();
            }
        }

        /// <summary>
        /// Get loan EMI deduction for employee in given month
        /// </summary>
        private static decimal GetLoanDeductionForMonth(string empCode, string monthYear)
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();

                var deductions = connection.Query<decimal?>(
                    @"SELECT SUM(MonthlyEMI) FROM LoanLedgers 
                      WHERE EmpCode = @EmpCode AND MonthYear = @MonthYear",
                    new { EmpCode = empCode, MonthYear = monthYear }).FirstOrDefault();

                return deductions ?? 0;
            }
        }

        /// <summary>
        /// Get advance recovery deduction for employee in given month
        /// </summary>
        private static decimal GetAdvanceDeductionForMonth(string empCode, string monthYear)
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();

                // Get pending advances
                var advances = connection.Query<AdvanceLedger>(
                    @"SELECT * FROM AdvanceLedgers 
                      WHERE EmpCode = @EmpCode AND IsRecovered = 0",
                    new { EmpCode = empCode }).ToList();

                if (!advances.Any())
                    return 0;

                // Calculate recovery amount (spread over 3 months by default)
                decimal totalPending = advances.Sum(a => a.AdvanceAmount - a.RecoveryAmount);
                return Math.Round(totalPending / 3, 2);
            }
        }

        /// <summary>
        /// Get late penalty deduction for employee in given month
        /// </summary>
        private static decimal GetLatePenaltyForMonth(string empCode, string monthYear)
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();

                var penalty = connection.QuerySingleOrDefault<decimal?>(
                    @"SELECT SUM(LatePenalty) FROM VillageVisits 
                      WHERE EmpCode = @EmpCode 
                      AND strftime('%Y-%m', DepartureDate) = @MonthYear",
                    new { EmpCode = empCode, MonthYear = monthYear });

                return penalty ?? 0;
            }
        }

        /// <summary>
        /// Calculate income tax (simplified slab system)
        /// India Tax Slab (approximate):
        /// 0 - 2.5L: Nil
        /// 2.5L - 5L: 5%
        /// 5L - 10L: 20%
        /// 10L+: 30%
        /// </summary>
        private static decimal CalculateTaxDeduction(decimal gross, decimal pf)
        {
            // Taxable Income = Gross - PF
            decimal taxableIncome = gross - pf;

            // Simplified monthly calculation (annual slabs / 12)
            decimal monthlyTax = 0;

            if (taxableIncome > 83333) // 10L / 12
                monthlyTax = Math.Round((taxableIncome - 83333) * 0.30m + 4167m, 2);
            else if (taxableIncome > 41667) // 5L / 12
                monthlyTax = Math.Round((taxableIncome - 41667) * 0.20m + 417m, 2);
            else if (taxableIncome > 20833) // 2.5L / 12
                monthlyTax = Math.Round((taxableIncome - 20833) * 0.05m, 2);

            return Math.Max(0, monthlyTax);
        }

        /// <summary>
        /// Get payroll statistics for a month
        /// </summary>
        public static (decimal TotalGross, decimal TotalDeductions, decimal TotalNet, int EmployeeCount) 
            GetPayrollStats(string monthYear)
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();

                var stats = connection.QuerySingleOrDefault<dynamic>(
                    @"SELECT 
                        COUNT(*) as EmployeeCount,
                        SUM(Gross) as TotalGross,
                        SUM(TotalDeduction) as TotalDeductions,
                        SUM(NetSalary) as TotalNet
                      FROM SalarySlips 
                      WHERE MonthYear = @MonthYear",
                    new { MonthYear = monthYear });

                if (stats == null)
                    return (0, 0, 0, 0);

                return (
                    stats.TotalGross ?? 0,
                    stats.TotalDeductions ?? 0,
                    stats.TotalNet ?? 0,
                    stats.EmployeeCount ?? 0
                );
            }
        }
    }
}
