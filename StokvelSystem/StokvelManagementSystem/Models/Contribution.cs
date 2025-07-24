using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace StokvelManagementSystem.Models
{
    public class Contribution
    {
        public int ID { get; set; }
        
        [Required]
        public int MemberGroupID { get; set; } 
        public int MemberID { get; set; }
        
        public int Phone { get; set; }
        public string Email { get; set; }
      
        [Required]
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

        [Display(Name = "Proof of Payment")]
        public string ProofOfPaymentPath { get; set; }

        [Required]
        [Display(Name = "Transaction Date")]
        public DateTime TransactionDate { get; set; } = DateTime.Now;

        public string GroupName { get; set; }
        public string FirstName { get; set; }
        public string CreatedBy { get; set; }
        public DateTime? DueDate { get; set; }  
        public decimal GroupContributionAmount { get; set; }  
        
        public List<PaymentMethod> PaymentMethods { get; set; } = new List<PaymentMethod>();
    }

    public class PaymentMethod
    {
        public int Id { get; set; }
        public string Method { get; set; }
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