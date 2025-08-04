using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace StokvelManagementSystem.Models
{
    public class Payout
    {
        public int Id { get; set; }

        [Required]
        public int GroupId { get; set; }
        public string? GroupName { get; set; }

        [Required]
        [Display(Name = "Member Group")]
        public int MemberGroupID { get; set; }

        [Required]
        [Display(Name = "Member")]
        public int MemberId { get; set; }
        public string? MemberName { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }

        [Required]
        [Display(Name = "Payment Method")]
        public int PaymentMethodID { get; set; }

        [Required]
        [Display(Name = "Payout Type")]
        public int PayoutTypeId { get; set; }
        public string? PayoutTypeName { get; set; }

        [Required]
        [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than 0")]
        public decimal Amount { get; set; }

        [Required]
        [DataType(DataType.Date)]
        [Display(Name = "Payout Date")]
        public DateTime PayoutDate { get; set; }

        public string? Reference { get; set; }


        public string? ProofOfPaymentPath { get; set; }

        [Display(Name = "Processed By")]
        public string? CreatedBy { get; set; }

        // For dropdowns (do not validate these)
        public List<SelectListItem> PayoutTypes { get; set; } = new();
        public List<MemberOption> MemberOptions { get; set; } = new();
        public decimal GroupBalance { get; set; }
        public int MemberCount { get; set; }
        public bool EnablePayout { get; set; }
        public DateTime? NextPayoutDate { get; set; }
        public MemberOption? Member { get; set; }
        public MemberOption? NextMember { get; set; }       


    }
}
