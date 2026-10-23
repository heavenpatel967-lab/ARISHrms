using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace ARISHrms.Views
{
    public partial class DocumentsView : UserControl
    {
        private string _selectedFilePath = "";

        public DocumentsView()
        {
            InitializeComponent();
        }

        private void CmbSelectEmployee_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // string selectedEmpCode = CmbSelectEmployee.SelectedValue?.ToString();
            // LoadDocuments(selectedEmpCode);
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "Select Employee Document",
                Filter = "All Files|*.*|Images|*.jpg;*.jpeg;*.png|PDFs|*.pdf"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                _selectedFilePath = openFileDialog.FileName;
                TxtSelectedFilePath.Text = _selectedFilePath;

                // Auto-fill document name if empty
                if (string.IsNullOrWhiteSpace(TxtDocName.Text))
                {
                    TxtDocName.Text = System.IO.Path.GetFileNameWithoutExtension(_selectedFilePath);
                }
            }
        }

        private void BtnSaveDocument_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("File copy and database insert logic will go here.", "UI Only");
        }

        private void BtnViewDocument_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Document viewer will open the file here.", "UI Only");
        }

        private void BtnDeleteDocument_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("File delete logic will go here.", "UI Only");
        }
    }
}