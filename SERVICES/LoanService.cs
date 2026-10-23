using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using Dapper;
using ARISHrms.Models;
using ARISHrms.Data;

namespace ARISHrms.Services
{
    public static class LoanService
    {
        /// <summary>
        /// Calculate EMI (Equated Monthly Installment)
        /// Formula: EMI = (P * R * (1 + R)^N) / ((1 + R)^N - 1)
        /// where P = Principal, R = Monthly Rate, N = Number of Months
        /// </summary>
        public static decimal CalculateEMI(decimal principal, decimal annualInterestRate, int tenureMonths)
        {
            if (tenureMonths <= 0)
                return 0;

            decimal monthlyRate = annualInterestRate / 12 / 100;

            // Special case: 0% interest
            if (monthlyRate == 0)
                return Math.Round(principal / tenureMonths, 2);

            // EMI Formula
            double rate = (double)monthlyRate;
            double power = Math.Pow(1 + rate, tenureMonths);
            decimal numerator = principal * monthlyRate * (decimal)power;
            decimal denominator = (decimal)(power - 1);

            decimal emi = numerator / denominator;
            return Math.Round(emi, 2);
        }

        /// <summary>
        /// Get outstanding balance for a loan after N months of EMI payments
        /// </summary>
        public static decimal GetOutstandingBalance(int loanId, string empCode)
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();

                // Get loan master info
                var loanMaster = connection.QuerySingleOrDefault<LoanMaster>(
                    "SELECT * FROM LoanMasters WHERE Id = @LoanId AND EmpCode = @EmpCode",
                    new { LoanId = loanId, EmpCode = empCode });

                if (loanMaster == null)
                    return 0;

                // Get all ledger entries for this loan
                var ledgers = connection.Query<LoanLedger>(
                    "SELECT * FROM LoanLedgers WHERE LoanId = @LoanId ORDER BY MonthYear",
                    new { LoanId = loanId }).ToList();

                // Calculate: Principal - Total EMI Deducted
                decimal totalDeducted = ledgers.Sum(l => l.EmiDeducted ?? 0);
                decimal outstanding = loanMaster.LoanAmount - totalDeducted;

                return Math.Max(0, outstanding);
            }
        }

        /// <summary>
        /// Check if employee is in arrears (not paid full EMI for a month)
        /// </summary>
        public static bool IsInArrears(int loanId, string monthYear)
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();

                var ledger = connection.QuerySingleOrDefault<LoanLedger>(
                    "SELECT * FROM LoanLedgers WHERE LoanId = @LoanId AND MonthYear = @MonthYear",
                    new { LoanId = loanId, MonthYear = monthYear });

                if (ledger == null)
                    return false;

                // In arrears if deducted amount < EMI amount
                return (ledger.EmiDeducted ?? 0) < ledger.MonthlyEMI;
            }
        }

        /// <summary>
        /// Create a new loan and generate monthly ledger entries
        /// </summary>
        public static int CreateLoan(string empCode, DateTime loanDate, decimal loanAmount, 
                                     int tenureMonths, decimal annualInterestRate)
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        // Calculate EMI
                        decimal monthlyEmi = CalculateEMI(loanAmount, annualInterestRate, tenureMonths);

                        // Create LoanMaster
                        var loanMaster = new LoanMaster
                        {
                            EmpCode = empCode,
                            LoanDate = loanDate,
                            LoanAmount = loanAmount,
                            TenureMonths = tenureMonths,
                            InterestRate = annualInterestRate,
                            MonthlyEMI = monthlyEmi,
                            Status = "Active"
                        };

                        string insertLoanSql = @"
                            INSERT INTO LoanMasters (EmpCode, LoanDate, LoanAmount, TenureMonths, InterestRate, MonthlyEMI, Status)
                            VALUES (@EmpCode, @LoanDate, @LoanAmount, @TenureMonths, @InterestRate, @MonthlyEMI, @Status);
                            SELECT last_insert_rowid();";

                        int loanId = connection.QuerySingle<int>(insertLoanSql, loanMaster, transaction);

                        // Generate monthly ledger entries
                        var ledgerEntries = new List<LoanLedger>();
                        for (int i = 0; i < tenureMonths; i++)
                        {
                            var monthDate = loanDate.AddMonths(i);
                            string monthYear = monthDate.ToString("yyyy-MM");

                            decimal outstandingBalance = loanAmount;
                            for (int j = 0; j < i; j++)
                            {
                                outstandingBalance -= monthlyEmi;
                            }

                            ledgerEntries.Add(new LoanLedger
                            {
                                LoanId = loanId,
                                EmpCode = empCode,
                                MonthYear = monthYear,
                                MonthlyEMI = monthlyEmi,
                                EmiDeducted = 0,
                                OutstandingBalance = Math.Max(0, outstandingBalance),
                                IsInArrears = false
                            });
                        }

                        string insertLedgerSql = @"
                            INSERT INTO LoanLedgers (LoanId, EmpCode, MonthYear, MonthlyEMI, EmiDeducted, OutstandingBalance, IsInArrears)
                            VALUES (@LoanId, @EmpCode, @MonthYear, @MonthlyEMI, @EmiDeducted, @OutstandingBalance, @IsInArrears)";

                        connection.Execute(insertLedgerSql, ledgerEntries, transaction);

                        transaction.Commit();
                        return loanId;
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        throw new Exception($"Error creating loan: {ex.Message}", ex);
                    }
                }
            }
        }

        /// <summary>
        /// Get all active loans for an employee
        /// </summary>
        public static List<LoanMaster> GetActiveLoansByEmployee(string empCode)
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();
                return connection.Query<LoanMaster>(
                    "SELECT * FROM LoanMasters WHERE EmpCode = @EmpCode AND Status = 'Active' ORDER BY LoanDate DESC",
                    new { EmpCode = empCode }).ToList();
            }
        }

        /// <summary>
        /// Get loan ledger for a specific month
        /// </summary>
        public static LoanLedger GetLoanLedgerForMonth(int loanId, string monthYear)
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();
                return connection.QuerySingleOrDefault<LoanLedger>(
                    "SELECT * FROM LoanLedgers WHERE LoanId = @LoanId AND MonthYear = @MonthYear",
                    new { LoanId = loanId, MonthYear = monthYear });
            }
        }

        /// <summary>
        /// Update EMI deduction for a month (called during payroll processing)
        /// </summary>
        public static void RecordEMIDeduction(int loanId, string monthYear, decimal amountDeducted)
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();

                var ledger = GetLoanLedgerForMonth(loanId, monthYear);
                if (ledger == null)
                    return;

                bool isInArrears = amountDeducted < ledger.MonthlyEMI;

                string updateSql = @"
                    UPDATE LoanLedgers 
                    SET EmiDeducted = @EmiDeducted, IsInArrears = @IsInArrears
                    WHERE LoanId = @LoanId AND MonthYear = @MonthYear";

                connection.Execute(updateSql, new
                {
                    EmiDeducted = amountDeducted,
                    IsInArrears = isInArrears,
                    LoanId = loanId,
                    MonthYear = monthYear
                });
            }
        }

        /// <summary>
        /// Get all loans in arrears (for management alerts)
        /// </summary>
        public static List<LoanLedger> GetLoanArrearsReport()
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();
                return connection.Query<LoanLedger>(
                    @"SELECT ll.* FROM LoanLedgers ll
                      WHERE ll.IsInArrears = 1
                      ORDER BY ll.MonthYear DESC").ToList();
            }
        }

        /// <summary>
        /// Close a loan (mark as paid/closed)
        /// </summary>
        public static void CloseLoan(int loanId)
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();
                connection.Execute("UPDATE LoanMasters SET Status = 'Closed' WHERE Id = @Id", new { Id = loanId });
            }
        }
    }

    public class LoanMaster
    {
        public int Id { get; set; }
        public string EmpCode { get; set; }
        public DateTime LoanDate { get; set; }
        public decimal LoanAmount { get; set; }
        public int TenureMonths { get; set; }
        public decimal InterestRate { get; set; }
        public decimal MonthlyEMI { get; set; }
        public string Status { get; set; } = "Active";
    }

    public class LoanLedger
    {
        public int Id { get; set; }
        public int LoanId { get; set; }
        public string EmpCode { get; set; }
        public string MonthYear { get; set; }
        public decimal MonthlyEMI { get; set; }
        public decimal? EmiDeducted { get; set; } = 0;
        public decimal OutstandingBalance { get; set; }
        public bool IsInArrears { get; set; } = false;
    }
}
