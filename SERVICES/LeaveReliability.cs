using System;
using System.Collections.Generic;
using System.Linq;

namespace ARISHrms.Services
{
    /// <summary>One village visit / long leave as needed for the reliability dots.</summary>
    public class LeaveInput
    {
        public DateTime Departure;
        public DateTime? ExpectedReturn;
        public DateTime? ActualReturn;
        public int LateDays;
    }

    public class LeaveReliabilityResult
    {
        public string Text = "No Leaves Taken";
        public List<SummaryDot> Dots = new List<SummaryDot>();
    }

    public static class LeaveReliability
    {
        /// <summary>
        /// Same rules as the old summary page: look at the last 6 leaves (oldest first); a leave that has not
        /// ended and is past its expected date counts as late by the days since it was due. Green = on time,
        /// yellow = late up to 7 days, red = more than 7 days late.
        /// </summary>
        public static LeaveReliabilityResult Build(IList<LeaveInput> oldestFirst, DateTime today)
        {
            var result = new LeaveReliabilityResult();
            var last6 = oldestFirst.Skip(Math.Max(0, oldestFirst.Count - 6)).ToList();
            int onTime = 0;

            foreach (var l in last6)
            {
                int lateDays = l.LateDays;
                if (l.ActualReturn == null && l.ExpectedReturn.HasValue && l.ExpectedReturn.Value.Date < today.Date)
                    lateDays = (today.Date - l.ExpectedReturn.Value.Date).Days;

                string color = "#27AE60";
                if (lateDays > 7) color = "#FF5252";
                else if (lateDays > 0) color = "#F1C40F";

                if (lateDays <= 0) onTime++;
                result.Dots.Add(new SummaryDot { Month = $"Departed: {l.Departure:yyyy-MM-dd}", Color = color });
            }

            if (last6.Count > 0) result.Text = $"{onTime} / {last6.Count} On Time";
            return result;
        }
    }
}
