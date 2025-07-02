using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace StokvelManagementSystem.Models
{
    public class PayoutType
    {
        public int ID { get; set; }

        [Required]
        [StringLength(50)]
        public string PayoutTypeName { get; set; } 
    }

    public class PayoutSchedule
    {
        public int ID { get; set; }

        [Required]
        public int GroupID { get; set; }

        [Required]
        public int PayoutTypeID { get; set; }

        [Required]
        public DateTime StartDate { get; set; }

        [Required]
        public DateTime EndDate { get; set; }

        [Required]
        public int StatusID { get; set; }

        public int? OrderNo { get; set; } // For rotational payouts

        public Group Group { get; set; }
        public PayoutType PayoutType { get; set; }
        public Status Status { get; set; }
    }

    public class PayoutCycle
    {
        public int ID { get; set; }

        [Required]
        public int GroupID { get; set; }

        [Required]
        public int PayoutTypeID { get; set; }

        [Required]
        public DateTime StartDate { get; set; }

        [Required]
        public DateTime EndDate { get; set; }

        [Required]
        public int StatusID { get; set; }

        public Group Group { get; set; }
        public PayoutType PayoutType { get; set; }
        public Status Status { get; set; }
    }

    public class Status
    {
        public int ID { get; set; }

        [Required]
        [StringLength(50)]
        public string StatusName { get; set; } // "Pending", "Active", "Completed"
    }

    public class PayoutDashboardViewModel
    {
        public List<PayoutSchedule> ActiveSchedules { get; set; }
        public List<PayoutCycle> UpcomingCycles { get; set; }
    }

    // Add these new classes for member payouts
    public class MemberPayout
    {
        public int ID { get; set; }
        public int MemberID { get; set; }
        public string MemberName { get; set; }
        public string PayoutType { get; set; }
        public decimal Amount { get; set; }
        public DateTime PayoutDate { get; set; }
        public string Notes { get; set; }
    }

    public class PayoutCreateViewModel
    {
        [Required]
        public int MemberID { get; set; }

        [Required]
        public int PayoutTypeID { get; set; }

        [Required]
        [DataType(DataType.Currency)]
        public decimal Amount { get; set; }

        [Required]
        [DataType(DataType.Date)]
        public DateTime PayoutDate { get; set; } = DateTime.Now;

        [DataType(DataType.MultilineText)]
        public string Notes { get; set; }

        public SelectList Members { get; set; }
        public SelectList PayoutTypes { get; set; }
    }
}