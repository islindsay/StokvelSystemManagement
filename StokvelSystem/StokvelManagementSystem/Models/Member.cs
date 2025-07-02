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
       
        public string Address { get; set; }
        //FOR LOGIN 
        public string Username { get; set; }
        public string Password { get; set; }
        [Required]
        [Compare("Password", ErrorMessage ="Passwords dont match")]
        public string ConfirmPassword { get; set; }

    }
}
