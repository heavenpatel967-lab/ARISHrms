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
        public string Code, Card, Name, Dept, Category, Shift, Designation, Basic, WageType, YearMonth;

        /// <summary>How many of the useful columns were found.</summary>
        public int Score => new[] { Code, Card, Name, Dept, Category, Shift, Designation, Basic }.Count(c => c != null);

        /// <summary>
        /// A usable employee master has an employee code plus at least one more employee attribute.
        /// A name column is NOT required: payroll summary files (e.g. monsum.dbf) have no names.
        /// </summary>
        public bool IsEmployeeMaster => Code != null && Score >= 2;
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

        private static string NormalizeEmpCode(string raw)
        {
            raw = (raw ?? "").Trim();
            if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
                && d >= 0 && d == Math.Truncate(d))
                return ((long)d).ToString("D4");
            return raw;
        }

        /// <summary>Find which columns of a DBF correspond to employee-master fields.</summary>
        public static EmployeeColumns MapEmployeeColumns(DbfTable table) => new EmployeeColumns
        {
            // EMP_CODE is checked before EMPCD (monsum.dbf has both)
            Code = FindColumn(table, "EMPCODE", "EMPCD", "ECODE", "EMPNO", "EMPID", "EMPLOYEEID",
                              "EMPLOYEECODE", "PERNO", "CODE", "ID"),
            Card = FindColumn(table, "CARDNO", "CARD", "CARDNUM", "CARDNUMBER"),
            Name = FindColumn(table, "NAME", "EMPNAME", "ENAME", "FULLNAME", "EMPLOYEENAME", "EMPLOYEE", "NAME1"),
            Dept = FindColumn(table, "DEPT", "DEPARTMENT", "DEPTCODE", "DEPTNAME"),
            Category = FindColumn(table, "CAT", "CATEGORY", "CATCODE"),
            Shift = FindColumn(table, "SHIFT", "SHFT", "SHIFTCODE"),
            Designation = FindColumn(table, "DESIG", "DESIGNATION", "DESIGCODE"),
            Basic = FindColumn(table, "BASIC", "BASICSALARY", "BASICPAY"),
            WageType = FindColumn(table, "WAGETYPE", "WAGETYP"),
            YearMonth = FindColumn(table, "YRMONTH", "YEARMONTH", "PAYMONTH")
        };

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
        /// Build employees from an already-loaded table. When the file has several rows per employee
        /// (one per month), the latest month wins; if months can't be read, the last row in the file wins.
        /// </summary>
        public static List<Employee> ReadEmployees(DbfTable table)
        {
            var cols = MapEmployeeColumns(table);
            if (cols.Code == null) return new List<Employee>();

            string Get(Dictionary<string, string> row, string col) =>
                col != null && row.TryGetValue(col, out var v) ? (v ?? "").Trim() : "";

            var latest = new Dictionary<string, (long order, Employee emp)>();
            long rowIndex = 0;

            foreach (var row in table.Rows)
            {
                rowIndex++;
                string code = NormalizeEmpCode(Get(row, cols.Code));
                if (string.IsNullOrEmpty(code)) continue;

                // order = month (if readable) * 1e9 + row index, so a later month always beats an earlier one
                long month = MonthKey(Get(row, cols.YearMonth)) ?? 0;
                long order = month * 1_000_000_000L + rowIndex;

                if (latest.TryGetValue(code, out var existing) && existing.order > order)
                    continue;

                latest[code] = (order, new Employee
                {
                    EmpCode = code,
                    CardNo = Get(row, cols.Card),
                    FullName = Get(row, cols.Name),
                    Department = Get(row, cols.Dept),
                    Category = Get(row, cols.Category),
                    Shift = Get(row, cols.Shift),
                    Designation = Get(row, cols.Designation),
                    BasicSalary = ParseDecimal(Get(row, cols.Basic)),
                    WageType = Get(row, cols.WageType)
                });
            }

            return latest.Values.Select(v => v.emp).OrderBy(e => e.EmpCode, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// Map employee code -> name from a table that has both columns (used to fill in names
        /// when the main file, e.g. monsum.dbf, has none).
        /// </summary>
        public static Dictionary<string, string> ReadNames(DbfTable table)
        {
            var names = new Dictionary<string, string>();
            var cols = MapEmployeeColumns(table);
            if (cols.Code == null || cols.Name == null) return names;

            foreach (var row in table.Rows)
            {
                string code = NormalizeEmpCode(row.TryGetValue(cols.Code, out var c) ? c : "");
                string name = row.TryGetValue(cols.Name, out var n) ? (n ?? "").Trim() : "";
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
