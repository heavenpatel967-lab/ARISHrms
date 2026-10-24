using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ARISHrms.Data;
using ARISHrms.Services;
using Microsoft.Data.Sqlite;
using Dapper;

namespace ARISHrms.Views
{
    /// <summary>
    /// Good / bad remarks with a date, the village-leave record, and a one-line verdict for one employee.
    /// Used on the Advance Ledger, Loan Ledger and the 360 degree profile.
    /// </summary>
    public partial class TrackRecordPanel : UserControl
    {
        private string _empCode;

        /// <summary>Raised after a remark is added or deleted, so a host screen can refresh.</summary>
        public event EventHandler Changed;

        public TrackRecordPanel()
        {
            InitializeComponent();
            DpRemarkDate.SelectedDate = DateTime.Today;
            LeaveMonths.Changed += (s, e) => { Reload(); Changed?.Invoke(this, EventArgs.Empty); };
            ShowEmployee(null);
        }

        /// <summary>Hide the add-remark area (e.g. on the printable 360 page).</summary>
        public bool AllowEditing
        {
            get => AddRemarkBox.Visibility == Visibility.Visible;
            set => AddRemarkBox.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Hide the monthly leave table (when the host screen already shows one).</summary>
        public bool ShowLeaveMonths
        {
            get => LeaveMonths.Visibility == Visibility.Visible;
            set => LeaveMonths.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }

        public void ShowEmployee(string empCode)
        {
            _empCode = string.IsNullOrWhiteSpace(empCode) ? null : empCode;
            IsEnabled = _empCode != null;
            LeaveMonths.Show(_empCode, _empCode);
            Reload();
        }

        private void Reload()
        {
            List<EmployeeRemark> remarks = new List<EmployeeRemark>();
            LeaveReliabilityResult leave = new LeaveReliabilityResult();
            AbsenceSummary absences = new AbsenceSummary();

            if (_empCode != null)
            {
                try
                {
                    remarks = RemarkStore.ForEmployee(_empCode);
                    leave = LoadLeave(_empCode);
                    absences = LeaveMonths.Current.AsSummary();
                }
                catch (Exception ex)
                {
                    TxtVerdictDetail.Text = "Could not load the track record: " + ex.Message;
                }
            }

            LstRemarks.ItemsSource = remarks;
            TxtNoRemarks.Visibility = remarks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            TxtLeave.Text = leave.Text;
            IcLeaveDots.ItemsSource = leave.Dots;

            var verdict = ARISHrms.Services.TrackRecord.Verdict(remarks, leave, absences);
            TxtVerdict.Text = _empCode == null ? "Select an employee" : verdict.Text;
            VerdictBadge.Background = (SolidColorBrush)new BrushConverter().ConvertFromString(_empCode == null ? "#94A3B8" : verdict.Color);
            if (_empCode != null) TxtVerdictDetail.Text = verdict.Detail;
            else TxtVerdictDetail.Text = "";
        }

        /// <summary>Village visits for the employee, same rules as the 360 summary.</summary>
        private static LeaveReliabilityResult LoadLeave(string empCode) =>
            VillageVisitStore.Reliability(VillageVisitStore.ForEmployee(empCode), DateTime.Today);

        private void BtnAddRemark_Click(object sender, RoutedEventArgs e)
        {
            if (_empCode == null) return;

            string text = (TxtRemark.Text ?? "").Trim();
            if (text.Length == 0)
            {
                MessageBox.Show("Please write the remark first.", "Remark", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtRemark.Focus();
                return;
            }

            DateTime date = DpRemarkDate.SelectedDate ?? DateTime.Today;
            try
            {
                RemarkStore.Add(_empCode, date, RbBad.IsChecked == true, text);
                TxtRemark.Clear();
                RbGood.IsChecked = true;
                Reload();
                Changed?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not save the remark: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnDeleteRemark_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.Tag is int id)) return;
            if (MessageBox.Show("Delete this remark?", "Delete Remark", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            try
            {
                RemarkStore.Delete(id);
                Reload();
                Changed?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not delete the remark: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
