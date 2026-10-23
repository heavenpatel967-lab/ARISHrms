using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using Dapper;
using ARISHrms.Data;

namespace ARISHrms.Services
{
    public class VillageVisit
    {
        public int Id { get; set; }
        public string EmpCode { get; set; }
        public int? LeaveTypeId { get; set; }
        public DateTime DepartureDate { get; set; }
        public DateTime ExpectedReturn { get; set; }
        public DateTime? ActualReturn { get; set; }
        public int LateDays { get; set; } = 0;
        public decimal LatePenalty { get; set; } = 0;
        public string ReasonNotes { get; set; }
        public string ApprovedBy { get; set; }
        public string Status { get; set; } = "Approved";
    }

    public class LeaveBalance
    {
        public int Id { get; set; }
        public string EmpCode { get; set; }
        public int Year { get; set; }
        public int? LeaveTypeId { get; set; }
        public int BalanceAtStart { get; set; }
        public int TakenDays { get; set; } = 0;
        public int RemainingBalance { get; set; }
        public int CarryOverDays { get; set; } = 0;
        public DateTime LastUpdated { get; set; } = DateTime.Now;
    }

    public class LeaveType
    {
        public int Id { get; set; }
        public string LeaveCode { get; set; }
        public string LeaveName { get; set; }
        public int MaxDaysPerYear { get; set; }
    }

    public static class LeaveService
    {
        /// <summary>
        /// Calculate late days from expected return date
        /// </summary>
        public static int CalculateLateDays(DateTime expectedReturn, DateTime actualReturn)
        {
            if (actualReturn <= expectedReturn)
                return 0;

            return (int)(actualReturn.Date - expectedReturn.Date).TotalDays;
        }

        /// <summary>
        /// Calculate late penalty (default Rs 500 per day)
        /// </summary>
        public static decimal CalculateLatePenalty(int lateDays, decimal penaltyPerDay = 500)
        {
            return lateDays * penaltyPerDay;
        }

        /// <summary>
        /// Initialize leave balance for new employee (called on hire)
        /// </summary>
        public static void InitializeLeaveBalance(string empCode, int year, int leaveTypeId, int maxDays)
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();

                // Check if already exists
                var existing = connection.QuerySingleOrDefault<LeaveBalance>(
                    @"SELECT * FROM LeaveBalance 
                      WHERE EmpCode = @EmpCode AND Year = @Year AND LeaveTypeId = @LeaveTypeId",
                    new { EmpCode = empCode, Year = year, LeaveTypeId = leaveTypeId });

                if (existing != null)
                    return;

                // Create new balance record
                var balance = new LeaveBalance
                {
                    EmpCode = empCode,
                    Year = year,
                    LeaveTypeId = leaveTypeId,
                    BalanceAtStart = maxDays,
                    TakenDays = 0,
                    RemainingBalance = maxDays,
                    CarryOverDays = 0
                };

                string insertSql = @"
                    INSERT INTO LeaveBalance (EmpCode, Year, LeaveTypeId, BalanceAtStart, TakenDays, RemainingBalance, CarryOverDays, LastUpdated)
                    VALUES (@EmpCode, @Year, @LeaveTypeId, @BalanceAtStart, @TakenDays, @RemainingBalance, @CarryOverDays, @LastUpdated)";

                connection.Execute(insertSql, balance);
            }
        }

        /// <summary>
        /// Get leave balance for employee in given year
        /// </summary>
        public static List<LeaveBalance> GetLeaveBalance(string empCode, int? year = null)
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();

                year = year ?? DateTime.Now.Year;

                return connection.Query<LeaveBalance>(
                    @"SELECT * FROM LeaveBalance 
                      WHERE EmpCode = @EmpCode AND Year = @Year
                      ORDER BY LeaveTypeId",
                    new { EmpCode = empCode, Year = year }).ToList();
            }
        }

        /// <summary>
        /// Record village visit (leave taken)
        /// </summary>
        public static int RecordVillageVisit(string empCode, int? leaveTypeId, 
                                            DateTime departureDate, DateTime expectedReturn, 
                                            string reasonNotes, string approvedBy = "System")
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        // Create village visit record
                        var visit = new VillageVisit
                        {
                            EmpCode = empCode,
                            LeaveTypeId = leaveTypeId,
                            DepartureDate = departureDate,
                            ExpectedReturn = expectedReturn,
                            ReasonNotes = reasonNotes,
                            ApprovedBy = approvedBy,
                            Status = "Approved"
                        };

                        string insertVisitSql = @"
                            INSERT INTO VillageVisits (EmpCode, LeaveTypeId, DepartureDate, ExpectedReturn, ReasonNotes, ApprovedBy, Status)
                            VALUES (@EmpCode, @LeaveTypeId, @DepartureDate, @ExpectedReturn, @ReasonNotes, @ApprovedBy, @Status);
                            SELECT last_insert_rowid();";

                        int visitId = connection.QuerySingle<int>(insertVisitSql, visit, transaction);

                        // Calculate days taken
                        int daysTaken = (int)(expectedReturn.Date - departureDate.Date).TotalDays + 1;

                        // Update leave balance
                        string updateBalanceSql = @"
                            UPDATE LeaveBalance 
                            SET TakenDays = TakenDays + @DaysTaken,
                                RemainingBalance = BalanceAtStart - (TakenDays + @DaysTaken),
                                LastUpdated = @Now
                            WHERE EmpCode = @EmpCode AND Year = @Year AND LeaveTypeId = @LeaveTypeId";

                        connection.Execute(updateBalanceSql, new
                        {
                            DaysTaken = daysTaken,
                            EmpCode = empCode,
                            Year = departureDate.Year,
                            LeaveTypeId = leaveTypeId,
                            Now = DateTime.Now
                        }, transaction);

                        transaction.Commit();
                        return visitId;
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        throw new Exception($"Error recording village visit: {ex.Message}", ex);
                    }
                }
            }
        }

        /// <summary>
        /// Update actual return date and calculate late penalty
        /// </summary>
        public static void UpdateActualReturn(int visitId, DateTime actualReturn, decimal penaltyPerDay = 500)
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();

                var visit = connection.QuerySingleOrDefault<VillageVisit>(
                    "SELECT * FROM VillageVisits WHERE Id = @Id",
                    new { Id = visitId });

                if (visit == null)
                    return;

                // Calculate late days
                int lateDays = CalculateLateDays(visit.ExpectedReturn, actualReturn);
                decimal latePenalty = CalculateLatePenalty(lateDays, penaltyPerDay);

                string updateSql = @"
                    UPDATE VillageVisits 
                    SET ActualReturn = @ActualReturn, LateDays = @LateDays, LatePenalty = @LatePenalty
                    WHERE Id = @Id";

                connection.Execute(updateSql, new
                {
                    ActualReturn = actualReturn,
                    LateDays = lateDays,
                    LatePenalty = latePenalty,
                    Id = visitId
                });
            }
        }

        /// <summary>
        /// Get all village visits for employee in given year
        /// </summary>
        public static List<VillageVisit> GetVillageVisits(string empCode, int? year = null)
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();

                year = year ?? DateTime.Now.Year;

                return connection.Query<VillageVisit>(
                    @"SELECT * FROM VillageVisits 
                      WHERE EmpCode = @EmpCode 
                      AND strftime('%Y', DepartureDate) = @Year
                      ORDER BY DepartureDate DESC",
                    new { EmpCode = empCode, Year = year.ToString() }).ToList();
            }
        }

        /// <summary>
        /// Get visits that are still pending actual return
        /// </summary>
        public static List<VillageVisit> GetPendingReturns()
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();

                return connection.Query<VillageVisit>(
                    @"SELECT * FROM VillageVisits 
                      WHERE ActualReturn IS NULL
                      ORDER BY ExpectedReturn ASC").ToList();
            }
        }

        /// <summary>
        /// Get late returns (actual return > expected return)
        /// </summary>
        public static List<VillageVisit> GetLateReturns()
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();

                return connection.Query<VillageVisit>(
                    @"SELECT * FROM VillageVisits 
                      WHERE LateDays > 0
                      ORDER BY LateDays DESC").ToList();
            }
        }

        /// <summary>
        /// Calculate remaining leave balance for current month
        /// </summary>
        public static int GetRemainingLeaveBalance(string empCode, int? leaveTypeId = null, int? year = null)
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();

                year = year ?? DateTime.Now.Year;

                string query = leaveTypeId.HasValue ?
                    @"SELECT RemainingBalance FROM LeaveBalance 
                      WHERE EmpCode = @EmpCode AND Year = @Year AND LeaveTypeId = @LeaveTypeId" :
                    @"SELECT SUM(RemainingBalance) FROM LeaveBalance 
                      WHERE EmpCode = @EmpCode AND Year = @Year";

                object param = leaveTypeId.HasValue ?
                    new { EmpCode = empCode, Year = year, LeaveTypeId = leaveTypeId } :
                    (object)new { EmpCode = empCode, Year = year };

                var result = connection.QuerySingleOrDefault<int?>(query, param);
                return result ?? 0;
            }
        }

        /// <summary>
        /// Get total late penalty for an employee (current year)
        /// </summary>
        public static decimal GetTotalLatePenalty(string empCode, int? year = null)
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();

                year = year ?? DateTime.Now.Year;

                var result = connection.QuerySingleOrDefault<decimal?>(
                    @"SELECT SUM(LatePenalty) FROM VillageVisits 
                      WHERE EmpCode = @EmpCode 
                      AND strftime('%Y', DepartureDate) = @Year",
                    new { EmpCode = empCode, Year = year.ToString() });

                return result ?? 0;
            }
        }

        /// <summary>
        /// Handle year-end carry-over (move unused days to next year, max 5 days)
        /// </summary>
        public static void ProcessYearEndCarryOver(string empCode, int maxCarryOverDays = 5)
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        int currentYear = DateTime.Now.Year;
                        int nextYear = currentYear + 1;

                        // Get current year balances
                        var balances = connection.Query<LeaveBalance>(
                            @"SELECT * FROM LeaveBalance 
                              WHERE EmpCode = @EmpCode AND Year = @Year",
                            new { EmpCode = empCode, Year = currentYear }, transaction).ToList();

                        foreach (var balance in balances)
                        {
                            int carryOverDays = Math.Min(balance.RemainingBalance, maxCarryOverDays);

                            // Update current year record
                            connection.Execute(
                                @"UPDATE LeaveBalance SET CarryOverDays = @CarryOverDays WHERE Id = @Id",
                                new { CarryOverDays = carryOverDays, Id = balance.Id }, transaction);

                            // Create next year record with carry-over
                            var nextYearBalance = new LeaveBalance
                            {
                                EmpCode = empCode,
                                Year = nextYear,
                                LeaveTypeId = balance.LeaveTypeId,
                                BalanceAtStart = balance.BalanceAtStart + carryOverDays,
                                TakenDays = 0,
                                RemainingBalance = balance.BalanceAtStart + carryOverDays,
                                CarryOverDays = carryOverDays
                            };

                            string insertSql = @"
                                INSERT OR REPLACE INTO LeaveBalance 
                                (EmpCode, Year, LeaveTypeId, BalanceAtStart, TakenDays, RemainingBalance, CarryOverDays, LastUpdated)
                                VALUES (@EmpCode, @Year, @LeaveTypeId, @BalanceAtStart, @TakenDays, @RemainingBalance, @CarryOverDays, @LastUpdated)";

                            connection.Execute(insertSql, nextYearBalance, transaction);
                        }

                        transaction.Commit();
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        throw new Exception($"Error processing year-end carry-over: {ex.Message}", ex);
                    }
                }
            }
        }
    }
}
