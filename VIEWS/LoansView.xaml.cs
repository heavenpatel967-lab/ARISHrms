using System.Windows;
using System.Windows.Controls;
// using ARISHrms.Data;
// using ARISHrms.Models;
// using Dapper;
// using Microsoft.Data.Sqlite;

namespace ARISHrms.Views
{
    public partial class LoansView : UserControl
    {
        public LoansView()
        {
            InitializeComponent();
            // LoadEmployeeList();
        }

        private void CmbSelectEmployee_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Triggered when you select an employee from the top right dropdown
            // string selectedEmpCode = CmbSelectEmployee.SelectedValue?.ToString();
            // LoadLoanData(selectedEmpCode);
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