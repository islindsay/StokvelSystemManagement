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
using System.Text.RegularExpressions;

namespace StokvelManagementSystem.Controllers
{
    [Authorize]
    public class ContributionsController : Controller
    { 
        private readonly IConfiguration _configuration;
        private readonly ILogger<ContributionsController> _logger; // ✅ Add this

       public ContributionsController(IConfiguration configuration, ILogger<ContributionsController> logger)
        {
            _configuration = configuration;
            _logger = logger;
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
    
    _logger.LogInformation($"The endpoint has been hit {memberId}");

    using (var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
            {
                var query = @"
            SELECT 
                g.GroupName AS GroupName, 
                g.ContributionAmount AS GroupContributionAmount, 
                g.MemberLimit,
                DATEADD(DAY, gs.PenaltyGraceDays, DATEADD(MONTH, DATEDIFF(MONTH, 0, GETDATE()), 0)) AS DueDate
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

                            var memberLimit = Convert.ToInt32(reader["MemberLimit"]);

                            // Compute ContributionAmount
                            if (memberLimit > 0)
                            {
                                groupDetails.ContributionAmount =
                                    groupDetails.GroupContributionAmount / memberLimit;
                                _logger.LogInformation($"the contribution Amount is {groupDetails.ContributionAmount}");
                            }
                            else
                            {
                                _logger.LogInformation($"Member limit is 0 or less");
                            }
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
        //[Authorize(Roles = "Admin")]
        // [ValidateAntiForgeryToken]
        public IActionResult ContributionsCreate(Contribution model, int groupId)
        {
            _logger.LogInformation($"Creating contribution for group ID: {groupId}");  

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

            var memberIdClaim = User.Claims.FirstOrDefault(c => c.Type == "member_id");
            if (memberIdClaim != null && int.TryParse(memberIdClaim.Value, out var memberId))
            {
                model.CreatedBy = memberId.ToString();
                ModelState.Remove("CreatedBy");
            }
            else
            {
                _logger.LogError("MemberID not found in JWT claims");
                ModelState.AddModelError("CreatedBy", "Unable to determine member identity.");
            }

            model.PaymentMethods = GetPaymentMethodsFromDatabase();

            try
            {
                model.TotalAmount = model.ContributionAmount + model.PenaltyAmount;
                model.GroupId = groupId;

                // ✅ Validation for card inputs
                if (string.IsNullOrWhiteSpace(model.CVC) || !Regex.IsMatch(model.CVC, @"^\d{3,4}$"))
                {
                    ModelState.AddModelError("CVC", "CVC must be 3 or 4 digits.");
                }

                if (string.IsNullOrWhiteSpace(model.Expiry) || !Regex.IsMatch(model.Expiry, @"^(0[1-9]|1[0-2])\/\d{2}$"))
                {
                    ModelState.AddModelError("Expiry", "Expiry date must be in MM/YY format.");
                }
                else
                {
                    var parts = model.Expiry.Split('/');
                    var month = int.Parse(parts[0]);
                    var year = 2000 + int.Parse(parts[1]);
                    var expiry = new DateTime(year, month, DateTime.DaysInMonth(year, month));
                    if (expiry < DateTime.Now.Date)
                        ModelState.AddModelError("Expiry", "Expiry date cannot be in the past.");
                }

                if (!ModelState.IsValid)
                {
                    _logger.LogInformation("Model not valid. Listing all validation errors:");
                    foreach (var state in ModelState)
                    {
                        foreach (var error in state.Value.Errors)
                        {
                            _logger.LogError("Field: {Field}, Error: {ErrorMessage}", state.Key, error.ErrorMessage);
                        }
                    }
                    return View("~/Views/Transactions/ContributionsCreate.cshtml", model);
                }

                using (var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
                {
                    connection.Open();

                    // ✅ Step 1: Ensure Cycles is not null
                    var updateCyclesQuery = @"
                        UPDATE g
                        SET g.Cycles = 0
                        FROM Groups g
                        JOIN MemberGroups mg ON g.ID = mg.GroupID
                        WHERE mg.ID = @MemberGroupID AND g.Cycles IS NULL;
                    ";

                    using (var updateCmd = new SqlCommand(updateCyclesQuery, connection))
                    {
                        updateCmd.Parameters.AddWithValue("@MemberGroupID", model.MemberGroupID);
                        updateCmd.ExecuteNonQuery();
                    }

                    // ✅ Step 2: Insert the contribution
                    var insertQuery = @"
                        INSERT INTO Contributions 
                            (MemberGroupID, PaymentMethodID, PenaltyAmount, ContributionAmount, 
                            TotalAmount, TransactionDate, AccountNumber, CVC, Expiry, CreatedBy, PaidForCycle, Status)
                        SELECT 
                            @MemberGroupID, 
                            @PaymentMethodID, 
                            @PenaltyAmount, 
                            @ContributionAmount, 
                            @TotalAmount, 
                            @TransactionDate, 
                            @AccountNumber, 
                            @CVC, 
                            @Expiry, 
                            @CreatedBy,
                            ISNULL(g.Cycles, 0),   -- ✅ always safe
                            @Status
                        FROM MemberGroups mg
                        JOIN Groups g ON mg.GroupID = g.ID
                        WHERE mg.ID = @MemberGroupID;
                    ";

                    using (var insertCmd = new SqlCommand(insertQuery, connection))
                    {
                        insertCmd.Parameters.AddWithValue("@MemberGroupID", model.MemberGroupID);
                        insertCmd.Parameters.AddWithValue("@PaymentMethodID", model.PaymentMethodID);
                        insertCmd.Parameters.AddWithValue("@PenaltyAmount", model.PenaltyAmount);
                        insertCmd.Parameters.AddWithValue("@ContributionAmount", model.ContributionAmount);
                        insertCmd.Parameters.AddWithValue("@TotalAmount", model.TotalAmount);
                        insertCmd.Parameters.AddWithValue("@TransactionDate", model.TransactionDate);
                        insertCmd.Parameters.AddWithValue("@AccountNumber", model.AccountNumber ?? (object)DBNull.Value);
                        insertCmd.Parameters.AddWithValue("@CVC", model.CVC ?? (object)DBNull.Value);
                        insertCmd.Parameters.AddWithValue("@Expiry", model.Expiry ?? (object)DBNull.Value);
                        insertCmd.Parameters.AddWithValue("@CreatedBy", model.CreatedBy);
                        insertCmd.Parameters.AddWithValue("@Status", status);

                        int rowsAffected = insertCmd.ExecuteNonQuery();
                        if (rowsAffected == 0)
                            throw new Exception("Insert failed: No rows affected.");
                    }
                }

                TempData["SuccessMessage"] = "Transaction recorded successfully!";
                return RedirectToAction("ContributionsIndex", new { groupId = model.GroupId });
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"Error saving transaction: {ex.Message}");
                model.PaymentMethods = GetPaymentMethodsFromDatabase();
                return View("~/Views/Transactions/ContributionsCreate.cshtml", model);
            }
        }


        [AllowAnonymous]
        public IActionResult ContributionsIndex(int groupId)
        {
            var contributions = new List<Contribution>();
            int currentCycle = 0;
            bool hasContributedThisCycle = false;

            _logger.LogInformation($"Group Id being passed down now: {groupId}");

            using (var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
            {
                connection.Open();

                // 1️⃣ Get the current Cycle from Groups table
                using (var cycleCmd = new SqlCommand("SELECT ISNULL(Cycles, 0) FROM Groups WHERE ID = @GroupId", connection))
                {
                    cycleCmd.Parameters.AddWithValue("@GroupId", groupId);
                    currentCycle = (int)cycleCmd.ExecuteScalar();
                }

                int memberId2 = int.Parse(User.FindFirst("member_id")?.Value ?? "0");
                // 2️⃣ Check if PaidForCycle = currentCycle for any member
                using (var checkCmd = new SqlCommand(@"
                    SELECT CASE WHEN COUNT(*) > 0 THEN 1 ELSE 0 END
                    FROM Contributions c
                    JOIN MemberGroups mg ON mg.ID = c.MemberGroupID
                    WHERE mg.GroupID = @GroupId 
                    AND mg.MemberID = @MemberId
                    AND c.PaidForCycle = @CurrentCycle", connection))
                {
                    checkCmd.Parameters.AddWithValue("@GroupId", groupId);
                    checkCmd.Parameters.AddWithValue("@MemberId", memberId2);
                    checkCmd.Parameters.AddWithValue("@CurrentCycle", currentCycle);

                    hasContributedThisCycle = (int)checkCmd.ExecuteScalar() == 1;
                }


                // 3️⃣ Load contributions
                var query = @"
                    SELECT 
                        c.ID, 
                        c.PaymentMethodID, 
                        c.PenaltyAmount, 
                        c.ContributionAmount, 
                        c.TotalAmount, 
                        c.TransactionDate, 
                        c.ProofOfPaymentPath,
                        c.AccountNumber,
                        c.PaidForCycle,
                        c.CVC,
                        c.Expiry,
                        c.Status,
                        g.GroupName AS GroupName,
                        CONCAT(m.FirstName, ' ', m.LastName) AS MemberName,
                        m.Phone, 
                        m.Email,
                        CONCAT(mcreator.FirstName, ' ', mcreator.LastName) AS CreatedBy,
                        cur.Currency AS Currency
                    FROM dbo.Contributions c
                    JOIN dbo.MemberGroups mg ON mg.ID = c.MemberGroupID
                    JOIN dbo.Groups g ON mg.GroupID = g.ID
                    JOIN dbo.Members m ON m.ID = mg.MemberID
                    LEFT JOIN dbo.Members mcreator ON mcreator.ID = TRY_CAST(c.CreatedBy AS INT)
                    JOIN dbo.Currencies cur ON cur.ID = g.CurrencyID
                    WHERE mg.GroupID = @groupID
                    ORDER BY c.TransactionDate DESC;
                ";

                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@GroupId", groupId);
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
                                CreatedBy = reader["CreatedBy"]?.ToString(),
                                GroupName = reader["GroupName"].ToString(),
                                FirstName = reader["MemberName"].ToString(),
                                Phone = reader["Phone"].ToString(),
                                Email = reader["Email"].ToString(),
                                AccountNumber = reader["AccountNumber"]?.ToString(),
                                CVC = reader["CVC"]?.ToString(),
                                Expiry = reader["Expiry"]?.ToString(),
                                CurrencySymbol = reader["Currency"]?.ToString(),
                                Status = reader["Status"]?.ToString(),
                                PaidForCycle = reader["PaidForCycle"]?.ToString(),
                            });
                        }
                    }
                }

                // 4️⃣ Get Group Info (includes Closed)
                bool groupClosed = false;
                using (var cmd = new SqlCommand(
                    @"SELECT Closed FROM Groups WHERE ID = @groupId", connection))
                {
                    cmd.Parameters.AddWithValue("@groupId", groupId);
                    var closedObj = cmd.ExecuteScalar();
                    if (closedObj != null && bool.TryParse(closedObj.ToString(), out bool closed))
                    {
                        groupClosed = closed;
                    }
                }

                // 5️⃣ Determine if current user is admin (RoleID)
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

                    // 6️⃣ Get account status
                    using var statusCmd = new SqlCommand(@"
                        SELECT m.Status
                        FROM Members m
                        JOIN Logins l ON l.MemberID = m.ID
                        WHERE l.ID = @UserId", connection);
                    statusCmd.Parameters.AddWithValue("@UserId", memberId);
                    var reader = statusCmd.ExecuteReader();
                    if (reader.Read())
                    {
                        int status = reader["Status"] != DBNull.Value ? Convert.ToInt32(reader["Status"]) : 1;
                        ViewBag.AccountPaused = (status == 0);
                        ViewBag.AccountDeactivated = (status == 3);

                    }
                    reader.Close();
                }

                // 7️⃣ Pass values to ViewBag
                ViewBag.CurrentCycle = currentCycle;
                ViewBag.HasContributedThisCycle = hasContributedThisCycle;
                ViewBag.IsMemberNotAdmin = isMemberNotAdmin;
                ViewBag.GroupClosed = groupClosed;

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

            [HttpGet]
            [Authorize(Roles = "Admin")]
            public IActionResult CreateContributionForm(int groupId)
            {
                var model = new Contribution
                {
                    TransactionDate = DateTime.Now,
                    PaymentMethodID = 1,
                    PenaltyAmount = 0,
                    GroupId = groupId,
                    ContributionAmount = 0,
                    TotalAmount = 0,
                    MemberOptions = new List<MemberOption>()
                };

                // Load payment methods
                model.PaymentMethods = GetPaymentMethodsFromDatabase();

                using (var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
                {
                    connection.Open();

                    // STEP 1: Get Group Info with Frequency details + contribution info
                    string groupQuery = @"
                        SELECT 
                            g.GroupName AS GroupName, 
                            g.StartDate, 
                            g.Cycles, 
                            g.Penalty, 
                            g.ContributionAmount AS GroupContributionAmount,
                            g.MemberLimit,
                            c.Currency, 
                            f.FrequencyName
                        FROM Groups g
                        JOIN Frequencies f ON g.FrequencyID = f.ID
                        JOIN Currencies c ON g.CurrencyID = c.ID
                        WHERE g.ID = @GroupId";


                    string groupName = "";
                    DateTime dueDate = DateTime.Now;

                    using (var command = new SqlCommand(groupQuery, connection))
                    {
                        command.Parameters.AddWithValue("@GroupId", groupId);
                        using (var reader = command.ExecuteReader())
                        {
                          if (reader.Read())
                                {
                                    groupName = reader["GroupName"].ToString();
                                    DateTime startDate = reader["StartDate"] != DBNull.Value
                                        ? Convert.ToDateTime(reader["StartDate"])
                                        : DateTime.Now;

                                    int cycles = reader["Cycles"] != DBNull.Value ? Convert.ToInt32(reader["Cycles"]) : 0;
                                    model.CurrencySymbol = reader["Currency"].ToString(); 
                                    string frequency = reader["FrequencyName"].ToString().ToLower();
                                    decimal groupPenalty = reader["Penalty"] != DBNull.Value ? Convert.ToDecimal(reader["Penalty"]) : 0;

                                    // ✅ new: contribution + memberLimit
                                    decimal groupContributionAmount = reader["GroupContributionAmount"] != DBNull.Value ? Convert.ToDecimal(reader["GroupContributionAmount"]) : 0;
                                    int memberLimit = reader["MemberLimit"] != DBNull.Value ? Convert.ToInt32(reader["MemberLimit"]) : 0;

                                    // frequency → days logic (same as before)
                                    int frequencyDays = frequency switch
                                    {
                                        "weekly" => 7,
                                        "monthly" => 30,
                                        "daily" => 1,
                                        "annually" => 365,
                                        _ => 0
                                    };

                                    int totalDaysToAdd = (cycles * frequencyDays) + frequencyDays;
                                    dueDate = startDate.AddDays(totalDaysToAdd);

                                    if (DateTime.Now > dueDate)
                                    {
                                        model.PenaltyAmount = groupPenalty;
                                    }

                                    // ✅ pre-fill contribution amount
                                    if (memberLimit > 0)
                                    {
                                        decimal rawAmount = groupContributionAmount ;

                                        // Round up to the nearest 0.10
                                        model.ContributionAmount = Math.Ceiling(rawAmount * 10) / 10;
                                        model.TotalAmount = model.ContributionAmount + model.PenaltyAmount;

                                        _logger.LogInformation($"[CreateContributionForm] Pre-filled ContributionAmount = {model.ContributionAmount}");
                                    }
                                }

                        }
                    }

                    // Save values to model
                    model.GroupName = groupName;
                    model.DueDate = dueDate;
                    
                            // Step 2: Run your balance and payout date query
                    string balanceAndPayoutDateQuery = @"
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

                    // Step 3: Run enable payout query
                    string enablePayoutQuery = @"
                        SELECT 
                            CASE 
                                WHEN ISNULL(SUM(c.ContributionAmount), 0) = g.ContributionAmount * COUNT(DISTINCT mg.ID) THEN 1
                                ELSE 0
                            END AS EnablePayout,
                            COUNT(DISTINCT mg.ID) AS MemberCount,
                            g.ContributionAmount * COUNT(DISTINCT mg.ID) AS ExpectedPayment,
                            g.FrequencyID,
                            g.PayoutTypeID
                        FROM MemberGroups mg
                        JOIN Groups g ON mg.GroupID = g.ID
                        LEFT JOIN Contributions c ON c.MemberGroupID = mg.ID
                        WHERE mg.GroupID = @GroupId
                        GROUP BY g.ContributionAmount, g.FrequencyID, g.PayoutTypeID;
                    ";

                    using (var command = new SqlCommand(enablePayoutQuery, connection))
                    {
                        command.Parameters.AddWithValue("@GroupId", groupId);
                        using (var reader = command.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                model.EnablePayout = reader["EnablePayout"] != DBNull.Value ? Convert.ToInt32(reader["EnablePayout"]) == 1 : false;
                                model.MemberCount = reader["MemberCount"] != DBNull.Value ? Convert.ToInt32(reader["MemberCount"]) : 0;
                                model.ExpectedPayment = reader["ExpectedPayment"] != DBNull.Value ? Convert.ToDecimal(reader["ExpectedPayment"]) : 0;
                                model.FrequencyID = reader["FrequencyID"] != DBNull.Value ? Convert.ToInt32(reader["FrequencyID"]) : 0;
                                model.PayoutTypeID = reader["PayoutTypeID"] != DBNull.Value ? Convert.ToInt32(reader["PayoutTypeID"]) : 0;
                            }
                        }
                    }



                // STEP 5: Load member options
                var memberIdClaim = User.Claims.FirstOrDefault(c => c.Type == "member_id");
                int memberId = memberIdClaim != null ? Convert.ToInt32(memberIdClaim.Value) : 0;

                    string memberQuery = @"
                        SELECT mg.ID, CONCAT(m.FirstName, ' ', m.LastName) AS FullName, m.Email, m.Phone, m.AccountNumber, m.CVC, m.Expiry
                        FROM MemberGroups mg
                        JOIN Members m ON mg.MemberID = m.ID
                        WHERE mg.GroupID = @GroupId AND m.ID = @MemberId";

                   using (var command = new SqlCommand(memberQuery, connection))
                    {
                        command.Parameters.AddWithValue("@GroupId", groupId);
                        command.Parameters.AddWithValue("@MemberId", memberId);

                        using (var reader = command.ExecuteReader())
                        {
                           if (reader.Read())
                                {
                                    model.MemberGroupID = Convert.ToInt32(reader["ID"]);
                                    model.FullName = reader["FullName"].ToString();
                                    model.Email = reader["Email"].ToString();
                                    model.Phone = reader["Phone"].ToString();
                                    model.AccountNumber = reader["AccountNumber"].ToString();
                                    model.CVC = reader["CVC"].ToString();
                                    model.Expiry = reader["Expiry"].ToString();


                                    _logger.LogInformation("Found MemberGroupID: {MemberGroupID}", model.MemberGroupID);
                                }
                                else
                                {
                                    _logger.LogWarning("No MemberGroup found for MemberId {MemberId} in GroupId {GroupId}", memberId, groupId);
                                    throw new Exception("No valid MemberGroup found — cannot create contribution.");
                                }

                        }
                    }

                }

                return View("~/Views/Transactions/ContributionsCreate.cshtml", model);
            }



    }
}