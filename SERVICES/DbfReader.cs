using System;
using System.Collections.Generic;
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

    /// <summary>
    /// Dependency-free dBASE (.dbf) reader. Reads the real column names from the file header,
    /// so it works with any DBF layout and does not need the 32-bit-only Jet/OLEDB driver.
    /// </summary>
    public static class DbfReader
    {
        public static DbfTable Read(string path, int maxRows = int.MaxValue)
        {
            byte[] data = File.ReadAllBytes(path);
            if (data.Length < 33)
                throw new InvalidDataException("The file is too small to be a DBF file.");

            int headerLength = BitConverter.ToUInt16(data, 8);
            int recordLength = BitConverter.ToUInt16(data, 10);
            if (headerLength < 33 || headerLength > data.Length || recordLength < 2)
                throw new InvalidDataException("The file header is not a valid DBF header.");

            var table = new DbfTable();
            var encoding = Encoding.Latin1;

            // Field descriptors: 32 bytes each, starting at byte 32, ended by 0x0D
            var offsets = new List<int>();
            int pos = 32;
            int offset = 1; // byte 0 of every record is the deleted-flag
            while (pos + 32 <= headerLength && data[pos] != 0x0D)
            {
                string name = encoding.GetString(data, pos, 11).Split('\0')[0].Trim();
                table.Fields.Add(new DbfField { Name = name, Type = (char)data[pos + 11], Length = data[pos + 16] });
                offsets.Add(offset);
                offset += data[pos + 16];
                pos += 32;
            }

            if (table.Fields.Count == 0 || pos >= data.Length || data[pos] != 0x0D)
                throw new InvalidDataException("No column definitions were found - this is not a standard DBF file.");

            int possible = (data.Length - headerLength) / recordLength;
            int declared = BitConverter.ToInt32(data, 4);
            int recordCount = (declared <= 0 || declared > possible) ? possible : declared;
            table.RecordCount = recordCount;

            for (int i = 0; i < recordCount && table.Rows.Count < maxRows; i++)
            {
                int start = headerLength + i * recordLength;
                if (start + recordLength > data.Length) break;
                if (data[start] == 0x2A) continue; // record marked as deleted

                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int f = 0; f < table.Fields.Count; f++)
                {
                    int fieldStart = start + offsets[f];
                    int len = Math.Min(table.Fields[f].Length, data.Length - fieldStart);
                    row[table.Fields[f].Name] = len > 0
                        ? encoding.GetString(data, fieldStart, len).Trim('\0', ' ', '\t')
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
            return int.TryParse(raw, out int n) ? n.ToString("D4") : raw;
        }

        /// <summary>
        /// Read employees using the file's own column names. Returns an empty list when the
        /// expected columns are not present (caller can then fall back or show Describe()).
        /// </summary>
        public static List<Employee> ReadEmployees(string path)
        {
            var table = Read(path);
            var result = new List<Employee>();

            string codeCol = FindColumn(table, "EMPCODE", "EMP_CODE", "ECODE", "EMPNO", "EMP_NO", "EMPID", "EMP_ID",
                                        "EMPLOYEEID", "EMPLOYEECODE", "PERNO", "CODE", "ID");
            if (codeCol == null) return result;

            string cardCol = FindColumn(table, "CARDNO", "CARD_NO", "CARD", "CARDNUM", "CARDNUMBER");
            string nameCol = FindColumn(table, "NAME", "EMPNAME", "EMP_NAME", "ENAME", "FULLNAME", "EMPLOYEENAME", "EMPLOYEE");
            string deptCol = FindColumn(table, "DEPT", "DEPARTMENT", "DEPTCODE", "DEPT_CODE", "DEPTNAME");
            string catCol = FindColumn(table, "CAT", "CATEGORY", "CATCODE", "CAT_CODE");
            string shiftCol = FindColumn(table, "SHIFT", "SHFT", "SHIFTCODE", "SHIFT_CODE");

            string Get(Dictionary<string, string> row, string col) =>
                col != null && row.TryGetValue(col, out var v) ? (v ?? "").Trim() : "";

            foreach (var row in table.Rows)
            {
                string code = NormalizeEmpCode(Get(row, codeCol));
                if (string.IsNullOrEmpty(code)) continue;

                result.Add(new Employee
                {
                    EmpCode = code,
                    CardNo = Get(row, cardCol),
                    FullName = Get(row, nameCol),
                    Department = Get(row, deptCol),
                    Category = Get(row, catCol),
                    Shift = Get(row, shiftCol)
                });
            }

            return result;
        }

        /// <summary>
        /// The original ARISAttendance approach: scan the raw text for a fixed layout
        /// (4-digit code, 8-digit card, 25-char name, 3-char dept, 3-char category, 1-char shift).
        /// Kept as a fallback for files the header-based reader cannot map.
        /// </summary>
        public static List<Employee> ReadEmployeesLegacy(string path)
        {
            var employees = new List<Employee>();
            string content = File.ReadAllText(path, Encoding.Latin1);

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
