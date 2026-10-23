using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Data.Sqlite;
using Dapper;
using ARISHrms.Data;
using ARISHrms.Models;

namespace ARISHrms.Services
{
    public class DbfFileInfo
    {
        public string FullPath { get; set; } = "";
        public string FileName => Path.GetFileName(FullPath);
        public int RecordCount { get; set; }
        public List<DbfField> Columns { get; set; } = new List<DbfField>();
        public string Kind { get; set; } = "Unrecognized";
        public string Error { get; set; }
        public int MappedColumns { get; set; }
    }

    public class DbfScanResult
    {
        public string Folder { get; set; } = "";
        public List<DbfFileInfo> Files { get; } = new List<DbfFileInfo>();
        public DbfFileInfo EmployeeMasterFile { get; set; }
        public int EmployeesImported { get; set; }
        public string ReportPath { get; set; }
        public string MappingSummary { get; set; } = "";
        public string NamesSourceFile { get; set; }

        public string ToReportText()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"DBF folder scan - {DateTime.Now:dd-MM-yyyy HH:mm}");
            sb.AppendLine($"Folder: {Folder}");
            sb.AppendLine($"DBF files found: {Files.Count}");
            sb.AppendLine(new string('-', 70));

            foreach (var f in Files.OrderBy(x => x.FileName, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine();
                sb.AppendLine($"{f.FileName}   [{f.Kind}]");
                if (f.Error != null)
                {
                    sb.AppendLine($"   Could not read: {f.Error}");
                    continue;
                }
                sb.AppendLine($"   Records: {f.RecordCount}");
                sb.AppendLine("   Columns: " + string.Join(", ", f.Columns.Select(c => $"{c.Name}({c.Type}{c.Length})")));
            }

            sb.AppendLine();
            sb.AppendLine(new string('-', 70));
            sb.AppendLine(EmployeeMasterFile != null
                ? $"Employee master used: {EmployeeMasterFile.FileName} -> {EmployeesImported} employee(s) imported/updated"
                : "No employee master file was recognized in this folder.");
            if (NamesSourceFile != null)
                sb.AppendLine($"Employee names taken from: {NamesSourceFile}");
            if (!string.IsNullOrEmpty(MappingSummary))
            {
                sb.AppendLine();
                sb.AppendLine(MappingSummary);
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Scans a folder for .dbf files, reads each file's header, recognizes the ones it knows how
    /// to import, and produces a report of everything it found.
    /// </summary>
    public static class DbfFolderScanner
    {
        private const int MaxFiles = 300;

        public static DbfScanResult Scan(string folder, bool includeSubfolders = true)
        {
            if (!Directory.Exists(folder))
                throw new DirectoryNotFoundException($"Folder not found: {folder}");

            var result = new DbfScanResult { Folder = folder };

            // Case-insensitive match (MASTER.DBF / master.dbf) and skip any folder we are not allowed to open
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = includeSubfolders,
                MatchCasing = MatchCasing.CaseInsensitive,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.System
            };
            var paths = Directory.EnumerateFiles(folder, "*.dbf", options).Take(MaxFiles).ToList();

            long bestScore = -1;

            foreach (var path in paths)
            {
                var info = new DbfFileInfo { FullPath = path };
                result.Files.Add(info);

                try
                {
                    // Read just the header + first row first (fast, safe for big files)
                    var peek = DbfReader.Read(path, 1);
                    info.RecordCount = peek.RecordCount;
                    info.Columns = peek.Fields;

                    var cols = DbfReader.MapEmployeeColumns(peek);
                    info.MappedColumns = cols.Score;

                    if (cols.IsEmployeeMaster)
                    {
                        info.Kind = "Employee master";
                        // Priority: file named like the payroll summary (monsum) > other "master"-like names
                        // > more mapped columns > more records
                        string fn = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                        int hint = fn.Contains("monsum") ? 2 : (fn.Contains("empmast") || fn.Contains("employee") || fn.Contains("master")) ? 1 : 0;
                        long score = hint * 1_000_000_000L + cols.Score * 1_000_000L + Math.Min(peek.RecordCount, 999_999);
                        if (score > bestScore)
                        {
                            bestScore = score;
                            result.EmployeeMasterFile = info;
                        }
                    }
                }
                catch (Exception ex)
                {
                    info.Kind = "Unreadable";
                    info.Error = ex.Message;
                }
            }

            return result;
        }

        /// <summary>Import employees from the recognized employee-master file (if any).</summary>
        public static int ImportEmployees(DbfScanResult scan)
        {
            if (scan.EmployeeMasterFile == null)
                return 0;

            var table = DbfReader.Read(scan.EmployeeMasterFile.FullPath);
            var cols = DbfReader.MapEmployeeColumns(table);
            var employees = DbfReader.ReadEmployees(table);
            if (employees.Count == 0)
                return 0;

            // The main file (e.g. monsum.dbf) may have no names - look for them in another DBF of the folder
            if (cols.Name == null)
                FillNamesFromOtherFiles(scan, employees);

            UpsertEmployees(employees);
            scan.EmployeesImported = employees.Count;
            scan.MappingSummary = BuildMappingSummary(scan.EmployeeMasterFile.FileName, cols, employees, scan.NamesSourceFile);
            return employees.Count;
        }

        private static void FillNamesFromOtherFiles(DbfScanResult scan, List<Employee> employees)
        {
            foreach (var file in scan.Files
                .Where(f => f != scan.EmployeeMasterFile && f.Error == null && f.RecordCount > 0)
                .OrderByDescending(f => f.RecordCount))
            {
                try
                {
                    var probe = new DbfTable();
                    probe.Fields.AddRange(file.Columns);
                    var c = DbfReader.MapEmployeeColumns(probe);
                    if (c.Code == null || c.Name == null) continue;   // header says this file has no code+name

                    var names = DbfReader.ReadNames(DbfReader.Read(file.FullPath));
                    if (names.Count == 0) continue;

                    int filled = 0;
                    foreach (var emp in employees)
                        if (string.IsNullOrEmpty(emp.FullName) && names.TryGetValue(emp.EmpCode, out var n))
                        { emp.FullName = n; filled++; }

                    if (filled > 0) { scan.NamesSourceFile = file.FileName; return; }
                }
                catch { /* unreadable file - try the next one */ }
            }
        }

        private static string BuildMappingSummary(string file, EmployeeColumns c, List<Employee> employees, string namesFile)
        {
            string M(string col) => col ?? "(not found in file)";
            var sb = new StringBuilder();
            sb.AppendLine($"How {file} was read");
            sb.AppendLine($"  Employee code : {M(c.Code)}");
            sb.AppendLine($"  Name          : {M(c.Name)}{(c.Name == null && namesFile != null ? "  -> filled from " + namesFile : "")}");
            sb.AppendLine($"  Department    : {M(c.Dept)}");
            sb.AppendLine($"  Category      : {M(c.Category)}");
            sb.AppendLine($"  Designation   : {M(c.Designation)}");
            sb.AppendLine($"  Basic salary  : {M(c.Basic)}");
            sb.AppendLine($"  Wage type     : {M(c.WageType)}");
            sb.AppendLine($"  Month column  : {M(c.YearMonth)}  (latest month used per employee)");
            sb.AppendLine();
            sb.AppendLine("First 5 employees imported:");
            foreach (var e in employees.Take(5))
                sb.AppendLine($"  {e.EmpCode} | {e.FullName} | dept {e.Department} | cat {e.Category} | desig {e.Designation} | basic {e.BasicSalary:0.##} | wage {e.WageType}");
            return sb.ToString();
        }

        /// <summary>
        /// Insert new employees and update existing ones. Only values that are actually present in the
        /// DBF overwrite what is stored, so details entered inside this app are never erased.
        /// </summary>
        public static void UpsertEmployees(List<Employee> employees)
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    connection.Execute(@"
                        INSERT INTO Employees (EmpCode, CardNo, FullName, Department, Category, Shift, Designation, BasicSalary, WageType)
                        VALUES (@EmpCode, @CardNo, @FullName, @Department, @Category, COALESCE(NULLIF(@Shift, ''), 'B'), @Designation, @BasicSalary, @WageType)
                        ON CONFLICT(EmpCode) DO UPDATE SET
                            CardNo = CASE WHEN excluded.CardNo <> '' THEN excluded.CardNo ELSE Employees.CardNo END,
                            FullName = CASE WHEN excluded.FullName <> '' THEN excluded.FullName ELSE Employees.FullName END,
                            Department = CASE WHEN excluded.Department <> '' THEN excluded.Department ELSE Employees.Department END,
                            Category = CASE WHEN excluded.Category <> '' THEN excluded.Category ELSE Employees.Category END,
                            Shift = CASE WHEN @Shift <> '' THEN @Shift ELSE Employees.Shift END,
                            Designation = CASE WHEN excluded.Designation <> '' THEN excluded.Designation ELSE Employees.Designation END,
                            BasicSalary = CASE WHEN excluded.BasicSalary > 0 THEN excluded.BasicSalary ELSE Employees.BasicSalary END,
                            WageType = CASE WHEN excluded.WageType <> '' THEN excluded.WageType ELSE Employees.WageType END;",
                        employees, transaction);
                    transaction.Commit();
                }
            }
        }

        /// <summary>Save the scan report next to the app so it can be reviewed or shared.</summary>
        public static string SaveReport(DbfScanResult scan)
        {
            string path = Path.Combine(Directory.GetCurrentDirectory(), $"DbfScanReport_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            File.WriteAllText(path, scan.ToReportText(), Encoding.UTF8);
            scan.ReportPath = path;
            return path;
        }

        // ---- remember the last folder the user picked ----
        public static string GetLastFolder()
        {
            try
            {
                using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
                {
                    connection.Open();
                    return connection.QueryFirstOrDefault<string>(
                        "SELECT SettingValue FROM AppSettings WHERE SettingKey = 'LastDbfFolder'");
                }
            }
            catch { return null; }
        }

        public static void SaveLastFolder(string folder)
        {
            try
            {
                using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
                {
                    connection.Open();
                    connection.Execute(
                        "INSERT OR REPLACE INTO AppSettings (SettingKey, SettingValue) VALUES ('LastDbfFolder', @Path)",
                        new { Path = folder });
                }
            }
            catch { /* remembering the folder is a convenience only */ }
        }
    }
}
