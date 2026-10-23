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

        // From the payroll DBF (monsum.dbf: BASIC / WAGE_TYPE)
        public decimal BasicSalary { get; set; }
        public string WageType { get; set; }
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