using System.Windows;
using System.Windows.Controls;

namespace ARISHrms.Views
{
    public partial class LeavesView : UserControl
    {
        public LeavesView()
        {
            InitializeComponent();
        }

        private void CmbSelectEmployee_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
        }

        private void BtnSaveLeave_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Leave record save logic will go here.", "UI Only");
        }

        private void BtnImportCsv_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("CSV Import will be connected here.", "UI Only");
        }

        private void BtnDeleteLeave_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Delete logic will go here.", "UI Only");
        }
    }
}