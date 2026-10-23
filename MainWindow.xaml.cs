using System.Windows;
using System.Windows.Controls;
using ARISHrms.Data;
using ARISHrms.Views;

namespace ARISHrms
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DatabaseInitializer.Initialize();
            LoadView("Dashboard");
        }

        private void Nav_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag != null)
            {
                LoadView(btn.Tag.ToString());
            }
        }

        public void LoadView(string viewKey)
        {
            switch (viewKey)
            {
                case "Dashboard":
                    TxtCurrentViewTitle.Text = "Dashboard & KPIs";
                    MainContentHost.Content = new DashboardView();
                    break;
                case "Attendance":
                    TxtCurrentViewTitle.Text = "Attendance & Punch Management";
                    MainContentHost.Content = new AttendanceView();
                    break;
                case "Employees":
                    TxtCurrentViewTitle.Text = "Employee Master Directory";
                    MainContentHost.Content = new EmployeesView();
                    break;
                case "Loans":
                    TxtCurrentViewTitle.Text = "Loan Ledger & Repayment Health";
                    MainContentHost.Content = new LoansView();
                    break;
                case "Advances":
                    TxtCurrentViewTitle.Text = "Monthly Advance Ledger";
                    MainContentHost.Content = new AdvancesView();
                    break;
                case "Leaves":
                    TxtCurrentViewTitle.Text = "Leave & Village Visit Tracker";
                    MainContentHost.Content = new LeavesView();
                    break;
                case "Documents":
                    TxtCurrentViewTitle.Text = "Employee Document Repository";
                    MainContentHost.Content = new DocumentsView();
                    break;
                case "Payroll":
                    TxtCurrentViewTitle.Text = "Salary Slips & Payroll";
                    MainContentHost.Content = new PayrollView();
                    break;
            }
        }
    }
}