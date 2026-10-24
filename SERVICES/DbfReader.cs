using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ARISHrms.Models;

namespace ARISHrms.Services
{
    public class DbfField
    {
        public string Name { get; set; } = "";
        public char Type { get; set; }
        public int Length { get; set; }
    }

    public class DbfTable
    {
        public List<DbfField> Fields { get; } = new List<DbfField>();
        public List<Dictionary<string, string>> Rows { get; } = new List<Dictionary<string, string>>();
        public int RecordCount { get; set; }
    }

    /// <summary>Which DBF column feeds which employee property (null = not present in the file).</summary>
    public class EmployeeColumns
    {
        public bool CodeIsStrong;
        public List<string> NameColumns = new List<string>();
        public string Code, Card, CompCode, Name, Dept, Category, Shift, Designation, Basic, WageType, YearMonth, LeavingDate;

        /// <summary>How many of the useful columns were found.</summary>
        public int Score => new[] { Code, Card, Name, Dept, Category, Shift, Designation, Basic }.Count(c => c != null);

        /// <summary>
        /// A usable employee master has a clearly-named employee code column (EMPCD, EMPCODE, ECODE, EMPNO...)
        /// plus at least one more employee attribute. A bare ID/CODE column is not enough: system files
        /// such as FoxPro's FOXUSER.DBF also have ID and NAME columns.
        /// A name column is NOT required: payroll summary files (e.g. monsum.dbf) have no names.
        /// </summary>
        public bool IsEmployeeMaster => CodeIsStrong && Score >= 2;
    }

    /// <summary>
    /// Dependency-free dBASE (.dbf) reader. Reads the real column names from the file header,
    /// so it works with any DBF layout and does not need the 32-bit-only Jet/OLEDB driver.
    /// Streams the file and opens it with shared access, so large files and files that are
    /// open in another program still work.
    /// </summary>
    public static class DbfReader
    {
        public static DbfTable Read(string path, int maxRows = int.MaxValue)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 1 << 16);

            long fileLen = fs.Length;
            if (fileLen < 33)
                throw new InvalidDataException("The file is too small to be a DBF file.");

            var head = new byte[32];
            fs.ReadExactly(head, 0, 32);

            int headerLength = BitConverter.ToUInt16(head, 8);
            int recordLength = BitConverter.ToUInt16(head, 10);
            if (headerLength < 33 || headerLength > fileLen || recordLength < 2)
                throw new InvalidDataException("The file header is not a valid DBF header.");

            var header = new byte[headerLength];
            Array.Copy(head, header, 32);
            fs.ReadExactly(header, 32, headerLength - 32);

            var table = new DbfTable();
            var encoding = Encoding.Latin1;

            // Field descriptors: 32 bytes each, starting at byte 32, terminated by 0x0D
            var offsets = new List<int>();
            int pos = 32;
            int offset = 1; // byte 0 of every record is the deleted-flag
            while (pos + 32 <= headerLength && header[pos] != 0x0D)
            {
                string name = encoding.GetString(header, pos, 11).Split('\0')[0].Trim();
                table.Fields.Add(new DbfField { Name = name, Type = (char)header[pos + 11], Length = header[pos + 16] });
                offsets.Add(offset);
                offset += header[pos + 16];
                pos += 32;
            }

            if (table.Fields.Count == 0 || pos >= headerLength || header[pos] != 0x0D)
                throw new InvalidDataException("No column definitions were found - this is not a standard DBF file.");

            long possible = (fileLen - headerLength) / recordLength;
            int declared = BitConverter.ToInt32(head, 4);
            long recordCount = (declared <= 0 || declared > possible) ? possible : declared;
            table.RecordCount = (int)Math.Min(recordCount, int.MaxValue);

            var rec = new byte[recordLength];
            for (long i = 0; i < recordCount && table.Rows.Count < maxRows; i++)
            {
                try { fs.ReadExactly(rec, 0, recordLength); }
                catch (EndOfStreamException) { break; }

                if (rec[0] == 0x2A) continue; // record marked as deleted

                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int f = 0; f < table.Fields.Count; f++)
                {
                    int len = Math.Min(table.Fields[f].Length, recordLength - offsets[f]);
                    row[table.Fields[f].Name] = len > 0
                        ? encoding.GetString(rec, offsets[f], len).Trim('\0', ' ', '\t')
                        : "";
                }
                table.Rows.Add(row);
            }

            return table;
        }

        /// <summary>
        /// Streams a DBF file row by row without keeping it in memory - for big files such as the daily
        /// attendance file. Only the requested columns are extracted (names are matched ignoring case).
        /// The callback receives a dictionary that is reused between rows, so copy anything you keep.
        /// Return false from the callback to stop early. Returns the column list found in the header.
        /// </summary>
        public static List<DbfField> ForEachRow(string path, IEnumerable<string> columns, Func<Dictionary<string, string>, bool> onRow)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 1 << 16);

            long fileLen = fs.Length;
            if (fileLen < 33)
                throw new InvalidDataException("The file is too small to be a DBF file.");

            var head = new byte[32];
            fs.ReadExactly(head, 0, 32);

            int headerLength = BitConverter.ToUInt16(head, 8);
            int recordLength = BitConverter.ToUInt16(head, 10);
            if (headerLength < 33 || headerLength > fileLen || recordLength < 2)
                throw new InvalidDataException("The file header is not a valid DBF header.");

            var header = new byte[headerLength];
            Array.Copy(head, header, 32);
            fs.ReadExactly(header, 32, headerLength - 32);

            var encoding = Encoding.Latin1;
            var fields = new List<DbfField>();
            var offsets = new List<int>();
            int pos = 32, offset = 1;
            while (pos + 32 <= headerLength && header[pos] != 0x0D)
            {
                string name = encoding.GetString(header, pos, 11).Split('\0')[0].Trim();
                fields.Add(new DbfField { Name = name, Type = (char)header[pos + 11], Length = header[pos + 16] });
                offsets.Add(offset);
                offset += header[pos + 16];
                pos += 32;
            }

            if (fields.Count == 0 || pos >= headerLength || header[pos] != 0x0D)
                throw new InvalidDataException("No column definitions were found - this is not a standard DBF file.");

            var wanted = new HashSet<string>(columns.Select(c => c.ToUpperInvariant()));
            var picks = new List<(string name, int offset, int length)>();
            for (int f = 0; f < fields.Count; f++)
                if (wanted.Contains(fields[f].Name.ToUpperInvariant()))
                    picks.Add((fields[f].Name.ToUpperInvariant(), offsets[f], fields[f].Length));

            long possible = (fileLen - headerLength) / recordLength;
            int declared = BitConverter.ToInt32(head, 4);
            long recordCount = (declared <= 0 || declared > possible) ? possible : declared;

            var rec = new byte[recordLength];
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (long i = 0; i < recordCount; i++)
            {
                try { fs.ReadExactly(rec, 0, recordLength); }
                catch (EndOfStreamException) { break; }

                if (rec[0] == 0x2A) continue; // deleted

                row.Clear();
                foreach (var (name, off, len) in picks)
                {
                    int l = Math.Min(len, recordLength - off);
                    row[name] = l > 0 ? encoding.GetString(rec, off, l).Trim('\0', ' ', '\t') : "";
                }

                if (!onRow(row)) break;
            }

            return fields;
        }

        private static string Normalize(string s) =>
            (s ?? "").Replace("_", "").Replace(" ", "").ToUpperInvariant();

        private static string FindColumn(DbfTable table, params string[] aliases)
        {
            foreach (var alias in aliases)
            {
                var match = table.Fields.FirstOrDefault(f => Normalize(f.Name) == Normalize(alias));
                if (match != null) return match.Name;
            }
            return null;
        }

        private static List<string> FindColumns(DbfTable table, params string[] aliases)
        {
            var found = new List<string>();
            foreach (var alias in aliases)
            {
                var match = table.Fields.FirstOrDefault(f => Normalize(f.Name) == Normalize(alias));
                if (match != null && !found.Contains(match.Name)) found.Add(match.Name);
            }
            return found;
        }

        private static string NormalizeEmpCode(string raw)
        {
            raw = (raw ?? "").Trim();
            if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
                && d >= 0 && d == Math.Truncate(d))
                return ((long)d).ToString("D4");
            return raw;
        }

        /// <summary>Find which columns of a DBF correspond to employee-master fields.</summary>
        public static EmployeeColumns MapEmployeeColumns(DbfTable table)
        {
            string strongCode = FindColumn(table, "EMPCD", "EMPCODE", "ECODE", "EMPNO", "EMPID", "EMPLOYEEID",
                                           "EMPLOYEECODE", "PERNO");
            return new EmployeeColumns
            {
            // Same names the old ARIS Payroll Companion used: EMPCD (code), CARDNO / COMP_CODE (card),
            // EMPNM (name), DEPT, DESIG, CAT, LEAVING_DT. Other common spellings are accepted as fallbacks.
            CodeIsStrong = strongCode != null,
            Code = strongCode ?? FindColumn(table, "CODE", "ID"),
            Card = FindColumn(table, "CARDNO", "CARD", "CARDNUM", "CARDNUMBER"),
            CompCode = FindColumn(table, "COMPCODE"),
            // Old app order per row: EMPNM, then EMP_NAME, then NAME. Other spellings are only a last resort.
            Name = FindColumn(table, "EMPNM", "EMPNAME", "NAME", "ENAME", "FULLNAME", "EMPLOYEENAME", "EMPLOYEE", "NAME1"),
            NameColumns = FindColumns(table, "EMPNM", "EMPNAME", "NAME", "ENAME", "FULLNAME", "EMPLOYEENAME", "EMPLOYEE", "NAME1"),
            Dept = FindColumn(table, "DEPT", "DEPARTMENT", "DEPTCODE", "DEPTNAME"),
            Category = FindColumn(table, "CAT", "CATEGORY", "CATCODE"),
            Shift = FindColumn(table, "SHIFT", "SHFT", "SHIFTCODE", "SHFTCD", "SHF"),
            Designation = FindColumn(table, "DESIG", "DESIGNATION", "DESIGCODE"),
            Basic = FindColumn(table, "BASIC", "BASICSALARY", "BASICPAY"),
            WageType = FindColumn(table, "WAGETYPE", "WAGETYP"),
            YearMonth = FindColumn(table, "YRMONTH", "YEARMONTH", "PAYMONTH"),
            LeavingDate = FindColumn(table, "LEAVINGDT")   // exactly LEAVING_DT, like the old app
            };
        }

        /// <summary>FoxPro keeps window layouts and preferences in FOXUSER.DBF / FOXCODE.DBF - never employee data.</summary>
        public static bool IsFoxProSystemFile(string path, DbfTable table)
        {
            string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            if (name.StartsWith("foxuser") || name.StartsWith("foxcode") || name.StartsWith("foxhelp")) return true;

            // FoxPro resource-file layout: TYPE, ID, NAME, READONLY, CKVAL, DATA, UPDATED
            bool Has(string col) => table.Fields.Any(f => string.Equals(f.Name, col, StringComparison.OrdinalIgnoreCase));
            return Has("CKVAL") && Has("READONLY") && Has("TYPE");
        }

        /// <summary>Turns a YRMONTH-style value (202607, 2026-07, 07/2026, 072026) into a sortable number, or null.</summary>
        private static int? MonthKey(string raw)
        {
            string d = Regex.Replace(raw ?? "", @"\D", "");
            if (d.Length != 6) return null;
            int first4 = int.Parse(d.Substring(0, 4)), last4 = int.Parse(d.Substring(2, 4));
            if (first4 is >= 1990 and <= 2100 && int.Parse(d.Substring(4, 2)) is >= 1 and <= 12)
                return first4 * 100 + int.Parse(d.Substring(4, 2));              // yyyyMM
            if (last4 is >= 1990 and <= 2100 && int.Parse(d.Substring(0, 2)) is >= 1 and <= 12)
                return last4 * 100 + int.Parse(d.Substring(0, 2));               // MMyyyy
            return null;
        }

        private static decimal ParseDecimal(string raw) =>
            decimal.TryParse((raw ?? "").Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : 0m;

        /// <summary>
        /// Old-app rule: a blank LEAVING_DT (ignoring '-', '/', '0') means the employee is active.
        /// A leaving date that is still in the future means the person has not left yet.
        /// Returns "" when the file has no LEAVING_DT column, meaning "unknown - leave status unchanged".
        /// </summary>
        private static string StatusFromLeaving(string raw, bool hasColumn)
        {
            if (!hasColumn) return "";
            string cleaned = (raw ?? "").Replace("-", "").Replace("/", "").Replace("0", "").Trim();
            if (cleaned.Length == 0) return "Y";

            if (AttendanceReportService.TryParseDate(raw, out var when) && when.Date > DateTime.Today)
                return "Y";

            return "N";
        }

        /// <summary>
        /// One Employee per DBF row, read exactly like the old Payroll Companion's ReadEmployees loop:
        /// code = EMPCD (else the card number), card = CARDNO (else COMP_CODE), name = EMPNM, then EMP_NAME,
        /// then NAME - tried row by row. Nothing is collapsed here; combine rows with MergeEmployees.
        /// </summary>
        public static List<Employee> ReadEmployeeRows(DbfTable table)
        {
            var cols = MapEmployeeColumns(table);
            var rows = new List<Employee>();
            if (cols.Code == null && cols.Card == null) return rows;

            string Get(Dictionary<string, string> row, string col) =>
                col != null && row.TryGetValue(col, out var v) ? (v ?? "").Trim() : "";

            foreach (var row in table.Rows)
            {
                // COMP_CODE is the COMPANY code (000001 on every row in mstsal), so it is never used as a card or
                // employee number - the old app did, which merged unrelated rows into employee 0001.
                string card = Get(row, cols.Card);

                string code = Get(row, cols.Code);
                if (string.IsNullOrWhiteSpace(code)) code = card;       // old app: no code -> use the card number
                code = NormalizeEmpCode(code);
                if (string.IsNullOrEmpty(code)) continue;

                string name = "";
                foreach (var col in cols.NameColumns)
                {
                    string v = Get(row, col);
                    if (v.Length > 0) { name = v; break; }
                }

                string rawLeaving = Get(row, cols.LeavingDate);
                string status = StatusFromLeaving(rawLeaving, cols.LeavingDate != null);

                rows.Add(new Employee
                {
                    EmpCode = code,
                    CardNo = card,                         // padded to 8 digits after merging, like the old app
                    FullName = name,
                    Department = Get(row, cols.Dept),
                    Category = Get(row, cols.Category),
                    Shift = Get(row, cols.Shift),
                    Designation = Get(row, cols.Designation),
                    BasicSalary = ParseDecimal(Get(row, cols.Basic)),
                    WageType = Get(row, cols.WageType),
                    Status = status,
                    LeavingDate = status == "N" ? rawLeaving : ""
                });
            }

            return rows;
        }

        /// <summary>Rows of one file, merged like the old app (same person on several rows fills blanks).</summary>
        public static List<Employee> ReadEmployees(DbfTable table) => MergeEmployees(new[] { ReadEmployeeRows(table) });

        /// <summary>
        /// The old app's merge, copied rule for rule. Rows are processed in the order given (EMPMAS rows, then
        /// mstsal rows, then monsum rows). The first row creates the employee; every later row only fills blanks:
        /// name, department, designation, category, shift, and the card number (which is also replaced when it is
        /// "00000001" or contains "0000000"). An employee who is "left" (N) on any row stays left.
        /// </summary>
        public static List<Employee> MergeEmployees(IEnumerable<List<Employee>> sourcesInPriorityOrder)
        {
            var map = new Dictionary<string, Employee>();
            var order = new List<string>();

            foreach (var source in sourcesInPriorityOrder)
            {
                foreach (var e in source)
                {
                    if (!map.TryGetValue(e.EmpCode, out var cur))
                    {
                        map[e.EmpCode] = Clone(e);
                        order.Add(e.EmpCode);
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(cur.FullName) && !string.IsNullOrWhiteSpace(e.FullName)) cur.FullName = e.FullName;

                    if ((string.IsNullOrWhiteSpace(cur.CardNo) || cur.CardNo == "00000001" || cur.CardNo.Contains("0000000"))
                        && !string.IsNullOrWhiteSpace(e.CardNo))
                        cur.CardNo = e.CardNo;

                    if (string.IsNullOrWhiteSpace(cur.Department) && !string.IsNullOrWhiteSpace(e.Department)) cur.Department = e.Department;
                    if (string.IsNullOrWhiteSpace(cur.Designation) && !string.IsNullOrWhiteSpace(e.Designation)) cur.Designation = e.Designation;
                    if (string.IsNullOrWhiteSpace(cur.Category) && !string.IsNullOrWhiteSpace(e.Category)) cur.Category = e.Category;
                    if (string.IsNullOrWhiteSpace(cur.Shift) && !string.IsNullOrWhiteSpace(e.Shift)) cur.Shift = e.Shift;
                    if (cur.BasicSalary <= 0 && e.BasicSalary > 0) cur.BasicSalary = e.BasicSalary;
                    if (string.IsNullOrWhiteSpace(cur.WageType) && !string.IsNullOrWhiteSpace(e.WageType)) cur.WageType = e.WageType;

                    if (cur.Status != "N" && e.Status == "N") { cur.Status = "N"; cur.LeavingDate = e.LeavingDate; cur.StatusSource = e.StatusSource; }
                    else if (string.IsNullOrEmpty(cur.Status) && e.Status == "Y") cur.Status = "Y";
                }
            }

            var result = order.Select(k => map[k]).ToList();
            foreach (var e in result)
                if (!string.IsNullOrWhiteSpace(e.CardNo) && e.CardNo.Length < 8) e.CardNo = e.CardNo.PadLeft(8, '0');   // old app pads at the end

            return result.OrderBy(e => e.EmpCode, StringComparer.Ordinal).ToList();
        }

        private static Employee Clone(Employee e) => new Employee
        {
            EmpCode = e.EmpCode, CardNo = e.CardNo, FullName = e.FullName, Department = e.Department, Category = e.Category,
            Shift = e.Shift, Designation = e.Designation, BasicSalary = e.BasicSalary, WageType = e.WageType,
            Status = e.Status, LeavingDate = e.LeavingDate, StatusSource = e.StatusSource
        };

        /// <summary>
        /// The old Employee List screen replaced each person's department and category with the values from
        /// monsum.DBF: the LAST non-blank value found in the file for that person. Blank values keep what the
        /// master files said.
        /// </summary>
        public static void ApplyMonsumOverrides(IEnumerable<Employee> employees, List<Employee> monsumRows)
        {
            var truth = new Dictionary<string, (string dept, string cat)>();
            foreach (var r in monsumRows)
            {
                if (truth.TryGetValue(r.EmpCode, out var existing))
                    truth[r.EmpCode] = (!string.IsNullOrWhiteSpace(r.Department) ? r.Department : existing.dept,
                                        !string.IsNullOrWhiteSpace(r.Category) ? r.Category : existing.cat);
                else
                    truth[r.EmpCode] = (r.Department, r.Category);
            }

            foreach (var emp in employees)
            {
                if (!truth.TryGetValue(emp.EmpCode, out var t)) continue;
                if (!string.IsNullOrWhiteSpace(t.dept)) emp.Department = t.dept;
                if (!string.IsNullOrWhiteSpace(t.cat)) emp.Category = t.cat;
            }
        }

        /// <summary>
        /// Map employee code -> name from a table that has both columns (used to fill in names
        /// when the main file, e.g. monsum.dbf, has none).
        /// </summary>
        public static Dictionary<string, string> ReadNames(DbfTable table)
        {
            var names = new Dictionary<string, string>();
            var cols = MapEmployeeColumns(table);
            if (cols.Code == null || cols.NameColumns.Count == 0) return names;

            foreach (var row in table.Rows)
            {
                string code = NormalizeEmpCode(row.TryGetValue(cols.Code, out var c) ? c : "");
                string name = "";
                foreach (var col in cols.NameColumns)
                {
                    if (row.TryGetValue(col, out var n) && !string.IsNullOrWhiteSpace(n)) { name = n.Trim(); break; }
                }
                if (code.Length > 0 && name.Length > 0) names[code] = name;
            }
            return names;
        }

        /// <summary>
        /// Read employees using the file's own column names. Returns an empty list when the
        /// expected columns are not present (caller can then fall back or show Describe()).
        /// </summary>
        public static List<Employee> ReadEmployees(string path) => ReadEmployees(Read(path));

        /// <summary>
        /// The original ARISAttendance approach: scan the raw text for a fixed layout
        /// (4-digit code, 8-digit card, 25-char name, 3-char dept, 3-char category, 1-char shift).
        /// Kept as a fallback for single files the header-based reader cannot map.
        /// </summary>
        public static List<Employee> ReadEmployeesLegacy(string path)
        {
            var employees = new List<Employee>();
            string content;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sr = new StreamReader(fs, Encoding.Latin1))
                content = sr.ReadToEnd();

            string pattern = @"(?:[YN\s\*])?(?<EmpCode>\d{4})\s*(?<CardNo>\d{8})(?<Name>.{25})(?<Sex>.)(?<Comp>.{3})\s+(?<Dept>.{3})\s+(?<Cat>.{3})(?<Shift>.)";
            foreach (Match m in Regex.Matches(content, pattern))
            {
                employees.Add(new Employee
                {
                    EmpCode = NormalizeEmpCode(m.Groups["EmpCode"].Value),
                    CardNo = m.Groups["CardNo"].Value.Trim(),
                    FullName = m.Groups["Name"].Value.Trim(),
                    Department = m.Groups["Dept"].Value.Trim(),
                    Category = m.Groups["Cat"].Value.Trim(),
                    Shift = m.Groups["Shift"].Value.Trim()
                });
            }
            return employees;
        }

        /// <summary>
        /// Human-readable summary of what is actually inside the file (columns + first row).
        /// Shown to the user when nothing could be imported.
        /// </summary>
        public static string Describe(string path)
        {
            try
            {
                var table = Read(path, 1);
                var sb = new StringBuilder();
                sb.AppendLine($"File: {Path.GetFileName(path)}");
                sb.AppendLine($"Records in file: {table.RecordCount}");
                sb.AppendLine($"Columns ({table.Fields.Count}):");
                sb.AppendLine(string.Join(", ", table.Fields.Select(f => $"{f.Name} [{f.Type}{f.Length}]")));

                if (table.Rows.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("First row:");
                    sb.AppendLine(string.Join(" | ", table.Fields.Select(f => $"{f.Name}={table.Rows[0][f.Name]}")));
                }
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"File: {Path.GetFileName(path)}\n{ex.Message}\nThis may not be the employee master .dbf.";
            }
        }
    }
}
