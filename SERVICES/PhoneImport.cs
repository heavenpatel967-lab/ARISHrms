using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using Dapper;
using ARISHrms.Data;
using ARISHrms.Models;

namespace ARISHrms.Services
{
    /// <summary>One phone number planned for one employee.</summary>
    public class PhoneUpdate
    {
        // properties (not fields) - Dapper only reads properties for @EmpCode / @NewPhone
        public string EmpCode { get; set; } = "";
        public string Name { get; set; } = "";
        public string OldPhone { get; set; } = "";
        public string NewPhone { get; set; } = "";
    }

    /// <summary>What an Excel phone list would change - nothing is saved until <see cref="PhoneImport.Apply"/>.</summary>
    public class PhoneImportPlan
    {
        public string SheetName = "";
        public string ColumnInfo = "";
        public int RowsRead;                                         // rows with a card number
        public List<PhoneUpdate> New = new List<PhoneUpdate>();       // employee had no phone yet
        public List<PhoneUpdate> Different = new List<PhoneUpdate>(); // employee already has another number
        public int Same;                                              // same number already saved
        public int Empty;                                             // card number but no contact number
        public List<string> NotFound = new List<string>();            // "row 12: 0256 SECURITY"
        public int Duplicates;                                        // same employee twice in the sheet (first one used)
    }

    /// <summary>
    /// Bulk-fills Employees.Phone from an Excel sheet (SR.NO | CARD NO. | DEP. | NAME | CONTACT NUMBER).
    /// Only the Phone column is written. Rows without a number never erase a saved one.
    /// The card number in the sheet is matched to the Emp Code (0001 = 1), and to the Card No if no Emp Code matches.
    /// </summary>
    public static class PhoneImport
    {
        private static readonly string[] CodeHeaders = { "CARD", "EMP CODE", "EMPCODE", "CODE" };
        private static readonly string[] PhoneHeaders = { "CONTACT", "PHONE", "MOBILE", "MOB" };

        public static PhoneImportPlan Plan(string path)
        {
            var plan = new PhoneImportPlan();
            var byCode = new Dictionary<string, Employee>();
            var byCard = new Dictionary<string, Employee>();
            foreach (var e in EmployeeLookup.LoadAll())
            {
                byCode[EmployeeLookup.Key(e.EmpCode)] = e;
                if (!string.IsNullOrWhiteSpace(e.CardNo)) byCard[EmployeeLookup.Key(e.CardNo)] = e;
            }

            using var wb = new XLWorkbook(path);
            IXLWorksheet ws = null;
            int headerRow = 0, codeCol = 0, phoneCol = 0, nameCol = 0;

            // first sheet that has a card-number header and a contact-number header in its first 15 rows
            foreach (var sheet in wb.Worksheets)
            {
                var used = sheet.RangeUsed();
                if (used == null) continue;
                int lastCol = used.LastColumn().ColumnNumber();
                for (int r = 1; r <= Math.Min(15, used.LastRow().RowNumber()); r++)
                {
                    int c1 = 0, c2 = 0, c3 = 0;
                    for (int c = 1; c <= lastCol; c++)
                    {
                        string h = Normalize(sheet.Cell(r, c).GetString());
                        if (h.Length == 0) continue;
                        if (c1 == 0 && CodeHeaders.Any(x => h.Contains(x))) c1 = c;
                        else if (c2 == 0 && PhoneHeaders.Any(x => h.Contains(x))) c2 = c;
                        else if (c3 == 0 && h.Contains("NAME")) c3 = c;
                    }
                    if (c1 > 0 && c2 > 0) { ws = sheet; headerRow = r; codeCol = c1; phoneCol = c2; nameCol = c3; break; }
                }
                if (ws != null) break;
            }

            if (ws == null)
                throw new InvalidOperationException(
                    "Could not find the header row. The sheet needs a 'CARD NO.' column and a 'CONTACT NUMBER' (or PHONE / MOBILE) column.");

            plan.SheetName = ws.Name;
            plan.ColumnInfo = $"Sheet '{ws.Name}', header on row {headerRow}: card = '{ws.Cell(headerRow, codeCol).GetString().Trim()}', " +
                              $"number = '{ws.Cell(headerRow, phoneCol).GetString().Trim()}'";

            var seen = new HashSet<string>();
            int lastRow = ws.RangeUsed().LastRow().RowNumber();
            for (int r = headerRow + 1; r <= lastRow; r++)
            {
                string code = CellText(ws.Cell(r, codeCol));
                if (code.Length == 0) continue;
                plan.RowsRead++;

                string phone = CleanPhone(CellText(ws.Cell(r, phoneCol)));
                string sheetName = nameCol > 0 ? CellText(ws.Cell(r, nameCol)) : "";

                string key = EmployeeLookup.Key(code);
                if (!byCode.TryGetValue(key, out var emp) && !byCard.TryGetValue(key, out emp))
                {
                    plan.NotFound.Add($"row {r}: {code} {sheetName}".TrimEnd());
                    continue;
                }

                if (phone.Length == 0) { plan.Empty++; continue; }
                if (!seen.Add(emp.EmpCode)) { plan.Duplicates++; continue; }

                string old = (emp.Phone ?? "").Trim();
                var u = new PhoneUpdate { EmpCode = emp.EmpCode, Name = emp.FullName ?? sheetName, OldPhone = old, NewPhone = phone };
                if (old.Length == 0) plan.New.Add(u);
                else if (string.Equals(Digits(old), Digits(phone), StringComparison.Ordinal)) plan.Same++;
                else plan.Different.Add(u);
            }

            return plan;
        }

        /// <summary>Saves the new numbers (and the changed ones when replaceDifferent). Returns how many were saved.</summary>
        public static int Apply(PhoneImportPlan plan, bool replaceDifferent)
        {
            var list = plan.New.Concat(replaceDifferent ? plan.Different : Enumerable.Empty<PhoneUpdate>()).ToList();
            if (list.Count == 0) return 0;

            using var c = new SqliteConnection(DatabaseInitializer.ConnectionString);
            c.Open();
            using var tx = c.BeginTransaction();
            int n = c.Execute("UPDATE Employees SET Phone = @NewPhone WHERE EmpCode = @EmpCode", list, tx);
            tx.Commit();
            return n;
        }

        private static string CellText(IXLCell cell)
        {
            var v = cell.Value;
            if (v.IsNumber)
            {
                double d = v.GetNumber();
                return Math.Abs(d % 1) < 1e-9 ? ((long)Math.Round(d)).ToString(CultureInfo.InvariantCulture)
                                              : d.ToString(CultureInfo.InvariantCulture);
            }
            return (cell.GetString() ?? "").Trim();
        }

        /// <summary>Keeps the number as written (e.g. "7839328863/7848328863"), just tidies spaces.</summary>
        private static string CleanPhone(string s)
        {
            s = string.Join(" ", (s ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
            return Digits(s).Length == 0 ? "" : s;
        }

        private static string Digits(string s) => new string((s ?? "").Where(char.IsDigit).ToArray());

        private static string Normalize(string h) => (h ?? "").Trim().ToUpperInvariant().Replace(".", "");
    }
}
