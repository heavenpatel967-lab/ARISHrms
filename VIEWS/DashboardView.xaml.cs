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
                string today = DateTime.Now.ToString("yyyy-MM-dd");

                using (var connection = new SqliteConnection(DatabaseInitializer.ConnectionString))
                {
                    connection.Open();

                    // Employees in this app (anyone not marked as left)
                    TxtTotalEmployees.Text = connection.QuerySingle<int>("SELECT COUNT(*) FROM Employees WHERE Status <> 'N'").ToString();

                    // Attendance punches imported into this app
                    TxtTodayPunches.Text = connection.QuerySingle<int>("SELECT COUNT(*) FROM DailyAttendance WHERE WorkDate = @today", new { today }).ToString();
                    TxtTodayExceptions.Text = connection.QuerySingle<int>("SELECT COUNT(*) FROM DailyAttendance WHERE WorkDate = @today AND IsException = 1", new { today }).ToString();

                    // Leave tracking
                    TxtOnLeaveToday.Text = connection.QuerySingle<int>(
                        "SELECT COUNT(*) FROM VillageVisits WHERE DepartureDate <= @today AND (ActualReturn IS NULL OR ActualReturn > @today)", new { today }).ToString();
                }

                TxtDocsExpiring.Text = DocumentService.GetExpiringDocuments(30).Count.ToString();

                LoadPayrollFigures();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading dashboard data: {ex.Message}", "Error");
            }
        }

        /// <summary>Factory-wide loan and advance totals from EasyPay's monsum.DBF.</summary>
        private void LoadPayrollFigures()
        {
            TxtActiveLoanCount.Text = "-";
            TxtPendingAdvances.Text = "-";
            TxtTotalLoanAmount.Text = "-";
            TxtLatestMonth.Text = "-";

            string path = DbfSource.Find("monsum");
            if (path == null)
            {
                TxtDataInfo.Text = "Loan and advance figures need monsum.DBF. Use Employee Master -> Sync DBFS Folder to choose your DBFS folder.";
                return;
            }

            try
            {
                var totals = MonsumData.Load(path).BuildFactoryTotals();

                TxtActiveLoanCount.Text = totals.EmployeesWithLoans.ToString("N0");
                TxtPendingAdvances.Text = $"₹ {totals.TotalAdvances:N0}";
                TxtTotalLoanAmount.Text = $"₹ {totals.PendingLoans:N0}";
                TxtLatestMonth.Text = string.IsNullOrEmpty(totals.LatestMonth) ? "-" : totals.LatestMonth;
                TxtDataInfo.Text = "Loan and advance figures are read from monsum.DBF in your DBFS folder.";
            }
            catch (Exception ex)
            {
                TxtDataInfo.Text = "Could not read monsum.DBF: " + ex.Message;
            }
        }
    }
}
