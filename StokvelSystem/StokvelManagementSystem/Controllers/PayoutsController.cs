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
                connection.Open();

                // ✅ 1. Get members for the group
                var memberQuery = @"
                    SELECT mg.ID, m.ID AS MemberID, CONCAT(m.FirstName, ' ', m.LastName) AS FullName, m.Email, m.Phone
                    FROM MemberGroups mg
                    JOIN Members m ON mg.MemberID = m.ID
                    WHERE mg.GroupID = @GroupId";

                using (var command = new SqlCommand(memberQuery, connection))
                {
                    command.Parameters.AddWithValue("@GroupId", groupId);
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

                // ✅ 2. Get group name
                var groupNameQuery = "SELECT GroupName FROM Groups WHERE ID = @GroupId";
                using (var command = new SqlCommand(groupNameQuery, connection))
                {
                    command.Parameters.AddWithValue("@GroupId", groupId);
                    var result = command.ExecuteScalar();
                    model.GroupName = result?.ToString();  // Nullable safe assignment
                }
            }

            model.GroupId = groupId;
            return View("~/Views/Transactions/PayoutsCreate.cshtml", model);
        }


        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> PayoutsCreate(Payout model, int groupId)
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

            // Handle file upload
            if (Request.Form.Files.Count > 0)
            {
                var file = Request.Form.Files["proofFile"];
                if (file != null && file.Length > 0)
                {
                    var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
                    if (!Directory.Exists(uploadsFolder))
                        Directory.CreateDirectory(uploadsFolder);

                    var uniqueFileName = $"{Guid.NewGuid()}_{Path.GetFileName(file.FileName)}";
                    var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await file.CopyToAsync(stream);
                    }

                    model.ProofOfPaymentPath = $"/uploads/{uniqueFileName}";
                    ModelState.Remove("ProofOfPaymentPath");
                }
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
                            _logger.LogError("Field: {Field} - Error: {Message}", state.Key, error.ErrorMessage);
                        }
                    }
                    return View("~/Views/Transactions/PayoutsCreate.cshtml", model);
                }

                using (var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
                {
                    await connection.OpenAsync();

                    // ✅ Get correct MemberGroupID from MemberGroups table
                    var getGroupQuery = @"SELECT ID FROM MemberGroups WHERE MemberID = @MemberID AND GroupID = @GroupID";
                    int memberGroupId;

                    using (var getGroupCmd = new SqlCommand(getGroupQuery, connection))
                    {
                        getGroupCmd.Parameters.AddWithValue("@MemberID", model.MemberId);
                        getGroupCmd.Parameters.AddWithValue("@GroupID", groupId);

                        var result = await getGroupCmd.ExecuteScalarAsync();
                        if (result == null)
                        {
                            ModelState.AddModelError("", "Selected member is not part of this group.");
                            return View("~/Views/Transactions/PayoutsCreate.cshtml", model);
                        }

                        memberGroupId = Convert.ToInt32(result);
                    }

                    // ✅ Proceed to insert using the correct MemberGroupID
                    var insertQuery = @"INSERT INTO Payouts 
                                        (MemberGroupID, PaymentMethodID, Amount, 
                                        TransactionDate, Reference, ProofOfPaymentPath, CreatedBy)
                                        VALUES 
                                        (@MemberGroupID, @PaymentMethodID, @Amount, 
                                        @PayoutDate, @Reference, @ProofOfPaymentPath, @CreatedBy);
                                        SELECT SCOPE_IDENTITY();";

                    using (var command = new SqlCommand(insertQuery, connection))
                    {
                        command.Parameters.AddWithValue("@MemberGroupID", memberGroupId);
                        command.Parameters.AddWithValue("@PaymentMethodID", model.PayoutTypeId);
                        command.Parameters.AddWithValue("@Amount", model.Amount);
                        command.Parameters.AddWithValue("@PayoutDate", model.PayoutDate);
                        command.Parameters.AddWithValue("@Reference", model.Reference ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@ProofOfPaymentPath",
                            string.IsNullOrEmpty(model.ProofOfPaymentPath) ? DBNull.Value : (object)model.ProofOfPaymentPath);
                        command.Parameters.AddWithValue("@CreatedBy", model.CreatedBy);

                        await command.ExecuteScalarAsync();
                    }
                }

                TempData["SuccessMessage"] = "Payout recorded successfully!";
                return RedirectToAction("PayoutIndex", new { groupId = groupId });
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

            try
            {
                using (var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
                {
                    var query = @"
                        SELECT 
                            p.PayoutID,
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
                        ORDER BY p.CreatedAt DESC;";

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
                                    Id = Convert.ToInt32(reader["PayoutID"]),
                                    PaymentMethodID = Convert.ToInt32(reader["PaymentMethodID"]),
                                    Amount = Convert.ToDecimal(reader["Amount"]),
                                    PayoutDate = Convert.ToDateTime(reader["PayoutDate"]),
                                    Reference = reader["Reference"].ToString(),
                                    ProofOfPaymentPath = reader["ProofOfPaymentPath"]?.ToString(),
                                    CreatedBy = reader["CreatedBy"]?.ToString(),
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
            catch (Exception ex)
            {

                TempData["ErrorMessage"] = "Something went wrong while loading payouts.";
                return View("~/Views/Transactions/PayoutIndex.cshtml", payouts); // return empty list
            }
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
                  // ✅ 2. Get group name
                var groupNameQuery = "SELECT GroupName FROM Groups WHERE ID = @GroupId";
                using (var command = new SqlCommand(groupNameQuery, connection))
                {
                    command.Parameters.AddWithValue("@GroupId", groupId);
                    var result = command.ExecuteScalar();
                    model.GroupName = result?.ToString();  // Nullable safe assignment
                }
            }

            return members;
        }
    }
}