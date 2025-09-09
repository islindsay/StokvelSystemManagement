using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.IdentityModel.Tokens;
using StokvelManagementSystem.Models;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using System.Text;

namespace StokvelManagementSystem.Controllers
{
    [Authorize]
    public class GroupsController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<GroupsController> _logger;

        public GroupsController(IConfiguration configuration, ILogger<GroupsController> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }
        [HttpGet]
        public IActionResult ListGroups(bool created = false, bool showCreate = false, bool showNewGroups = false, bool showNewTab = false)
        {

            var nationalId = User.Claims.FirstOrDefault(c => c.Type == "national_id")?.Value;
            var memberIdStr = User.Claims.FirstOrDefault(c => c.Type == "member_id")?.Value;

            int memberId = int.TryParse(memberIdStr, out var id) ? id : 0;

            var viewModel = new Group
            {
                CreatedDate = DateTime.Now,
                PayoutTypes = GetPayoutTypes(),
                Currencies = GetCurrencies(),
                FrequencyOptions = GetFrequencyOptions(),
                MemberId = memberId,
                GroupCreated = created,
                CanCreate = showCreate,
                SearchNationalId = nationalId,
                ShowNewGroups = showNewGroups
            };
            ViewData["ShowNewTab"] = showNewTab;
            viewModel.MyGroups = !string.IsNullOrEmpty(nationalId)
                ? GetMyGroupsByNationalId(nationalId)
                : GetMyGroups(memberId);

            if (showNewGroups && !string.IsNullOrEmpty(nationalId))
            {
                var myGroups = GetMyGroupsByNationalId(nationalId);
                var allGroups = GetNewGroupsForMember(memberId);

                viewModel.NewGroups = allGroups
                .Where(g => !myGroups.Any(mg => mg.ID == g.ID))
                .ToList();
            }
            else
            {
                viewModel.NewGroups = new List<Group>();
            }

            return View(viewModel);
        }



        [HttpGet]
        public IActionResult GetNewGroups()
        {
            var userIdClaim = User.FindFirst("user_id");

            if (userIdClaim == null)
                return Unauthorized();

            int userId = int.Parse(userIdClaim.Value);

            int memberId;
            using (var conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
            {
                conn.Open();

                // You can use either "Users" or "Members" depending on your schema.
                using (var cmd = new SqlCommand(
                  "SELECT ID FROM Members WHERE UserID = @UserId", conn)) // Assumes userId is also used as MemberID
                {
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    var result = cmd.ExecuteScalar();

                    if (result == null)
                    {
                        _logger.LogWarning("No MemberID found for userId: {UserId}", userId);
                        return NotFound("User not associated with any MemberID.");
                    }

                    memberId = Convert.ToInt32(result);
                }
            }

            _logger.LogInformation("Fetching new groups for memberId: {MemberId}", memberId);
            var newerGroups = GetNewGroupsForMember(memberId);

            return Json(newerGroups);
        }



        private List<Group> GetMyGroups(int memberId)
        {
            var myGroups = new List<Group>();

            using (var conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
            {
                string query = @"
            SELECT 
                g.*, 
                pt.PayoutType AS PayoutType, 
                f.FrequencyName, 
                gs.PenaltyAmount, 
                gs.PenaltyGraceDays, 
                gs.AllowDeferrals, 
                c.Currency,
                mg.RoleID
            FROM Groups g
            JOIN MemberGroups mg ON g.ID = mg.GroupID
            JOIN PayoutTypes pt ON g.PayoutTypeID = pt.ID
            JOIN Frequencies f ON g.FrequencyID = f.ID
            LEFT JOIN GroupSettings gs ON g.ID = gs.GroupID
            JOIN Currencies c ON g.CurrencyID = c.ID
            WHERE mg.MemberID = @MemberID
            ORDER BY g.ID DESC";

                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@MemberID", memberId);
                    conn.Open();
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            myGroups.Add(MapGroupFromReader(reader));
                        }
                    }
                }
            }

            return myGroups;
        }


        private List<Group> GetMyGroupsByNationalId(string nationalId)
        {
            var groups = new List<Group>();

            using (var conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
            {
                string query = @"
                                    SELECT 
                            g.*, 
                            pt.PayoutType AS PayoutType, 
                            f.FrequencyName, 
                            c.Currency AS Currency,
                            gs.PenaltyAmount, 
                            gs.PenaltyGraceDays, 
                            gs.AllowDeferrals,
                            mg.RoleID  -- ✅ Added RoleID from MemberGroups
                        FROM Groups g
                        JOIN MemberGroups mg ON g.ID = mg.GroupID
                        JOIN Members m ON mg.MemberID = m.ID
                        JOIN PayoutTypes pt ON g.PayoutTypeID = pt.ID
                        JOIN Currencies c ON g.CurrencyID = c.ID
                        JOIN Frequencies f ON g.FrequencyID = f.ID
                        LEFT JOIN GroupSettings gs ON g.ID = gs.GroupID
                        WHERE m.NationalID = @NationalID
                        ";

                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@NationalID", nationalId);
                    conn.Open();
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            groups.Add(MapGroupFromReader(reader));
                        }
                    }
                }
            }

            return groups;
        }

        private List<Group> GetNewGroupsForMember(int memberId)
        {
            _logger.LogInformation("GetNewGroupsForMember called with memberId: {MemberId}", memberId);
            var newGroups = new List<Group>();

            using (var conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
            {
                string query = @"SELECT g.*, pt.PayoutType AS PayoutType, f.FrequencyName, c.Currency, gs.PenaltyAmount, gs.PenaltyGraceDays, gs.AllowDeferrals, g.Duration,
                g.Status, g.Closed
                                 FROM Groups g
                                 JOIN PayoutTypes pt ON g.PayoutTypeID = pt.ID
                                 JOIN Currencies c ON g.CurrencyID = c.ID
                                 JOIN Frequencies f ON g.FrequencyID = f.ID
                                 LEFT JOIN GroupSettings gs ON g.ID = gs.GroupID
                                 WHERE g.ID NOT IN (SELECT GroupID FROM MemberGroups WHERE MemberID = @MemberID)";

                using (var cmd = new SqlCommand(query, conn))

                {
                    cmd.Parameters.AddWithValue("@MemberID", memberId);
                    conn.Open();
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            newGroups.Add(new Group
                            {
                                ID = Convert.ToInt32(reader["ID"]),
                                GroupName = reader["GroupName"].ToString(),
                                Status = reader["Status"].ToString(),
                                Closed = Convert.ToBoolean(reader["Closed"]),
                                ContributionAmount = reader["ContributionAmount"] != DBNull.Value ? Convert.ToDecimal(reader["ContributionAmount"]) : 0,
                                MemberLimit = reader["MemberLimit"] != DBNull.Value ? Convert.ToInt32(reader["MemberLimit"]) : 0,
                                PayoutType = reader["PayoutType"]?.ToString(),
                                FrequencyName = reader["FrequencyName"]?.ToString(),
                                Currency = reader["Currency"]?.ToString(),
                                StartDate = reader["StartDate"] != DBNull.Value ? (DateTime?)Convert.ToDateTime(reader["StartDate"]) : null,
                                PenaltyAmount = reader["PenaltyAmount"] != DBNull.Value ? Convert.ToDecimal(reader["PenaltyAmount"]) : 0,
                                Duration = reader["Duration"].ToString(),
                                PenaltyGraceDays = reader["PenaltyGraceDays"] != DBNull.Value ? Convert.ToInt32(reader["PenaltyGraceDays"]) : 0,
                                AllowDeferrals = reader["AllowDeferrals"] != DBNull.Value && Convert.ToBoolean(reader["AllowDeferrals"])
                            });
                        }
                    }
                }
            }

            return newGroups;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult CreateGroup(Group model)
        {
            model.CreatedDate = DateTime.Now;

            if (!ModelState.IsValid)
            {
                LogModelStateErrors();
                model.PayoutTypes = GetPayoutTypes();
                model.Currencies = GetCurrencies();
                model.FrequencyOptions = GetFrequencyOptions();
                model.CanCreate = true;
                ViewBag.CreateError = true;
                return View("ListGroups", model);
            }

            if (ModelState.IsValid)
            {
                ViewBag.CreateError = false;
            }

            try
            {
                int memberId;

                using (var conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
                {
                    conn.Open();

                    var memberIdClaim = User.Claims.FirstOrDefault(c => c.Type == "member_id");
                    if (memberIdClaim == null || !int.TryParse(memberIdClaim.Value, out memberId))
                    {
                        ModelState.AddModelError("", "Unable to determine member identity from token.");
                        model.PayoutTypes = GetPayoutTypes();
                        model.Currencies = GetCurrencies();
                        model.FrequencyOptions = GetFrequencyOptions();
                        model.CanCreate = true;
                        ViewBag.CreateError = true;
                        return View("ListGroups", model);
                    }

                    memberId = Convert.ToInt32(memberIdClaim.Value);
                    model.MemberId = memberId;

                    int groupId;

                    string checkGroupQuery = "SELECT COUNT(*) FROM Groups WHERE GroupName = @GroupName";
                    using (var checkCmd = new SqlCommand(checkGroupQuery, conn))
                    {
                        checkCmd.Parameters.AddWithValue("@GroupName", model.GroupName);
                        int exists = (int)checkCmd.ExecuteScalar();
                        if (exists > 0)
                        {
                            ModelState.AddModelError("GroupName", "A group with this name already exists.");
                            model.PayoutTypes = GetPayoutTypes();
                            model.Currencies = GetCurrencies();
                            model.FrequencyOptions = GetFrequencyOptions();
                            model.CanCreate = true;
                            ViewBag.CreateError = true;
                            return View("ListGroups", model);
                        }

                    }
                    var insertGroupQuery = @"
                        INSERT INTO Groups (
                            GroupName, PayoutTypeID, Duration, ContributionAmount, MemberLimit, Status,
                            CreatedDate, StartDate, CurrencyID, FrequencyID, Cycles, Closed
                        )
                        VALUES (
                            @GroupName, @PayoutTypeID, @Duration, @ContributionAmount, @MemberLimit, 0,
                            @CreatedDate, @StartDate, @CurrencyID, @FrequencyID, @Cycles, @Closed
                        );
                        SELECT CAST(SCOPE_IDENTITY() AS INT);
                    ";


                    using (var cmd = new SqlCommand(insertGroupQuery, conn))
                    {
                        cmd.Parameters.AddWithValue("@GroupName", model.GroupName);
                        cmd.Parameters.AddWithValue("@PayoutTypeID", model.PayoutTypeID);
                        cmd.Parameters.AddWithValue("@Duration", model.Duration);
                        cmd.Parameters.AddWithValue("@ContributionAmount", model.ContributionAmount);
                        cmd.Parameters.AddWithValue("@MemberLimit", model.MemberLimit);
                        cmd.Parameters.AddWithValue("@CreatedDate", model.CreatedDate);
                        cmd.Parameters.AddWithValue("@StartDate", (object?)model.StartDate ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@CurrencyID", model.CurrencyID);
                        cmd.Parameters.AddWithValue("@FrequencyID", model.FrequencyID);
                        cmd.Parameters.AddWithValue("@Cycles", 0); // Always start with zero cycles
                        cmd.Parameters.AddWithValue("@Closed", false); // ✅ Set Closed = false

                        groupId = Convert.ToInt32(cmd.ExecuteScalar());
                    }

                    var settingsQuery = @"INSERT INTO GroupSettings (GroupID, PenaltyAmount, PenaltyGraceDays, AllowDeferrals)
                                          VALUES (@GroupID, @PenaltyAmount, @PenaltyGraceDays, @AllowDeferrals);";

                    using (var cmd = new SqlCommand(settingsQuery, conn))
                    {
                        cmd.Parameters.AddWithValue("@GroupID", groupId);
                        cmd.Parameters.AddWithValue("@PenaltyAmount", model.PenaltyAmount);
                        cmd.Parameters.AddWithValue("@PenaltyGraceDays", model.PenaltyGraceDays);
                        cmd.Parameters.AddWithValue("@AllowDeferrals", model.AllowDeferrals ? 1 : 0);
                        cmd.ExecuteNonQuery();
                    }

                    // Add member as group creator
                    var insertMemberGroup = @"INSERT INTO MemberGroups (MemberID, GroupID, RoleID)
                                              VALUES (@MemberID, @GroupID, 1);";

                    using (var cmd = new SqlCommand(insertMemberGroup, conn))
                    {
                        cmd.Parameters.AddWithValue("@MemberID", memberId);
                        cmd.Parameters.AddWithValue("@GroupID", groupId);
                        cmd.ExecuteNonQuery();
                    }
                }



                return RedirectToAction("ListGroups", new { memberId = model.MemberId, created = true });
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception while creating group: " + ex.Message);
                ModelState.AddModelError("", "Error: " + ex.Message);

                model.PayoutTypes = GetPayoutTypes();
                model.Currencies = GetCurrencies();
                model.CanCreate = true;
                ViewBag.CreateError = true;
                return View("ListGroups", model);

            }
        }

        [HttpPost]
        public IActionResult JoinGroupConfirmed(int groupId)
        {
            var memberIdClaim = User.FindFirst("member_id");

            if (memberIdClaim == null || !int.TryParse(memberIdClaim.Value, out int memberId))
            {
                return Unauthorized("Member ID not found in token.");
            }


            using (var conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))

            {

                conn.Open();

                // Check if this request already exists
                using (var checkCmd = new SqlCommand(
                    "SELECT COUNT(*) FROM JoinRequests WHERE MemberID = @MemberID AND GroupID = @GroupID", conn))
                {
                    checkCmd.Parameters.AddWithValue("@MemberID", memberId);
                    checkCmd.Parameters.AddWithValue("@GroupID", groupId);
                    int count = (int)checkCmd.ExecuteScalar();

                    if (count > 0)
                    {
                        return BadRequest("Join request already exists.");
                    }
                }

                // Insert new join request
                var insertSql = @"INSERT INTO JoinRequests (MemberID, GroupID, Status) 
                                VALUES (@MemberID, @GroupID, @Status)";

                using (var cmd = new SqlCommand(insertSql, conn))

                {

                    cmd.Parameters.AddWithValue("@MemberID", memberId);

                    cmd.Parameters.AddWithValue("@GroupID", groupId);
                    cmd.Parameters.AddWithValue("@Status", "Pending");
                    cmd.ExecuteNonQuery();

                }

            }

            return RedirectToAction("ListGroups", new { memberId });
        }

        [HttpGet]
        public IActionResult RequestToJoin(int groupId, string nationalId)
        {
            var model = new RequestToJoinView();

            using (var conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
            {
                conn.Open();

                string query = @"SELECT g.GroupName, g.ContributionAmount, g.MemberLimit, g.StartDate,g.Duration,pt.PayoutType, f.FrequencyName, c.Currency,gs.PenaltyAmount, gs.PenaltyGraceDays, gs.AllowDeferrals
                                 FROM Groups g
                                JOIN PayoutTypes pt ON g.PayoutTypeID = pt.ID
                                JOIN Frequencies f ON g.FrequencyID = f.ID
                                JOIN Currencies c ON g.CurrencyID = c.ID
                                LEFT JOIN GroupSettings gs ON g.ID = gs.GroupID WHERE g.ID = @GroupID";

                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@GroupID", groupId);

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            model.GroupId = groupId;
                            model.GroupName = reader["GroupName"].ToString();
                            model.ContributionAmount = reader["ContributionAmount"] != DBNull.Value ? Convert.ToDecimal(reader["ContributionAmount"]) : (decimal?)null;
                            model.FrequencyName = reader["FrequencyName"].ToString();
                            model.Currency = reader["Currency"].ToString();
                            model.MemberLimit = Convert.ToInt32(reader["MemberLimit"]);
                            model.StartDate = reader["StartDate"] != DBNull.Value ? (DateTime?)Convert.ToDateTime(reader["StartDate"]) : null;
                            model.PayoutType = reader["PayoutType"].ToString();
                            model.PenaltyAmount = reader["PenaltyAmount"] != DBNull.Value ? Convert.ToDecimal(reader["PenaltyAmount"]) : 0;
                            model.Duration = reader["Duration"].ToString();
                            model.PenaltyGraceDays = reader["PenaltyGraceDays"] != DBNull.Value ? (int?)Convert.ToInt32(reader["PenaltyGraceDays"]) : null;
                            model.AllowDeferrals = reader["AllowDeferrals"] != DBNull.Value && Convert.ToBoolean(reader["AllowDeferrals"]);
                            model.NationalId = nationalId;
                        }
                        else
                        {
                            return NotFound("Group not found.");
                        }
                    }
                }
            }

            return View("RequestToJoin", model);
        }
        [Authorize]
        public IActionResult JoinRequestsDashboard(int? groupId, string status = "Pending")
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
            var isAdmin = User.IsInRole("Admin");
            var memberIdStr = User.Claims.FirstOrDefault(c => c.Type == "member_id")?.Value;

            int memberId = int.TryParse(memberIdStr, out var id) ? id : 0;

            _logger.LogInformation("The status is: {status}", status);

            using (var conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
            {
                conn.Open();

                // For Members: Restrict to their groups
                if (!isAdmin)
                {
                    using (var cmd = new SqlCommand(
                        "SELECT GroupID FROM MemberGroups WHERE MemberID = @userId", conn))
                    {
                        cmd.Parameters.AddWithValue("@userId", userId);
                        groupId = (int?)cmd.ExecuteScalar();
                        if (groupId == null) return Forbid();
                    }
                }

                // Get Group Info
                var group = new GroupInfoDto();
                using (var cmd = new SqlCommand(
                    @"SELECT g.ID, g.GroupName, c.Currency, f.FrequencyName, g.MemberLimit, g.Status, g.StartDate, g.Closed,
                            (SELECT COUNT(*) FROM MemberGroups mg WHERE mg.GroupID = g.ID) AS CurrentMembers
                    FROM Groups g
                    JOIN Currencies c ON g.CurrencyID = c.ID
                    JOIN Frequencies f ON g.FrequencyID = f.ID
                    WHERE g.ID = @groupId", conn))

                {
                    cmd.Parameters.AddWithValue("@groupId", groupId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            group = new GroupInfoDto
                            {
                                ID = reader.GetInt32(reader.GetOrdinal("ID")),
                                Name = reader.GetString(reader.GetOrdinal("GroupName")),
                                Currency = reader.GetString(reader.GetOrdinal("Currency")),
                                FrequencyName = reader.GetString(reader.GetOrdinal("FrequencyName")),
                                Closed = !reader.IsDBNull(reader.GetOrdinal("Closed")) && reader.GetBoolean(reader.GetOrdinal("Closed"))
                            };

                            // ✅ Add "started" flag (g.Status == 1 means started)
                            int groupStatus = reader.GetInt32(reader.GetOrdinal("Status"));
                            ViewBag.GroupStarted = (groupStatus == 1);

                            // ✅ Set CanBePaused if StartDate is not null
                            ViewBag.CanBePaused = !reader.IsDBNull(reader.GetOrdinal("StartDate"));

                            // ✅ Check if group is already full
                            int maxMembers = reader.GetInt32(reader.GetOrdinal("MemberLimit"));
                            int currentMembers = reader.GetInt32(reader.GetOrdinal("CurrentMembers"));

                            if (currentMembers < maxMembers && ViewBag.CanBePaused)
                            {
                                // Group has started and is not full → cannot activate
                                ViewBag.CanBeActivated = false;
                                Console.WriteLine("Case 1: currentMembers < maxMembers && CanBePaused → CanBeActivated = false");
                            }
                            else if (currentMembers >= maxMembers && !ViewBag.CanBePaused)
                            {
                                // Group has not started but is full → can activate
                                ViewBag.CanBeActivated = true;
                                Console.WriteLine("Case 2: currentMembers >= maxMembers && !CanBePaused → CanBeActivated = true");
                            }
                            else if (currentMembers < maxMembers && !ViewBag.CanBePaused)
                            {
                                // Group has not started and is not full → cannot activate
                                ViewBag.CanBeActivated = false;
                                Console.WriteLine("Case 3: currentMembers < maxMembers && !CanBePaused → CanBeActivated = false");
                            }
                            else if (currentMembers >= maxMembers && ViewBag.CanBePaused)
                            {
                                // Group has started and is full → cannot activate
                                ViewBag.CanBeActivated = false;
                                Console.WriteLine("Case 4: currentMembers >= maxMembers && CanBePaused → CanBeActivated = false");
                            }

                            if (status == "Pending" && currentMembers >= maxMembers)
                            {
                                return RedirectToAction("JoinRequestsDashboard", new { groupId, status = "Accepted" });
                            }
                        }
                    }
                }


                // Get Requests
                var requests = new List<JoinRequestView>();
                string query = @"SELECT jr.ID, jr.MemberID, jr.RequestedDate,jr.Status,
                     m.FirstName, m.LastName, m.NationalID, ge.Gender, m.Email,
                     g.GroupName AS GroupName, g.ContributionAmount, c.Currency, f.FrequencyName
               FROM JoinRequests jr
               JOIN Members m ON jr.MemberID = m.ID
               JOIN Gender ge ON m.GenderID = ge.ID
               JOIN Groups g ON jr.GroupID = g.ID
               JOIN Currencies c ON g.CurrencyID = c.ID
               JOIN Frequencies f ON g.FrequencyID = f.ID
               WHERE jr.GroupID = @groupId AND jr.Status = @status";

                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@groupId", groupId);
                    if (isAdmin) cmd.Parameters.AddWithValue("@status", status);
                    else cmd.Parameters.AddWithValue("@userId", userId);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var request = new JoinRequestView
                            {
                                RequestId = reader.GetInt32(reader.GetOrdinal("ID")),
                                MemberId = reader.GetInt32(reader.GetOrdinal("MemberID")),
                                Gender = reader.GetString(reader.GetOrdinal("Gender")),
                                Email = reader.GetString(reader.GetOrdinal("Email")),
                                RequestedDate = reader.GetDateTime(reader.GetOrdinal("RequestedDate")),
                                Status = reader.GetString(reader.GetOrdinal("Status")),
                                GroupName = reader.GetString(reader.GetOrdinal("GroupName")),
                                ContributionAmount = decimal.Parse(reader["ContributionAmount"].ToString()),
                                Currency = reader.GetString(reader.GetOrdinal("Currency")),
                                Frequency = reader.GetString(reader.GetOrdinal("FrequencyName"))
                            };

                            if (isAdmin)
                            {
                                request.FirstName = reader.GetString(reader.GetOrdinal("FirstName"));
                                request.LastName = reader.GetString(reader.GetOrdinal("LastName"));
                                request.NationalID = reader.GetString(reader.GetOrdinal("NationalID"));
                            }

                            requests.Add(request);
                        }

                    }
                }


                // Get additional dashboard data
                var model = new DashboardModel
                {
                    Group = group,
                    Requests = requests,
                    SelectedStatus = status,
                    RequestType = "Join",
                    IsMemberView = !isAdmin
                };

                // Set IsMemberNotAdmin based on RoleID == 1
                using (var cmd = new SqlCommand(
                    @"SELECT RoleID FROM MemberGroups 
                    WHERE MemberID = @memberId AND GroupID = @groupId", conn))
                {
                    cmd.Parameters.AddWithValue("@memberId", memberId);
                    cmd.Parameters.AddWithValue("@groupId", groupId);
                    var roleIdObj = cmd.ExecuteScalar();

                    if (roleIdObj != null && int.TryParse(roleIdObj.ToString(), out int roleId))
                    {
                        _logger.LogInformation("MemberID: {MemberID}, GroupID: {GroupID}, RoleID: {RoleID}", memberId, groupId, roleId);
                        model.IsMemberNotAdmin = (roleId == 2);
                    }
                    else
                    {
                        _logger.LogWarning("Could not determine RoleID for MemberID: {MemberID}, GroupID: {GroupID}", memberId, groupId);
                    }
                }


                // Add pending request count for admin
                if (isAdmin)
                {
                    using (var cmd = new SqlCommand(
                        "SELECT COUNT(*) FROM JoinRequests WHERE GroupID = @groupId AND Status = 'Pending'",
                        conn))
                    {
                        cmd.Parameters.AddWithValue("@groupId", groupId);
                        model.PendingRequestCount = (int)cmd.ExecuteScalar();
                    }
                }

                // Add next contribution date for members
                if (!isAdmin)
                {
                    using (var cmd = new SqlCommand(
                        @"SELECT DATEADD(DAY, f.Days, GETDATE()) 
                  FROM Groups g
                  JOIN Frequencies f ON g.FrequencyID = f.ID
                  WHERE g.ID = @groupId",
                        conn))
                    {
                        cmd.Parameters.AddWithValue("@groupId", groupId);
                        model.NextContributionDate = (DateTime?)cmd.ExecuteScalar();
                    }
                }

                // ✅ Check if group can be closed
                using (var cmd = new SqlCommand(@"
                    SELECT 
                        CASE 
                            WHEN EXISTS (
                                SELECT 1 
                                FROM Payouts p
                                JOIN MemberGroups mg ON p.MemberGroupID = mg.ID
                                WHERE mg.GroupID = @groupId 
                                AND p.PaidForCycle = g.Cycles
                            ) THEN 0 -- ❌ Cannot close if any payout exists in current cycle

                            WHEN EXISTS (
                                SELECT 1 
                                FROM Contributions c
                                JOIN MemberGroups mg ON c.MemberGroupID = mg.ID
                                WHERE mg.GroupID = @groupId
                                AND c.PaidForCycle = g.Cycles
                                AND c.Status = 'Success'
                            ) THEN 0 -- ❌ Cannot close if any contribution is 'Success'

                            ELSE 1 -- ✅ Can close if:
                                --   a) contributions exist but none are 'Success'
                                --   b) no contributions or payouts at all
                        END AS CanBeClosed
                    FROM Groups g
                    WHERE g.ID = @groupId", conn))
                {
                    cmd.Parameters.AddWithValue("@groupId", groupId);
                    var result = cmd.ExecuteScalar();
                    bool canBeClosed = (result != null && Convert.ToInt32(result) == 1);
                    ViewBag.CanBeClosed = canBeClosed;

                    // Cast to bool to satisfy ILogger
                    _logger.LogInformation("GroupID {GroupId} CanBeClosed: {CanBeClosed}", groupId, (bool)canBeClosed);
                }


                return View(model);
            }
        }

        [Authorize]
        public IActionResult LeaveRequests(int? groupId, string status = "Pending")
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
            var isAdmin = User.IsInRole("Admin");
            var memberIdStr = User.Claims.FirstOrDefault(c => c.Type == "member_id")?.Value;
            int memberId = int.TryParse(memberIdStr, out var id) ? id : 0;

            _logger.LogInformation("The leave request status is: {status}", status);

            using (var conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
            {
                conn.Open();

                // For Members: Restrict to their group
                if (!isAdmin)
                {
                    using (var cmd = new SqlCommand(
                        "SELECT GroupID FROM MemberGroups WHERE MemberID = @userId", conn))
                    {
                        cmd.Parameters.AddWithValue("@userId", userId);
                        groupId = (int?)cmd.ExecuteScalar();
                        if (groupId == null) return Forbid();
                    }
                }

                // Get Group Info (now includes Closed)
                var group = new GroupInfoDto();
                using (var cmd = new SqlCommand(
                    @"SELECT g.ID, g.GroupName, g.Closed, c.Currency, f.FrequencyName 
                    FROM Groups g
                    JOIN Currencies c ON g.CurrencyID = c.ID
                    JOIN Frequencies f ON g.FrequencyID = f.ID
                    WHERE g.ID = @groupId", conn))
                {
                    cmd.Parameters.AddWithValue("@groupId", groupId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            group = new GroupInfoDto
                            {
                                ID = reader.GetInt32(reader.GetOrdinal("ID")),
                                Name = reader.GetString(reader.GetOrdinal("GroupName")),
                                Closed = reader.GetBoolean(reader.GetOrdinal("Closed")), // ✅ Include Closed
                                Currency = reader.GetString(reader.GetOrdinal("Currency")),
                                FrequencyName = reader.GetString(reader.GetOrdinal("FrequencyName"))
                            };
                        }
                    }
                }

                // Get Leave Requests
                var requests = new List<LeaveRequestView>();
                string query = @"SELECT lr.ID, lr.MemberID, lr.RequestedDate, lr.Status,
                                        m.FirstName, m.LastName, m.NationalID,
                                        g.GroupName AS GroupName
                                FROM LeaveRequests lr
                                JOIN Members m ON lr.MemberID = m.ID
                                JOIN Groups g ON lr.GroupID = g.ID
                                WHERE lr.GroupID = @groupId AND lr.Status = @status";

                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@groupId", groupId);
                    cmd.Parameters.AddWithValue("@status", status);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var request = new LeaveRequestView
                            {
                                RequestId = reader.GetInt32(reader.GetOrdinal("ID")),
                                MemberId = reader.GetInt32(reader.GetOrdinal("MemberID")),
                                RequestedDate = reader.GetDateTime(reader.GetOrdinal("RequestedDate")),
                                Status = reader.GetString(reader.GetOrdinal("Status")),
                                GroupName = reader.GetString(reader.GetOrdinal("GroupName")),
                                FirstName = reader.GetString(reader.GetOrdinal("FirstName")),
                                LastName = reader.GetString(reader.GetOrdinal("LastName")),
                                NationalID = reader.GetString(reader.GetOrdinal("NationalID"))
                            };

                            requests.Add(request);
                        }
                    }
                }

                // Create the dashboard model
                var model = new DashboardModel
                {
                    Group = group,
                    LeaveRequests = requests,
                    SelectedStatus = status,
                    RequestType = "Leave",
                    IsMemberView = !isAdmin
                };

                // Set IsMemberNotAdmin based on RoleID == 1
                using (var cmd = new SqlCommand(
                    @"SELECT RoleID FROM MemberGroups 
                    WHERE MemberID = @memberId AND GroupID = @groupId", conn))
                {
                    cmd.Parameters.AddWithValue("@memberId", memberId);
                    cmd.Parameters.AddWithValue("@groupId", groupId);
                    var roleIdObj = cmd.ExecuteScalar();

                    if (roleIdObj != null && int.TryParse(roleIdObj.ToString(), out int roleId))
                    {
                        model.IsMemberNotAdmin = (roleId == 2);
                    }
                }

                // Pending count for admin
                if (isAdmin)
                {
                    using (var cmd = new SqlCommand(
                        "SELECT COUNT(*) FROM LeaveRequests WHERE GroupID = @groupId AND Status = 'Pending'", conn))
                    {
                        cmd.Parameters.AddWithValue("@groupId", groupId);
                        model.PendingRequestCount = (int)cmd.ExecuteScalar();
                    }
                }

                return View("JoinRequestsDashboard", model); // ✅ Explicitly render the desired view
            }
        }

        private void LogModelStateErrors()
        {
            foreach (var key in ModelState.Keys)
            {
                foreach (var error in ModelState[key].Errors)
                {
                    Console.WriteLine($"Validation error in '{key}': {error.ErrorMessage}");
                }
            }
        }

        private List<SelectListItem> GetPayoutTypes()
        {
            var list = new List<SelectListItem>();
            using (var conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
            {
                conn.Open();
                using (var cmd = new SqlCommand("SELECT ID, PayoutType FROM PayoutTypes", conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add(new SelectListItem
                        {
                            Value = reader["ID"].ToString(),
                            Text = reader["PayoutType"].ToString()
                        });
                    }
                }
            }
            return list;
        }

        private List<SelectListItem> GetCurrencies()
        {
            var list = new List<SelectListItem>();
            using (var conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
            {
                conn.Open();
                using (var cmd = new SqlCommand("SELECT ID, Currency FROM Currencies", conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add(new SelectListItem
                        {
                            Value = reader["ID"].ToString(),
                            Text = reader["Currency"].ToString()
                        });
                    }
                }
            }
            return list;
        }

        private Group MapGroupFromReader(SqlDataReader reader)
        {
            return new Group
            {
                ID = Convert.ToInt32(reader["ID"]),
                GroupName = reader["GroupName"].ToString(),
                ContributionAmount = reader["ContributionAmount"] != DBNull.Value ? Convert.ToDecimal(reader["ContributionAmount"]) : 0,
                MemberLimit = reader["MemberLimit"] != DBNull.Value ? Convert.ToInt32(reader["MemberLimit"]) : 0,
                PayoutType = reader["PayoutType"]?.ToString(),
                FrequencyName = reader["FrequencyName"]?.ToString(),
                StartDate = reader["StartDate"] != DBNull.Value ? (DateTime?)Convert.ToDateTime(reader["StartDate"]) : null,
                PenaltyAmount = reader["PenaltyAmount"] != DBNull.Value ? Convert.ToDecimal(reader["PenaltyAmount"]) : 0,
                PenaltyGraceDays = reader["PenaltyGraceDays"] != DBNull.Value ? Convert.ToInt32(reader["PenaltyGraceDays"]) : 0,
                Currency = reader["Currency"] != DBNull.Value ? reader["Currency"].ToString() : null,
                Duration = reader["Duration"].ToString(),
                AllowDeferrals = reader["AllowDeferrals"] != DBNull.Value && Convert.ToBoolean(reader["AllowDeferrals"]),
                RoleID = reader["RoleID"] != DBNull.Value ? Convert.ToInt32(reader["RoleID"]) : 0
            };
        }
        private List<SelectListItem> GetFrequencyOptions()
        {
            var options = new List<SelectListItem>();

            using (var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
            {
                connection.Open();
                var command = new SqlCommand("SELECT ID, FrequencyName FROM Frequencies", connection);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        options.Add(new SelectListItem
                        {
                            Value = reader["ID"].ToString(),
                            Text = reader["FrequencyName"].ToString()
                        });
                    }
                }
            }

            return options;
        }
        [HttpPost]
        public IActionResult RequestToLeave(int groupId, string nationalId)
        {
            int memberId;
            var model = new RequestToLeaveView();

            using (var conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
            {
                conn.Open();

                // Get member ID
                using (var cmd = new SqlCommand("SELECT ID FROM Members WHERE NationalID = @NationalID", conn))
                {
                    cmd.Parameters.AddWithValue("@NationalID", nationalId);
                    var result = cmd.ExecuteScalar();

                    if (result == null)
                    {
                        return NotFound("Member not found.");
                    }

                    memberId = Convert.ToInt32(result);
                }

                // Check if member is in group
                using (var cmd = new SqlCommand("SELECT COUNT(*) FROM MemberGroups WHERE MemberID = @MemberID AND GroupID = @GroupID", conn))
                {
                    cmd.Parameters.AddWithValue("@MemberID", memberId);
                    cmd.Parameters.AddWithValue("@GroupID", groupId);

                    int count = (int)cmd.ExecuteScalar();
                    if (count == 0)
                    {
                        return BadRequest("Member is not part of the group.");
                    }
                }

                // Check if there's already a pending leave request
                using (var cmd = new SqlCommand("SELECT COUNT(*) FROM LeaveRequests WHERE MemberID = @MemberID AND GroupID = @GroupID AND Status = 'Pending'", conn))
                {
                    cmd.Parameters.AddWithValue("@MemberID", memberId);
                    cmd.Parameters.AddWithValue("@GroupID", groupId);

                    int existing = (int)cmd.ExecuteScalar();
                    if (existing > 0)
                    {
                        return BadRequest("A leave request is already pending.");
                    }
                }

                // Insert into LeaveRequests table
                string insertSql = @"INSERT INTO LeaveRequests (MemberID, GroupID, RequestedDate, Status)
                            VALUES (@MemberID, @GroupID, GETDATE(),  'Pending')";

                using (var cmd = new SqlCommand(insertSql, conn))
                {
                    cmd.Parameters.AddWithValue("@MemberID", memberId);
                    cmd.Parameters.AddWithValue("@GroupID", groupId);
                    cmd.ExecuteNonQuery();
                }
            }
            return Ok("Leave request submitted successfully.");
        }

        [HttpGet]
        public IActionResult UpdateGroupPartial(int groupId)
        {
            GroupInfoDto groupInfo = null;
            using (var conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
            {
                conn.Open();

                // Get group info with CurrencyID
                string query = @"
                    SELECT 
                        g.ID, 
                        g.GroupName AS Name, 
                        g.CurrencyID, 
                        f.FrequencyName,
                        (SELECT COUNT(*) FROM MemberGroups WHERE GroupID = g.ID) AS CurrentMembers,
                        g.MemberLimit AS MaxMembers,
                        CASE WHEN g.Status = 1 THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END AS IsActive,
                        g.StartDate
                    FROM Groups g
                    JOIN Frequencies f ON g.FrequencyID = f.ID
                    WHERE g.ID = @groupId";

                using (var cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@groupId", groupId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            groupInfo = new GroupInfoDto
                            {
                                ID = Convert.ToInt32(reader["ID"]),
                                Name = reader["Name"].ToString(),
                                CurrencyID = Convert.ToInt32(reader["CurrencyID"]),
                                FrequencyName = reader["FrequencyName"].ToString(),
                                CurrentMembers = Convert.ToInt32(reader["CurrentMembers"]),
                                MaxMembers = Convert.ToInt32(reader["MaxMembers"]),
                                IsActive = Convert.ToBoolean(reader["IsActive"]),
                                StartDate = reader["StartDate"] != DBNull.Value ? (DateTime?)Convert.ToDateTime(reader["StartDate"]) : null
                            };
                        }
                    }
                }

                if (groupInfo != null)
                {
                    // Fetch all currencies for dropdown
                    string currencyQuery = "SELECT ID, Currency FROM Currencies";
                    using (var cmd = new SqlCommand(currencyQuery, conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            groupInfo.CurrencyOptions.Add(new SelectListItem
                            {
                                Value = reader["ID"].ToString(),
                                Text = reader["Currency"].ToString(),
                                Selected = (int)reader["ID"] == groupInfo.CurrencyID
                            });
                        }
                    }

                    // Fetch all frequencies for dropdown
                    string freqQuery = "SELECT FrequencyName FROM Frequencies";
                    using (var cmd = new SqlCommand(freqQuery, conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string freq = reader["FrequencyName"].ToString();
                            groupInfo.FrequencyOptions.Add(new SelectListItem
                            {
                                Value = freq,
                                Text = freq,
                                Selected = freq == groupInfo.FrequencyName
                            });
                        }
                    }
                }
            }

            if (groupInfo == null)
            {
                return NotFound("Group not found.");
            }
            return PartialView("_UpdateGroupPartial", groupInfo);
        }

        [HttpPost]
        public IActionResult UpdateGroup(GroupInfoDto model)
        {
            _logger.LogInformation("UpdateGroup called with model: ID={ID}, Name={Name}, CurrencyID={CurrencyID}, FrequencyName={FrequencyName}, MaxMembers={MaxMembers}, IsActive={IsActive}, StartDate={StartDate}",
                                model.ID, model.Name, model.CurrencyID, model.FrequencyName, model.MaxMembers, model.IsActive, model.StartDate);

            if (!ModelState.IsValid)
            {
                _logger.LogWarning("Model state is invalid for UpdateGroup. Errors: {Errors}",
                    string.Join("; ", ModelState.Values
                        .SelectMany(x => x.Errors)
                        .Select(x => x.ErrorMessage)));
                return PartialView("_UpdateGroupPartial", model);
            }

            try
            {
                // ✅ If IsActive is true, set StartDate to today
                if (model.IsActive)
                {
                    model.StartDate = DateTime.Now;
                }

                using (var conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
                {
                    conn.Open();
                    string query = @"
                        UPDATE Groups
                        SET 
                            GroupName = @Name,
                            CurrencyID = @CurrencyID,
                            FrequencyID = (SELECT ID FROM Frequencies WHERE FrequencyName = @FrequencyName),
                            MemberLimit = @MaxMembers,
                            Status = @IsActive,
                            StartDate = @StartDate
                        WHERE ID = @ID";

                    using (var cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@Name", model.Name);
                        cmd.Parameters.AddWithValue("@CurrencyID", model.CurrencyID);
                        cmd.Parameters.AddWithValue("@FrequencyName", model.FrequencyName);
                        cmd.Parameters.AddWithValue("@MaxMembers", model.MaxMembers);
                        cmd.Parameters.AddWithValue("@IsActive", model.IsActive ? 1 : 0);
                        cmd.Parameters.AddWithValue("@StartDate", (object)model.StartDate ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@ID", model.ID);

                        cmd.ExecuteNonQuery();
                    }
                }

                // ✅ Only after successful DB update, set the ViewBag values
                ViewBag.CanBeActivated = false;
                ViewBag.GroupStarted = model.IsActive;

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating group with ID {ID}", model.ID);
                return StatusCode(500, $"Error updating group: {ex.Message}");
            }
        }

        [HttpGet]
        [Route("Groups/GroupStatus/{groupId}")]
        public IActionResult GroupStatus(int groupId)
        {
            try
            {
                using (var conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
                {
                    conn.Open();

                    var dashboard = new DashboardModel();

                    using (var cmd = new SqlCommand(@"SELECT Closed FROM Groups WHERE ID = @groupId", conn))
                    {
                        cmd.Parameters.AddWithValue("@groupId", groupId);

                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                dashboard.Closed = !reader.IsDBNull(0) && reader.GetBoolean(0);
                                _logger.LogInformation("Dashboard.Closed value for GroupID {GroupID}: {Closed}", groupId, dashboard.Closed);
                            }
                            else
                            {
                                return NotFound(new { message = "Group not found." });
                            }
                        }
                    }

                    return Json(new { closed = dashboard.Closed });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching group status for GroupID {GroupID}", groupId);
                return StatusCode(500, new { message = "Internal server error." });
            }
        }

        [HttpPost]
        public IActionResult DeleteMemberFromGroup(int memberId, int groupId)
        {
            Console.WriteLine($"Deleting member {memberId} from group {groupId}");

            using (var conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
            {
                conn.Open();
                using (var transaction = conn.BeginTransaction())
                {
                    try
                    {
                        // 1️⃣ Delete from MemberGroups
                        string deleteMemberSql = "DELETE FROM MemberGroups WHERE MemberID = @MemberID AND GroupID = @GroupID";
                        using (var cmd = new SqlCommand(deleteMemberSql, conn, transaction))
                        {
                            cmd.Parameters.AddWithValue("@MemberID", memberId);
                            cmd.Parameters.AddWithValue("@GroupID", groupId);
                            cmd.ExecuteNonQuery();
                        }

                        // 2️⃣ Delete any associated leave requests
                        string deleteLeaveSql = "DELETE FROM JoinRequests WHERE MemberID = @MemberID AND GroupID = @GroupID";
                        using (var cmd = new SqlCommand(deleteLeaveSql, conn, transaction))
                        {
                            cmd.Parameters.AddWithValue("@MemberID", memberId);
                            cmd.Parameters.AddWithValue("@GroupID", groupId);
                            cmd.ExecuteNonQuery();
                        }

                        transaction.Commit();
                        return Json(new { success = true });
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        Console.WriteLine($"Error deleting member {memberId} from group {groupId}: {ex.Message}");
                        return Json(new { success = false, message = "Error deleting member." });
                    }
                }
            }
        }

        [HttpPost]
        public IActionResult DeactivateGroup(int groupId)
        {
            try
            {
                using (var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
                {
                    connection.Open();

                    var sql = "UPDATE Groups SET Closed = 1 WHERE ID = @GroupId";
                    using (var command = new SqlCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@GroupId", groupId);
                        var rowsAffected = command.ExecuteNonQuery();

                        if (rowsAffected > 0)
                        {
                            return Json(new { success = true, message = "Group deactivated successfully." });
                        }
                        else
                        {
                            return Json(new { success = false, message = "Group not found." });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error: " + ex.Message });
            }
        }


    }
}
