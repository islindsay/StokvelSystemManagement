
namespace StokvelManagementSystem.Models
    {
        public class PayoutsDashboardViewModel
        {
            public List<PayoutSchedule> ActiveSchedules { get; set; }
            public List<PayoutCycle> UpcomingCycles { get; set; }
        }
    }

