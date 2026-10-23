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
        public string Kind { get; set; } = "Other";
        public string Error { get; set; }
        public int MappedColumns { get; set; }

        /// <summary>3 = EMPMAS, 2 = mstsal, 1 = monsum (the files the old Payroll Companion read), 0 = other.</summary>
        public int Priority { get; set; }
        public bool IsUsed { get; set; }
    }

    public class DbfScanResult
    {
        public string Folder { get; set; } = "";
        public List<DbfFileInfo> Files { get; } = new List<DbfFileInfo>();

        /// <summary>Files actually used to build the employee list, highest priority first.</summary>
        public List<DbfFileInfo> SourceFiles { get; } = new List<DbfFileInfo>();
        public DbfFileInfo EmployeeMasterFile => SourceFiles.FirstOrDefault();

        public int EmployeesImported { get; set; }
        public int ActiveCount { get; set; }
        public int LeftCount { get; set; }
        public string ReportPath { get; set; }
        public string MappingSummary { get; set; } = "";

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
                sb.AppendLine($"{f.FileName}   [{f.Kind}]{(f.IsUsed ? "  <- used for employees" : "")}");
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
            sb.AppendLine(SourceFiles.Count > 0
                ? $"Employee files used (in priority order): {string.Join(" > ", SourceFiles.Select(f => f.FileName))}\n" +
                  $"Employees imported/updated: {EmployeesImported}  (active {ActiveCount}, left {LeftCount})"
                : "No employee file was recognized in this folder.");
            if (!string.IsNullOrEmpty(MappingSummary))
            {
                sb.AppendLine();
                sb.AppendLine(MappingSummary);
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Scans a folder for .dbf files and builds the employee list the same way the old ARIS Payroll
    /// Companion did: EMPMAS.DBF, then mstsal.DBF, then monsum.DBF are merged (the first file to supply a
    /// value wins, later files fill blanks). If none of those files exist, the best employee-looking
    /// file (by its columns) is used instead.
    /// </summary>
    public static class DbfFolderScanner
    {
        private const int MaxFiles = 300;

        private static int PriorityForName(string path)
        {
            switch (Path.GetFileNameWithoutExtension(path).ToLowerInvariant())
            {
                case "empmas": return 3;
                case "mstsal": return 2;
                case "monsum": return 1;
                default: return 0;
            }
        }

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

            DbfFileInfo bestGeneric = null;
            long bestGenericScore = -1;
            var candidatesByPriority = new Dictionary<int, List<(DbfFileInfo info, int depth)>>();

            foreach (var path in paths)
            {
                var info = new DbfFileInfo { FullPath = path, Priority = PriorityForName(path) };
                result.Files.Add(info);

                try
                {
                    // Read just the header + first row (fast, safe for big files)
                    var peek = DbfReader.Read(path, 1);
                    info.RecordCount = peek.RecordCount;
                    info.Columns = peek.Fields;

                    if (DbfReader.IsFoxProSystemFile(path, peek))
                    {
                        info.Kind = "FoxPro system file (ignored)";
                        info.Priority = 0;
                        continue;
                    }

                    var cols = DbfReader.MapEmployeeColumns(peek);
                    info.MappedColumns = cols.Score;

                    bool hasIdentity = cols.Code != null || cols.Card != null || cols.CompCode != null;

                    if (info.Priority > 0 && hasIdentity)
                    {
                        info.Kind = $"Employee file ({Path.GetFileNameWithoutExtension(path).ToUpperInvariant()})";
                        int depth = path.Substring(folder.Length).Count(ch => ch == Path.DirectorySeparatorChar || ch == Path.AltDirectorySeparatorChar);
                        if (!candidatesByPriority.TryGetValue(info.Priority, out var list))
                            candidatesByPriority[info.Priority] = list = new List<(DbfFileInfo, int)>();
                        list.Add((info, depth));
                    }
                    else if (info.Priority > 0)
                    {
                        info.Kind = "Expected employee file, but no employee-code column found";
                    }
                    else if (cols.IsEmployeeMaster)
                    {
                        info.Kind = "Possible employee file";
                        long score = cols.Score * 1_000_000L + Math.Min(peek.RecordCount, 999_999);
                        if (score > bestGenericScore) { bestGenericScore = score; bestGeneric = info; }
                    }
                }
                catch (Exception ex)
                {
                    info.Kind = "Unreadable";
                    info.Error = ex.Message;
                }
            }

            // Highest priority first (EMPMAS > mstsal > monsum). If a name exists in several subfolders
            // (backup copies), use the one closest to the folder the user picked.
            foreach (var kv in candidatesByPriority.OrderByDescending(k => k.Key))
            {
                var pick = kv.Value.OrderBy(x => x.depth).ThenByDescending(x => x.info.RecordCount).First().info;
                pick.IsUsed = true;
                result.SourceFiles.Add(pick);
            }

            // None of the known names exist: fall back to the best employee-looking file
            if (result.SourceFiles.Count == 0 && bestGeneric != null)
            {
                bestGeneric.IsUsed = true;
                bestGeneric.Kind = "Employee file (detected by columns)";
                result.SourceFiles.Add(bestGeneric);
            }

            return result;
        }

        /// <summary>Import employees from the recognized employee file(s) (add/update only).</summary>
        public static int ImportEmployees(DbfScanResult scan)
        {
            var merged = BuildEmployees(scan);
            if (merged.Count == 0) return 0;
            SaveEmployees(scan, merged, removeStale: false, out _, out _);
            return merged.Count;
        }

        /// <summary>Read and merge the employee files WITHOUT touching the database.</summary>
        public static List<Employee> BuildEmployees(DbfScanResult scan)
        {
            if (scan.SourceFiles.Count == 0)
                return new List<Employee>();

            var perFile = new List<List<Employee>>();
            var mappings = new StringBuilder();

            foreach (var file in scan.SourceFiles)
            {
                var table = DbfReader.Read(file.FullPath);
                var cols = DbfReader.MapEmployeeColumns(table);

                var employees = DbfReader.ReadEmployees(table);
                perFile.Add(employees);
                mappings.AppendLine(DescribeMapping(file.FileName, cols, employees.Count));
            }

            var merged = DbfReader.MergeEmployees(perFile);
            if (merged.Count == 0)
                return merged;

            // Last resort: none of the files had names (e.g. only monsum was found) - look in other DBFs
            if (merged.Any(e => string.IsNullOrEmpty(e.FullName)))
                FillNamesFromOtherFiles(scan, merged, mappings);

            scan.EmployeesImported = merged.Count;
            scan.ActiveCount = merged.Count(e => e.Status != "N");
            scan.LeftCount = merged.Count(e => e.Status == "N");

            mappings.AppendLine();
            mappings.AppendLine("First 5 employees:");
            foreach (var e in merged.Take(5))
                mappings.AppendLine($"  {e.EmpCode} | {e.FullName} | card {e.CardNo} | dept {e.Department} | cat {e.Category} | desig {e.Designation} | basic {e.BasicSalary:0.##} | wage {e.WageType} | status {(string.IsNullOrEmpty(e.Status) ? "-" : e.Status)}");
            scan.MappingSummary = mappings.ToString();
            return merged;
        }

        /// <summary>Employee codes already stored in the app that are NOT in this sync (candidates to remove).</summary>
        public static List<string> FindStaleEmployeeCodes(List<Employee> merged)
        {
            var incoming = new HashSet<string>(merged.Select(e => e.EmpCode));
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();
                return connection.Query<string>("SELECT EmpCode FROM Employees ORDER BY EmpCode")
                                 .Where(c => !incoming.Contains(c)).ToList();
            }
        }

        /// <summary>
        /// Save the merged employees. With removeStale, employees that are no longer in the files are deleted -
        /// unless they have loans, advances, village visits, documents or payslips, which are always kept.
        /// </summary>
        public static void SaveEmployees(DbfScanResult scan, List<Employee> merged, bool removeStale,
                                         out int removed, out int keptBecauseInUse)
        {
            removed = 0;
            keptBecauseInUse = 0;

            var stale = removeStale ? FindStaleEmployeeCodes(merged) : new List<string>();

            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    UpsertEmployees(connection, merged, transaction);

                    foreach (var code in stale)
                    {
                        int deleted = connection.Execute(@"
                            DELETE FROM Employees
                            WHERE EmpCode = @code
                              AND NOT EXISTS (SELECT 1 FROM LoanMasters WHERE EmpCode = @code)
                              AND NOT EXISTS (SELECT 1 FROM AdvanceLedgers WHERE EmpCode = @code)
                              AND NOT EXISTS (SELECT 1 FROM VillageVisits WHERE EmpCode = @code)
                              AND NOT EXISTS (SELECT 1 FROM EmployeeDocuments WHERE EmpCode = @code)
                              AND NOT EXISTS (SELECT 1 FROM SalarySlips WHERE EmpCode = @code);",
                            new { code }, transaction);

                        if (deleted > 0)
                        {
                            // balances are created automatically when the Leaves screen opens - nothing worth keeping
                            connection.Execute("DELETE FROM LeaveBalance WHERE EmpCode = @code;", new { code }, transaction);
                            removed++;
                        }
                        else keptBecauseInUse++;
                    }

                    transaction.Commit();
                }
            }

            scan.EmployeesImported = merged.Count;
        }

        private static string DescribeMapping(string file, EmployeeColumns c, int count)
        {
            string M(string col) => col ?? "-";
            return $"{file}: {count} employee(s)\n" +
                   $"   code={M(c.Code)}  card={M(c.Card)}/{M(c.CompCode)}  name={M(c.Name)}  dept={M(c.Dept)}  cat={M(c.Category)}  " +
                   $"desig={M(c.Designation)}  basic={M(c.Basic)}  wage={M(c.WageType)}  month={M(c.YearMonth)}  leaving={M(c.LeavingDate)}";
        }

        private static void FillNamesFromOtherFiles(DbfScanResult scan, List<Employee> employees, StringBuilder log)
        {
            foreach (var file in scan.Files
                .Where(f => !f.IsUsed && f.Error == null && f.RecordCount > 0)
                .OrderByDescending(f => f.RecordCount))
            {
                try
                {
                    var probe = new DbfTable();
                    probe.Fields.AddRange(file.Columns);
                    var c = DbfReader.MapEmployeeColumns(probe);
                    if (c.Code == null || c.Name == null) continue;   // header says this file has no code+name

                    var names = DbfReader.ReadNames(DbfReader.Read(file.FullPath));
                    int filled = 0;
                    foreach (var emp in employees)
                        if (string.IsNullOrEmpty(emp.FullName) && names.TryGetValue(emp.EmpCode, out var n))
                        { emp.FullName = n; filled++; }

                    if (filled > 0)
                    {
                        log.AppendLine($"Names for {filled} employee(s) were filled in from {file.FileName}");
                        return;
                    }
                }
                catch { /* unreadable file - try the next one */ }
            }
        }

        /// <summary>
        /// Insert new employees and update existing ones. Only values that are actually present in the
        /// DBF overwrite what is stored, so details entered inside this app are never erased.
        /// A status ("Y"/"N") only changes when a LEAVING_DT column told us something.
        /// </summary>
        public static void UpsertEmployees(List<Employee> employees)
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    UpsertEmployees(connection, employees, transaction);
                    transaction.Commit();
                }
            }
        }

        private static void UpsertEmployees(SqliteConnection connection, List<Employee> employees, SqliteTransaction transaction)
        {
            connection.Execute(@"
                        INSERT INTO Employees (EmpCode, CardNo, FullName, Department, Category, Shift, Designation, BasicSalary, WageType, Status)
                        VALUES (@EmpCode, @CardNo, @FullName, @Department, @Category, COALESCE(NULLIF(@Shift, ''), 'B'), @Designation, @BasicSalary, @WageType, COALESCE(NULLIF(@Status, ''), 'Y'))
                        ON CONFLICT(EmpCode) DO UPDATE SET
                            CardNo = CASE WHEN excluded.CardNo <> '' THEN excluded.CardNo ELSE Employees.CardNo END,
                            FullName = CASE WHEN excluded.FullName <> '' THEN excluded.FullName ELSE Employees.FullName END,
                            Department = CASE WHEN excluded.Department <> '' THEN excluded.Department ELSE Employees.Department END,
                            Category = CASE WHEN excluded.Category <> '' THEN excluded.Category ELSE Employees.Category END,
                            Shift = CASE WHEN @Shift <> '' THEN @Shift ELSE Employees.Shift END,
                            Designation = CASE WHEN excluded.Designation <> '' THEN excluded.Designation ELSE Employees.Designation END,
                            BasicSalary = CASE WHEN excluded.BasicSalary > 0 THEN excluded.BasicSalary ELSE Employees.BasicSalary END,
                            WageType = CASE WHEN excluded.WageType <> '' THEN excluded.WageType ELSE Employees.WageType END,
                            Status = CASE WHEN @Status <> '' THEN @Status ELSE Employees.Status END;",
                        employees, transaction);
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
