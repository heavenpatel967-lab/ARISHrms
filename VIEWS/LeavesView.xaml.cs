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
                // everyone, including employees who have left (they are marked "(left)")
                _employees = EmployeeLookup.LoadAll();

                CmbSelectEmployee.ItemsSource = _employees;
                CmbSelectEmployee.DisplayMemberPath = "DisplayName";
                CmbSelectEmployee.SelectedValuePath = "EmpCode";

                if (_employees.Count > 0)
                    CmbSelectEmployee.SelectedIndex = 0;

                // search by card number / code / name
                EmployeeSearchBinder.Attach(CmbSelectEmployee, TxtEmpSearch, TxtSearchHint, _employees, () =>
                {
                    _selectedEmpCode = null;
                    LeaveGrid.ItemsSource = null;
                });
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

                // every visit for this employee, newest first - old (CSV-imported) years too
                var visits = VillageVisitStore.ForEmployee(empCode)
                    .OrderByDescending(v => v.DepartureDate).ThenByDescending(v => v.Id).ToList();
                LeaveGrid.ItemsSource = visits;
                TxtLeaveCount.Text = visits.Count == 0 ? "No visits recorded" : $"{visits.Count} visit(s)";
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
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select old village visit / long leave CSV",
                Filter = "CSV Files (*.csv)|*.csv"
            };
            if (dlg.ShowDialog() != true) return;

            LeaveCsvResult result;
            try
            {
                result = LeaveCsvImport.Parse(LeaveCsvImport.ReadLines(dlg.FileName), DateTime.Today);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not read the CSV file.\n\nDetails: {ex.Message}", "File Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (result.Rows.Count == 0)
            {
                string msg = "No records could be imported.\n\nSave the sheet as \"CSV (Comma delimited)\" with columns:\n" +
                             "Card No, Departure, Expected Return, Actual Return, Remarks\n\n" + result.ColumnInfo;
                if (result.SkippedLines.Count > 0)
                    msg += "\n\nRows with a card number but unreadable dates:\n" + string.Join("\n", result.SkippedLines.Take(10));
                MessageBox.Show(msg, "Import Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // confirm before writing - shows exactly what will happen
            var confirm = MessageBox.Show(
                $"Found {result.Rows.Count} village visit(s) for {result.Rows.Select(r => r.EmpCode).Distinct().Count()} employee(s).\n" +
                (result.Skipped > 0 ? $"{result.Skipped} row(s) will be skipped (blank or unreadable dates).\n" : "") +
                $"\n{result.ColumnInfo}\n\nVisits that are already saved are not added twice.\n\nImport now?",
                "Import Village Visits", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                LeaveCsvImport.Save(result);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not save the records - nothing was imported.\n\nDetails: {ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var report = new System.Text.StringBuilder();
            report.AppendLine($"Imported: {result.Imported} village visit(s)");
            if (result.Duplicates > 0) report.AppendLine($"Already saved (not added again): {result.Duplicates}");
            if (result.Skipped > 0)
            {
                report.AppendLine($"Skipped: {result.Skipped} row(s) with blank or unreadable dates");
                foreach (var l in result.SkippedLines.Take(10)) report.AppendLine("   " + l);
                if (result.SkippedLines.Count > 10) report.AppendLine($"   ... and {result.SkippedLines.Count - 10} more");
            }
            if (result.UnknownEmployees.Count > 0)
            {
                report.AppendLine();
                report.AppendLine($"{result.UnknownEmployees.Count} card number(s) are not in Employee Master yet - their visits were saved and");
                report.AppendLine("will appear once the employee is synced: " +
                                  string.Join(", ", result.UnknownEmployees.Take(12)) + (result.UnknownEmployees.Count > 12 ? ", ..." : ""));
            }

            MessageBox.Show(report.ToString(), result.Skipped > 0 ? "Import Completed with Skips" : "Import Complete",
                MessageBoxButton.OK, result.Skipped > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);

            if (!string.IsNullOrEmpty(_selectedEmpCode)) LoadLeaveData(_selectedEmpCode);
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
