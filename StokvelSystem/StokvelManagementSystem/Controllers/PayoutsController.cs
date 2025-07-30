using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;
using StokvelManagementSystem.Models;
using System.IO;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;
using System.Linq;
using Microsoft.Extensions.Logging;
using System;

namespace StokvelManagementSystem.Controllers
{
    [Authorize]
    public class PayoutsController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<PayoutsController> _logger;

        public PayoutsController(IConfiguration configuration, ILogger<PayoutsController> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        [HttpGet]
        [Authorize(Roles = "Admin")]
        public IActionResult PayoutsCreate(int groupId)
        {
            var model = new Payout
            {
                PayoutDate = DateTime.Now,
                PaymentMethodID = 1,
                Amount = 0,
                MemberOptions = new List<MemberOption>(),
                PayoutTypes = GetPaymentMethodsFromDatabase().Select(p => new SelectListItem
                {
                    Value = p.Id.ToString(),
                    Text = p.Method
                }).ToList()
            };

        
            using (var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
            {
                var memberQuery = @"
                    SELECT mg.ID, m.ID AS MemberID, CONCAT(m.FirstName, ' ', m.LastName) AS FullName, m.Email, m.Phone
                    FROM MemberGroups mg
                    JOIN Members m ON mg.MemberID = m.ID
                    WHERE mg.GroupID = @GroupId";

                using (var command = new SqlCommand(memberQuery, connection))
                {
                    command.Parameters.AddWithValue("@GroupId", groupId);
                    connection.Open();
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            model.MemberOptions.Add(new MemberOption
                            {
                                Id = Convert.ToInt32(reader["ID"]),
                                MemberId = Convert.ToInt32(reader["MemberID"]),
                                FullName = reader["FullName"].ToString(),
                                Email = reader["Email"].ToString(),
                                Phone = reader["Phone"].ToString()
                            });
                        }
                    }
                }
            }

            model.GroupId = groupId;
            return View("~/Views/Transactions/PayoutsCreate.cshtml", model);
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        public IActionResult PayoutsCreate(Payout model, int groupId)
        {
            var memberIdClaim = User.Claims.FirstOrDefault(c => c.Type == "member_id");
            if (memberIdClaim != null && int.TryParse(memberIdClaim.Value, out var memberId))
            {
                model.CreatedBy = memberId.ToString();
                ModelState.Remove("CreatedBy");
            }
            else
            {
                _logger.LogError("MemberID not found in JWT claims");
                ModelState.AddModelError("ProcessedBy", "Unable to determine member identity.");
            }

            model.MemberOptions = GetMemberOptionsForGroup(groupId);
            model.PayoutTypes = GetPaymentMethodsFromDatabase().Select(p => new SelectListItem
            {
                Value = p.Id.ToString(),
                Text = p.Method
            }).ToList();
            model.GroupId = groupId;

            try
            {
                if (!ModelState.IsValid)
                {
                    foreach (var state in ModelState)
                    {
                        foreach (var error in state.Value.Errors)
                        {
                            _logger.LogError($"Field: {state.Key} - Error: {error.ErrorMessage}");
                        }
                    }
                    return View("~/Views/Transactions/PayoutsCreate.cshtml", model);
                }
                if (Request.Form.Files.Count > 0)
                {
                    var file = Request.Form.Files["proofFile"];
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
                        ModelState.Remove("ProofOfPaymentPath");
                    }
                }

              
                using (var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
                {
                    var query = @"INSERT INTO Payouts 
                                (MemberGroupID, PaymentMethodID, Amount, 
                                TransactionDate, Reference, ProofOfPaymentPath, CreatedBy)
                                VALUES 
                                (@MemberGroupID, @PaymentMethodID, @Amount, 
                                @PayoutDate, @Reference, @ProofOfPaymentPath, @CreatedBy);
                                SELECT SCOPE_IDENTITY();";

                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@MemberGroupID", model.Id); // This should be MemberGroupID
                        command.Parameters.AddWithValue("@PaymentMethodID", model.PayoutTypeId);
                        command.Parameters.AddWithValue("@Amount", model.Amount);
                        command.Parameters.AddWithValue("@PayoutDate", model.PayoutDate);
                        command.Parameters.AddWithValue("@Reference", model.Reference ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@ProofOfPaymentPath", 
                            string.IsNullOrEmpty(model.ProofOfPaymentPath) ? 
                            DBNull.Value : (object)model.ProofOfPaymentPath);
                        command.Parameters.AddWithValue("@CreatedBy", model.CreatedBy);
                       
                        connection.Open();
                        command.ExecuteScalar();
                    }
                }

                TempData["SuccessMessage"] = "Payout recorded successfully!";
                return RedirectToAction("PayoutsIndex", new { groupId = groupId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving payout");
                ModelState.AddModelError("", $"Error saving payout: {ex.Message}");
                return View("~/Views/Transactions/PayoutsCreate.cshtml", model);
            }
        }

        [HttpGet]
        public IActionResult PayoutIndex(int groupId)
        {
            var payouts = new List<Payout>();
            
            using (var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
            {
                var query = @"
                    SELECT 
                     
                        p.PaymentMethodID, 
                        p.Amount, 
                        p.TransactionDate AS PayoutDate, 
                        p.Reference, 
                        p.ProofOfPaymentPath,
                   
                        g.GroupName,
                        CONCAT(m.FirstName, ' ', m.LastName) AS MemberName,
                        m.Phone, 
                        m.Email,
                        CONCAT(mcreator.FirstName, ' ', mcreator.LastName) AS CreatedBy
                    FROM dbo.Payouts p
                    JOIN dbo.MemberGroups mg ON mg.ID = p.MemberGroupID
                    JOIN dbo.Groups g ON mg.GroupID = g.ID
                    JOIN dbo.Members m ON m.ID = mg.MemberID
                    LEFT JOIN dbo.Members mcreator ON mcreator.ID = TRY_CAST(p.CreatedBy AS INT)
                    WHERE mg.GroupID = @GroupId
                    ORDER BY p.TransactionDate DESC;";

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@GroupId", groupId);
                    connection.Open();
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            payouts.Add(new Payout
                            {
                                PaymentMethodID = Convert.ToInt32(reader["PaymentMethodID"]),
                                Amount = Convert.ToDecimal(reader["Amount"]),
                                PayoutDate = Convert.ToDateTime(reader["PayoutDate"]),
                                Reference = reader["Reference"].ToString(),
                                ProofOfPaymentPath = reader["ProofOfPaymentPath"]?.ToString(),
                                CreatedBy = reader["ProcessedBy"]?.ToString(),
                                GroupName = reader["GroupName"].ToString(),
                                MemberName = reader["MemberName"].ToString(),
                                Phone = reader["Phone"].ToString(),
                                Email = reader["Email"].ToString()
                            });
                        }
                    }
                }
            }

            return View("~/Views/Transactions/PayoutIndex.cshtml", payouts);
        }

        [HttpGet]
        public IActionResult GetAvailableBalance(int memberGroupId)
        {
            decimal availableBalance = 0;
            
            using (var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
            {
                // Calculate available balance (contributions - payouts)
                var query = @"
                    SELECT 
                        (SELECT ISNULL(SUM(TotalAmount), 0) FROM Contributions WHERE MemberGroupID = @MemberGroupID) -
                        (SELECT ISNULL(SUM(Amount), 0) FROM Payouts WHERE MemberGroupID = @MemberGroupID) AS AvailableBalance";

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@MemberGroupID", memberGroupId);
                    connection.Open();
                    var result = command.ExecuteScalar();
                    availableBalance = result != DBNull.Value ? Convert.ToDecimal(result) : 0;
                }
            }
            
            return Json(new { availableBalance });
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

        private List<MemberOption> GetMemberOptionsForGroup(int groupId)
        {
            var members = new List<MemberOption>();

            using (var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
            {
                var memberQuery = @"
                    SELECT mg.ID, m.ID AS MemberID, CONCAT(m.FirstName, ' ', m.LastName) AS FullName, m.Email, m.Phone
                    FROM MemberGroups mg
                    JOIN Members m ON mg.MemberID = m.ID
                    WHERE mg.GroupID = @GroupId";

                using (var command = new SqlCommand(memberQuery, connection))
                {
                    command.Parameters.AddWithValue("@GroupId", groupId);
                    connection.Open();
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            members.Add(new MemberOption
                            {
                                Id = Convert.ToInt32(reader["ID"]),
                                MemberId = Convert.ToInt32(reader["MemberID"]),
                                FullName = reader["FullName"].ToString(),
                                Email = reader["Email"].ToString(),
                                Phone = reader["Phone"].ToString()
                            });
                        }
                    }
                }
            }

            return members;
        }
    }
}