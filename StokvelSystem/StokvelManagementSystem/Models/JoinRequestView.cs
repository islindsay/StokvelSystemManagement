namespace StokvelManagementSystem.Models
{
    public class JoinRequestView
    {
        public int RequestId { get; set; }
        public int MemberId { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string NationalID { get; set; }
        public string GroupName { get; set; }
        public decimal ContributionAmount { get; set; }
        public string Currency { get; set; }
        public string Frequency { get; set; }
        public DateTime RequestedDate { get; set; }
        public int StatusID { get; set; }
        public string Status { get; set; }
    }

    public class GroupInfoDto
    {
        public int ID { get; set; }
        public string Name { get; set; }
        public string Currency { get; set; }
        public string FrequencyName { get; set; }

        // New properties for member tracking
        public int CurrentMembers { get; set; } = 0;
        public int MaxMembers { get; set; } = 1;
        public bool IsActive { get; set; }
        public DateTime? StartDate { get; set; }


    }
    public class LeaveRequestView
    {
        public int RequestId { get; set; }
        public int MemberId { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string NationalID { get; set; }
        public string GroupName { get; set; }
        public DateTime RequestedDate { get; set; }
        public int StatusID { get; set; }
        public string Status { get; set; }
        public string Reason { get; set; }
    }

    public class DashboardModel
    {
        public GroupInfoDto Group { get; set; }
        public List<JoinRequestView> Requests { get; set; }
        public string SelectedStatus { get; set; }
        public bool IsMemberView { get; set; }
        public bool AdminTools => !IsMemberView;
        // Join request counts
        public int PendingJoinCount { get; set; }
        public int ApprovedJoinCount { get; set; }
        public int DeniedJoinCount { get; set; }

        // Leave request properties
        public List<LeaveRequestView> LeaveRequests { get; set; }
        public string SelectedLeaveStatus { get; set; }
        public int PendingLeaveCount { get; set; }
        public int ApprovedLeaveCount { get; set; }
        public int DeniedLeaveCount { get; set; }


        // Additional properties for both roles
        public int PendingRequestCount { get; set; }
        public DateTime? NextContributionDate { get; set; }
    }
}