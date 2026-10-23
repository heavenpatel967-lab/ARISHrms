using System.IO;
using Microsoft.Data.Sqlite;
using Dapper;

namespace ARISHrms.Data
{
    public static class DatabaseInitializer
    {
        public static string DbPath => Path.Combine(Directory.GetCurrentDirectory(), "ArisHRMS.db");
        public static string ConnectionString => $"Data Source={DbPath};";

        public static void Initialize()
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();

                // 1. Employees Master
                connection.Execute(@"
                    CREATE TABLE IF NOT EXISTS Employees (
                        EmpCode TEXT PRIMARY KEY,
                        CardNo TEXT,
                        FullName TEXT,
                        Department TEXT,
                        Designation TEXT,
                        Category TEXT,
                        Shift TEXT DEFAULT 'B',
                        Status TEXT DEFAULT 'Y',
                        Phone TEXT,
                        AadharNo TEXT,
                        NativeVillage TEXT
                    );");

                // 2. Attendance & Punches (8-Punch Architecture)
                connection.Execute(@"
                    CREATE TABLE IF NOT EXISTS DailyAttendance (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        EmpCode TEXT,
                        WorkDate DATE,
                        PunchIn DATETIME,
                        LunchOut DATETIME,
                        LunchIn DATETIME,
                        PunchOut DATETIME,
                        Punch5 DATETIME,
                        Punch6 DATETIME,
                        Punch7 DATETIME,
                        Punch8 DATETIME,
                        IsException INTEGER DEFAULT 0,
                        UNIQUE(EmpCode, WorkDate)
                    );");

                // 3. Raw Punches & App Settings
                connection.Execute(@"
                    CREATE TABLE IF NOT EXISTS RawPunches (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        EmpCode TEXT,
                        PunchTime DATETIME
                    );
                    CREATE TABLE IF NOT EXISTS AppSettings (
                        SettingKey TEXT PRIMARY KEY,
                        SettingValue TEXT
                    );
                    CREATE TABLE IF NOT EXISTS SpecialRules (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        RuleType TEXT,
                        EmpCode TEXT,
                        UNIQUE(RuleType, EmpCode)
                    );");

                // 4. Financials: Loans
                connection.Execute(@"
                    CREATE TABLE IF NOT EXISTS LoanMasters (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        EmpCode TEXT NOT NULL,
                        LoanDate DATE NOT NULL,
                        LoanAmount REAL NOT NULL,
                        TenureMonths INTEGER NOT NULL,
                        InterestRate REAL DEFAULT 0,
                        MonthlyEMI REAL NOT NULL,
                        Status TEXT DEFAULT 'Active',
                        FOREIGN KEY (EmpCode) REFERENCES Employees(EmpCode)
                    );
                    
                    CREATE TABLE IF NOT EXISTS LoanLedgers (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        LoanId INTEGER NOT NULL,
                        EmpCode TEXT NOT NULL,
                        MonthYear TEXT NOT NULL,
                        MonthlyEMI REAL NOT NULL,
                        EmiDeducted REAL DEFAULT 0,
                        OutstandingBalance REAL NOT NULL,
                        IsInArrears INTEGER DEFAULT 0,
                        UNIQUE(LoanId, MonthYear),
                        FOREIGN KEY (LoanId) REFERENCES LoanMasters(Id),
                        FOREIGN KEY (EmpCode) REFERENCES Employees(EmpCode)
                    );");

                // 5. Financials: Advances
                connection.Execute(@"
                    CREATE TABLE IF NOT EXISTS AdvanceLedgers (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        EmpCode TEXT NOT NULL,
                        MonthYear TEXT NOT NULL,
                        AdvanceAmount REAL NOT NULL,
                        AdvanceDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                        RecoveryAmount REAL DEFAULT 0,
                        IsRecovered INTEGER DEFAULT 0,
                        IsOverLimit INTEGER DEFAULT 0,
                        FOREIGN KEY (EmpCode) REFERENCES Employees(EmpCode)
                    );");

                // 6. Leaves & Village Visits
                connection.Execute(@"
                    CREATE TABLE IF NOT EXISTS LeaveTypes (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        LeaveCode TEXT UNIQUE NOT NULL,
                        LeaveName TEXT NOT NULL,
                        MaxDaysPerYear INTEGER
                    );
                    
                    CREATE TABLE IF NOT EXISTS VillageVisits (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        EmpCode TEXT NOT NULL,
                        LeaveTypeId INTEGER,
                        DepartureDate DATE NOT NULL,
                        ExpectedReturn DATE NOT NULL,
                        ActualReturn DATE,
                        LateDays INTEGER DEFAULT 0,
                        LatePenalty REAL DEFAULT 0,
                        ReasonNotes TEXT,
                        ApprovedBy TEXT,
                        Status TEXT DEFAULT 'Approved',
                        FOREIGN KEY (EmpCode) REFERENCES Employees(EmpCode),
                        FOREIGN KEY (LeaveTypeId) REFERENCES LeaveTypes(Id)
                    );
                    
                    CREATE TABLE IF NOT EXISTS LeaveBalance (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        EmpCode TEXT NOT NULL,
                        Year INTEGER NOT NULL,
                        LeaveTypeId INTEGER,
                        BalanceAtStart INTEGER NOT NULL,
                        TakenDays INTEGER DEFAULT 0,
                        RemainingBalance INTEGER NOT NULL,
                        CarryOverDays INTEGER DEFAULT 0,
                        LastUpdated DATETIME DEFAULT CURRENT_TIMESTAMP,
                        UNIQUE(EmpCode, Year, LeaveTypeId),
                        FOREIGN KEY (EmpCode) REFERENCES Employees(EmpCode),
                        FOREIGN KEY (LeaveTypeId) REFERENCES LeaveTypes(Id)
                    );");

                // 7. Documents
                connection.Execute(@"
                    CREATE TABLE IF NOT EXISTS EmployeeDocuments (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        EmpCode TEXT NOT NULL,
                        DocCategory TEXT NOT NULL,
                        DocName TEXT NOT NULL,
                        FilePath TEXT,
                        FileSize INTEGER,
                        UploadDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                        ExpiryDate DATE,
                        IsExpired INTEGER DEFAULT 0,
                        FOREIGN KEY (EmpCode) REFERENCES Employees(EmpCode)
                    );");

                // 8. Payroll - Salary Slips
                connection.Execute(@"
                    CREATE TABLE IF NOT EXISTS SalarySlips (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        EmpCode TEXT NOT NULL,
                        MonthYear TEXT NOT NULL,
                        BasicSalary REAL NOT NULL,
                        Dearness REAL DEFAULT 0,
                        OtherAllowances REAL DEFAULT 0,
                        Gross REAL NOT NULL,
                        LoanEMI REAL DEFAULT 0,
                        AdvanceRecovery REAL DEFAULT 0,
                        LatePenalty REAL DEFAULT 0,
                        PF REAL DEFAULT 0,
                        TaxDeduction REAL DEFAULT 0,
                        TotalDeduction REAL NOT NULL,
                        NetSalary REAL NOT NULL,
                        GeneratedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                        UNIQUE(EmpCode, MonthYear),
                        FOREIGN KEY (EmpCode) REFERENCES Employees(EmpCode)
                    );");

                // 9. Audit Trail
                connection.Execute(@"
                    CREATE TABLE IF NOT EXISTS AuditLog (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        EntityType TEXT NOT NULL,
                        EntityId TEXT NOT NULL,
                        ChangeType TEXT NOT NULL,
                        OldValue TEXT,
                        NewValue TEXT,
                        ChangedBy TEXT,
                        ChangedDate DATETIME DEFAULT CURRENT_TIMESTAMP
                    );");
            }
        }
    }
}