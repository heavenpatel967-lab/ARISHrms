using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using Dapper;
using ARISHrms.Data;

namespace ARISHrms.Services
{
    public class AdvanceLedger
    {
        public int Id { get; set; }
        public string EmpCode { get; set; }
        public string MonthYear { get; set; }
        public decimal AdvanceAmount { get; set; }
        public DateTime AdvanceDate { get; set; } = DateTime.Now;
        public decimal RecoveryAmount { get; set; } = 0;
        public bool IsRecovered { get; set; } = false;
        public bool IsOverLimit { get; set; } = false;
    }

    public static class AdvanceService
    {
        /// <summary>
        /// Check if advance request exceeds monthly limit
        /// Advance Limit = (BasicSalary * 50%) * (Remaining Months / 12)
        /// </summary>
        public static (bool IsOverLimit, decimal AvailableAmount) CheckAdvanceLimit(
            string empCode, decimal requestAmount, decimal basicSalary, decimal limitPercent = 0.5m)
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();

                // Annual advance cap (50% of annual salary)
                decimal annualCap = basicSalary * 12 * limitPercent;

                // Get current month
                int currentMonth = DateTime.Now.Month;
                int remainingMonths = 12 - currentMonth + 1;

                // Monthly quota: cap * remaining months / 12
                decimal monthlyQuota = (annualCap * remainingMonths) / 12;

                // Get total advances already given this year
                int currentYear = DateTime.Now.Year;
                var yearAdvances = connection.Query<AdvanceLedger>(
                    @"SELECT * FROM AdvanceLedgers 
                      WHERE EmpCode = @EmpCode 
                      AND strftime('%Y', AdvanceDate) = @Year",
                    new { EmpCode = empCode, Year = currentYear.ToString() }).ToList();

                decimal totalAdvancedThisYear = yearAdvances.Sum(a => a.AdvanceAmount);
                decimal availableAmount = annualCap - totalAdvancedThisYear;

                bool isOverLimit = (totalAdvancedThisYear + requestAmount) > annualCap;

                return (isOverLimit, availableAmount);
            }
        }

        /// <summary>
        /// Create new advance request
        /// </summary>
        public static int RequestAdvance(string empCode, decimal advanceAmount, decimal basicSalary)
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        // Check limit
                        var (isOverLimit, _) = CheckAdvanceLimit(empCode, advanceAmount, basicSalary);

                        // Create advance record
                        string monthYear = DateTime.Now.ToString("yyyy-MM");
                        var advance = new AdvanceLedger
                        {
                            EmpCode = empCode,
                            MonthYear = monthYear,
                            AdvanceAmount = advanceAmount,
                            AdvanceDate = DateTime.Now,
                            RecoveryAmount = 0,
                            IsRecovered = false,
                            IsOverLimit = isOverLimit
                        };

                        string insertSql = @"
                            INSERT INTO AdvanceLedgers (EmpCode, MonthYear, AdvanceAmount, AdvanceDate, RecoveryAmount, IsRecovered, IsOverLimit)
                            VALUES (@EmpCode, @MonthYear, @AdvanceAmount, @AdvanceDate, @RecoveryAmount, @IsRecovered, @IsOverLimit);
                            SELECT last_insert_rowid();";

                        int advanceId = connection.QuerySingle<int>(insertSql, advance, transaction);
                        transaction.Commit();

                        return advanceId;
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        throw new Exception($"Error requesting advance: {ex.Message}", ex);
                    }
                }
            }
        }

        /// <summary>
        /// Get all advances for an employee (current year)
        /// </summary>
        public static List<AdvanceLedger> GetEmployeeAdvances(string empCode, int? year = null)
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();

                year = year ?? DateTime.Now.Year;

                return connection.Query<AdvanceLedger>(
                    @"SELECT * FROM AdvanceLedgers 
                      WHERE EmpCode = @EmpCode 
                      AND strftime('%Y', AdvanceDate) = @Year
                      ORDER BY AdvanceDate DESC",
                    new { EmpCode = empCode, Year = year.ToString() }).ToList();
            }
        }

        /// <summary>
        /// Record advance recovery from salary
        /// </summary>
        public static void RecordAdvanceRecovery(int advanceId, decimal recoveryAmount)
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();

                var advance = connection.QuerySingleOrDefault<AdvanceLedger>(
                    "SELECT * FROM AdvanceLedgers WHERE Id = @Id",
                    new { Id = advanceId });

                if (advance == null)
                    return;

                decimal totalRecovered = advance.RecoveryAmount + recoveryAmount;
                bool isRecovered = totalRecovered >= advance.AdvanceAmount;

                string updateSql = @"
                    UPDATE AdvanceLedgers 
                    SET RecoveryAmount = @RecoveryAmount, IsRecovered = @IsRecovered
                    WHERE Id = @Id";

                connection.Execute(updateSql, new
                {
                    RecoveryAmount = totalRecovered,
                    IsRecovered = isRecovered,
                    Id = advanceId
                });
            }
        }

        /// <summary>
        /// Get advances pending recovery
        /// </summary>
        public static List<AdvanceLedger> GetPendingAdvances(string empCode)
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();

                return connection.Query<AdvanceLedger>(
                    @"SELECT * FROM AdvanceLedgers 
                      WHERE EmpCode = @EmpCode 
                      AND IsRecovered = 0
                      ORDER BY AdvanceDate DESC",
                    new { EmpCode = empCode }).ToList();
            }
        }

        /// <summary>
        /// Get total advance amount pending recovery
        /// </summary>
        public static decimal GetTotalPendingAdvance(string empCode)
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();

                var result = connection.QuerySingleOrDefault<decimal?>(
                    @"SELECT SUM(AdvanceAmount - RecoveryAmount) 
                      FROM AdvanceLedgers 
                      WHERE EmpCode = @EmpCode AND IsRecovered = 0",
                    new { EmpCode = empCode });

                return result ?? 0;
            }
        }

        /// <summary>
        /// Get advances with over-limit flag (for management report)
        /// </summary>
        public static List<AdvanceLedger> GetOverLimitAdvances()
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();

                return connection.Query<AdvanceLedger>(
                    @"SELECT * FROM AdvanceLedgers 
                      WHERE IsOverLimit = 1
                      ORDER BY AdvanceDate DESC").ToList();
            }
        }

        /// <summary>
        /// Get advance statistics for the month
        /// </summary>
        public static (decimal TotalAdvanced, int CountRequests, decimal OverLimitAmount) GetMonthlyAdvanceStats()
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();

                string monthYear = DateTime.Now.ToString("yyyy-MM");

                var result = connection.QuerySingleOrDefault<dynamic>(
                    @"SELECT 
                        SUM(AdvanceAmount) as TotalAdvanced,
                        COUNT(*) as CountRequests,
                        SUM(CASE WHEN IsOverLimit = 1 THEN AdvanceAmount ELSE 0 END) as OverLimitAmount
                      FROM AdvanceLedgers 
                      WHERE MonthYear = @MonthYear",
                    new { MonthYear = monthYear });

                return (
                    result?.TotalAdvanced ?? 0,
                    result?.CountRequests ?? 0,
                    result?.OverLimitAmount ?? 0
                );
            }
        }
    }
}
