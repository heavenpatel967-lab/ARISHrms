using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Data.Sqlite;
using Dapper;
using ARISHrms.Data;
using ARISHrms.Models;
using ARISHrms.Services;
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
                var depts = _allEmployees.Select(e => e.DepartmentName).Where(d => !string.IsNullOrWhiteSpace(d)).Distinct().OrderBy(d => d).ToList();
                depts.Insert(0, "All Departments");
                CmbDepartment.ItemsSource = depts;
                CmbDepartment.SelectedIndex = 0;

                var desigs = _allEmployees.Select(e => e.Designation).Where(d => !string.IsNullOrWhiteSpace(d)).Distinct().OrderBy(d => d).ToList();
                desigs.Insert(0, "All Designations");
                CmbDesignation.ItemsSource = desigs;
                CmbDesignation.SelectedIndex = 0;

                var cats = _allEmployees.Select(e => e.CategoryName).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().OrderBy(c => c).ToList();
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
                    (e.CardNo != null && e.CardNo.ToLower().Contains(searchText)) ||
                    (e.Phone != null && e.Phone.ToLower().Contains(searchText)));
            }

            // 2. Dropdown Filters
            if (CmbDepartment.SelectedIndex > 0 && CmbDepartment.SelectedItem != null)
                filtered = filtered.Where(e => e.DepartmentName == CmbDepartment.SelectedItem.ToString());

            if (CmbDesignation.SelectedIndex > 0 && CmbDesignation.SelectedItem != null)
                filtered = filtered.Where(e => e.Designation == CmbDesignation.SelectedItem.ToString());

            if (CmbCategory.SelectedIndex > 0 && CmbCategory.SelectedItem != null)
                filtered = filtered.Where(e => e.CategoryName == CmbCategory.SelectedItem.ToString());

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

        private void BtnImportPhones_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select the Excel sheet with phone numbers (CARD NO. + CONTACT NUMBER)",
                Filter = "Excel Workbook (*.xlsx;*.xlsm)|*.xlsx;*.xlsm"
            };
            if (dlg.ShowDialog() != true) return;

            PhoneImportPlan plan;
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                plan = PhoneImport.Plan(dlg.FileName);
            }
            catch (System.IO.IOException ex)
            {
                MessageBox.Show($"Could not open the file. If it is open in Excel, close it and try again.\n\n{ex.Message}",
                    "Import Phones", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not read the Excel file: {ex.Message}", "Import Phones", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(plan.ColumnInfo);
            sb.AppendLine($"Rows with a card number: {plan.RowsRead}");
            sb.AppendLine($"New numbers (employee had none): {plan.New.Count}");
            sb.AppendLine($"Already saved, same number: {plan.Same}");
            sb.AppendLine($"Employee already has a DIFFERENT number: {plan.Different.Count}");
            if (plan.Empty > 0) sb.AppendLine($"No contact number in the sheet (left as it is): {plan.Empty}");
            if (plan.Duplicates > 0) sb.AppendLine($"Same employee twice in the sheet (first row used): {plan.Duplicates}");
            if (plan.NotFound.Count > 0)
            {
                sb.AppendLine($"Card number not in Employee Master (skipped): {plan.NotFound.Count}");
                sb.AppendLine("   " + string.Join("\n   ", plan.NotFound.Take(8)) + (plan.NotFound.Count > 8 ? "\n   ..." : ""));
            }

            if (plan.New.Count == 0 && plan.Different.Count == 0)
            {
                MessageBox.Show(sb + "\nNothing to change.", "Import Phones", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            bool replace = false;
            if (plan.Different.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Different numbers, saved -> sheet:");
                foreach (var d in plan.Different.Take(6))
                    sb.AppendLine($"   {d.EmpCode} {d.Name}: {d.OldPhone} -> {d.NewPhone}");
                if (plan.Different.Count > 6) sb.AppendLine("   ...");
                sb.AppendLine();
                sb.AppendLine("YES = save the new numbers AND replace the different ones with the sheet's number");
                sb.AppendLine("NO = save the new numbers only, keep the numbers already saved");
                sb.AppendLine("CANCEL = change nothing");

                var answer = MessageBox.Show(sb.ToString(), "Import Phones", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (answer == MessageBoxResult.Cancel) return;
                replace = answer == MessageBoxResult.Yes;
            }
            else
            {
                sb.AppendLine();
                sb.AppendLine($"Save {plan.New.Count} phone number(s)? Only the phone numbers are changed.");
                if (MessageBox.Show(sb.ToString(), "Import Phones", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return;
            }

            try
            {
                int saved = PhoneImport.Apply(plan, replace);
                LoadEmployees();
                MessageBox.Show($"{saved} phone number(s) saved.", "Import Phones", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not save the phone numbers: {ex.Message}", "Import Phones", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EmployeeGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (EmployeeGrid.SelectedItem is Employee selectedEmp)
            {
                new EmployeeProfileWindow(selectedEmp) { Owner = Window.GetWindow(this) }.ShowDialog();
                LoadEmployees();   // the profile window can edit phone / village / name
            }
        }

        private void BtnSyncFolder_Click(object sender, RoutedEventArgs e)
        {
            var folderDialog = new OpenFolderDialog { Title = "Select the folder that contains your .dbf files" };
            string last = DbfFolderScanner.GetLastFolder();
            if (!string.IsNullOrWhiteSpace(last) && System.IO.Directory.Exists(last))
                folderDialog.InitialDirectory = last;

            if (folderDialog.ShowDialog() != true)
                return;

            string folder = folderDialog.FolderName;

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;

                // 1) Read and merge the files - nothing is saved yet
                // remember the folder first, so Loans / Advances / Reports find monsum + mstsal even if the sync stops early
                DbfFolderScanner.SaveLastFolder(folder);
                var scan = DbfFolderScanner.Scan(folder);
                var employees = DbfFolderScanner.BuildEmployees(scan);

                int removed = 0, kept = 0;
                bool saved = false;

                if (employees.Count > 0)
                {
                    // 2) Employees already in the app that are not in these files (old test data, wrong files...)
                    var stale = DbfFolderScanner.FindStaleEmployeeCodes(employees);
                    bool removeStale = false;

                    if (stale.Count > 0)
                    {
                        Mouse.OverrideCursor = null;
                        string sample = string.Join(", ", stale.Take(8)) + (stale.Count > 8 ? ", ..." : "");
                        var answer = MessageBox.Show(
                            $"The files contain {employees.Count} employee(s).\n\n" +
                            $"{stale.Count} employee(s) already in this app are NOT in these files:\n{sample}\n\n" +
                            "YES = remove them (like the old Payroll Companion, which replaced its list on every sync)\n" +
                            "NO = keep them and only add/update\n" +
                            "CANCEL = stop, change nothing\n\n" +
                            "Employees that have village visits, documents, advance ticks or remarks are always kept.",
                            "Remove employees that are not in the files?",
                            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

                        if (answer == MessageBoxResult.Cancel)
                            return;

                        removeStale = answer == MessageBoxResult.Yes;
                        Mouse.OverrideCursor = Cursors.Wait;
                    }

                    // 3) Save
                    DbfFolderScanner.SaveEmployees(scan, employees, removeStale, out removed, out kept);
                    saved = true;
                }

                string report = DbfFolderScanner.SaveReport(scan);
                LoadEmployees();
                Mouse.OverrideCursor = null;

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Folder: {folder}");
                sb.AppendLine($"DBF files found: {scan.Files.Count}");
                sb.AppendLine();

                if (scan.Files.Count == 0)
                {
                    sb.AppendLine("No .dbf files were found in this folder or its subfolders.");
                }
                else if (saved)
                {
                    sb.AppendLine($"Files used: {string.Join(" > ", scan.SourceFiles.Select(f => f.FileName))}");
                    sb.AppendLine($"Employees imported / updated: {employees.Count}  (active {scan.ActiveCount}, left {scan.LeftCount})");
                    if (removed > 0) sb.AppendLine($"Removed from the app: {removed}");
                    if (kept > 0) sb.AppendLine($"Kept because they have village visits, documents, advance ticks or remarks: {kept}");
                }
                else
                {
                    sb.AppendLine("No file in this folder looks like an employee file,");
                    sb.AppendLine("so nothing was imported.");
                }

                if (!string.IsNullOrEmpty(scan.MappingSummary))
                {
                    sb.AppendLine();
                    sb.AppendLine(scan.MappingSummary);
                }

                sb.AppendLine();
                sb.AppendLine("A detailed report listing every file and its columns was saved to:");
                sb.AppendLine(report);

                MessageBox.Show(sb.ToString(), saved ? "Folder Sync Complete" : "Folder Scanned",
                    MessageBoxButton.OK, saved ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                MessageBox.Show($"Error scanning folder: {ex.Message}", "Folder Sync Error");
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
                    DbfFolderScanner.SaveLastFolder(System.IO.Path.GetDirectoryName(openFileDialog.FileName));
                    int count = PunchParserService.ParseEmployeesFromDbf(openFileDialog.FileName);
                    LoadEmployees(); // Instantly refresh the UI grid

                    if (count == 0)
                        MessageBox.Show("No employee records were found in this file.\n\nMake sure you selected the employee master .dbf.", "Nothing Imported", MessageBoxButton.OK, MessageBoxImage.Warning);
                    else
                        MessageBox.Show($"{count} employee record(s) synced from DBF.", "Sync Complete");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error syncing DBF: {ex.Message}", "Sync Error");
                }
            }
        }
    }
}