using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;
using StokvelManagementSystem.Models;
using System.IO;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;
using System.Linq;
using System;

namespace StokvelManagementSystem.Controllers
{
    [Authorize]
    public class ContributionsController : Controller
    {
        private readonly IConfiguration _configuration;

        public ContributionsController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [HttpGet]
        [Authorize(Roles = "Admin")]
        public IActionResult ContributionsCreate()
        {
            var model = new Contribution
            {
                TransactionDate = DateTime.Now,
                PaymentMethodID = 1,
                PenaltyAmount = 0,
                ContributionAmount = 0, 
                TotalAmount = 0
            };
            model.PaymentMethods = GetPaymentMethodsFromDatabase();
            return View("~/Views/Transactions/ContributionsCreate.cshtml", model);
        }

        [HttpGet]
        public IActionResult GetGroupDetails(int memberId)
        {
            var groupDetails = new GroupDetailsResponse();
            
            using (var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
            {
                var query = @"SELECT g.GroupName as GroupName, g.ContributionAmount as GroupContributionAmount, 
                            DATEADD(DAY, gs.PenaltyGraceDays, DATEADD(MONTH, DATEDIFF(MONTH, 0, GETDATE()), 0)) as DueDate
                            FROM Members m
                            JOIN Groups g ON m.GroupID = g.ID
                            JOIN GroupSettings gs ON g.ID = gs.GroupID
                            WHERE m.ID = @MemberId";
                
                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@MemberId", memberId);
                    connection.Open();
                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            groupDetails.GroupName = reader["GroupName"].ToString();
                            groupDetails.DueDate = Convert.ToDateTime(reader["DueDate"]);
                            groupDetails.GroupContributionAmount = Convert.ToDecimal(reader["GroupContributionAmount"]);
                        }
                    }
                }
            }
            
            return Json(groupDetails);
        }

        [HttpGet]
        public IActionResult GetPenaltySettings(string groupName)
        {
            var penaltySettings = new PenaltySettingsResponse();
            
            using (var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
            {
                var query = "SELECT PenaltyAmount FROM GroupSettings WHERE GroupId = @GroupId";
                
                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@GroupName", groupName);
                    connection.Open();
                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            penaltySettings.DailyPenaltyAmount = Convert.ToDecimal(reader["PenaltyAmount"]);
                        }
                    }
                }
            }
            
            return Json(penaltySettings);
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public IActionResult ContributionsCreate(Contribution model)
        {
            if (ModelState.IsValid)
            {
                model.PaymentMethods = GetPaymentMethodsFromDatabase();
                try
                {
                    // Calculate total
                    model.TotalAmount = model.ContributionAmount + model.PenaltyAmount;

                    // File upload
                    if (Request.Form.Files.Count > 0)
                    {
                        var file = Request.Form.Files["ProofOfPayment"];
                        if (file != null && file.Length > 0)
                        {
                            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
                            if (!Directory.Exists(uploadsFolder))
                                Directory.CreateDirectory(uploadsFolder);

                            var uniqueFileName = $"{Guid.NewGuid()}_{file.FileName}";
                            var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                            using (var stream = new FileStream(filePath, FileMode.Create))
                            {
                                file.CopyTo(stream);
                            }

                            model.ProofOfPaymentPath = $"/uploads/{uniqueFileName}";
                        }
                    }

                    // Save to database
                    using (var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
                    {
                        var query = @"INSERT INTO Contributions 
                                    (MemberGroupID, PaymentMethodID, PenaltyAmount, ContributionAmount, 
                                    TotalAmount, TransactionDate, Reference, ProofOfPaymentPath, CreatedBy)
                                    VALUES 
                                    (@MemberGroupID, @PaymentMethodID, @PenaltyAmount, @ContributionAmount, 
                                    @TotalAmount, @TransactionDate, @Reference, @ProofOfPaymentPath, @CreatedBy);
                                    SELECT SCOPE_IDENTITY();";

                        using (var command = new SqlCommand(query, connection))
                        {
                            command.Parameters.AddWithValue("@MemberGroupID", model.MemberGroupID);
                            command.Parameters.AddWithValue("@PaymentMethodID", model.PaymentMethodID);
                            command.Parameters.AddWithValue("@PenaltyAmount", model.PenaltyAmount);
                            command.Parameters.AddWithValue("@ContributionAmount", model.ContributionAmount);
                            command.Parameters.AddWithValue("@TotalAmount", model.TotalAmount);
                            command.Parameters.AddWithValue("@TransactionDate", model.TransactionDate);
                            command.Parameters.AddWithValue("@Reference", model.Reference);
                            command.Parameters.AddWithValue("@ProofOfPaymentPath", 
                                string.IsNullOrEmpty(model.ProofOfPaymentPath) ? 
                                DBNull.Value : (object)model.ProofOfPaymentPath);
                            command.Parameters.AddWithValue("@CreatedBy", User.Identity.Name); // Use current user

                            connection.Open();
                            var newId = command.ExecuteScalar();
                        }
                    }

                    TempData["SuccessMessage"] = "Transaction recorded successfully!";
                    return RedirectToAction("ContributionsIndex");
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", $"Error saving transaction: {ex.Message}");
                }
            }
            return View("~/Views/Transactions/ContributionsCreate.cshtml", model);
        }

        [AllowAnonymous]
        public IActionResult ContributionsIndex()
        {
            var contributions = new List<Contribution>();

            using (var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
            {
                var query = @"SELECT 
    c.ID, 
    c.PaymentMethodID, 
    c.PenaltyAmount, 
    c.ContributionAmount, 
    c.TotalAmount, 
    c.TransactionDate, 
    c.Reference, 
    c.ProofOfPaymentPath, 
    c.CreatedBy,
    g.GroupName AS GroupName,
    CONCAT(m.FirstName, ' ', m.LastName) AS MemberName,
    m.Phone, 
    m.Email
FROM Contributions c
JOIN Groups g ON c.MemberGroupID = g.ID
JOIN Members m ON c.CreatedBy = m.UserID 
ORDER BY c.TransactionDate DESC";

                using (var command = new SqlCommand(query, connection))
                {
                    connection.Open();
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            contributions.Add(new Contribution
                            {
                                ID = Convert.ToInt32(reader["ID"]),
                                PaymentMethodID = Convert.ToInt32(reader["PaymentMethodID"]),
                                PenaltyAmount = Convert.ToDecimal(reader["PenaltyAmount"]),
                                ContributionAmount = Convert.ToDecimal(reader["ContributionAmount"]),
                                TotalAmount = Convert.ToDecimal(reader["TotalAmount"]),
                                TransactionDate = Convert.ToDateTime(reader["TransactionDate"]),
                                Reference = reader["Reference"].ToString(),
                                ProofOfPaymentPath = reader["ProofOfPaymentPath"]?.ToString(),
                                CreatedBy = reader["CreatedBy"]?.ToString(),
                                GroupName = reader["GroupName"].ToString(),
                                FirstName = reader["MemberName"].ToString(),
                                Phone = Convert.ToInt32(reader["Phone"]),
                                Email = reader["Email"].ToString()
                            });
                        }
                    }
                }
            }

            return View("~/Views/Transactions/ContributionsIndex.cshtml", contributions);
        }

        private List<PaymentMethod> GetPaymentMethodsFromDatabase()
        {
            var paymentMethods = new List<PaymentMethod>();

            using (var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
            {
                var query = "SELECT Id, Method FROM PaymentMethods";
                using (var command = new SqlCommand(query, connection))
                {
                    connection.Open();
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            paymentMethods.Add(new PaymentMethod
                            {
                                Id = Convert.ToInt32(reader["Id"]),
                                Method = reader["Method"].ToString()
                            });
                        }
                    }
                }
            }

            return paymentMethods;
        }
    }
}