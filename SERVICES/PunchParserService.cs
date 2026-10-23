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

        public static void ParseEmployeesFromDbf(string filePath)
        {
            var employees = new List<Employee>();
            string fileContent = File.ReadAllText(filePath);

            // 🔥 FIX: The Regex is now highly forgiving. 
            // It uses '.' (any character) instead of strictly requiring numbers or letters.
            // This ensures new employees with blank categories or shifts are still captured!
            string pattern = @"(?:[YN\s\*])?(?<EmpCode>\d{4})\s*(?<CardNo>\d{8})(?<Name>.{25})(?<Sex>.)(?<Comp>.{3})\s+(?<Dept>.{3})\s+(?<Cat>.{3})(?<Shift>.)";
            MatchCollection matches = Regex.Matches(fileContent, pattern);

            foreach (Match match in matches)
            {
                string rawEmpCode = match.Groups["EmpCode"].Value.Trim();
                string finalEmpCode = int.Parse(rawEmpCode).ToString("D4");

                // 🔥 FIX: Added .Trim() to every single property to strip invisible DBF spacing
                employees.Add(new Employee
                {
                    EmpCode = finalEmpCode,
                    CardNo = match.Groups["CardNo"].Value.Trim(),
                    FullName = match.Groups["Name"].Value.Trim(),
                    Department = match.Groups["Dept"].Value.Trim(),
                    Category = match.Groups["Cat"].Value.Trim(),
                    Shift = match.Groups["Shift"].Value.Trim()
                });
            }

            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();
                // INSERT OR REPLACE gracefully handles updates for existing employees and adds new ones
                string sql = @"INSERT OR REPLACE INTO Employees 
                             (EmpCode, CardNo, FullName, Department, Category, Shift) 
                             VALUES (@EmpCode, @CardNo, @FullName, @Department, @Category, @Shift);";
                connection.Execute(sql, employees);
            }
        }
    }
}