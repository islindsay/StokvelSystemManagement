using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;
using StokvelManagementSystem.Models;
using System.IO;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ViewEngines;


namespace StokvelManagementSystem.Controllers
{
    [Authorize] //Authentication for all endpoints
    public class ContributionsController : Controller
    {
        private readonly IConfiguration _configuration;

        public ContributionsController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [HttpGet]
        [Authorize(Roles = "Admin")] // Only admins can access the create form
        public IActionResult ContributionsCreate()
        {
            var model = new Transaction
            {
                TransactionDate = DateTime.Now,
                PaymentMethodID = 1,
                PenaltyAmount = 0,
                ContributionAmount = 0, 
                TotalAmount = 0              
            };
            model.PaymentMethods = GetPaymentMethodsFromDatabase();
            return View("~/Views/Transactions/ContributionsCreate.cshtml",model);
        }
        private List<PaymentMethod> GetPaymentMethodsFromDatabase()
        {
            var paymentMethods = new List<PaymentMethod>();

            using (var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
            {
                var query = "SELECT Id,Method FROM PaymentMethods";
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

        [HttpPost]
        [Authorize(Roles = "Admin")] // Only admins can submit transactions
        [ValidateAntiForgeryToken]
        public IActionResult ContributionsCreate(Transaction model)
        {
            if (ModelState.IsValid)
            {
                model.PaymentMethods = GetPaymentMethodsFromDatabase();
                try
                {
                    // Calculate total
                    model.TotalAmount = model.ContributionAmount + model.PenaltyAmount;

                    // file upload
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
                        var query = @"SELECT 
    t.ID, 
    t.PaymentMethodID, 
    t.PenaltyAmount, 
    t.ContributionAmount, 
    t.TotalAmount, 
    t.TransactionDate, 
    t.Reference, 
    t.ProofOfPaymentPath, 
    t.CreatedBy,
    g.GroupName,
    m.FirstName
FROM Transactions t
LEFT JOIN Groups g ON t.MemberGroupID = g.ID
LEFT JOIN Members m ON t.CreatedBy = m.Username
ORDER BY t.TransactionDate DESC";


                        using (var command = new SqlCommand(query, connection))
                        {
                            command.Parameters.AddWithValue("@PaymentMethodID", model.PaymentMethodID);
                            command.Parameters.AddWithValue("@PenaltyAmount", model.PenaltyAmount);
                            command.Parameters.AddWithValue("@ContributionAmount", model.ContributionAmount);
                            command.Parameters.AddWithValue("@TotalAmount", model.TotalAmount);
                            command.Parameters.AddWithValue("@TransactionDate", model.TransactionDate);
                            command.Parameters.AddWithValue("@Reference", model.Reference);
                            command.Parameters.AddWithValue("@ProofOfPaymentPath",
                                string.IsNullOrEmpty(model.ProofOfPaymentPath) ?
                                DBNull.Value : (object)model.ProofOfPaymentPath);
                            command.Parameters.AddWithValue("@CreatedBy", model.CreatedBy);
                            command.Parameters.AddWithValue("@GroupName", model.GroupName);
                            command.Parameters.AddWithValue("@FirstName", model.FirstName);
                            connection.Open();
                            var newId = command.ExecuteScalar();
                        }
                    }

                    TempData["SuccessMessage"] = "Transaction recorded successfully!";
                    return RedirectToAction("Index");
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", $"Error saving transaction: {ex.Message}");
                   
                }
            }
            return View("~/Views/Transactions/ContributionsCreate.cshtml");
        }

        [AllowAnonymous] // Allow all authenticated users to view transactions
        public IActionResult ContributionsIndex()
        {
            var transactions = new List<Transaction>();

            using (var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
            {
                var query = @"SELECT ID, PaymentMethodID, PenaltyAmount, 
                            ContributionAmount, TotalAmount, TransactionDate, Reference, 
                            ProofOfPaymentPath, CreatedBy
                            FROM Transactions 
                            ORDER BY TransactionDate DESC";

                using (var command = new SqlCommand(query, connection))
                {
                    connection.Open();
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            transactions.Add(new Transaction
                            {
                                ID = Convert.ToInt32(reader["ID"]),
                                PaymentMethodID = Convert.ToInt32(reader["PaymentMethodID"]),
                                PenaltyAmount = Convert.ToDecimal(reader["PenaltyAmount"]),
                                ContributionAmount = Convert.ToDecimal(reader["ContributionAmount"]),
                                TotalAmount = Convert.ToDecimal(reader["TotalAmount"]),
                                TransactionDate = Convert.ToDateTime(reader["TransactionDate"]),
                                Reference = reader["Reference"].ToString(),
                                ProofOfPaymentPath = reader["ProofOfPaymentPath"]?.ToString(),
                                CreatedBy = reader["CreatedBy"]?.ToString()
                            });
                        }
                    }
                }
            }

            return View("~/Views/Transactions/ContributionsIndex.cshtml",transactions);
        }
    }
}