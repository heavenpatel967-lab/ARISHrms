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
            InitializeLeaveTypes();
            LoadEmployeeList();
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

                    // LeaveCode is UNIQUE, so INSERT OR IGNORE keeps this idempotent
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

                // Make sure this employee has a balance row for every leave type
                using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
                {
                    connection.Open();
                    var leaveTypes = connection.Query<LeaveType>("SELECT * FROM LeaveTypes").ToList();

                    foreach (var type in leaveTypes)
                    {
                        LeaveService.InitializeLeaveBalance(empCode, currentYear, type.Id, type.MaxDaysPerYear);
                    }
                }

                LeaveGrid.ItemsSource = LeaveService.GetVillageVisits(empCode, currentYear);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading leave data: {ex.Message}", "Error");
            }
        }

        private int GetVillageVisitTypeId()
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();
                return connection.QuerySingleOrDefault<int?>(
                    "SELECT Id FROM LeaveTypes WHERE LeaveCode = 'VV'") ?? 1;
            }
        }

        private void BtnSaveLeave_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedEmpCode))
            {
                MessageBox.Show("Please select an employee first", "Warning");
                return;
            }

            if (DpDeparture.SelectedDate == null || DpExpectedReturn.SelectedDate == null)
            {
                MessageBox.Show("Please choose both Departure Date and Expected Return", "Warning");
                return;
            }

            DateTime departure = DpDeparture.SelectedDate.Value;
            DateTime expected = DpExpectedReturn.SelectedDate.Value;

            if (expected < departure)
            {
                MessageBox.Show("Expected Return cannot be before Departure Date", "Warning");
                return;
            }

            try
            {
                string notes = string.IsNullOrWhiteSpace(TxtNotes.Text) ? "Village visit" : TxtNotes.Text.Trim();

                int visitId = LeaveService.RecordVillageVisit(
                    _selectedEmpCode,
                    GetVillageVisitTypeId(),
                    departure,
                    expected,
                    notes,
                    "System");

                // If the employee has already returned, record it so late days / penalty are computed
                if (DpActualReturn.SelectedDate != null)
                {
                    LeaveService.UpdateActualReturn(visitId, DpActualReturn.SelectedDate.Value);
                }

                MessageBox.Show("Leave record saved successfully", "Success");

                DpDeparture.SelectedDate = null;
                DpExpectedReturn.SelectedDate = null;
                DpActualReturn.SelectedDate = null;
                TxtNotes.Clear();

                LoadLeaveData(_selectedEmpCode);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error recording leave: {ex.Message}", "Error");
            }
        }

        private void BtnImportCsv_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("CSV import is not implemented yet.", "Not available");
        }

        private void BtnDeleteLeave_Click(object sender, RoutedEventArgs e)
        {
            // The button sits inside a grid row, so read the row from its DataContext
            if ((sender as FrameworkElement)?.DataContext is not VillageVisit visit)
            {
                MessageBox.Show("Please select a village visit first", "Warning");
                return;
            }

            if (MessageBox.Show("Are you sure you want to delete this village visit?", "Confirm Delete",
                MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                return;

            try
            {
                using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
                {
                    connection.Open();
                    connection.Execute("DELETE FROM VillageVisits WHERE Id = @Id", new { visit.Id });
                }

                LoadLeaveData(_selectedEmpCode);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error deleting village visit: {ex.Message}", "Error");
            }
        }
    }
}
