using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using System.ComponentModel.DataAnnotations;

namespace StokvelManagementSystem.Models
{
    public class Member
    {
      
        public string FirstName { get; set; }
        public string? MiddleName { get; set; }
        [Required]
        public string LastName { get; set; }
        public DateTime DOB { get; set; }
        [Required]
        public required string NationalID { get; set; }
        public string Phone { get; set; }
        [Required]
        public string Email { get; set; }
        [Required]
        public int GenderID { get; set; }
        [BindNever]
        [ValidateNever]
        public string GenderText{  get; set; }
        public int RoleID { get; set; }
        public DateTime RegistrationDate { get; set; }

        
        public string? CVC { get; set; }
        public string? Expiry { get; set; } 
        public string? Status { get; set; }
        public string? AccountNumber { get; set; }
       
        public string Address { get; set; }
        //FOR LOGIN 
        public string Username { get; set; }
        
        [DataType(DataType.Password)]
        public string? Password { get; set; }  // make nullable

        [DataType(DataType.Password)]
        [Compare("Password", ErrorMessage = "Passwords do not match.")]
        public string? ConfirmPassword { get; set; } // nullable


    }
}
