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

                // ✅ 1. Get first unpaid member and the next one (ordered by ID)
                var memberQuery = @"
                                    SELECT DISTINCT
                                        mg.ID AS ID,
                                        m.ID AS MemberID,
                                        CONCAT(m.FirstName, ' ', m.LastName) AS FullName,
                                        m.Email,
                                        m.Phone,
                                        mg.ID AS MemberGroupID
                                    FROM MemberGroups mg
                                    JOIN Members m ON mg.MemberID = m.ID
                                    JOIN Groups g ON mg.GroupID = g.ID
                                    LEFT JOIN (
                                        SELECT p1.*
                                        FROM Payouts p1
                                        JOIN (
                                            SELECT MemberGroupID, MAX(PaidForCycle) AS MaxCycle
                                            FROM Payouts
                                            GROUP BY MemberGroupID
                                        ) latest ON p1.MemberGroupID = latest.MemberGroupID AND p1.PaidForCycle = latest.MaxCycle
                                    ) p ON p.MemberGroupID = mg.ID
                                    WHERE mg.GroupID = @GroupId
                                    AND (p.PaidForCycle IS NULL OR p.PaidForCycle != g.Cycles)
                                ";

                    using (var command = new SqlCommand(memberQuery, connection))
                    {
                        command.Parameters.AddWithValue("@GroupId", groupId);
                        using (var reader = command.ExecuteReader())
                        {
                            int count = 0;
                            while (reader.Read())
                            {
                                var member = new MemberOption
                                {
                                    Id = Convert.ToInt32(reader["ID"]),
                                    MemberId = Convert.ToInt32(reader["MemberID"]),
                                    FullName = reader["FullName"].ToString(),
                                    Email = reader["Email"].ToString(),
                                    Phone = reader["Phone"].ToString()
                                };

                                if (count == 0)
                                {
                                    model.Member = member; // First member
                                }
                                else if (count == 1)
                                {
                                    model.NextMember = member; // Second member
                                    break; // No need to continue
                                }

                                count++;
                            }
                        }
                    }



                    // ✅ 3. Get group balance from Contributions via MemberGroups
                var balanceAndPayoutDateQuery = @"
                                                SELECT 
                                                    ISNULL(SUM(c.TotalAmount), 0) AS GroupBalance,

                                                    ISNULL(SUM(c.ContributionAmount), 0) 
                                                    - ISNULL((
                                                        SELECT SUM(p.Amount)
                                                        FROM Payouts p
                                                        JOIN MemberGroups mpg ON p.MemberGroupID = mpg.ID
                                                        WHERE mpg.GroupID = g.ID AND p.PaidForCycle = g.Cycles
                                                    ), 0) AS TotalContributions,

                                                    ISNULL(SUM(c.PenaltyAmount), 0) AS Penalties,

                                                    CASE 
                                                        WHEN LOWER(f.FrequencyName) = 'periodic' THEN g.PeriodicDate
                                                        ELSE DATEADD(DAY, 
                                                            (g.Cycles + 1) * 
                                                            CASE LOWER(f.FrequencyName)
                                                                WHEN 'weekly' THEN 7
                                                                WHEN 'monthly' THEN 30
                                                                WHEN 'annually' THEN 365
                                                                WHEN 'daily' THEN 1
                                                                ELSE 0
                                                            END,
                                                            g.StartDate
                                                        )
                                                    END AS NextPayoutDate

                                                FROM Groups g
                                                JOIN Frequencies f ON g.FrequencyID = f.ID
                                                LEFT JOIN MemberGroups mg ON mg.GroupID = g.ID
                                                LEFT JOIN Contributions c ON c.MemberGroupID = mg.ID AND c.PaidForCycle = g.Cycles
                                                WHERE g.ID = @GroupId
                                                GROUP BY g.ID, g.Cycles, f.FrequencyName, g.StartDate, g.PeriodicDate;
                                            ";

                    using (var command = new SqlCommand(balanceAndPayoutDateQuery, connection))
                    {
                        command.Parameters.AddWithValue("@GroupId", groupId);
                        using (var reader = command.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                model.GroupBalance = reader["GroupBalance"] != DBNull.Value ? Convert.ToDecimal(reader["GroupBalance"]) : 0;
                                model.TotalContributions = reader["TotalContributions"] != DBNull.Value ? Convert.ToDecimal(reader["TotalContributions"]) : 0;
                                model.Penalties = reader["Penalties"] != DBNull.Value ? Convert.ToDecimal(reader["Penalties"]) : 0;
                                model.NextPayoutDate = reader["NextPayoutDate"] != DBNull.Value ? Convert.ToDateTime(reader["NextPayoutDate"]) : (DateTime?)null;
                            }
                        }
                    }


                // ✅ 4. Check if total group contributions meet expected value (per-person amount × members)
                var enablePayoutQuery = @"
                                SELECT 
                                    CASE 
                                        WHEN ISNULL(SUM(c.ContributionAmount), 0) = g.ContributionAmount * COUNT(DISTINCT mg.ID) THEN 1
                                        ELSE 0
                                    END AS EnablePayout,
                                    COUNT(DISTINCT mg.ID) AS MemberCount,
                                    g.ContributionAmount * COUNT(DISTINCT mg.ID) AS ExpectedPayment
                                FROM MemberGroups mg
                                JOIN Groups g ON mg.GroupID = g.ID
                                LEFT JOIN Contributions c ON c.MemberGroupID = mg.ID
                                WHERE mg.GroupID = @GroupId
                                GROUP BY g.ContributionAmount;
                                ";

                using (var command = new SqlCommand(enablePayoutQuery, connection))
                {
                    command.Parameters.AddWithValue("@GroupId", groupId);
                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            model.EnablePayout = reader.GetInt32(0) == 1; // EnablePayout
                            model.MemberCount = reader.GetInt32(1);       // MemberCount
                            model.ExpectedPayment = reader["ExpectedPayment"] != DBNull.Value ? Convert.ToDecimal(reader["ExpectedPayment"]) : 0;
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

                    // Get MemberGroupID
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

                    // Get current Cycle from Groups table
                    int currentCycle = 0;
                    var getCycleQuery = @"SELECT Cycles FROM Groups WHERE ID = @GroupID";
                    using (var cycleCmd = new SqlCommand(getCycleQuery, connection))
                    {
                        cycleCmd.Parameters.AddWithValue("@GroupID", groupId);
                        var cycleResult = await cycleCmd.ExecuteScalarAsync();
                        currentCycle = Convert.ToInt32(cycleResult);
                    }

                    // Insert Payout with PaidForCycle
                    var insertQuery = @"INSERT INTO Payouts 
                                        (MemberGroupID, PaymentMethodID, Amount, 
                                        TransactionDate, Reference, ProofOfPaymentPath, CreatedBy, PaidForCycle)
                                        VALUES 
                                        (@MemberGroupID, @PaymentMethodID, @Amount, 
                                        @PayoutDate, @Reference, @ProofOfPaymentPath, @CreatedBy, @PaidForCycle);";

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
                        command.Parameters.AddWithValue("@PaidForCycle", currentCycle);

                        await command.ExecuteNonQueryAsync();
                    }

                    // Check if all members have submitted payout for this cycle
                    var checkMembersQuery = @"
                        SELECT COUNT(*) 
                        FROM MemberGroups 
                        WHERE GroupID = @GroupID";

                    var checkPayoutsQuery = @"
                        SELECT COUNT(*) 
                        FROM Payouts P
                        INNER JOIN MemberGroups MG ON MG.ID = P.MemberGroupID
                        WHERE MG.GroupID = @GroupID AND P.PaidForCycle = @Cycles";

                    int totalMembers, totalPayouts;

                    using (var cmd = new SqlCommand(checkMembersQuery, connection))
                    {
                        cmd.Parameters.AddWithValue("@GroupID", groupId);
                        totalMembers = (int)await cmd.ExecuteScalarAsync();
                    }

                    using (var cmd = new SqlCommand(checkPayoutsQuery, connection))
                    {
                        cmd.Parameters.AddWithValue("@GroupID", groupId);
                        cmd.Parameters.AddWithValue("@Cycles", currentCycle);
                        totalPayouts = (int)await cmd.ExecuteScalarAsync();
                    }

                    if (totalMembers == totalPayouts)
                    {
                        var updateCycleQuery = "UPDATE Groups SET Cycles = Cycles + 1 WHERE ID = @GroupID";
                        using (var updateCmd = new SqlCommand(updateCycleQuery, connection))
                        {
                            updateCmd.Parameters.AddWithValue("@GroupID", groupId);
                            await updateCmd.ExecuteNonQueryAsync();
                        }
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
                
            }

            return members;
        }
    }
}