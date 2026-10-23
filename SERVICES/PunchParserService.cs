using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Dapper;
using ARISHrms.Models;
using ARISHrms.Data;

namespace ARISHrms.Services
{
    public static class PunchParserService
    {
        public static void ParseAndSaveFile(string filePath)
        {
            var punchesToSave = new List<RawPunch>();
            string[] lines = File.ReadAllLines(filePath);

            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.Length < 14) continue;

                try
                {
                    string datePart = line.Substring(0, 6);
                    string timePart = line.Substring(8, 4);
                    DateTime punchTime = DateTime.ParseExact(datePart + timePart, "ddMMyyHHmm", CultureInfo.InvariantCulture);

                    string rawEmpCode = line.Substring(13).Trim();
                    string finalEmpCode = int.Parse(rawEmpCode).ToString("D4");

                    punchesToSave.Add(new RawPunch { EmpCode = finalEmpCode, PunchTime = punchTime });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error parsing line: {line}. Reason: {ex.Message}");
                }
            }

            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();
                string sql = "INSERT INTO RawPunches (EmpCode, PunchTime) VALUES (@EmpCode, @PunchTime);";
                connection.Execute(sql, punchesToSave);
            }
        }

        public static int ParseEmployeesFromDbf(string filePath)
        {
            // 1) Preferred: read the real column names from the DBF header
            List<Employee> employees = null;
            try { employees = DbfReader.ReadEmployees(filePath); }
            catch (InvalidDataException) { /* not a standard DBF - try the legacy text scan below */ }

            // 2) Fallback: the original ARISAttendance fixed-layout text scan
            if (employees == null || employees.Count == 0)
                employees = DbfReader.ReadEmployeesLegacy(filePath);

            if (employees.Count == 0)
            {
                throw new InvalidDataException(
                    "No employee records could be read from this file.\n\n" +
                    DbfReader.Describe(filePath) +
                    "\nExpected a column for the employee code (e.g. EMPCODE / ECODE / EMPNO).");
            }

            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    // Upsert: update only the fields that come from the DBF, so details entered
                    // inside this app (Designation, Phone, Aadhar, Village, Status) are never wiped.
                    string sql = @"INSERT INTO Employees (EmpCode, CardNo, FullName, Department, Category, Shift)
                                   VALUES (@EmpCode, @CardNo, @FullName, @Department, @Category, @Shift)
                                   ON CONFLICT(EmpCode) DO UPDATE SET
                                       CardNo = CASE WHEN excluded.CardNo <> '' THEN excluded.CardNo ELSE Employees.CardNo END,
                                       FullName = CASE WHEN excluded.FullName <> '' THEN excluded.FullName ELSE Employees.FullName END,
                                       Department = CASE WHEN excluded.Department <> '' THEN excluded.Department ELSE Employees.Department END,
                                       Category = CASE WHEN excluded.Category <> '' THEN excluded.Category ELSE Employees.Category END,
                                       Shift = CASE WHEN excluded.Shift <> '' THEN excluded.Shift ELSE Employees.Shift END;";
                    connection.Execute(sql, employees, transaction);
                    transaction.Commit();
                }
            }

            return employees.Count;
        }
    }
}
