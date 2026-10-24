using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ARISHrms.Models;
using ARISHrms.Services;
using ClosedXML.Excel;

namespace ARISHrms.Views
{
    /// <summary>
    /// Advance history for one employee, read from EasyPay's monsum.DBF (ADVANCE column).
    /// The monthly limit is set per employee here (default 5000); a month above it is flagged red.
    /// </summary>
    public partial class AdvancesView : UserControl
    {
        private List<Employee> _employees = new List<Employee>();
        private MonsumData _monsum;
        private Employee _selected;
        private AdvanceSummary _summary;
        private bool _loading = true;

        public AdvancesView()
        {
            InitializeComponent();
            LoadEmployeeList();
            _loading = false;
            if (_selected != null) LoadAdvanceData();
        }

        private void LoadEmployeeList()
        {
            try
            {
                _employees = EmployeeLookup.LoadAll();
                CmbSelectEmployee.ItemsSource = _employees;
                CmbSelectEmployee.DisplayMemberPath = "DisplayName";
                CmbSelectEmployee.SelectedValuePath = "EmpCode";

                if (_employees.Count == 0)
                {
                    TxtInfo.Text = "No employees yet. Use Employee Master -> Sync DBFS Folder.";
                    return;
                }

                CmbSelectEmployee.SelectedIndex = 0;

                // search by card number / code / name
                EmployeeSearchBinder.Attach(CmbSelectEmployee, TxtEmpSearch, TxtSearchHint, _employees, () =>
                {
                    _selected = null;
                    Refresh();
                    TxtInfo.Text = "No employee matches your search.";
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading employees: {ex.Message}", "Error");
            }
        }

        private bool EnsureMonsum()
        {
            string path = DbfSource.Find("monsum");
            if (path == null)
            {
                TxtInfo.Text = "monsum.DBF not found. " + DbfSource.NotFoundHelp.Replace("\n\n", " ").Replace("\n", " ");
                return false;
            }

            try
            {
                _monsum = MonsumData.Load(path);
                return true;
            }
            catch (Exception ex)
            {
                TxtInfo.Text = "Could not read monsum.DBF: " + ex.Message;
                return false;
            }
        }

        private void CmbSelectEmployee_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selected = CmbSelectEmployee.SelectedItem as Employee;
            if (_selected == null || _loading) return;
            LoadAdvanceData();
        }

        private double CurrentLimit()
        {
            return double.TryParse(TxtMonthlyLimit.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
        }

        private void LoadAdvanceData()
        {
            // show this employee's saved limit without writing it back
            bool wasLoading = _loading;
            _loading = true;
            TxtMonthlyLimit.Text = AdvanceLimitStore.Get(_selected.EmpCode).ToString("0", CultureInfo.InvariantCulture);
            _loading = wasLoading;

            Refresh();
        }

        private void Refresh()
        {
            AdvanceGrid.ItemsSource = null;
            TrendDotsContainer.Children.Clear();
            TakenDotsContainer.Children.Clear();
            TxtTotalAdvances.Text = "₹ 0.00";
            TxtHealth.Text = "0 / 0 Safe";
            TxtTakenHealth.Text = "0 / 6 Months";
            TxtTakenSummary.Text = "";
            TxtLifetimeAdvance.Text = "Total Lifetime Advance: ₹ 0.00";
            _summary = null;
            TrackRecord.ShowEmployee(_selected?.EmpCode);

            if (_selected == null) return;

            // ticks are kept in this app, so they show even when monsum.DBF is not available
            List<AdvanceTakenEntry> ticks;
            try { ticks = AdvanceTakenStore.ForEmployee(_selected.EmpCode); }
            catch (Exception ex) { ticks = new List<AdvanceTakenEntry>(); TxtInfo.Text = "Could not read advance ticks: " + ex.Message; }

            bool haveMonsum = EnsureMonsum();
            var monsumItems = new List<AdvanceItem>();
            if (haveMonsum)
            {
                _summary = _monsum.BuildAdvanceSummary(_selected.EmpCode, _selected.CardNo, CurrentLimit());
                monsumItems = _summary.Items;

                TxtTotalAdvances.Text = $"₹ {_summary.Total:N2}";
                TxtLifetimeAdvance.Text = $"Total Lifetime Advance: ₹ {_summary.Total:N2}";
                TxtHealth.Text = $"{_summary.SafeCount} / {_summary.Items.Count} Safe";

                foreach (var item in _summary.Items.Skip(Math.Max(0, _summary.Items.Count - 6)))
                    TrendDotsContainer.Children.Add(Dot(item.StatusColor, $"{item.MonthYear}: ₹ {item.AdvanceAmount:N0}"));
            }

            AdvanceGrid.ItemsSource = AdvanceLedgerBuilder.Build(monsumItems, ticks);

            // last 6 months: was an advance actually handed over?
            var takenDots = TrackRecord_TakenDots(ticks);
            foreach (var d in takenDots) TakenDotsContainer.Children.Add(Dot(d.Color, d.Month));
            TxtTakenHealth.Text = ARISHrms.Services.TrackRecord.AdvanceTakenText(ticks, DateTime.Today);
            TxtTakenSummary.Text = ticks.Count == 0
                ? "No advance ticked yet."
                : $"{ticks.Count} advance(s) ticked - last on {ticks.Max(t => t.TakenDate):dd-MMM-yyyy}";

            if (haveMonsum)
                TxtInfo.Text = _summary.Items.Count == 0
                    ? "No advances in monsum.DBF for this employee."
                    : "Data from monsum.DBF  |  red = above this employee's monthly limit  |  blue = added here";
        }

        private static List<SummaryDot> TrackRecord_TakenDots(List<AdvanceTakenEntry> ticks) =>
            ARISHrms.Services.TrackRecord.AdvanceTakenDots(ticks, DateTime.Today);

        private static Ellipse Dot(string color, string tip) => new Ellipse
        {
            Width = 14,
            Height = 14,
            Margin = new Thickness(0, 0, 6, 0),
            Fill = (SolidColorBrush)new BrushConverter().ConvertFromString(color),
            ToolTip = tip
        };

        // ---------------- advance "taken" tick + date ----------------
        private void ChkTaken_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null || !((sender as FrameworkElement)?.DataContext is AdvanceLedgerRow row)) return;

            try
            {
                if (row.IsTaken)
                {
                    if (MessageBox.Show($"Remove the 'taken' tick for {row.MonthYear} ({row.TakenDate:dd-MMM-yyyy})?", "Untick advance",
                            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                        AdvanceTakenStore.Delete(row.TakenId.Value);
                }
                else
                {
                    // default date: today if it is that month, otherwise the 1st of that month - change it in the date box
                    DateTime date = DateTime.Today;
                    if (row.MonthKey.Length == 6)
                    {
                        var first = new DateTime(int.Parse(row.MonthKey.Substring(0, 4)), int.Parse(row.MonthKey.Substring(4, 2)), 1);
                        if (AdvanceTakenStore.MonthKeyOf(DateTime.Today) != row.MonthKey) date = first;
                    }
                    AdvanceTakenStore.Add(_selected.EmpCode, date, row.MonthKey, null, "");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not save: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            Refresh();
        }

        private void DpTakenDate_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_selected == null || !(sender is DatePicker dp) || !(dp.DataContext is AdvanceLedgerRow row)) return;
            if (!row.IsTaken || dp.SelectedDate == null || dp.SelectedDate == row.TakenDate) return;

            try
            {
                AdvanceTakenStore.UpdateDate(row.TakenId.Value, dp.SelectedDate.Value);
                row.TakenDate = dp.SelectedDate.Value.Date;
                // dots and "last on" text depend on the date - refresh after the picker has closed
                Dispatcher.BeginInvoke(new Action(Refresh), System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not save the date: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnAddAdvanceDate_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null)
            {
                MessageBox.Show("Please select an employee first", "Warning");
                return;
            }

            var dlg = new AddAdvanceDateWindow(_selected.DisplayName) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() != true) return;

            try
            {
                AdvanceTakenStore.Add(_selected.EmpCode, dlg.TakenDate, AdvanceTakenStore.MonthKeyOf(dlg.TakenDate), dlg.Amount, dlg.Note);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not save: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            Refresh();
        }

        private void TxtMonthlyLimit_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading || _selected == null) return;

            // save as you type, like the old app, then recolor the months
            if (double.TryParse(TxtMonthlyLimit.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var limit) && limit >= 0)
            {
                try { AdvanceLimitStore.Set(_selected.EmpCode, limit); } catch { }
            }
            Refresh();
        }

        private void BtnImportLimits_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Select Advance Limits CSV", Filter = "CSV Files (*.csv)|*.csv" };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var result = AdvanceLimitStore.ImportCsv(dlg.FileName);

                if (result.Updated == 0)
                {
                    MessageBox.Show("No records could be imported. Please make sure the file is a CSV with 'Card No' and 'ADV' headers.",
                        "Import Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                string msg = $"Updated {result.Updated} employee advance limit(s).";
                if (result.Skipped > 0) msg += $"\n\nSkipped {result.Skipped} row(s) (the ADV limit was empty or text).";
                MessageBox.Show(msg, "Import Complete", MessageBoxButton.OK,
                    result.Skipped > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);

                if (_selected != null) LoadAdvanceData();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not import the CSV: {ex.Message}", "Error");
            }
        }

        private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null || AdvanceGrid.ItemsSource == null)
            {
                MessageBox.Show("Please select an employee first", "Warning");
                return;
            }

            try
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "Excel Workbook|*.xlsx",
                    FileName = $"AdvanceLedger_{_selected.EmpCode}.xlsx"
                };
                if (dlg.ShowDialog() != true) return;

                using (var wb = new XLWorkbook())
                {
                    var ws = wb.Worksheets.Add("Advance Report");
                    ws.Cell("A1").Value = $"Advance Ledger - {_selected.DisplayName}";
                    ws.Cell("A1").Style.Font.SetBold();

                    ws.Cell("A3").Value = "Total Advances Taken"; ws.Cell("B3").Value = _summary?.Total ?? 0;
                    ws.Cell("A4").Value = "Monthly Limit";        ws.Cell("B4").Value = CurrentLimit();

                    ws.Cell("A6").Value = "Status";
                    ws.Cell("B6").Value = "Month/Year";
                    ws.Cell("C6").Value = "Advance Amount";
                    ws.Cell("D6").Value = "Taken";
                    ws.Cell("E6").Value = "Taken On";
                    ws.Cell("F6").Value = "Note";
                    ws.Range("A6:F6").Style.Font.SetBold();

                    int row = 7;
                    foreach (var item in (AdvanceGrid.ItemsSource as IEnumerable<AdvanceLedgerRow>) ?? new List<AdvanceLedgerRow>())
                    {
                        bool high = item.StatusColor == "#E74C3C";
                        ws.Cell(row, 1).Value = item.FromMonsum ? (high ? "High" : "Safe") : "Added here";
                        ws.Cell(row, 2).Value = item.MonthYear;
                        if (item.AdvanceAmount.HasValue) ws.Cell(row, 3).Value = item.AdvanceAmount.Value; else ws.Cell(row, 3).Value = "-";
                        ws.Cell(row, 4).Value = item.IsTaken ? "Yes" : "";
                        ws.Cell(row, 5).Value = item.TakenDate.HasValue ? item.TakenDate.Value.ToString("dd-MM-yyyy") : "";
                        ws.Cell(row, 6).Value = item.Note;
                        if (high)
                        {
                            ws.Cell(row, 1).Style.Font.FontColor = XLColor.Red;
                            ws.Cell(row, 3).Style.Font.FontColor = XLColor.Red;
                        }
                        row++;
                    }

                    ws.Columns().AdjustToContents();
                    wb.SaveAs(dlg.FileName);
                }

                MessageBox.Show("Excel report generated successfully!", "Success");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting to Excel: {ex.Message}", "Error");
            }
        }

        private void BtnSavePdf_Click(object sender, RoutedEventArgs e)
        {
            var print = new PrintDialog();
            if (print.ShowDialog() == true)
                print.PrintVisual(this, $"Advance Ledger - {_selected?.EmpCode}");
        }
    }
}
