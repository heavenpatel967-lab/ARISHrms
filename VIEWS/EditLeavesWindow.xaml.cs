using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using ARISHrms.Services;

namespace ARISHrms.Views
{
    /// <summary>
    /// Edit informed / without-informing leaves one month at a time. Every change is saved immediately.
    /// </summary>
    public partial class EditLeavesWindow : Window
    {
        public class DayRow
        {
            public DateTime Date { get; set; }
            public bool Informed { get; set; }
            public bool Uninformed => !Informed;
            public bool InAttendance { get; set; }       // absent in MONDATA
            public bool Marked { get; set; }             // has a saved mark
            public string DateDisplay => Date.ToString("dd-MMM-yyyy (ddd)", CultureInfo.InvariantCulture);
            public string SourceLabel => InAttendance ? "Absent" : "Added";
            public Visibility RemoveVisibility => !InAttendance && Marked ? Visibility.Visible : Visibility.Collapsed;
        }

        private readonly string _empCode;
        private DateTime _month;
        private List<DateTime> _absent;      // null = MONDATA not available

        public bool ChangesMade { get; private set; }

        public EditLeavesWindow(string empCode, string empName, DateTime? month = null)
        {
            InitializeComponent();
            _empCode = empCode;
            TxtEmployee.Text = empName;

            _absent = AttendanceAbsences.For(empCode, out string source);
            TxtSource.Text = _absent == null ? "MONDATA.DBF not found - only hand-added dates are listed" : $"Absent days from {source}";

            var m = month ?? LatestLeaveMonth() ?? DateTime.Today;
            _month = new DateTime(m.Year, m.Month, 1);
            DpAdd.SelectedDate = _month.Year == DateTime.Today.Year && _month.Month == DateTime.Today.Month ? DateTime.Today : _month;
            Reload();
        }

        /// <summary>Open on the most recent month that has a leave, so there is something to edit straight away.</summary>
        private DateTime? LatestLeaveMonth()
        {
            var dates = new List<DateTime>();
            if (_absent != null) dates.AddRange(_absent);
            try { dates.AddRange(AbsenceStore.ForEmployee(_empCode).Select(x => x.AbsenceDate)); } catch { }
            var past = dates.Where(d => d.Date <= DateTime.Today).ToList();
            return past.Count == 0 ? (DateTime?)null : past.Max();
        }

        private void Reload()
        {
            TxtMonth.Text = _month.ToString("MMM-yyyy", CultureInfo.InvariantCulture);
            var next = _month.AddMonths(1);

            List<AbsenceMark> marks;
            try { marks = AbsenceStore.ForEmployee(_empCode); }
            catch (Exception ex) { MessageBox.Show(this, "Could not read leaves: " + ex.Message, "Error"); marks = new List<AbsenceMark>(); }

            var markByDate = marks.GroupBy(m => m.AbsenceDate.Date).ToDictionary(g => g.Key, g => g.OrderBy(x => x.Id).Last());
            var absentSet = new HashSet<DateTime>((_absent ?? new List<DateTime>()).Select(d => d.Date));

            var days = new HashSet<DateTime>(absentSet.Where(d => d >= _month && d < next));
            foreach (var d in markByDate.Keys) if (d >= _month && d < next) days.Add(d);

            var rows = days.OrderBy(d => d).Select(d => new DayRow
            {
                Date = d,
                InAttendance = absentSet.Contains(d),
                Marked = markByDate.ContainsKey(d),
                Informed = !markByDate.TryGetValue(d, out var mk) || mk.Informed
            }).ToList();

            DaysGrid.ItemsSource = rows;
            TxtEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            int inf = rows.Count(r => r.Informed), un = rows.Count - inf;
            TxtMonthTotals.Text = rows.Count == 0 ? "" : $"{inf} informed  /  {un} without informing";
            TxtMonthTotals.Foreground = un > 0 ? System.Windows.Media.Brushes.Firebrick : System.Windows.Media.Brushes.SeaGreen;
        }

        private void Save(DateTime date, bool informed)
        {
            try
            {
                AbsenceStore.Set(_empCode, date, informed, "");
                ChangesMade = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not save: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            Reload();
        }

        private void RbInformed_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is DayRow r) Save(r.Date, true);
        }

        private void RbUninformed_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is DayRow r) Save(r.Date, false);
        }

        private void BtnRemove_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.DataContext is DayRow r)) return;
            if (MessageBox.Show(this, $"Remove {r.DateDisplay}?", "Remove leave date", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            try { AbsenceStore.Delete(_empCode, r.Date); ChangesMade = true; }
            catch (Exception ex) { MessageBox.Show(this, "Could not remove: " + ex.Message, "Error"); }
            Reload();
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            if (DpAdd.SelectedDate == null)
            {
                MessageBox.Show(this, "Please choose a date.", "Date missing");
                return;
            }
            var d = DpAdd.SelectedDate.Value.Date;
            _month = new DateTime(d.Year, d.Month, 1);      // jump to the month of the added date
            Save(d, CmbAddKind.SelectedIndex == 0);
        }

        private void BtnPrev_Click(object sender, RoutedEventArgs e) { _month = _month.AddMonths(-1); Reload(); }
        private void BtnNext_Click(object sender, RoutedEventArgs e) { _month = _month.AddMonths(1); Reload(); }
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
