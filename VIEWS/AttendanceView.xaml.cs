using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.IO;
using System.Globalization;
using Microsoft.Win32;
using Microsoft.Data.Sqlite;
using Dapper;
using ARISHrms.Data;
using ARISHrms.Services; // Uncomment when you paste your services
using ARISHrms.Models;   // Uncomment when you paste your models

namespace ARISHrms.Views
{
    public partial class AttendanceView : UserControl
    {
        public AttendanceView()
        {
            InitializeComponent();
            LoadExceptions();
        }

        private void BtnImportDdd_Click(object sender, RoutedEventArgs e)
        {
            string lastImportPath = "";
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();
                try { lastImportPath = connection.QueryFirstOrDefault<string>("SELECT SettingValue FROM AppSettings WHERE SettingKey = 'LastImportPath'"); } catch { }
            }

            var openFileDialog = new OpenFileDialog { Multiselect = true, Filter = "Attendance Files (*.ddd;*.ren)|*.ddd;*.ren" };
            if (!string.IsNullOrWhiteSpace(lastImportPath) && Directory.Exists(lastImportPath)) { openFileDialog.InitialDirectory = lastImportPath; }

            if (openFileDialog.ShowDialog() == true)
            {
                string newImportPath = Path.GetDirectoryName(openFileDialog.FileNames[0]);
                using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
                {
                    connection.Open();
                    connection.Execute("INSERT OR REPLACE INTO AppSettings (SettingKey, SettingValue) VALUES ('LastImportPath', @Path)", new { Path = newImportPath });
                }

                try
                {
                    foreach (string filename in openFileDialog.FileNames) { PunchParserService.ParseAndSaveFile(filename); }
                    AttendanceProcessorService.ProcessRawPunches();

                    MessageBox.Show("Punches imported and processed successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    LoadExceptions();
                }
                catch (Exception ex) { MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
        }

        private void BtnImportDbf_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog { Multiselect = false, Filter = "Master DBF File (*.dbf)|*.dbf" };
            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    int count = PunchParserService.ParseEmployeesFromDbf(openFileDialog.FileName);

                    if (count == 0)
                        MessageBox.Show("No employee records were found in this file.", "Nothing Imported", MessageBoxButton.OK, MessageBoxImage.Warning);
                    else
                        MessageBox.Show($"Employee Master updated: {count} record(s).", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    LoadExceptions();
                }
                catch (Exception ex) { MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
        }

        private void BtnClearData_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Are you sure you want to delete all attendance punches? (Your Employee Master Data will be kept).", "Clear Data", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
                    {
                        connection.Open();
                        connection.Execute("DELETE FROM RawPunches; DELETE FROM DailyAttendance;");
                        MessageBox.Show("All old attendance data cleared!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                        LoadExceptions();
                    }
                }
                catch (Exception ex) { MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
        }

        private void BtnAutoCorrect_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("AutoCorrect engine triggered. (Ensure DailyAttendance model is linked)", "Info");
            // The full massive Auto-Correct logic from your old MainWindow.xaml.cs goes here!
            // Just paste the contents of your old BtnAutoCorrect_Click method inside here.
        }

        private void chkSelectAll_Click(object sender, RoutedEventArgs e)
        {
            // Requires DailyAttendance model to be uncommented
            /*
            bool isChecked = (sender as CheckBox)?.IsChecked == true;
            var currentData = ExceptionGrid.ItemsSource as IEnumerable<DailyAttendance>;
            if (currentData != null)
            {
                foreach (var record in currentData) record.IsSelected = isChecked;
                ExceptionGrid.Items.Refresh();
            }
            */
        }

        private void BtnEditSelected_Click(object sender, RoutedEventArgs e)
        {
            // Requires DailyAttendance model and BatchEditWindow
            /*
            var currentData = ExceptionGrid.ItemsSource as IEnumerable<DailyAttendance>;
            if (currentData == null) return;
            var selectedRecords = currentData.Where(r => r.IsSelected).ToList();

            if (selectedRecords.Count == 0)
            {
                MessageBox.Show("Please select at least one employee.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var editWindow = new BatchEditWindow { Owner = Window.GetWindow(this) };
            if (editWindow.ShowDialog() == true)
            {
                 // Paste the Batch Edit logic here...
                 LoadExceptions();
            }
            */
        }

        private void BtnExportDdd_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Export triggered. (Ensure DailyAttendance model is linked)", "Info");
            // Paste your Export logic here
        }

        private void BtnRefreshGrid_Click(object sender, RoutedEventArgs e) => LoadExceptions(TxtSearchEmp.Text.Trim());
        private void ChkShowAll_Changed(object sender, RoutedEventArgs e) => LoadExceptions(TxtSearchEmp.Text.Trim());
        private void BtnSearch_Click(object sender, RoutedEventArgs e) => LoadExceptions(TxtSearchEmp.Text.Trim());
        private void BtnClearSearch_Click(object sender, RoutedEventArgs e) { TxtSearchEmp.Clear(); LoadExceptions(); }
        private void TxtSearchEmp_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) LoadExceptions(TxtSearchEmp.Text.Trim()); }

        private void LoadExceptions(string searchTerm = "")
        {
            // Requires DailyAttendance model to be copied over
            /*
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();
                bool showAll = ChkShowAll?.IsChecked == true;
                var conditions = new List<string>();

                if (!showAll) conditions.Add("d.IsException = 1");
                if (!string.IsNullOrWhiteSpace(searchTerm)) conditions.Add("(d.EmpCode LIKE @Search OR e.CardNo LIKE @Search OR e.FullName LIKE @Search)");

                string whereClause = conditions.Any() ? "WHERE " + string.Join(" AND ", conditions) : "";

                if (GridGroupBox != null)
                {
                    string headerBase = showAll ? "📋 All Attendance Records" : "⚠️ Exceptions (Missing Punches)";
                    GridGroupBox.Header = string.IsNullOrWhiteSpace(searchTerm) ? headerBase : $"{headerBase} - Searching: {searchTerm}";
                }

                string sql = $@"
                    SELECT d.*, e.FullName, e.CardNo, e.Department, e.Category, e.Shift 
                    FROM DailyAttendance d
                    LEFT JOIN Employees e ON d.EmpCode = e.EmpCode
                    {whereClause}
                    ORDER BY d.WorkDate DESC, d.EmpCode ASC";

                ExceptionGrid.ItemsSource = connection.Query<DailyAttendance>(sql, new { Search = $"%{searchTerm}%" }).ToList();
            }
            */
        }

        private void LoadSettings()
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();
                ListAutoFill.ItemsSource = connection.Query<string>("SELECT EmpCode FROM SpecialRules WHERE RuleType = 'AutoFillAll'").ToList();
                ListEarlyLunch.ItemsSource = connection.Query<string>("SELECT EmpCode FROM SpecialRules WHERE RuleType = 'ShiftEarlyLunch'").ToList();
            }
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e) { LoadSettings(); SettingsPanelOverlay.Visibility = Visibility.Visible; }
        private void BtnCloseSettings_Click(object sender, RoutedEventArgs e) => SettingsPanelOverlay.Visibility = Visibility.Collapsed;

        private void AddRule(string ruleType, string empCode)
        {
            if (string.IsNullOrWhiteSpace(empCode) || empCode.Length != 4) { MessageBox.Show("Enter valid 4-digit code.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();
                connection.Execute("INSERT OR IGNORE INTO SpecialRules (RuleType, EmpCode) VALUES (@RuleType, @EmpCode)", new { RuleType = ruleType, EmpCode = empCode });
            }
            LoadSettings();
        }

        private void RemoveRule(string ruleType, string empCode)
        {
            if (string.IsNullOrWhiteSpace(empCode)) return;
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();
                connection.Execute("DELETE FROM SpecialRules WHERE RuleType = @RuleType AND EmpCode = @EmpCode", new { RuleType = ruleType, EmpCode = empCode });
            }
            LoadSettings();
        }

        private void BtnAddAutoFill_Click(object sender, RoutedEventArgs e) { AddRule("AutoFillAll", TxtAutoFillEmp.Text); TxtAutoFillEmp.Clear(); }
        private void BtnRemoveAutoFill_Click(object sender, RoutedEventArgs e) => RemoveRule("AutoFillAll", ListAutoFill.SelectedItem as string);
        private void BtnAddEarlyLunch_Click(object sender, RoutedEventArgs e) { AddRule("ShiftEarlyLunch", TxtEarlyLunchEmp.Text); TxtEarlyLunchEmp.Clear(); }
        private void BtnRemoveEarlyLunch_Click(object sender, RoutedEventArgs e) => RemoveRule("ShiftEarlyLunch", ListEarlyLunch.SelectedItem as string);

        private void BtnApplyDateSwap_Click(object sender, RoutedEventArgs e)
        {
            // Paste Date Swap logic here
        }
    }
}