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
            //[Authorize(Roles = "Admin")]
            // [ValidateAntiForgeryToken]
            public IActionResult ContributionsCreate(Contribution model, int groupId)
            {
                _logger.LogInformation($"The endpoint has been hit {groupId}");

                var memberIdClaim = User.Claims.FirstOrDefault(c => c.Type == "member_id");
                if (memberIdClaim != null && int.TryParse(memberIdClaim.Value, out var memberId))
                {
                    model.CreatedBy = memberId.ToString();
                    ModelState.Remove("CreatedBy"); // ✅ Clear error manually since we set it ourselves
                }
                else
                {
                    _logger.LogError("MemberID not found in JWT claims");
                    ModelState.AddModelError("CreatedBy", "Unable to determine member identity.");
                }

                var members = new List<SelectListItem>();
                using (var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
                {
                    var memberQuery = @"
                        SELECT 
                        mg.ID, 
                        CONCAT(m.FirstName, ' ', m.LastName) AS FullName,
                        m.Email,
                        m.Phone
                        FROM MemberGroups mg
                        JOIN Members m ON mg.MemberID = m.ID
                        WHERE mg.GroupID = @GroupId
                        ";

                    using (var command = new SqlCommand(memberQuery, connection))
                    {
                        command.Parameters.AddWithValue("@GroupId", groupId);
                        connection.Open();
                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                members.Add(new SelectListItem
                                {
                                    Value = reader["ID"].ToString(),
                                    Text = reader["FullName"].ToString()
                                });
                            }
                        }
                    }
                }

                model.MemberOptions = members
                .Select(m => new MemberOption
                {
                    Id = int.Parse(m.Value),
                    FullName = m.Text
                })
                .ToList();

                model.PaymentMethods = GetPaymentMethodsFromDatabase();

                try
                {
                    // Calculate total amount
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
                            _logger.LogInformation($"ProofOfPaymentPath: {model.ProofOfPaymentPath}");
                            ModelState.Remove("ProofOfPaymentPath"); // ✅ Clear error manually since we set it ourselves
                        }
                        else{
                            _logger.LogInformation($"File does not exist");
                        }
                    }
                    else{
                            _logger.LogInformation($"No uploaded files");
                        }

                    model.GroupId = groupId;

                    if (!ModelState.IsValid)
                        {
                            foreach (var state in ModelState)
                                {
                                    foreach (var error in state.Value.Errors)
                                    {
                                        _logger.LogError($"Field: {state.Key} - Error: {error.ErrorMessage}");
                                    }
                                }

                            model.GroupId = groupId;

                            // Optional: Reload member list and payment methods if needed again

                            model.PaymentMethods = GetPaymentMethodsFromDatabase();

                            _logger.LogInformation("Model not valid");
                            return View("~/Views/Transactions/ContributionsCreate.cshtml", model);
                        }

                    // Insert into DB
                    using (var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
                    {
                       var query = @"
                        INSERT INTO Contributions 
                            (MemberGroupID, PaymentMethodID, PenaltyAmount, ContributionAmount, 
                            TotalAmount, TransactionDate, Reference, ProofOfPaymentPath, CreatedBy, PaidForCycle)
                        SELECT 
                            @MemberGroupID, @PaymentMethodID, @PenaltyAmount, @ContributionAmount, 
                            @TotalAmount, @TransactionDate, @Reference, @ProofOfPaymentPath, @CreatedBy,
                            g.Cycles  -- This sets PaidForCycle from Groups table
                        FROM MemberGroups mg
                        JOIN Groups g ON mg.GroupID = g.ID
                        WHERE mg.ID = @MemberGroupID;

                        SELECT SCOPE_IDENTITY();";


                        using (var command = new SqlCommand(query, connection))
                        {
                            command.Parameters.AddWithValue("@MemberGroupID", model.MemberGroupID);
                            command.Parameters.AddWithValue("@PaymentMethodID", model.PaymentMethodID);
                            command.Parameters.AddWithValue("@PenaltyAmount", model.PenaltyAmount);
                            command.Parameters.AddWithValue("@ContributionAmount", model.ContributionAmount);
                            command.Parameters.AddWithValue("@TotalAmount", model.TotalAmount);
                            command.Parameters.AddWithValue("@TransactionDate", model.TransactionDate);
                            command.Parameters.AddWithValue("@Reference", model.Reference ?? (object)DBNull.Value);
                            command.Parameters.AddWithValue("@ProofOfPaymentPath", 
                                string.IsNullOrEmpty(model.ProofOfPaymentPath) ? 
                                DBNull.Value : (object)model.ProofOfPaymentPath);
                            command.Parameters.AddWithValue("@CreatedBy", model.CreatedBy);

                            connection.Open();
                            command.ExecuteScalar(); // Save and get inserted ID (not used here)
                        }
                    }

                    model.GroupId = groupId;
                    _logger.LogInformation($"Group Id being passdown: {model.GroupId} vs {groupId}");
                    TempData["SuccessMessage"] = "Transaction recorded successfully!";
                    return RedirectToAction("ContributionsIndex", new { groupId = model.GroupId });
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", $"Error saving transaction: {ex.Message}");
                    model.GroupId = groupId;

                    // Optional: Reload member list and payment methods if needed again
                    model.PaymentMethods = GetPaymentMethodsFromDatabase();
                    return View("~/Views/Transactions/ContributionsCreate.cshtml", model);
                }
            }



            [AllowAnonymous]
            public IActionResult ContributionsIndex(int groupId)
            {
                var contributions = new List<Contribution>();
                _logger.LogInformation($"Group Id being passdown now: {groupId}");
                using (var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
                {
                    var query = @"
                    SELECT 
                    c.ID, 
                    c.PaymentMethodID, 
                    c.PenaltyAmount, 
                    c.ContributionAmount, 
                    c.TotalAmount, 
                    c.TransactionDate, 
                    c.Reference, 
                    c.ProofOfPaymentPath,

                    g.GroupName AS GroupName,

                    CONCAT(m.FirstName, ' ', m.LastName) AS MemberName,
                    m.Phone, 
                    m.Email,
                 
                    CONCAT(mcreator.FirstName, ' ', mcreator.LastName) AS CreatedBy

                FROM dbo.Contributions c

                JOIN dbo.MemberGroups mg ON mg.ID = c.MemberGroupID

                JOIN dbo.Groups g ON mg.GroupID = g.ID

                JOIN dbo.Members m ON m.ID = mg.MemberID

                LEFT JOIN dbo.Members mcreator ON mcreator.ID = TRY_CAST(c.CreatedBy AS INT)

                WHERE mg.GroupID = @groupID
                ORDER BY c.TransactionDate DESC;
                        ";



                    using (var command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@GroupId", groupId);
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
                                    Phone = reader["Phone"].ToString(), // ✅
                                    Email = reader["Email"].ToString()
                                });
                            }
                        }
                    }
                }

            //model.GroupId = groupId;
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
                    ContributionAmount = 0,
                    TotalAmount = 0,
                    MemberOptions = new List<MemberOption>()
                };

                // Load payment methods
                model.PaymentMethods = GetPaymentMethodsFromDatabase();

                using (var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
                {
                    connection.Open();

                    // STEP 1: Get Group Info with Frequency details
                    string groupQuery = @"
                        SELECT g.GroupName AS GroupName, g.StartDate, g.Cycles, g.Penalty, f.FrequencyName
                        FROM Groups g
                        JOIN Frequencies f ON g.FrequencyID = f.ID
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
                                DateTime startDate = Convert.ToDateTime(reader["StartDate"] != DBNull.Value ? (DateTime?)Convert.ToDateTime(reader["StartDate"]) : null);
                                int cycles = reader["Cycles"] != DBNull.Value ? Convert.ToInt32(reader["Cycles"]) : 0; // or some default
                                string frequency = reader["FrequencyName"].ToString().ToLower();
                                decimal groupPenalty = 0;

                                if (reader["Penalty"] != DBNull.Value)
                                    groupPenalty = Convert.ToDecimal(reader["Penalty"]);

                                _logger.LogInformation("Frequency: {Frequency}", frequency);

                                // Frequency to days
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

                                // ✅ Apply penalty if due date passed
                                if (DateTime.Now > dueDate)
                                {
                                    model.PenaltyAmount = groupPenalty;
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
                string memberQuery = @"
                        SELECT mg.ID, CONCAT(m.FirstName, ' ', m.LastName) AS FullName, m.Email, m.Phone
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
                                    FullName = reader["FullName"].ToString(),
                                    Email = reader["Email"].ToString(),
                                    Phone = reader["Phone"].ToString()
                                });
                            }
                        }
                    }
                }

                return View("~/Views/Transactions/ContributionsCreate.cshtml", model);
            }



    }
}