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
                            ISNULL(SUM(CASE WHEN c.Status = 'Success' THEN c.TotalAmount ELSE 0 END), 0) AS GroupBalance,

                            ISNULL(SUM(CASE WHEN c.Status = 'Success' THEN c.ContributionAmount ELSE 0 END), 0)
                            -
                            ISNULL((
                                SELECT SUM(p.Amount)
                                FROM Payouts p
                                JOIN MemberGroups mpg ON p.MemberGroupID = mpg.ID
                                WHERE mpg.GroupID = g.ID 
                                AND p.PaidForCycle = g.Cycles
                                AND p.Status = 'Success'
                            ), 0) AS TotalContributions,


                            ISNULL(SUM(CASE WHEN c.Status = 'Success' THEN c.PenaltyAmount ELSE 0 END), 0) AS Penalties

                            CASE 
                                WHEN g.PayoutTypeID = 2 THEN g.PeriodicDate
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
                        GROUP BY g.ID, g.Cycles, f.FrequencyName, g.StartDate, g.PeriodicDate, g.PayoutTypeID;
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
                                    g.ContributionAmount * COUNT(DISTINCT mg.ID) AS ExpectedPayment,
                                    ISNULL(SUM(c.ContributionAmount), 0) AS CurrentAmount,
                                    g.FrequencyID,
                                    g.PayoutTypeID,
                                    g.Cycles AS CurrentCycle,
                                    ccy.Currency
 
                                FROM MemberGroups mg
                                JOIN Groups g ON mg.GroupID = g.ID
                                JOIN Currencies ccy ON g.CurrencyID = ccy.ID   -- ✅ bring currency from Currencies table
                                LEFT JOIN Contributions c 
                                    ON c.MemberGroupID = mg.ID 
                                AND c.PaidForCycle = g.Cycles   -- ✅ match only contributions for the current cycle
                                WHERE mg.GroupID = @GroupId
                                GROUP BY 
                                    g.ContributionAmount, 
                                    g.FrequencyID, 
                                    g.PayoutTypeID, 
                                    g.Cycles,
                                    ccy.Currency


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
                            model.FrequencyID = reader["FrequencyID"] != DBNull.Value ? Convert.ToInt32(reader["FrequencyID"]) : 0;
                            model.PayoutTypeID = reader["PayoutTypeID"] != DBNull.Value ? Convert.ToInt32(reader["PayoutTypeID"]) : 0;
                            model.Currency = reader["Currency"].ToString();
                            
                        }
                    }
                }

                // ✅ 5. Bulk payout stats (only if group is Periodic)
                if (model.PayoutTypeID == 2) 
                {
                    var bulkStatsQuery = @"
                        SELECT 
                            COUNT(DISTINCT mg.ID) AS TotalMembers,
                            COUNT(DISTINCT p.MemberGroupID) AS PaidMembers
                        FROM MemberGroups mg
                        LEFT JOIN Payouts p 
                            ON p.MemberGroupID = mg.ID
                            AND p.PaidForCycle = g.Cycles
                            AND p.Reference IS NOT NULL
                        JOIN Groups g ON g.ID = mg.GroupID
                        WHERE mg.GroupID = @GroupId;
                    ";

                    using (var command = new SqlCommand(bulkStatsQuery, connection))
                    {
                        command.Parameters.AddWithValue("@GroupId", groupId);
                        using (var reader = command.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                ViewBag.TotalMembers = reader["TotalMembers"] != DBNull.Value ? Convert.ToInt32(reader["TotalMembers"]) : 0;
                                ViewBag.PaidMembers = reader["PaidMembers"] != DBNull.Value ? Convert.ToInt32(reader["PaidMembers"]) : 0;
                                ViewBag.UnpaidMembers = ViewBag.TotalMembers - ViewBag.PaidMembers;
                            }
                        }
                    }
                }



                // ✅ 6. Get group name
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

            string status = "Success"; // default
            
            // Example test account numbers
            // 4111111111111111 → Fail
            // 4000000000009995 → Pending

            if (!string.IsNullOrEmpty(model.AccountNumber))
            {
                if (model.AccountNumber == "4111111111111111")
                {
                    status = "Fail";
                }
                else if (model.AccountNumber == "4000000000009995")
                {
                    status = "Pending";
                }
            }

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

            /*
            // ✅ Handle multiple file uploads (COMMENTED OUT)
            if (Request.Form.Files.Count > 0)
            {
                var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
                if (!Directory.Exists(uploadsFolder))
                    Directory.CreateDirectory(uploadsFolder);

                var filePaths = new List<string>();

                foreach (var file in Request.Form.Files)
                {
                    if (file != null && file.Length > 0)
                    {
                        var uniqueFileName = $"{Guid.NewGuid()}_{Path.GetFileName(file.FileName)}";
                        var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                        using (var stream = new FileStream(filePath, FileMode.Create))
                        {
                            await file.CopyToAsync(stream);
                        }

                        // Store relative path
                        filePaths.Add($"/uploads/{uniqueFileName}");
                    }
                }

                // Save as comma-separated string
                model.ProofOfPaymentPath = string.Join(",", filePaths);
                ModelState.Remove("ProofOfPaymentPath");
            }
            */

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

                    int currentCycle = 0;
                    int payoutTypeId = 0;

                    // Get cycle + payout type
                    var getCycleAndPayoutTypeQuery = @"SELECT Cycles, PayoutTypeID FROM Groups WHERE ID = @GroupID";
                    using (var cmd = new SqlCommand(getCycleAndPayoutTypeQuery, connection))
                    {
                        cmd.Parameters.AddWithValue("@GroupID", groupId);
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                currentCycle = !reader.IsDBNull(0) ? reader.GetInt32(0) : 0;
                                payoutTypeId = !reader.IsDBNull(1) ? reader.GetInt32(1) : 0;
                            }
                        }
                    }

                    if (payoutTypeId == 2)
                    {
                        // Bulk payout - only members with payment info
                        var memberGroupQuery = @"
                            SELECT mg.ID, m.AccountNumber, m.CVC, m.Expiry
                            FROM MemberGroups mg
                            INNER JOIN Members m ON mg.MemberID = m.ID
                            WHERE mg.GroupID = @GroupID
                            AND m.AccountNumber IS NOT NULL
                            AND m.CVC IS NOT NULL
                            AND m.Expiry IS NOT NULL";

                        var memberGroupList = new List<(int MemberGroupID, string AccountNumber, string CVC, string Expiry)>();

                        using (var cmd = new SqlCommand(memberGroupQuery, connection))
                        {
                            cmd.Parameters.AddWithValue("@GroupID", groupId);
                            using (var reader = await cmd.ExecuteReaderAsync())
                            {
                                while (await reader.ReadAsync())
                                {
                                    memberGroupList.Add((
                                        reader.GetInt32(0),      // MemberGroupID
                                        reader.GetString(1),     // AccountNumber
                                        reader.GetString(2),     // CVC
                                        reader.GetString(3)      // Expiry
                                    ));
                                }
                            }
                        }

                        if (memberGroupList.Count == 0)
                        {
                            ModelState.AddModelError("", "No members with payment info found for bulk payout.");
                            return View("~/Views/Transactions/PayoutsCreate.cshtml", model);
                        }

                        decimal perMemberAmount = model.Amount / memberGroupList.Count;

                        var bulkInsertQuery = @"
                            INSERT INTO Payouts 
                            (MemberGroupID, PaymentMethodID, Amount, TransactionDate, Reference, CreatedBy, PaidForCycle, AccountNumber, CVC, Expiry, Status)
                            VALUES 
                            (@MemberGroupID, @PaymentMethodID, @Amount, @PayoutDate, @Reference, @CreatedBy, @PaidForCycle, @AccountNumber, @CVC, @Expiry, @Status);";

                            foreach (var mg in memberGroupList)
                            {
                                // First check if a payout already exists for this MemberGroupID + Reference
                                var existsQuery = @"
                                    SELECT COUNT(1) 
                                    FROM Payouts 
                                    WHERE MemberGroupID = @MemberGroupID 
                                    AND Reference = @Reference";

                                using (var existsCmd = new SqlCommand(existsQuery, connection))
                                {
                                    existsCmd.Parameters.AddWithValue("@MemberGroupID", mg.MemberGroupID);
                                    existsCmd.Parameters.AddWithValue("@Reference", model.Reference ?? (object)DBNull.Value);

                                    int alreadyPaid = (int)await existsCmd.ExecuteScalarAsync();
                                    if (alreadyPaid > 0)
                                    {
                                        // Skip this member, already paid with the same reference
                                        continue;
                                    }
                                }

                                // Determine payout status
                                string bulkPayoutStatus = "Success"; // default status
                                if (!string.IsNullOrEmpty(mg.AccountNumber))
                                {
                                    if (mg.AccountNumber == "4111111111111111")
                                        bulkPayoutStatus = "Fail";
                                    else if (mg.AccountNumber == "4000000000009995")
                                        bulkPayoutStatus = "Pending";
                                }

                                // Insert payout record
                                using (var cmd = new SqlCommand(bulkInsertQuery, connection))
                                {
                                    cmd.Parameters.AddWithValue("@MemberGroupID", mg.MemberGroupID);
                                    cmd.Parameters.AddWithValue("@PaymentMethodID", model.PayoutTypeId);
                                    cmd.Parameters.AddWithValue("@Amount", perMemberAmount);
                                    cmd.Parameters.AddWithValue("@PayoutDate", model.PayoutDate);
                                    cmd.Parameters.AddWithValue("@Reference", model.Reference ?? (object)DBNull.Value);
                                    cmd.Parameters.AddWithValue("@CreatedBy", model.CreatedBy);
                                    cmd.Parameters.AddWithValue("@PaidForCycle", currentCycle);
                                    cmd.Parameters.AddWithValue("@AccountNumber", mg.AccountNumber);
                                    cmd.Parameters.AddWithValue("@CVC", mg.CVC);
                                    cmd.Parameters.AddWithValue("@Expiry", mg.Expiry);
                                    cmd.Parameters.AddWithValue("@Status", bulkPayoutStatus);

                                    await cmd.ExecuteNonQueryAsync();
                                }
                            }

                    }
                    else
                    {
                        // Single payout
                        var getMemberGroupQuery = @"SELECT ID FROM MemberGroups WHERE MemberID = @MemberID AND GroupID = @GroupID";
                        int memberGroupId;
                        using (var cmd = new SqlCommand(getMemberGroupQuery, connection))
                        {
                            cmd.Parameters.AddWithValue("@MemberID", model.MemberId);
                            cmd.Parameters.AddWithValue("@GroupID", groupId);

                            var result = await cmd.ExecuteScalarAsync();
                            if (result == null)
                            {
                                ModelState.AddModelError("", "Selected member is not part of this group.");
                                return View("~/Views/Transactions/PayoutsCreate.cshtml", model);
                            }

                            memberGroupId = Convert.ToInt32(result);
                        }

                        var insertQuery = @"INSERT INTO Payouts 
                            (MemberGroupID, PaymentMethodID, Amount, TransactionDate, Reference, CreatedBy, PaidForCycle, AccountNumber, CVC, Expiry, Status)
                            VALUES 
                            (@MemberGroupID, @PaymentMethodID, @Amount, @PayoutDate, @Reference, @CreatedBy, @PaidForCycle, @AccountNumber, @CVC, @Expiry, @Status);";

                        using (var command = new SqlCommand(insertQuery, connection))
                        {
                            command.Parameters.AddWithValue("@MemberGroupID", memberGroupId);
                            command.Parameters.AddWithValue("@PaymentMethodID", model.PayoutTypeId);
                            command.Parameters.AddWithValue("@Amount", model.Amount);
                            command.Parameters.AddWithValue("@PayoutDate", model.PayoutDate);
                            command.Parameters.AddWithValue("@Reference", model.Reference ?? (object)DBNull.Value);
                            // command.Parameters.AddWithValue("@ProofOfPaymentPath", DBNull.Value); // removed
                            command.Parameters.AddWithValue("@CreatedBy", model.CreatedBy);
                            command.Parameters.AddWithValue("@PaidForCycle", currentCycle);
                            command.Parameters.AddWithValue("@AccountNumber", model.AccountNumber ?? (object)DBNull.Value);
                            command.Parameters.AddWithValue("@CVC", model.CVC ?? (object)DBNull.Value);
                            command.Parameters.AddWithValue("@Expiry", model.Expiry ?? (object)DBNull.Value);
                            command.Parameters.AddWithValue("@Status", status);

                            await command.ExecuteNonQueryAsync();
                        }
                    }

                    // Cycle completion check
                    var totalMembersQuery = @"SELECT COUNT(*) FROM MemberGroups WHERE GroupID = @GroupID";
                    var totalPayoutsQuery = @"SELECT COUNT(*) 
                                            FROM Payouts P
                                            INNER JOIN MemberGroups MG ON MG.ID = P.MemberGroupID
                                            WHERE MG.GroupID = @GroupID AND P.PaidForCycle = @Cycle";

                    int totalMembers, totalPayouts;

                    using (var cmd = new SqlCommand(totalMembersQuery, connection))
                    {
                        cmd.Parameters.AddWithValue("@GroupID", groupId);
                        totalMembers = (int)await cmd.ExecuteScalarAsync();
                    }

                    using (var cmd = new SqlCommand(totalPayoutsQuery, connection))
                    {
                        cmd.Parameters.AddWithValue("@GroupID", groupId);
                        cmd.Parameters.AddWithValue("@Cycle", currentCycle);
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
                _logger.LogError(ex,
                    "Error saving payout. Exception: {Message}, StackTrace: {StackTrace}, InnerException: {InnerException}, Model: {@Model}",
                    ex.Message,
                    ex.StackTrace,
                    ex.InnerException?.Message,
                    model
                );

                ModelState.AddModelError("", "An unexpected error occurred while saving the payout. Please check logs for more details.");
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
                                        p.AccountNumber,
                                        p.CVC,
                                        p.Expiry,
                                        p.Status,
                                        g.GroupName,
                                        c.Currency,                      
                                        CONCAT(m.FirstName, ' ', m.LastName) AS MemberName,
                                        m.Phone, 
                                        m.Email,
                                        CONCAT(mcreator.FirstName, ' ', mcreator.LastName) AS CreatedBy
                                    FROM dbo.Payouts p
                                    JOIN dbo.MemberGroups mg ON mg.ID = p.MemberGroupID
                                    JOIN dbo.Groups g ON mg.GroupID = g.ID
                                    JOIN dbo.Currencies c ON g.CurrencyID = c.ID   -- ✅ Join here
                                    JOIN dbo.Members m ON m.ID = mg.MemberID
                                    LEFT JOIN dbo.Members mcreator ON mcreator.ID = TRY_CAST(p.CreatedBy AS INT)
                                    WHERE mg.GroupID = @GroupId
                                    ORDER BY p.CreatedAt DESC;
                                    ";

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
                                    //ProofOfPaymentPath = reader["ProofOfPaymentPath"]?.ToString(),
                                    CreatedBy = reader["CreatedBy"]?.ToString(),
                                    GroupName = reader["GroupName"].ToString(),
                                    MemberName = reader["MemberName"].ToString(),
                                    Phone = reader["Phone"].ToString(),
                                    Email = reader["Email"].ToString(),
                                    Currency = reader["Currency"].ToString(),
                                    AccountNumber = reader["AccountNumber"]?.ToString(),
                                    CVC = reader["CVC"]?.ToString(),
                                    Expiry = reader["Expiry"]?.ToString(),
                                    Status = reader["Status"].ToString()

                                });
                            }
                        }
                    }

                    bool isMemberNotAdmin = true;
                    int memberId = int.Parse(User.FindFirst("member_id")?.Value ?? "0"); // adjust based on auth
                    if (memberId > 0)
                    {
                        using (var cmd = new SqlCommand(@"
                        SELECT RoleID FROM MemberGroups 
                        WHERE MemberID = @memberId AND GroupID = @groupId", connection))
                        {
                            cmd.Parameters.AddWithValue("@memberId", memberId);
                            cmd.Parameters.AddWithValue("@groupId", groupId);
                            var roleIdObj = cmd.ExecuteScalar();

                            if (roleIdObj != null && int.TryParse(roleIdObj.ToString(), out int roleId))
                            {
                                _logger.LogInformation("MemberID: {MemberID}, GroupID: {GroupID}, RoleID: {RoleID}", memberId, groupId, roleId);
                                isMemberNotAdmin = (roleId == 2);
                            }
                            else
                            {
                                _logger.LogWarning("Could not determine RoleID for MemberID: {MemberID}, GroupID: {GroupID}", memberId, groupId);
                            }
                        }
                    }


                    // 6️⃣ Get account status
                    using var statusCmd = new SqlCommand(@"
                        SELECT m.Status
                        FROM Members m
                        JOIN Logins l ON l.MemberID = m.ID
                        WHERE l.ID = @UserId", connection);
                    statusCmd.Parameters.AddWithValue("@UserId", memberId);
                    var reader2 = statusCmd.ExecuteReader();
                    if (reader2.Read())
                    {
                        int status = reader2["Status"] != DBNull.Value ? Convert.ToInt32(reader2["Status"]) : 1;
                        ViewBag.AccountPaused = (status == 0);
                        ViewBag.AccountDeactivated = (status == 3);

                    }
                    reader2.Close();

                    // 7️⃣ Get group closed status
                    using (var closedCmd = new SqlCommand("SELECT Closed FROM Groups WHERE ID = @GroupId", connection))
                    {
                        closedCmd.Parameters.AddWithValue("@GroupId", groupId);
                        var closedResult = closedCmd.ExecuteScalar();
                        bool groupClosed = (closedResult != null && closedResult != DBNull.Value) && Convert.ToBoolean(closedResult);

                        ViewBag.GroupClosed = groupClosed;
                        _logger.LogInformation("Group {GroupId} closed status: {GroupClosed}", groupId, groupClosed);
                    }

                    ViewBag.IsMemberNotAdmin = isMemberNotAdmin;
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