using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Data.Sqlite;
using Dapper;
using ARISHrms.Data;
using ARISHrms.Models;
using Microsoft.Win32;

namespace ARISHrms.Views
{
    public partial class EmployeesView : UserControl
    {
        private List<Employee> _allEmployees = new List<Employee>();

        public EmployeesView()
        {
            InitializeComponent();
            LoadEmployees();
        }

        private void LoadEmployees()
        {
            try
            {
                using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
                {
                    connection.Open();
                    // Fetch all employees from the SQLite database using Dapper
                    _allEmployees = connection.Query<Employee>("SELECT * FROM Employees ORDER BY EmpCode").ToList();
                }

                PopulateFilterDropdowns();
                ApplyFilters();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error loading employee data: " + ex.Message, "Database Error");
            }
        }

        private void PopulateFilterDropdowns()
        {
            if (CmbDepartment.Items.Count == 0)
            {
                var depts = _allEmployees.Select(e => e.Department).Where(d => !string.IsNullOrWhiteSpace(d)).Distinct().OrderBy(d => d).ToList();
                depts.Insert(0, "All Departments");
                CmbDepartment.ItemsSource = depts;
                CmbDepartment.SelectedIndex = 0;

                var desigs = _allEmployees.Select(e => e.Designation).Where(d => !string.IsNullOrWhiteSpace(d)).Distinct().OrderBy(d => d).ToList();
                desigs.Insert(0, "All Designations");
                CmbDesignation.ItemsSource = desigs;
                CmbDesignation.SelectedIndex = 0;

                var cats = _allEmployees.Select(e => e.Category).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().OrderBy(c => c).ToList();
                cats.Insert(0, "All Categories");
                CmbCategory.ItemsSource = cats;
                CmbCategory.SelectedIndex = 0;
            }
        }

        private void ApplyFilters()
        {
            if (_allEmployees == null || !_allEmployees.Any()) return;

            var filtered = _allEmployees.AsEnumerable();

            // 1. Text Search Filter
            string searchText = TxtSearch.Text.Trim().ToLower();
            if (!string.IsNullOrEmpty(searchText))
            {
                filtered = filtered.Where(e =>
                    (e.FullName != null && e.FullName.ToLower().Contains(searchText)) ||
                    (e.EmpCode != null && e.EmpCode.ToLower().Contains(searchText)) ||
                    (e.CardNo != null && e.CardNo.ToLower().Contains(searchText)));
            }

            // 2. Dropdown Filters
            if (CmbDepartment.SelectedIndex > 0 && CmbDepartment.SelectedItem != null)
                filtered = filtered.Where(e => e.Department == CmbDepartment.SelectedItem.ToString());

            if (CmbDesignation.SelectedIndex > 0 && CmbDesignation.SelectedItem != null)
                filtered = filtered.Where(e => e.Designation == CmbDesignation.SelectedItem.ToString());

            if (CmbCategory.SelectedIndex > 0 && CmbCategory.SelectedItem != null)
                filtered = filtered.Where(e => e.Category == CmbCategory.SelectedItem.ToString());

            // 3. Status Filter (0 = All, 1 = Active (Y), 2 = Inactive (N))
            if (CmbStatus.SelectedIndex == 1)
                filtered = filtered.Where(e => e.Status == "Y");
            else if (CmbStatus.SelectedIndex == 2)
                filtered = filtered.Where(e => e.Status == "N");

            // 4. Update UI Grid
            var resultList = filtered.ToList();
            EmployeeGrid.ItemsSource = resultList;
            TxtTotalCount.Text = $"Total Employees: {resultList.Count}";
        }

        private void Filter_Changed(object sender, RoutedEventArgs e)
        {
            ApplyFilters();
        }

        private void BtnResetFilters_Click(object sender, RoutedEventArgs e)
        {
            TxtSearch.Clear();
            CmbDepartment.SelectedIndex = 0;
            CmbDesignation.SelectedIndex = 0;
            CmbCategory.SelectedIndex = 0;
            CmbStatus.SelectedIndex = 0;
            ApplyFilters();
        }

        private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Export to Excel will be migrated next.", "In Progress");
        }

        private void EmployeeGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (EmployeeGrid.SelectedItem is Employee selectedEmp)
            {
                MessageBox.Show($"Opening 360° Profile for {selectedEmp.FullName} (Code: {selectedEmp.EmpCode})", "Profile View");
            }
        }

        private void BtnSyncDbf_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "Select Employee DBF File",
                Filter = "DBF Files (*.dbf)|*.dbf"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    SyncDbfData(openFileDialog.FileName);
                    LoadEmployees(); // Instantly refresh the UI grid
                    MessageBox.Show("Employee data synced successfully from DBF!", "Sync Complete");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error syncing DBF: {ex.Message}\n\nMake sure the file isn't open in Excel.", "Sync Error");
                }
            }
        }

        private void SyncDbfData(string dbfFilePath)
        {
            string directory = System.IO.Path.GetDirectoryName(dbfFilePath);
            string fileName = System.IO.Path.GetFileName(dbfFilePath);

            // Standard OleDb connection string for DBF files
            string oleDbConnectionString = $@"Provider=Microsoft.Jet.OLEDB.4.0;Data Source={directory};Extended Properties=dBase IV;";

            var employeesToSync = new List<Employee>();

            // 1. Read the DBF File
            using (var oleConnection = new System.Data.OleDb.OleDbConnection(oleDbConnectionString))
            {
                oleConnection.Open();
                var command = new System.Data.OleDb.OleDbCommand($"SELECT * FROM [{fileName}]", oleConnection);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        employeesToSync.Add(new Employee
                        {
                            EmpCode = reader["EmpCode"]?.ToString(),
                            CardNo = reader["CardNo"]?.ToString(),
                            FullName = reader["Name"]?.ToString(),
                            Department = reader["Department"]?.ToString(),
                            Category = reader["Category"]?.ToString(),
                            Designation = reader["Designation"]?.ToString(),
                            Status = reader["Status"]?.ToString() ?? "Y",
                            Shift = reader["Shift"]?.ToString()
                        });
                    }
                }
            }

            // 2. Upsert into SQLite using Dapper
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    string sql = @"
                        INSERT INTO Employees (EmpCode, CardNo, FullName, Department, Category, Designation, Status, Shift)
                        VALUES (@EmpCode, @CardNo, @FullName, @Department, @Category, @Designation, @Status, @Shift)
                        ON CONFLICT(EmpCode) DO UPDATE SET
                            CardNo = excluded.CardNo,
                            FullName = excluded.FullName,
                            Department = excluded.Department,
                            Category = excluded.Category,
                            Designation = excluded.Designation,
                            Status = excluded.Status,
                            Shift = excluded.Shift;";

                    connection.Execute(sql, employeesToSync, transaction);
                    transaction.Commit();
                }
            }
        }
    }
}