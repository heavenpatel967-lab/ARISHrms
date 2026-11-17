using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ClosedXML.Excel;
using ARISHrms.Models;

namespace ARISHrms.Services
{
    /// <summary>
    /// Employee Master -> Excel: a Summary sheet, then one sheet per department.
    /// Each department sheet lists the active employees first, then the left / inactive ones, both by Emp Code.
    /// Read-only: nothing in the database is changed.
    /// </summary>
    public static class EmployeeExport
    {
        private const string NoDepartment = "No Department";

        public static void Save(IEnumerable<Employee> employees, string path)
        {
            var list = employees.ToList();
            var groups = list
                .GroupBy(e => string.IsNullOrWhiteSpace(e.DepartmentName) ? NoDepartment : e.DepartmentName.Trim())
                .OrderBy(g => g.Key == NoDepartment ? 1 : 0)
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            using var wb = new XLWorkbook();
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Summary" };

            // ---------- summary ----------
            var sum = wb.Worksheets.Add("Summary");
            sum.Cell(1, 1).Value = "EMPLOYEE MASTER - DEPARTMENT WISE";
            sum.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);
            sum.Cell(2, 1).Value = "Exported on " + DateTime.Now.ToString("dd-MMM-yyyy HH:mm", CultureInfo.InvariantCulture);
            sum.Cell(2, 1).Style.Font.SetItalic().Font.SetFontColor(XLColor.Gray);

            string[] sumHeaders = { "Sr No", "Department", "Active", "Left / Inactive", "Total" };
            for (int c = 0; c < sumHeaders.Length; c++) sum.Cell(4, c + 1).Value = sumHeaders[c];
            StyleHeader(sum.Range(4, 1, 4, sumHeaders.Length), XLColor.FromHtml("#2563EB"));

            int sr = 1, sumRow = 5;
            foreach (var g in groups)
            {
                int active = g.Count(IsActive), left = g.Count() - active;
                sum.Cell(sumRow, 1).Value = sr++;
                sum.Cell(sumRow, 2).Value = g.Key;
                sum.Cell(sumRow, 3).Value = active;
                sum.Cell(sumRow, 4).Value = left;
                sum.Cell(sumRow, 5).Value = g.Count();
                sumRow++;
            }
            int totActive = list.Count(IsActive);
            sum.Cell(sumRow, 2).Value = "TOTAL";
            sum.Cell(sumRow, 3).Value = totActive;
            sum.Cell(sumRow, 4).Value = list.Count - totActive;
            sum.Cell(sumRow, 5).Value = list.Count;
            sum.Range(sumRow, 1, sumRow, 5).Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#E2E8F0"));
            Borders(sum.Range(4, 1, sumRow, 5));
            sum.Columns().AdjustToContents();

            // ---------- one sheet per department ----------
            foreach (var g in groups)
            {
                var ws = wb.Worksheets.Add(SheetName(g.Key, usedNames));
                var active = g.Where(IsActive).OrderBy(e => CodeOrder(e.EmpCode)).ThenBy(e => e.EmpCode).ToList();
                var left = g.Where(e => !IsActive(e)).OrderBy(e => CodeOrder(e.EmpCode)).ThenBy(e => e.EmpCode).ToList();

                ws.Cell(1, 1).Value = "DEPARTMENT: " + g.Key;
                ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);
                ws.Cell(2, 1).Value = $"Active: {active.Count}    Left / Inactive: {left.Count}    Total: {g.Count()}";
                ws.Cell(2, 1).Style.Font.SetBold().Font.SetFontColor(XLColor.FromHtml("#475569"));

                int row = 4;
                row = WriteSection(ws, row, $"ACTIVE EMPLOYEES ({active.Count})", XLColor.FromHtml("#16A34A"), active, false);
                row += 2;
                WriteSection(ws, row, $"LEFT / INACTIVE EMPLOYEES ({left.Count})", XLColor.FromHtml("#DC2626"), left, true);

                ws.Columns().AdjustToContents();
                ws.Column(1).Width = 7;   // Sr No (the long title in A1 would make it too wide)
                ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
                ws.PageSetup.FitToPages(1, 0);
            }

            wb.SaveAs(path);
        }

        /// <summary>Writes a heading, the column headers and the rows. Returns the last row used.</summary>
        private static int WriteSection(IXLWorksheet ws, int row, string title, XLColor color, List<Employee> people, bool withLeftOn)
        {
            var headers = new List<string> { "Sr No", "Card No", "Emp Code", "Employee Name", "Phone", "Designation", "Category", "Status" };
            if (withLeftOn) headers.Add("Left On");
            int cols = headers.Count;

            ws.Cell(row, 1).Value = title;
            ws.Range(row, 1, row, cols).Merge().Style
                .Font.SetBold().Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(color);
            row++;

            for (int c = 0; c < cols; c++) ws.Cell(row, c + 1).Value = headers[c];
            StyleHeader(ws.Range(row, 1, row, cols), XLColor.FromHtml("#334155"));
            int first = row;

            if (people.Count == 0)
            {
                row++;
                ws.Cell(row, 1).Value = "None";
                ws.Range(row, 1, row, cols).Merge().Style.Font.SetItalic().Font.SetFontColor(XLColor.Gray);
            }

            int sr = 1;
            foreach (var e in people)
            {
                row++;
                ws.Cell(row, 1).Value = sr++;
                // text, so leading zeros (00000001, 0001) and long numbers stay exactly as they are
                ws.Cell(row, 2).Value = (e.CardNo ?? "").Trim();
                ws.Cell(row, 3).Value = (e.EmpCode ?? "").Trim();
                ws.Cell(row, 4).Value = (e.FullName ?? "").Trim();
                ws.Cell(row, 5).Value = (e.Phone ?? "").Trim();
                ws.Cell(row, 6).Value = (e.Designation ?? "").Trim();
                ws.Cell(row, 7).Value = (e.CategoryName ?? "").Trim();
                ws.Cell(row, 8).Value = IsActive(e) ? "Active" : "Left";
                if (withLeftOn) ws.Cell(row, 9).Value = e.LeavingDisplay ?? "";
                if (sr % 2 == 1) ws.Range(row, 1, row, cols).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#F8FAFC"));
            }

            Borders(ws.Range(first, 1, row, cols));
            return row;
        }

        public static bool IsActive(Employee e) => !string.Equals((e.Status ?? "").Trim(), "N", StringComparison.OrdinalIgnoreCase);

        /// <summary>Numeric codes in number order (2 before 10); other codes after them.</summary>
        private static long CodeOrder(string code)
        {
            string digits = new string((code ?? "").Where(char.IsDigit).ToArray());
            return digits.Length > 0 && digits.Length < 18 && long.TryParse(digits, out var n) ? n : long.MaxValue;
        }

        /// <summary>Excel sheet names: max 31 characters, none of : \ / ? * [ ], unique.</summary>
        private static string SheetName(string dept, HashSet<string> used)
        {
            var chars = dept.Select(ch => ":\\/?*[]".IndexOf(ch) >= 0 ? '-' : ch).ToArray();
            string name = new string(chars).Trim().Trim('\'');
            if (name.Length == 0) name = "Department";
            if (name.Length > 31) name = name.Substring(0, 31);

            string candidate = name;
            for (int i = 2; used.Contains(candidate); i++)
            {
                string suffix = $" ({i})";
                candidate = (name.Length + suffix.Length > 31 ? name.Substring(0, 31 - suffix.Length) : name) + suffix;
            }
            used.Add(candidate);
            return candidate;
        }

        private static void StyleHeader(IXLRange r, XLColor color) =>
            r.Style.Font.SetBold().Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(color);

        private static void Borders(IXLRange r)
        {
            r.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            r.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            r.Style.Border.OutsideBorderColor = XLColor.FromHtml("#CBD5E1");
            r.Style.Border.InsideBorderColor = XLColor.FromHtml("#CBD5E1");
        }
    }
}
