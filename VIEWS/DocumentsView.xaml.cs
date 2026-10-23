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
                DocumentGrid.ItemsSource = documents;

                if (documents.Count == 0)
                {
                    TxtInfo.Text = $"No documents for employee {empCode}";
                    TxtExpiring.Text = "0";
                    TxtExpired.Text = "0";
                    return;
                }

                // Get expiring documents
                var expiringDocs = DocumentService.GetExpiringDocuments(30);
                var expiredDocs = DocumentService.GetExpiredDocuments();

                TxtInfo.Text = $"Showing {documents.Count} document(s)";
                TxtExpiring.Text = expiringDocs.Count(d => d.EmpCode == empCode).ToString();
                TxtExpired.Text = expiredDocs.Count(d => d.EmpCode == empCode).ToString();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading documents: {ex.Message}", "Error");
            }
        }

        private void BtnUploadDocument_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedEmpCode))
            {
                MessageBox.Show("Please select an employee first", "Warning");
                return;
            }

            try
            {
                var openFileDialog = new OpenFileDialog
                {
                    Title = "Select Document File",
                    Filter = "All Files (*.*)|*.*|PDF Files (*.pdf)|*.pdf|Word Documents (*.docx)|*.docx|Images (*.jpg;*.png)|*.jpg;*.png"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    string docCategory = "General"; // Could get from combo box
                    string docName = System.IO.Path.GetFileNameWithoutExtension(openFileDialog.FileName);
                    DateTime? expiryDate = null; // Could get from date picker

                    int docId = DocumentService.UploadDocument(
                        _selectedEmpCode,
                        docCategory,
                        docName,
                        openFileDialog.FileName,
                        expiryDate
                    );

                    MessageBox.Show($"Document uploaded successfully (ID: {docId})", "Success");
                    LoadDocuments(_selectedEmpCode);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error uploading document: {ex.Message}", "Error");
            }
        }

        private void BtnDownloadDocument_Click(object sender, RoutedEventArgs e)
        {
            if (DocumentGrid.SelectedItem == null)
            {
                MessageBox.Show("Please select a document first", "Warning");
                return;
            }

            try
            {
                var document = (DocumentService.EmployeeDocument)DocumentGrid.SelectedItem;

                var saveFileDialog = new SaveFileDialog
                {
                    FileName = document.DocName,
                    Filter = "All Files (*.*)|*.*"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    byte[] fileData = DocumentService.DownloadDocument(document.Id, out string fileName);

                    if (fileData != null)
                    {
                        System.IO.File.WriteAllBytes(saveFileDialog.FileName, fileData);
                        MessageBox.Show($"Document saved to {saveFileDialog.FileName}", "Success");
                    }
                    else
                    {
                        MessageBox.Show("Document not found", "Error");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error downloading document: {ex.Message}", "Error");
            }
        }

        private void BtnDeleteDocument_Click(object sender, RoutedEventArgs e)
        {
            if (DocumentGrid.SelectedItem == null)
            {
                MessageBox.Show("Please select a document first", "Warning");
                return;
            }

            if (MessageBox.Show("Are you sure you want to delete this document?", "Confirm Delete", 
                MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                try
                {
                    var document = (DocumentService.EmployeeDocument)DocumentGrid.SelectedItem;

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

        private void BtnViewDocument_Click(object sender, RoutedEventArgs e)
        {
            if (DocumentGrid.SelectedItem == null)
            {
                MessageBox.Show("Please select a document first", "Warning");
                return;
            }

            try
            {
                var document = (DocumentService.EmployeeDocument)DocumentGrid.SelectedItem;

                if (System.IO.File.Exists(document.FilePath))
                {
                    // Open the file with default application
                    System.Diagnostics.Process.Start(document.FilePath);
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
    }
}
