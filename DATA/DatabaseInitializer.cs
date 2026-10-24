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
                        NativeVillage TEXT,
                        BasicSalary REAL NOT NULL DEFAULT 0,
                        WageType TEXT,
                        LeavingDate TEXT
                    );");

                // Upgrade databases created before these columns existed
                EnsureColumn(connection, "Employees", "BasicSalary", "REAL NOT NULL DEFAULT 0");
                EnsureColumn(connection, "Employees", "WageType", "TEXT");
                EnsureColumn(connection, "Employees", "LeavingDate", "TEXT");

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

                // 4. Advance limits (per employee, set by the user or imported from a CSV)
                connection.Execute(@"
                    CREATE TABLE IF NOT EXISTS AdvanceLimits (
                        EmpCode TEXT PRIMARY KEY,
                        LimitAmount REAL NOT NULL
                    );");

                // 4b. Advance "taken" ticks with the real date, and good / bad remarks
                connection.Execute(@"
                    CREATE TABLE IF NOT EXISTS AdvanceTaken (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        EmpCode TEXT NOT NULL,
                        TakenDate DATE NOT NULL,
                        MonthKey TEXT NOT NULL,
                        Amount REAL,
                        Note TEXT,
                        CreatedAt DATETIME
                    );
                    CREATE INDEX IF NOT EXISTS IX_AdvanceTaken_Emp ON AdvanceTaken (EmpCode, MonthKey);

                    CREATE TABLE IF NOT EXISTS EmployeeRemarks (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        EmpCode TEXT NOT NULL,
                        RemarkDate DATE NOT NULL,
                        Kind TEXT NOT NULL CHECK (Kind IN ('GOOD', 'BAD')),
                        Text TEXT NOT NULL,
                        CreatedAt DATETIME
                    );
                    CREATE INDEX IF NOT EXISTS IX_EmployeeRemarks_Emp ON EmployeeRemarks (EmpCode);

                    CREATE TABLE IF NOT EXISTS AbsenceMarks (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        EmpCode TEXT NOT NULL,
                        AbsenceDate DATE NOT NULL,
                        Informed INTEGER NOT NULL DEFAULT 1,
                        Note TEXT,
                        UpdatedAt DATETIME,
                        UNIQUE(EmpCode, AbsenceDate)
                    );");

                // 5. Leaves & Village Visits
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

        private static void EnsureColumn(SqliteConnection connection, string table, string column, string definition)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = $"PRAGMA table_info({table})";
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        // column 1 of PRAGMA table_info is the column name
                        if (string.Equals(reader.GetString(1), column, System.StringComparison.OrdinalIgnoreCase))
                            return;
                    }
                }
            }

            connection.Execute($"ALTER TABLE {table} ADD COLUMN {column} {definition}");
        }
    }
}