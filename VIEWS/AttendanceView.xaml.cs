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
            var result = MessageBox.Show("This will auto-fill missing gaps, round early 'Machine Rush' departures, and process special permission skipped lunches. Proceed?", "Confirm Smart Auto-Fill", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
                {
                    connection.Open();
                    var autoFillEmps = connection.Query<string>("SELECT EmpCode FROM SpecialRules WHERE RuleType = 'AutoFillAll'").ToList();

                    string sqlAll = @"
                        SELECT d.*, e.Shift, e.Category, e.Department 
                        FROM DailyAttendance d 
                        LEFT JOIN Employees e ON d.EmpCode = e.EmpCode";

                    var allRecords = connection.Query<DailyAttendance>(sqlAll).ToList();
                    int fixCount = 0;

                    var auditLogLines = new List<string>();
                    auditLogLines.Add("Emp Code,Employee Name,Category,Shift,Date,Old In,Old L-Out,Old L-In,Old Out,Old P5,Old P6,Old P7,Old P8");

                    string FormatTime(DateTime? t) => t?.ToString("HH:mm") ?? "";

                    foreach (var record in allRecords)
                    {
                        var rawP = new List<DateTime>();
                        if (record.PunchIn.HasValue) rawP.Add(record.PunchIn.Value);
                        if (record.LunchOut.HasValue) rawP.Add(record.LunchOut.Value);
                        if (record.LunchIn.HasValue) rawP.Add(record.LunchIn.Value);
                        if (record.PunchOut.HasValue) rawP.Add(record.PunchOut.Value);
                        if (record.Punch5.HasValue) rawP.Add(record.Punch5.Value);
                        if (record.Punch6.HasValue) rawP.Add(record.Punch6.Value);
                        if (record.Punch7.HasValue) rawP.Add(record.Punch7.Value);
                        if (record.Punch8.HasValue) rawP.Add(record.Punch8.Value);
                        rawP.Sort();

                        string originalStateCsv = $"{record.EmpCode},{record.FullName?.Replace(",", " ")},{record.Category},{record.Shift},{record.WorkDate:dd-MM-yyyy}," +
                                                  $"{FormatTime(record.PunchIn)},{FormatTime(record.LunchOut)},{FormatTime(record.LunchIn)},{FormatTime(record.PunchOut)}," +
                                                  $"{FormatTime(record.Punch5)},{FormatTime(record.Punch6)},{FormatTime(record.Punch7)},{FormatTime(record.Punch8)}";

                        var p = new List<DateTime>();
                        foreach (var time in rawP) { if (!p.Any() || (time - p.Last()).TotalMinutes > 5) { p.Add(time); } }

                        int endHour = 17;
                        if (record.Shift == "B") { endHour = 19; }
                        else if (record.Shift == "D" || record.Shift == "E") { endHour = 18; }

                        DateTime stdIn = record.WorkDate.AddHours(8).AddMinutes(30);
                        if (record.Shift == "E") { stdIn = record.WorkDate.AddHours(9); }

                        DateTime stdLOut = record.WorkDate.AddHours(12).AddMinutes(30);
                        DateTime stdLIn = record.WorkDate.AddHours(13);
                        DateTime baseStdOut = record.WorkDate.AddHours(endHour);
                        DateTime targetStdOut = baseStdOut;

                        var peers = allRecords.Where(r => r.WorkDate == record.WorkDate && r.Category == record.Category && r.Shift == record.Shift && !r.IsException).ToList();
                        if (peers.Any())
                        {
                            var maxOutHour = peers.Max(r =>
                            {
                                if (r.Punch8.HasValue) return r.Punch8.Value.TimeOfDay.TotalHours;
                                if (r.PunchOut.HasValue) return r.PunchOut.Value.TimeOfDay.TotalHours;
                                if (r.LunchOut.HasValue && !r.PunchOut.HasValue) return r.LunchOut.Value.TimeOfDay.TotalHours;
                                return 0;
                            });

                            if (maxOutHour >= 18.5 && maxOutHour < 20) { targetStdOut = record.WorkDate.AddHours(19); }
                            else if (maxOutHour >= 17.5 && maxOutHour < 18.5) { targetStdOut = record.WorkDate.AddHours(18); }
                            else if (maxOutHour >= 16.5 && maxOutHour < 17.5) { targetStdOut = record.WorkDate.AddHours(17); }
                        }

                        bool isModified = false;
                        bool hasEarlyCastingPunch = p.Any(x => x.TimeOfDay >= new TimeSpan(4, 0, 0) && x.TimeOfDay <= new TimeSpan(7, 45, 0));
                        bool hasNightPunches = p.Any(x => x.TimeOfDay < new TimeSpan(6, 0, 0) || x.TimeOfDay > new TimeSpan(21, 30, 0));
                        bool isSkippedLunch = p.Count == 2 && p[0].TimeOfDay < new TimeSpan(11, 0, 0) && p[1].TimeOfDay >= new TimeSpan(14, 30, 0);

                        if (hasEarlyCastingPunch)
                        {
                            p.Clear();
                            p.Add(record.WorkDate.AddHours(8).AddMinutes(30));
                            p.Add(record.WorkDate.AddHours(12).AddMinutes(30));
                            p.Add(record.WorkDate.AddHours(13));
                            p.Add(record.WorkDate.AddHours(19));
                            isModified = true;
                        }
                        else if (autoFillEmps.Contains(record.EmpCode))
                        {
                            if (p.Count != 4 || p.Last() != record.WorkDate.AddHours(19))
                            {
                                p.Clear();
                                p.Add(stdIn); p.Add(stdLOut); p.Add(stdLIn); p.Add(record.WorkDate.AddHours(19));
                                isModified = true;
                            }
                        }
                        else if (p.Count > 4 && !hasNightPunches)
                        {
                            p.Clear();
                            p.Add(stdIn); p.Add(stdLOut); p.Add(stdLIn); p.Add(targetStdOut);
                            isModified = true;
                        }
                        else if (p.Count < 4 && (record.IsException || isSkippedLunch))
                        {
                            bool hasIn = p.Any(x => x.TimeOfDay < new TimeSpan(11, 0, 0));
                            bool hasLOut = p.Any(x => x.TimeOfDay >= new TimeSpan(11, 0, 0) && x.TimeOfDay <= new TimeSpan(12, 45, 0));
                            bool hasLIn = p.Any(x => x.TimeOfDay > new TimeSpan(12, 45, 0) && x.TimeOfDay < new TimeSpan(15, 0, 0));
                            bool hasOut = p.Any(x => x.TimeOfDay >= new TimeSpan(15, 0, 0));

                            if (!hasOut && p.Count < 4) p.Add(targetStdOut);
                            if (!hasLIn && p.Count < 4) p.Add(stdLIn);
                            if (!hasLOut && p.Count < 4) p.Add(stdLOut);
                            if (!hasIn && p.Count < 4) p.Add(stdIn);
                            p.Sort();
                            isModified = true;
                        }

                        if (p.Any() && !hasNightPunches)
                        {
                            DateTime lastPunch = p.Last();
                            if (lastPunch >= baseStdOut.AddMinutes(-45) && lastPunch < baseStdOut)
                            {
                                p[p.Count - 1] = baseStdOut;
                                isModified = true;
                            }
                            else if (lastPunch >= targetStdOut.AddMinutes(-45) && lastPunch < targetStdOut)
                            {
                                p[p.Count - 1] = targetStdOut;
                                isModified = true;
                            }
                        }

                        if (isModified || record.IsException)
                        {
                            if (isModified) { auditLogLines.Add(originalStateCsv); }

                            record.PunchIn = p.Count > 0 ? p[0] : (DateTime?)null;
                            record.LunchOut = p.Count > 1 ? p[1] : (DateTime?)null;
                            record.LunchIn = p.Count > 2 ? p[2] : (DateTime?)null;
                            record.PunchOut = p.Count > 3 ? p[3] : (DateTime?)null;

                            if (p.Count <= 4)
                            {
                                record.Punch5 = null; record.Punch6 = null; record.Punch7 = null; record.Punch8 = null;
                            }
                            else
                            {
                                record.Punch5 = p.Count > 4 ? p[4] : (DateTime?)null;
                                record.Punch6 = p.Count > 5 ? p[5] : (DateTime?)null;
                                record.Punch7 = p.Count > 6 ? p[6] : (DateTime?)null;
                                record.Punch8 = p.Count > 7 ? p[7] : (DateTime?)null;
                            }

                            record.IsException = false;
                            string sqlUpdate = "UPDATE DailyAttendance SET PunchIn = @PunchIn, LunchOut = @LunchOut, LunchIn = @LunchIn, PunchOut = @PunchOut, Punch5 = @Punch5, Punch6 = @Punch6, Punch7 = @Punch7, Punch8 = @Punch8, IsException = @IsException WHERE EmpCode = @EmpCode AND WorkDate = @WorkDate";
                            connection.Execute(sqlUpdate, record);
                            fixCount++;
                        }
                    }

                    if (fixCount > 0 && auditLogLines.Count > 1)
                    {
                        var msgResult = MessageBox.Show($"Successfully auto-corrected {fixCount} records!\n\nWould you like to save the Backup Audit Excel file showing the old erased punches?", "Save Audit Report", MessageBoxButton.YesNo, MessageBoxImage.Information);

                        if (msgResult == MessageBoxResult.Yes)
                        {
                            var saveDialog = new SaveFileDialog
                            {
                                Filter = "Excel CSV File (*.csv)|*.csv",
                                FileName = $"AutoCorrect_AuditLog_{DateTime.Now:ddMMyyyy_HHmm}.csv",
                                Title = "Save Before-Modification Audit Report"
                            };

                            if (saveDialog.ShowDialog() == true)
                            {
                                File.WriteAllLines(saveDialog.FileName, auditLogLines);
                                MessageBox.Show("Audit Report saved successfully!", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
                            }
                        }
                    }
                    else if (fixCount > 0)
                    {
                        MessageBox.Show($"Successfully analyzed and auto-corrected {fixCount} records!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("No missing punches or exceptions needed correction.", "Up to Date", MessageBoxButton.OK, MessageBoxImage.Information);
                    }

                    LoadExceptions(TxtSearchEmp.Text.Trim());
                }
            }
        }

        private void chkSelectAll_Click(object sender, RoutedEventArgs e)
        {
            bool isChecked = (sender as System.Windows.Controls.CheckBox)?.IsChecked == true;
            var currentData = ExceptionGrid.ItemsSource as IEnumerable<DailyAttendance>;
            if (currentData != null)
            {
                foreach (var record in currentData)
                {
                    record.IsSelected = isChecked;
                }
                ExceptionGrid.Items.Refresh();
            }
        }

        private void BtnEditSelected_Click(object sender, RoutedEventArgs e)
        {
            var currentData = ExceptionGrid.ItemsSource as IEnumerable<DailyAttendance>;
            if (currentData == null) return;

            var selectedRecords = currentData.Where(r => r.IsSelected).ToList();

            if (selectedRecords.Count == 0)
            {
                MessageBox.Show("Please select at least one employee using the checkboxes.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var editWindow = new BatchEditWindow { Owner = Window.GetWindow(this) };

            if (selectedRecords.Count == 1)
            {
                var r = selectedRecords[0];
                string FormatTime(DateTime? t) => t?.ToString("HH:mm") ?? "";

                editWindow.PreFillData(
                    FormatTime(r.PunchIn), FormatTime(r.LunchOut), FormatTime(r.LunchIn), FormatTime(r.PunchOut),
                    FormatTime(r.Punch5), FormatTime(r.Punch6), FormatTime(r.Punch7), FormatTime(r.Punch8)
                );
            }

            if (editWindow.ShowDialog() == true)
            {
                using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
                {
                    connection.Open();

                    DateTime? ParseTime(DateTime baseDate, string timeStr)
                    {
                        if (string.IsNullOrWhiteSpace(timeStr)) return null;
                        try
                        {
                            var parts = timeStr.Split(':');
                            int hour = int.Parse(parts[0]);
                            DateTime correctDate = hour < 6 ? baseDate.AddDays(1) : baseDate;
                            return new DateTime(correctDate.Year, correctDate.Month, correctDate.Day, hour, int.Parse(parts[1]), 0);
                        }
                        catch { return null; }
                    }

                    int updateCount = 0;
                    bool keepBlank = editWindow.KeepBlankAsOriginal; // Grab the checkbox value

                    foreach (var record in selectedRecords)
                    {
                        // Logic:
                        // If box is blank AND Checkbox is checked -> Keep original punch. 
                        // If box is blank AND Checkbox is unchecked -> Clear punch to NULL.
                        DateTime? newIn = string.IsNullOrWhiteSpace(editWindow.NewIn) && keepBlank ? record.PunchIn : ParseTime(record.WorkDate, editWindow.NewIn);
                        DateTime? newLOut = string.IsNullOrWhiteSpace(editWindow.NewLOut) && keepBlank ? record.LunchOut : ParseTime(record.WorkDate, editWindow.NewLOut);
                        DateTime? newLIn = string.IsNullOrWhiteSpace(editWindow.NewLIn) && keepBlank ? record.LunchIn : ParseTime(record.WorkDate, editWindow.NewLIn);
                        DateTime? newOut = string.IsNullOrWhiteSpace(editWindow.NewOut) && keepBlank ? record.PunchOut : ParseTime(record.WorkDate, editWindow.NewOut);
                        DateTime? newP5 = string.IsNullOrWhiteSpace(editWindow.NewP5) && keepBlank ? record.Punch5 : ParseTime(record.WorkDate, editWindow.NewP5);
                        DateTime? newP6 = string.IsNullOrWhiteSpace(editWindow.NewP6) && keepBlank ? record.Punch6 : ParseTime(record.WorkDate, editWindow.NewP6);
                        DateTime? newP7 = string.IsNullOrWhiteSpace(editWindow.NewP7) && keepBlank ? record.Punch7 : ParseTime(record.WorkDate, editWindow.NewP7);
                        DateTime? newP8 = string.IsNullOrWhiteSpace(editWindow.NewP8) && keepBlank ? record.Punch8 : ParseTime(record.WorkDate, editWindow.NewP8);

                        string sqlUpdate = @"UPDATE DailyAttendance 
                                             SET PunchIn = @In, LunchOut = @LOut, LunchIn = @LIn, PunchOut = @Out, 
                                                 Punch5 = @P5, Punch6 = @P6, Punch7 = @P7, Punch8 = @P8, IsException = 0 
                                             WHERE EmpCode = @EmpCode AND WorkDate = @WorkDate";

                        connection.Execute(sqlUpdate, new
                        {
                            In = newIn,
                            LOut = newLOut,
                            LIn = newLIn,
                            Out = newOut,
                            P5 = newP5,
                            P6 = newP6,
                            P7 = newP7,
                            P8 = newP8,
                            EmpCode = record.EmpCode,
                            WorkDate = record.WorkDate
                        });

                        updateCount++;
                    }

                    MessageBox.Show($"Successfully updated {updateCount} records.", "Batch Edit Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                LoadExceptions(TxtSearchEmp.Text.Trim());
            }
        }

        private void BtnExportDdd_Click(object sender, RoutedEventArgs e)
        {
            var displayedRecords = ExceptionGrid.ItemsSource as IEnumerable<DailyAttendance>;

            if (displayedRecords == null || !displayedRecords.Any())
            {
                MessageBox.Show("There are no records currently displayed to export.", "Empty Grid", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string lastExportPath = "";

            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();
                try { lastExportPath = connection.QueryFirstOrDefault<string>("SELECT SettingValue FROM AppSettings WHERE SettingKey = 'LastExportPath'"); } catch { }
            }

            var folderDialog = new OpenFolderDialog { Title = "Select Folder to Save Daily .ddd Files" };
            if (!string.IsNullOrWhiteSpace(lastExportPath) && Directory.Exists(lastExportPath)) { folderDialog.InitialDirectory = lastExportPath; }

            if (folderDialog.ShowDialog() == true)
            {
                string selectedPath = folderDialog.FolderName;

                using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
                {
                    connection.Open();
                    connection.Execute("INSERT OR REPLACE INTO AppSettings (SettingKey, SettingValue) VALUES ('LastExportPath', @Path)", new { Path = selectedPath });
                }

                try
                {
                    var recordsByDate = displayedRecords.GroupBy(r => r.WorkDate.Date).ToList();
                    int filesGenerated = 0;

                    foreach (var dateGroup in recordsByDate)
                    {
                        var exportLines = new List<string>();

                        void AddPunchLine(DateTime? punchTime, string empCode)
                        {
                            if (punchTime.HasValue)
                            {
                                string dateStr = punchTime.Value.ToString("ddMMyy");
                                string timeStr = punchTime.Value.ToString("HHmm");
                                string formattedEmpCode = empCode.PadLeft(8, '0');
                                exportLines.Add($"{dateStr}01{timeStr} {formattedEmpCode}");
                            }
                        }

                        foreach (var record in dateGroup)
                        {
                            AddPunchLine(record.PunchIn, record.EmpCode);
                            AddPunchLine(record.LunchOut, record.EmpCode);
                            AddPunchLine(record.LunchIn, record.EmpCode);
                            AddPunchLine(record.PunchOut, record.EmpCode);
                            AddPunchLine(record.Punch5, record.EmpCode);
                            AddPunchLine(record.Punch6, record.EmpCode);
                            AddPunchLine(record.Punch7, record.EmpCode);
                            AddPunchLine(record.Punch8, record.EmpCode);
                        }

                        if (exportLines.Any())
                        {
                            var sortedLines = exportLines.OrderBy(line => DateTime.ParseExact(line.Substring(0, 6) + line.Substring(8, 4), "ddMMyyHHmm", CultureInfo.InvariantCulture)).ToList();
                            string fileName = $"{dateGroup.Key:ddMMyy}01.ddd";
                            string fullPath = System.IO.Path.Combine(selectedPath, fileName);

                            File.WriteAllLines(fullPath, sortedLines);
                            filesGenerated++;
                        }
                    }
                    MessageBox.Show($"Successfully generated {filesGenerated} daily .ddd file(s) for the visible records in the selected folder!", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex) { MessageBox.Show($"Error exporting files: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
        }

        private void BtnRefreshGrid_Click(object sender, RoutedEventArgs e) => LoadExceptions(TxtSearchEmp.Text.Trim());
        private void ChkShowAll_Changed(object sender, RoutedEventArgs e) => LoadExceptions(TxtSearchEmp.Text.Trim());
        private void BtnSearch_Click(object sender, RoutedEventArgs e) => LoadExceptions(TxtSearchEmp.Text.Trim());
        private void BtnClearSearch_Click(object sender, RoutedEventArgs e) { TxtSearchEmp.Clear(); LoadExceptions(); }
        private void TxtSearchEmp_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) LoadExceptions(TxtSearchEmp.Text.Trim()); }

        private void LoadExceptions(string searchTerm = "")
        {
            using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
            {
                connection.Open();
                bool showAll = ChkShowAll?.IsChecked == true;

                var conditions = new List<string>();

                if (!showAll)
                {
                    conditions.Add("d.IsException = 1");
                }

                if (!string.IsNullOrWhiteSpace(searchTerm))
                {
                    conditions.Add("(d.EmpCode LIKE @Search OR e.CardNo LIKE @Search OR e.FullName LIKE @Search)");
                }

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

        // 📅 Monthly Summary: one employee, one month (read only - does not touch the punches)
        private void BtnMonthlySummary_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var row = ExceptionGrid.SelectedItem as DailyAttendance;
                string who = row != null ? row.EmpCode : TxtSearchEmp.Text.Trim();
                DateTime? month = row != null ? row.WorkDate : (DateTime?)null;
                new MonthlyAttendanceWindow(who, month) { Owner = Window.GetWindow(this) }.ShowDialog();
            }
            catch (Exception ex) { MessageBox.Show($"Error: {ex.Message}", "Monthly Summary", MessageBoxButton.OK, MessageBoxImage.Error); }
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
            try
            {
                DateTime fromDate = DateTime.ParseExact(TxtSwapFrom.Text.Trim(), "dd-MM-yyyy", CultureInfo.InvariantCulture);
                DateTime toDate = DateTime.ParseExact(TxtSwapTo.Text.Trim(), "dd-MM-yyyy", CultureInfo.InvariantCulture);

                var result = MessageBox.Show($"Are you sure you want to move ALL attendance records from {fromDate:dd-MM-yyyy} to {toDate:dd-MM-yyyy}?\n\nThis will trick the legacy software into calculating regular hours instead of Overtime on week-offs.", "Confirm Date Swap", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
                    {
                        connection.Open();
                        var allRecords = connection.Query<DailyAttendance>("SELECT * FROM DailyAttendance").ToList();
                        var recordsToSwap = allRecords.Where(r => r.WorkDate.Date == fromDate.Date).ToList();

                        int count = 0;
                        foreach (var record in recordsToSwap)
                        {
                            record.WorkDate = toDate;

                            DateTime? SwapTime(DateTime? t) => t.HasValue ? new DateTime(toDate.Year, toDate.Month, toDate.Day, t.Value.Hour, t.Value.Minute, t.Value.Second) : (DateTime?)null;

                            record.PunchIn = SwapTime(record.PunchIn);
                            record.LunchOut = SwapTime(record.LunchOut);
                            record.LunchIn = SwapTime(record.LunchIn);
                            record.PunchOut = SwapTime(record.PunchOut);
                            record.Punch5 = SwapTime(record.Punch5);
                            record.Punch6 = SwapTime(record.Punch6);
                            record.Punch7 = SwapTime(record.Punch7);
                            record.Punch8 = SwapTime(record.Punch8);

                            string sqlUpdate = "UPDATE DailyAttendance SET WorkDate = @WorkDate, PunchIn = @PunchIn, LunchOut = @LunchOut, LunchIn = @LunchIn, PunchOut = @PunchOut, Punch5 = @Punch5, Punch6 = @Punch6, Punch7 = @Punch7, Punch8 = @Punch8 WHERE EmpCode = @EmpCode AND WorkDate = @OldDate";

                            connection.Execute(sqlUpdate, new
                            {
                                WorkDate = record.WorkDate,
                                PunchIn = record.PunchIn,
                                LunchOut = record.LunchOut,
                                LunchIn = record.LunchIn,
                                PunchOut = record.PunchOut,
                                Punch5 = record.Punch5,
                                Punch6 = record.Punch6,
                                Punch7 = record.Punch7,
                                Punch8 = record.Punch8,
                                EmpCode = record.EmpCode,
                                OldDate = fromDate.Date
                            });

                            count++;
                        }

                        MessageBox.Show($"Successfully moved {count} records to {toDate:dd-MM-yyyy}!", "Swap Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                        LoadExceptions(TxtSearchEmp.Text.Trim());
                        SettingsPanelOverlay.Visibility = Visibility.Collapsed;
                    }
                }
            }
            catch { MessageBox.Show("Please enter valid dates in DD-MM-YYYY format.", "Invalid Date", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
    }
}