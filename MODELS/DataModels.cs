using System;

namespace ARISHrms.Models
{
    public class Employee
    {
        public string CardNo { get; set; }
        public string EmpCode { get; set; }
        public string FullName { get; set; }
        public string Department { get; set; }
        public string Category { get; set; }
        public string Designation { get; set; }
        public string Status { get; set; }

        public string Shift { get; set; }

        // Personal details entered inside this app (kept when the DBF is re-synced)
        public string Phone { get; set; }
        public string AadharNo { get; set; }
        public string NativeVillage { get; set; }

        // From the payroll DBF (monsum.dbf: BASIC / WAGE_TYPE)
        public decimal BasicSalary { get; set; }
        public string WageType { get; set; }

        // LEAVING_DT exactly as found in the DBF (blank for active employees)
        public string LeavingDate { get; set; }

        // Display helpers (not stored in the database)
        public string DisplayName => (string.IsNullOrWhiteSpace(FullName) ? EmpCode : $"{EmpCode} - {FullName}") + (Status == "N" ? "  (left)" : "");
        public string DepartmentName => ARISHrms.Services.LookupNames.Department(Department);
        public string LeavingDisplay
        {
            get
            {
                if (string.IsNullOrWhiteSpace(LeavingDate)) return "";
                return ARISHrms.Services.AttendanceReportService.TryParseDate(LeavingDate, out var d)
                    ? d.ToString("dd-MMM-yyyy", System.Globalization.CultureInfo.InvariantCulture) : LeavingDate.Trim();
            }
        }
        public string CategoryName => ARISHrms.Services.LookupNames.Category(Category);
    }

    public class RawPunch
    {
        public int Id { get; set; }
        public string EmpCode { get; set; }
        public DateTime PunchTime { get; set; }
    }

    public class DailyAttendance
    {
        public int Id { get; set; }
        public string EmpCode { get; set; }
        public string CardNo { get; set; }
        public string FullName { get; set; }
        public string Department { get; set; }
        public string Category { get; set; }
        public string Shift { get; set; }
        public DateTime WorkDate { get; set; }
        public DateTime? PunchIn { get; set; }
        public DateTime? LunchOut { get; set; }
        public DateTime? LunchIn { get; set; }
        public DateTime? PunchOut { get; set; }
        public DateTime? Punch5 { get; set; }
        public DateTime? Punch6 { get; set; }
        public DateTime? Punch7 { get; set; }
        public DateTime? Punch8 { get; set; }
        public bool IsException { get; set; }

        // 🔥 NEW: Required for the Batch Edit Checkboxes
        public bool IsSelected { get; set; }
    }
}