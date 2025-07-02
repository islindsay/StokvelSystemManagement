using System;
using System.ComponentModel.DataAnnotations;

namespace StokvelManagementSystem.Models
{
    public class Transaction
    {
        public int ID { get; set; }

        [Required]
        [Display(Name = "Member Group")]
        public int MemberGroupID { get; set; }

        [Required]
        [Display(Name = "Transaction Type")]
        public int TransactionTypeID { get; set; }

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
    }
}