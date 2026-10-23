using System.Windows;
using System.Windows.Controls;

namespace ARISHrms.Views
{
    public partial class AdvancesView : UserControl
    {
        public AdvancesView()
        {
            InitializeComponent();
        }

        private void CmbSelectEmployee_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
        }

        private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Export to Excel will be connected here.", "UI Only");
        }

        private void BtnSavePdf_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Save as PDF will be connected here.", "UI Only");
        }
    }
}