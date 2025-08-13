using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace StokvelManagementSystem.Models
{
    public class Contribution
    {
        public int ID { get; set; }

        [Required]
        [Display(Name = "Member")]
        public int MemberGroupID { get; set; }

        public int MemberID { get; set; }

        [Display(Name = "Phone Number")]
        public string Phone { get; set; }  // Correctly string for phone format

        public string Email { get; set; }

        [Display(Name = "Contribution Amount")]
        [Range(0.01, double.MaxValue)]
        public decimal ContributionAmount { get; set; }

        [Display(Name = "Penalty Amount")]
        [Range(0, double.MaxValue)]
        public decimal PenaltyAmount { get; set; }

        [Display(Name = "Total Amount")]
        public decimal TotalAmount { get; set; }

        [Required]
        [Display(Name = "Payment Method")]
        public int PaymentMethodID { get; set; }

        [Required]
        [Display(Name = "Reference")]
        [StringLength(50)]
        public string Reference { get; set; }

        // ❌ Removed [Required] — you're setting this in controller
        [Display(Name = "Proof of Payment")]
        public string? ProofOfPaymentPath { get; set; }


        // ❌ Removed [Required] — you set it in the controller or use default
        [Display(Name = "Transaction Date")]
        public DateTime TransactionDate { get; set; } = DateTime.Now;

        [Display(Name = "Group Name")]
        public string GroupName { get; set; }

        // ❌ Removed [Required] — not filled from form
        public string? FirstName { get; set; }  // Use nullable if not binding it at all

        // ❌ Removed [Required] — you set this server-side
        public string CreatedBy { get; set; }

        [Display(Name = "Due Date")]
        public DateTime? DueDate { get; set; }

        // ✅ Allow this to be optional or bind via hidden field
        [Display(Name = "Group Contribution Amount")]
        public decimal? GroupContributionAmount { get; set; }

        public int GroupId { get; set; }

        // UI dropdown helpers
        public List<PaymentMethod> PaymentMethods { get; set; } = new List<PaymentMethod>();
        public List<MemberOption> MemberOptions { get; set; } = new List<MemberOption>();

        public decimal GroupBalance { get; set; }
        public decimal TotalContributions { get; set; }
        public decimal Penalties { get; set; }
        public DateTime? NextPayoutDate { get; set; }
        public decimal ExpectedPayment { get; set; }
        public bool EnablePayout { get; set; }
        public int MemberCount { get; set; }
        public int FrequencyID { get; set; }
        public int PayoutTypeID { get; set; }

    }

    public class PaymentMethod
    {
        public int Id { get; set; }
        public string Method { get; set; }
    }

    public class MemberOption
    {
        public int Id { get; set; }
        public int MemberId { get; set; }
        public string FullName { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
    }

    public class GroupDetailsResponse
    {
        public string GroupName { get; set; }
        public DateTime DueDate { get; set; }
        public decimal GroupContributionAmount { get; set; }
    }

    public class PenaltySettingsResponse
    {
        public decimal DailyPenaltyAmount { get; set; }
    }
}
