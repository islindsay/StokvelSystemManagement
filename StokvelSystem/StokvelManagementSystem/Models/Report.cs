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