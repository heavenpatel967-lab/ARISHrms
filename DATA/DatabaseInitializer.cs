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

                // 4. Financials: Loans & Advances
                connection.Execute(@"
                    CREATE TABLE IF NOT EXISTS LoanLedgers (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        EmpCode TEXT,
                        MonthYear TEXT,
                        PendingLoanAmount REAL,
                        EmiDeducted REAL
                    );
                    CREATE TABLE IF NOT EXISTS AdvanceLedgers (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        EmpCode TEXT,
                        MonthYear TEXT,
                        AdvanceAmount REAL,
                        IsOverLimit INTEGER DEFAULT 0
                    );");

                // 5. Leaves / Village Visits & Documents
                connection.Execute(@"
                    CREATE TABLE IF NOT EXISTS VillageVisits (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        EmpCode TEXT,
                        DepartureDate DATE,
                        ExpectedReturn DATE,
                        ActualReturn DATE,
                        LateDays INTEGER DEFAULT 0,
                        ReasonNotes TEXT
                    );
                    CREATE TABLE IF NOT EXISTS EmployeeDocuments (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        EmpCode TEXT,
                        DocCategory TEXT,
                        DocName TEXT,
                        FilePath TEXT,
                        UploadDate DATETIME DEFAULT CURRENT_TIMESTAMP
                    );");
            }
        }
    }
}