using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using Dapper;
using ARISHrms.Data;
using ARISHrms.Models;

namespace ARISHrms.Services
{
    public static class EmployeeLookup
    {
        /// <summary>Numeric codes are compared as numbers and stored as 4 digits: "7" and "0007" are the same person.</summary>
        public static string Key(string empCode)
        {
            string digits = new string((empCode ?? "").Where(char.IsDigit).ToArray());
            return digits.Length > 0 && long.TryParse(digits, out var n) ? n.ToString("D4") : (empCode ?? "").Trim();
        }

        /// <summary>All employees (including those who have left, marked "(left)"), ordered by code.</summary>
        public static List<Employee> LoadAll()
        {
            using (var c = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                c.Open();
                return c.Query<Employee>("SELECT * FROM Employees ORDER BY EmpCode").ToList();
            }
        }

        public static Dictionary<string, Employee> LoadByKey()
        {
            var map = new Dictionary<string, Employee>();
            foreach (var e in LoadAll()) map[Key(e.EmpCode)] = e;
            return map;
        }
    }
}
