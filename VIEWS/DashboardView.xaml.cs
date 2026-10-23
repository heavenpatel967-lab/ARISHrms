using System;
using System.Windows;
using System.Windows.Controls;
using ARISHrms.Data;
using ARISHrms.Services;
using Microsoft.Data.Sqlite;
using Dapper;

namespace ARISHrms.Views
{
    public partial class DashboardView : UserControl
    {
        public DashboardView()
        {
            InitializeComponent();
            LoadDashboardData();
        }

        private void LoadDashboardData()
        {
            try
            {
                using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
                {
                    connection.Open();

                    // Total Employees
                    int totalEmployees = connection.QuerySingle<int>("SELECT COUNT(*) FROM Employees WHERE Status = 'Y'");
                    TxtTotalEmployees.Text = totalEmployees.ToString();

                    // Active Loans
                    int activeLoanCount = connection.QuerySingle<int>("SELECT COUNT(DISTINCT EmpCode) FROM LoanMasters WHERE Status = 'Active'");
                    TxtActiveLoanCount.Text = activeLoanCount.ToString();

                    // Pending Advances
                    int pendingAdvances = connection.QuerySingle<int>("SELECT COUNT(*) FROM AdvanceLedgers WHERE IsRecovered = 0");
                    TxtPendingAdvances.Text = pendingAdvances.ToString();

                    // Total Loan Amount
                    var totalLoanAmount = connection.QuerySingleOrDefault<decimal?>(
                        "SELECT SUM(LoanAmount) FROM LoanMasters WHERE Status = 'Active'") ?? 0;
                    TxtTotalLoanAmount.Text = $"₹ {totalLoanAmount:N0}";

                    // Loans in Arrears
                    int loansInArrears = connection.QuerySingle<int>(
                        "SELECT COUNT(DISTINCT LoanId) FROM LoanLedgers WHERE IsInArrears = 1");
                    TxtLoansInArrears.Text = loansInArrears.ToString();

                    // Today's Punches
                    string today = DateTime.Now.ToString("yyyy-MM-dd");
                    int todayPunches = connection.QuerySingle<int>(
                        $"SELECT COUNT(*) FROM DailyAttendance WHERE WorkDate = '{today}'");
                    TxtTodayPunches.Text = todayPunches.ToString();

                    // Today's Exceptions
                    int todayExceptions = connection.QuerySingle<int>(
                        $"SELECT COUNT(*) FROM DailyAttendance WHERE WorkDate = '{today}' AND IsException = 1");
                    TxtTodayExceptions.Text = todayExceptions.ToString();

                    // Docs Expiring Soon
                    var expiringDocs = DocumentService.GetExpiringDocuments(30);
                    TxtDocsExpiring.Text = expiringDocs.Count.ToString();

                    // On Leave Today (approximate - check if return date is today or later)
                    int onLeaveToday = connection.QuerySingle<int>(
                        $"SELECT COUNT(*) FROM VillageVisits WHERE DepartureDate <= '{today}' AND (ActualReturn IS NULL OR ActualReturn > '{today}')");
                    TxtOnLeaveToday.Text = onLeaveToday.ToString();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading dashboard data: {ex.Message}", "Error");
            }
        }
    }
}
