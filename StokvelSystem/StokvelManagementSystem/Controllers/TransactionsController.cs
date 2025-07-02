using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;
using StokvelManagementSystem.Models;
using System.IO;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;

namespace StokvelManagementSystem.Controllers
{
    [Authorize] // Require authentication for all actions in this controller
    public class TransactionsController : Controller
    {
        private readonly IConfiguration _configuration;

        public TransactionsController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [HttpGet]
        [Authorize(Roles = "Admin")] // Only admins can access the create form
        public IActionResult Create()
        {
            var model = new Transaction
            {
                TransactionDate = DateTime.Now,
                PaymentMethodID = 1, // Default to Bank Transfer
                PenaltyAmount = 0,    // Default penalty amount
                ContributionAmount = 0, // Default contribution amount
                TotalAmount = 0              // Default total
            };
            return View(model);
        }

        [HttpPost]
        [Authorize(Roles = "Admin")] // Only admins can submit transactions
        [ValidateAntiForgeryToken]
        public IActionResult Create(Transaction model)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    // Calculate total before saving
                    model.TotalAmount = model.ContributionAmount + model.PenaltyAmount;

                    // Handle file upload
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
                        var query = @"INSERT INTO Transactions 
                                    (PaymentMethodID, PenaltyAmount, ContributionAmount, 
                                     TotalAmount, TransactionDate, Reference, ProofOfPaymentPath, CreatedBy)
                                    VALUES 
                                    (@PaymentMethodID, @PenaltyAmount, @ContributionAmount, 
                                     @TotalAmount, @TransactionDate, @Reference, @ProofOfPaymentPath, @CreatedBy);
                                    SELECT SCOPE_IDENTITY();";

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
                            command.Parameters.AddWithValue("@CreatedBy", User.Identity.Name); 

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
                    // Log the exception here if you have a logging mechanism
                }
            }

            return View(model);
        }

        [AllowAnonymous] // Allow all authenticated users to view transactions
        public IActionResult Index()
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

            return View(transactions);
        }
    }
}