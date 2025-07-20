namespace StokvelManagementSystem.Models
{
    public class RequestToLeaveView
    {

        public int GroupId { get; set; }
        public string GroupName { get; set; }
        public decimal? ContributionAmount { get; set; }
        public string FrequencyName { get; set; }
        public string Currency { get; set; }
        public string NationalId { get; set; }
        public int MemberLimit { get; set; }
        public string PayoutType { get; set; }
        public DateTime? StartDate { get; set; }
        public decimal PenaltyAmount { get; set; }
        public int? PenaltyGraceDays { get; set; }
        public bool AllowDeferrals { get; set; }
        public int MemberId { get; set; }
        public string Duration { get; set; }
    }
    
}
