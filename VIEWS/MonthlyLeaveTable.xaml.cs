using System;
using System.Windows;
using System.Windows.Controls;
using ARISHrms.Services;

namespace ARISHrms.Views
{
    /// <summary>
    /// Compact "last 6 months" leave table (Month / Informed / Without informing) with an Edit button.
    /// Used on the Track Record panel, the 360 page and Employee History.
    /// </summary>
    public partial class MonthlyLeaveTable : UserControl
    {
        private string _empCode;
        private string _empName;

        /// <summary>Raised after leaves were edited, so the host can refresh its own figures.</summary>
        public event EventHandler Changed;

        public MonthlyLeaveSummary Current { get; private set; } = new MonthlyLeaveSummary();

        public MonthlyLeaveTable()
        {
            InitializeComponent();
            Show(null, null);
        }

        public bool ShowEditButton
        {
            get => BtnEdit.Visibility == Visibility.Visible;
            set => BtnEdit.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }

        public void Show(string empCode, string empName)
        {
            _empCode = string.IsNullOrWhiteSpace(empCode) ? null : empCode;
            _empName = empName ?? empCode;
            BtnEdit.IsEnabled = _empCode != null;
            Reload();
        }

        public void Reload()
        {
            if (_empCode == null)
            {
                Current = new MonthlyLeaveSummary();
                IcMonths.ItemsSource = null;
                TxtSource.Text = "";
                return;
            }

            try
            {
                Current = AttendanceAbsences.Summary(_empCode, DateTime.Today);
                IcMonths.ItemsSource = Current.Months;
                TxtSource.Text = $"{Current.Text}   ·   {Current.Source}   ·   unmarked absent days count as informed";
            }
            catch (Exception ex)
            {
                IcMonths.ItemsSource = null;
                TxtSource.Text = "Could not read leaves: " + ex.Message;
            }
        }

        private void BtnEdit_Click(object sender, RoutedEventArgs e)
        {
            if (_empCode == null) return;
            var dlg = new EditLeavesWindow(_empCode, _empName) { Owner = Window.GetWindow(this) };
            dlg.ShowDialog();
            if (dlg.ChangesMade)
            {
                Reload();
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
