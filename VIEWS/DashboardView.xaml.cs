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

                    // Village leave
                    TxtOnLeaveToday.Text = connection.QuerySingle<int>(
                        "SELECT COUNT(*) FROM VillageVisits WHERE DepartureDate <= @today AND (ActualReturn IS NULL OR ActualReturn > @today)", new { today }).ToString();
                    TxtLateReturns.Text = connection.QuerySingle<int>(
                        "SELECT COUNT(*) FROM VillageVisits WHERE ActualReturn IS NULL AND ExpectedReturn < @today", new { today }).ToString();
                }

                TxtDocsExpiring.Text = DocumentService.GetExpiringDocuments(30).Count.ToString();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading dashboard data: {ex.Message}", "Error");
            }
        }
    }
}
