using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using ARISHrms.Data;
using ARISHrms.Models;
using ARISHrms.Services;
using Microsoft.Data.Sqlite;
using Dapper;
using System.Linq;

namespace ARISHrms.Views
{
    public partial class LeavesView : UserControl
    {
        private List<Employee> _employees = new List<Employee>();
        private string _selectedEmpCode;

        public LeavesView()
        {
            InitializeComponent();
            LoadEmployeeList();
            InitializeLeaveTypes();
        }

        private void LoadEmployeeList()
        {
            try
            {
                using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
                {
                    connection.Open();
                    _employees = connection.Query<Employee>("SELECT * FROM Employees WHERE Status = 'Y' ORDER BY FullName").ToList();
                }

                CmbSelectEmployee.ItemsSource = _employees;
                CmbSelectEmployee.DisplayMemberPath = "FullName";
                CmbSelectEmployee.SelectedValuePath = "EmpCode";

                if (_employees.Count > 0)
                    CmbSelectEmployee.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading employees: {ex.Message}", "Error");
            }
        }

        private void InitializeLeaveTypes()
        {
            try
            {
                using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
                {
                    connection.Open();

                    // Ensure leave types exist
                    connection.Execute(@"
                        INSERT OR IGNORE INTO LeaveTypes (LeaveCode, LeaveName, MaxDaysPerYear)
                        VALUES 
                            ('CL', 'Casual Leave', 12),
                            ('SL', 'Sick Leave', 10),
                            ('VV', 'Village Visit', 15),
                            ('ML', 'Medical Leave', 8);
                    ");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing leave types: {ex.Message}", "Error");
            }
        }

        private void CmbSelectEmployee_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbSelectEmployee.SelectedValue == null)
                return;

            _selectedEmpCode = CmbSelectEmployee.SelectedValue.ToString();
            LoadLeaveData(_selectedEmpCode);
        }

        private void LoadLeaveData(string empCode)
        {
            try
            {
                int currentYear = DateTime.Now.Year;

                // Initialize leave balance for all leave types if not exists
                using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
                {
                    connection.Open();
                    var leaveTypes = connection.Query<LeaveService.LeaveType>(
                        "SELECT * FROM LeaveTypes").ToList();

                    foreach (var type in leaveTypes)
                    {
                        LeaveService.InitializeLeaveBalance(empCode, currentYear, type.Id, type.MaxDaysPerYear);
                    }
                }

                // Load leave balances
                var balances = LeaveService.GetLeaveBalance(empCode, currentYear);
                BalanceGrid.ItemsSource = balances;

                // Load village visits
                var visits = LeaveService.GetVillageVisits(empCode, currentYear);
                VisitsGrid.ItemsSource = visits;

                // Calculate total late penalty
                decimal totalLatePenalty = LeaveService.GetTotalLatePenalty(empCode, currentYear);

                TxtInfo.Text = $"Showing {balances.Count} leave type(s) and {visits.Count} village visit(s)";
                TxtTotalLatePenalty.Text = $"₹ {totalLatePenalty:N2}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading leave data: {ex.Message}", "Error");
            }
        }

        private void BtnSaveLeave_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedEmpCode))
            {
                MessageBox.Show("Please select an employee first", "Warning");
                return;
            }

            try
            {
                DateTime departureDate = DateTime.Now;
                DateTime expectedReturn = DateTime.Now.AddDays(5); // Default 5 days

                int visitId = LeaveService.RecordVillageVisit(
                    _selectedEmpCode, 
                    1, // LeaveTypeId
                    departureDate, 
                    expectedReturn, 
                    "Village visit",
                    "System"
                );

                MessageBox.Show($"Village visit recorded successfully (ID: {visitId})", "Success");
                LoadLeaveData(_selectedEmpCode);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error recording leave: {ex.Message}", "Error");
            }
        }

        private void BtnImportCsv_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("CSV Import functionality would go here", "Feature");
        }

        private void BtnDeleteLeave_Click(object sender, RoutedEventArgs e)
        {
            if (VisitsGrid.SelectedItem == null)
            {
                MessageBox.Show("Please select a village visit first", "Warning");
                return;
            }

            if (MessageBox.Show("Are you sure you want to delete this village visit?", "Confirm Delete", 
                MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                try
                {
                    var visit = (LeaveService.VillageVisit)VisitsGrid.SelectedItem;

                    using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
                    {
                        connection.Open();
                        connection.Execute("DELETE FROM VillageVisits WHERE Id = @Id", new { Id = visit.Id });
                    }

                    MessageBox.Show("Village visit deleted successfully", "Success");
                    LoadLeaveData(_selectedEmpCode);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error deleting village visit: {ex.Message}", "Error");
                }
            }
        }
    }
}
