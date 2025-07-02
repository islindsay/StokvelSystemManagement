using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
namespace StokvelManagementSystem.Models
{
    // i used BindNever to exclude these fields from model binding and validation which will cause errors because the fields will be required 
    public class Group
    {
        public int ID { get; set; }
        public string GroupName { get; set; }
        public decimal? ContributionAmount { get; set; }
        public int MemberLimit { get; set; }
    
        public DateTime? StartDate { get; set; }
        [BindNever]
        [ValidateNever]
        public string FrequencyName { get; set; } = null;
        public DateTime? NextContributionDate { get; set; }
        public string Duration {  get; set; }
        public string CurrencyID {  get; set; }
        public DateTime CreatedDate { get; set; }
        public int PayoutTypeID { get; set; }
        public int FrequencyID { get; set; }

        public int MemberId { get; set; }
        [BindNever]
        public bool GroupCreated { get; set; }
        [BindNever]
        public bool CanCreate { get; set; }

        // From GroupSettings
        public decimal PenaltyAmount { get; set; }
        public int? PenaltyGraceDays { get; set; }
        public bool AllowDeferrals { get; set; }

        //From PayoutTypes 
        [BindNever]
        [ValidateNever]
        public string PayoutType { get; set; } = null;
        //From Currencies 
        [BindNever]
        [ValidateNever]
        public string? Currency { get; set; } = null;
        public string NationalID { get; set; }
        [BindNever]
        public List<SelectListItem> PayoutTypes { get; set; } = new();
        [BindNever]
        public List<SelectListItem> Currencies { get; set; } = new();
        public List<Group>? MyGroups { get; set; } = new();
        [BindNever]
        public List<Group> NewGroups { get; set; } = new();
        [BindNever]
        [ValidateNever]
        public string SearchNationalId { get; set; }
        [BindNever]
        [ValidateNever]
        public List<SelectListItem> FrequencyOptions { get; set; }
        public bool ShowNewGroups { get; set; }


    }
}
