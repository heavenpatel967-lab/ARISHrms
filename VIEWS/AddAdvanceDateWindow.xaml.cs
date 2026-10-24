using System;
using System.Globalization;
using System.Windows;

namespace ARISHrms.Views
{
    /// <summary>Records an advance handed over on any date (also outside the normal advance day).</summary>
    public partial class AddAdvanceDateWindow : Window
    {
        public DateTime TakenDate { get; private set; }
        public double? Amount { get; private set; }
        public string Note { get; private set; } = "";

        public AddAdvanceDateWindow(string employeeDisplay)
        {
            InitializeComponent();
            TxtEmployee.Text = employeeDisplay;
            DpDate.SelectedDate = DateTime.Today;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (DpDate.SelectedDate == null)
            {
                MessageBox.Show("Please choose the date the advance was taken.", "Date missing", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (DpDate.SelectedDate.Value.Date > DateTime.Today &&
                MessageBox.Show("That date is in the future. Save it anyway?", "Future date", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            string raw = (TxtAmount.Text ?? "").Trim().Replace(",", "").Replace("₹", "");
            if (raw.Length > 0)
            {
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var amt) || amt <= 0)
                {
                    MessageBox.Show("Amount must be a number above 0, or leave it empty.", "Amount", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                Amount = amt;
            }

            TakenDate = DpDate.SelectedDate.Value.Date;
            Note = (TxtNote.Text ?? "").Trim();
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
