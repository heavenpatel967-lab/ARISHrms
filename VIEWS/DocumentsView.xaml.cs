using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using ARISHrms.Data;
using ARISHrms.Models;
using ARISHrms.Services;
using Microsoft.Data.Sqlite;
using Dapper;
using Microsoft.Win32;
using System.Linq;

namespace ARISHrms.Views
{
    public partial class DocumentsView : UserControl
    {
        private List<Employee> _employees = new List<Employee>();
        private string _selectedEmpCode;

        public DocumentsView()
        {
            InitializeComponent();
            LoadEmployeeList();
        }

        private void LoadEmployeeList()
        {
            try
            {
                using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
                {
                    connection.Open();
                    _employees = connection.Query<Employee>("SELECT * FROM Employees WHERE Status = 'Y' ORDER BY FullName").ToList();
                }

                CmbSelectEmployee.ItemsSource = _employees;
                CmbSelectEmployee.DisplayMemberPath = "FullName";
                CmbSelectEmployee.SelectedValuePath = "EmpCode";

                if (_employees.Count > 0)
                    CmbSelectEmployee.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading employees: {ex.Message}", "Error");
            }
        }

        private void CmbSelectEmployee_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbSelectEmployee.SelectedValue == null)
                return;

            _selectedEmpCode = CmbSelectEmployee.SelectedValue.ToString();
            LoadDocuments(_selectedEmpCode);
        }

        private void LoadDocuments(string empCode)
        {
            try
            {
                var documents = DocumentService.GetEmployeeDocuments(empCode);
                DocumentsGrid.ItemsSource = documents;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading documents: {ex.Message}", "Error");
            }
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var openFileDialog = new OpenFileDialog
                {
                    Title = "Select Document File",
                    Filter = "All Files (*.*)|*.*|PDF Files (*.pdf)|*.pdf|Word Documents (*.docx)|*.docx|Images (*.jpg;*.png)|*.jpg;*.png"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    TxtSelectedFilePath.Text = openFileDialog.FileName;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error browsing files: {ex.Message}", "Error");
            }
        }

        private void BtnSaveDocument_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedEmpCode))
            {
                MessageBox.Show("Please select an employee first", "Warning");
                return;
            }

            if (string.IsNullOrEmpty(TxtSelectedFilePath.Text) || TxtSelectedFilePath.Text == "No file selected...")
            {
                MessageBox.Show("Please browse and select a file first", "Warning");
                return;
            }

            try
            {
                string docCategory = (CmbDocCategory.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "General";
                string docName = string.IsNullOrEmpty(TxtDocName.Text) ? 
                    System.IO.Path.GetFileNameWithoutExtension(TxtSelectedFilePath.Text) : 
                    TxtDocName.Text;
                
                int docId = DocumentService.UploadDocument(
                    _selectedEmpCode,
                    docCategory,
                    docName,
                    TxtSelectedFilePath.Text,
                    null
                );

                MessageBox.Show($"Document uploaded successfully (ID: {docId})", "Success");
                TxtSelectedFilePath.Text = "No file selected...";
                TxtDocName.Clear();
                LoadDocuments(_selectedEmpCode);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving document: {ex.Message}", "Error");
            }
        }

        private void BtnViewDocument_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not EmployeeDocument rowDoc)
            {
                MessageBox.Show("Please select a document first", "Warning");
                return;
            }

            try
            {
                var document = rowDoc;

                if (System.IO.File.Exists(document.FilePath))
                {
                    // Open the file with default application
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(document.FilePath) { UseShellExecute = true });
                }
                else
                {
                    MessageBox.Show("Document file not found", "Error");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening document: {ex.Message}", "Error");
            }
        }

        private void BtnDeleteDocument_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not EmployeeDocument rowDoc)
            {
                MessageBox.Show("Please select a document first", "Warning");
                return;
            }

            if (MessageBox.Show("Are you sure you want to delete this document?", "Confirm Delete", 
                MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                try
                {
                    var document = rowDoc;

                    bool deleted = DocumentService.DeleteDocument(document.Id);

                    if (deleted)
                    {
                        MessageBox.Show("Document deleted successfully", "Success");
                        LoadDocuments(_selectedEmpCode);
                    }
                    else
                    {
                        MessageBox.Show("Document not found", "Error");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error deleting document: {ex.Message}", "Error");
                }
            }
        }
    }
}
