using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using Dapper;
using ARISHrms.Models;
using ARISHrms.Data;

namespace ARISHrms.Services
{
    public static class AttendanceProcessorService
    {
        public static void ProcessRawPunches()
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();

                var earlyLunchEmps = connection.Query<string>("SELECT EmpCode FROM SpecialRules WHERE RuleType = 'ShiftEarlyLunch'").ToList();
                var rawPunches = connection.Query<RawPunch>("SELECT * FROM RawPunches").ToList();

                var groupedPunches = rawPunches.GroupBy(p => new { p.EmpCode, p.PunchTime.Date }).ToList();
                var dailyRecords = new List<DailyAttendance>();

                foreach (var group in groupedPunches)
                {
                    var punchesForDay = group.OrderBy(p => p.PunchTime).ToList();
                    int punchCount = punchesForDay.Count;

                    // It's an exception if there's an odd number of punches, OR if there are more than 4 punches (messy day)
                    var daily = new DailyAttendance { EmpCode = group.Key.EmpCode, WorkDate = group.Key.Date, IsException = (punchCount % 2 != 0 || punchCount > 4) };

                    if (punchCount > 0) daily.PunchIn = punchesForDay[0].PunchTime;
                    if (punchCount == 2) daily.PunchOut = punchesForDay[1].PunchTime;
                    else if (punchCount > 2)
                    {
                        daily.LunchOut = punchesForDay[1].PunchTime;
                        daily.LunchIn = punchesForDay[2].PunchTime;
                        if (punchCount >= 4) daily.PunchOut = punchesForDay[3].PunchTime;
                        if (punchCount >= 5) daily.Punch5 = punchesForDay[4].PunchTime;
                        if (punchCount >= 6) daily.Punch6 = punchesForDay[5].PunchTime;
                        if (punchCount >= 7) daily.Punch7 = punchesForDay[6].PunchTime;
                        if (punchCount >= 8) daily.Punch8 = punchesForDay[7].PunchTime;
                    }

                    // ⚙️ SHIFT EARLY LUNCH RULE
                    if (earlyLunchEmps.Contains(daily.EmpCode))
                    {
                        if (daily.LunchOut.HasValue && daily.LunchOut.Value.TimeOfDay >= new TimeSpan(11, 30, 0) && daily.LunchOut.Value.TimeOfDay <= new TimeSpan(12, 20, 0))
                        {
                            daily.LunchOut = new DateTime(daily.WorkDate.Year, daily.WorkDate.Month, daily.WorkDate.Day, 12, 30, 0);
                        }
                        if (daily.LunchIn.HasValue && daily.LunchIn.Value.TimeOfDay > new TimeSpan(12, 20, 0) && daily.LunchIn.Value.TimeOfDay <= new TimeSpan(12, 50, 0))
                        {
                            daily.LunchIn = new DateTime(daily.WorkDate.Year, daily.WorkDate.Month, daily.WorkDate.Day, 13, 0, 0);
                        }
                    }

                    dailyRecords.Add(daily);
                }

                connection.Execute("DELETE FROM DailyAttendance");
                string insertSql = "INSERT INTO DailyAttendance (EmpCode, WorkDate, PunchIn, LunchOut, LunchIn, PunchOut, Punch5, Punch6, Punch7, Punch8, IsException) VALUES (@EmpCode, @WorkDate, @PunchIn, @LunchOut, @LunchIn, @PunchOut, @Punch5, @Punch6, @Punch7, @Punch8, @IsException);";
                connection.Execute(insertSql, dailyRecords);
            }
        }
    }
}