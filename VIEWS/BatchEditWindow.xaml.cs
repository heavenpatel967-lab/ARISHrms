using System.Windows;

namespace ARISHrms.Views
{
    public partial class BatchEditWindow : Window
    {
        public string NewIn { get; private set; }
        public string NewLOut { get; private set; }
        public string NewLIn { get; private set; }
        public string NewOut { get; private set; }
        public string NewP5 { get; private set; }
        public string NewP6 { get; private set; }
        public string NewP7 { get; private set; }
        public string NewP8 { get; private set; }

        public bool KeepBlankAsOriginal { get; private set; }

        public BatchEditWindow()
        {
            InitializeComponent();
        }

        public void PreFillData(string pIn, string pLOut, string pLIn, string pOut, string p5, string p6, string p7, string p8)
        {
            txtIn.Text = pIn;
            txtLOut.Text = pLOut;
            txtLIn.Text = pLIn;
            txtOut.Text = pOut;
            txtP5.Text = p5;
            txtP6.Text = p6;
            txtP7.Text = p7;
            txtP8.Text = p8;
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            NewIn = txtIn.Text.Trim();
            NewLOut = txtLOut.Text.Trim();
            NewLIn = txtLIn.Text.Trim();
            NewOut = txtOut.Text.Trim();
            NewP5 = txtP5.Text.Trim();
            NewP6 = txtP6.Text.Trim();
            NewP7 = txtP7.Text.Trim();
            NewP8 = txtP8.Text.Trim();

            KeepBlankAsOriginal = chkKeepBlank.IsChecked == true;

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