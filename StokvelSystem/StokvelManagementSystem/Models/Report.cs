namespace StokvelManagementSystem.Models
{

    public class Report
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Currency { get; set; }
        public string GroupName { get; set; }
        public string Period { get; set; }
        public DateTime Date { get; set; }

        // Totals 
        public decimal TotalContributionsPaid { get; set; }
        public int TotalMissedPayments { get; set; }
        public int PenaltiesApplied { get; set; }
        public string CurrentStatus { get; set; }
        public List<ContributionViewModel> Contributions { get; set; }

        //Group Report 
        public int TotalMembers { get; set; }
        public DateTime? GroupStartDate { get; set; }
        public string ContributionFrequency { get; set; } // e.g. "Monthly", "Weekly"
        public decimal ContributionAmount { get; set; }
        public decimal GroupTotalContributions { get; set; }
        public decimal TotalGroupPenalties { get; set; }
        public decimal TotalPenaltiesAmount { get; set; }

        public int TotalCycles { get; set; }

        public List<GroupMemberSummary> MemberSummaries { get; set; }
    }
    public class ContributionViewModel
    {
        public DateTime Date { get; set; }
        public decimal Amount { get; set; }
        public string PaymentMethod { get; set; }
        public string ProofOfPayment { get; set; }
        public string CycleOrMonth { get; set; }
        public string Status { get; set; } // "Paid" or "Missed"
    }
    public class GroupMemberSummary
    {
        public string FullName { get; set; }
        public decimal TotalPaid { get; set; }
        public int MissedPayments { get; set; }
        public decimal Penalties { get; set; }
        public string Status { get; set; }
    }
}