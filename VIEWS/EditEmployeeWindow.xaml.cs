using System;
using System.Windows;
using ARISHrms.Data;
using ARISHrms.Models;
using Microsoft.Data.Sqlite;
using Dapper;

namespace ARISHrms.Views
{
    /// <summary>
    /// Edit the details that only exist inside ARIS HRMS (phone, Aadhar, village) and fix a missing name.
    /// A name typed here is kept on re-sync when the DBF has no name for the person; if the DBF does have one,
    /// the DBF value wins on the next sync.
    /// </summary>
    public partial class EditEmployeeWindow : Window
    {
        private readonly Employee _employee;

        public EditEmployeeWindow(Employee employee)
        {
            InitializeComponent();
            _employee = employee;

            TxtCode.Text = employee.EmpCode;
            TxtName.Text = employee.FullName ?? "";
            TxtPhone.Text = employee.Phone ?? "";
            TxtAadhar.Text = employee.AadharNo ?? "";
            TxtVillage.Text = employee.NativeVillage ?? "";
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
                {
                    connection.Open();
                    connection.Execute(
                        @"UPDATE Employees
                          SET FullName = @name, Phone = @phone, AadharNo = @aadhar, NativeVillage = @village
                          WHERE EmpCode = @code",
                        new
                        {
                            name = TxtName.Text.Trim(),
                            phone = TxtPhone.Text.Trim(),
                            aadhar = TxtAadhar.Text.Trim(),
                            village = TxtVillage.Text.Trim(),
                            code = _employee.EmpCode
                        });
                }

                _employee.FullName = TxtName.Text.Trim();
                _employee.Phone = TxtPhone.Text.Trim();
                _employee.AadharNo = TxtAadhar.Text.Trim();
                _employee.NativeVillage = TxtVillage.Text.Trim();

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving details: {ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
