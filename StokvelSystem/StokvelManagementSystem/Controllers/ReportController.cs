using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StokvelManagementSystem.Models;
using System.Data.SqlClient;
using System.Security.Claims;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace StokvelManagementSystem.Controllers
{
    public class ReportsController : Controller
    {
        private readonly string _connectionString;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ReportsController> _logger;

        public ReportsController(IConfiguration configuration, ILogger<ReportsController> logger)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("DefaultConnection");
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [Authorize]
        public IActionResult MemberReport(int groupId, DateTime? dateFrom, DateTime? dateTo, string statusFilter)
        {
            var memberId = User.FindFirst("member_id")?.Value;
            if (string.IsNullOrEmpty(memberId))
            {
                return RedirectToAction("Login", "Account");
            }

            // Determine reporting period string
            string period;
            if (dateFrom.HasValue && dateTo.HasValue)
                period = $"{dateFrom:yyyy-MM-dd} to {dateTo:yyyy-MM-dd}";
            else if (dateFrom.HasValue)
                period = $"From {dateFrom:yyyy-MM-dd} to {DateTime.Now:yyyy-MM-dd}";
            else if (dateTo.HasValue)
                period = $"Up to {dateTo:yyyy-MM-dd}";
            else
                period = "All time";

            // Fetch contributions filtered by date and status
            var contributions = GetMemberContributions(memberId, dateFrom, dateTo, statusFilter);

            if (!string.IsNullOrEmpty(statusFilter))
            {
                contributions = contributions
                    .Where(c => c.Status.Equals(statusFilter, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            var report = new Report
            {
                FirstName = GetMemberFirstName(memberId),
                LastName = GetMemberLastName(memberId),
                CurrentStatus = GetMemberStatus(memberId),
                GroupName = GetMemberGroupInfo(memberId),
                Contributions = contributions,
                Date = DateTime.Now,
                Period = period,
                TotalContributionsPaid = CalculateTotalContributions(memberId, dateFrom, dateTo, statusFilter),
                Graphs = GetGraphs(memberId, dateFrom, dateTo, statusFilter),
                TotalMissedPayments = CountMissedPayments(memberId, dateFrom, dateTo, statusFilter),
                PenaltiesApplied = CountPenalties(memberId, dateFrom, dateTo, statusFilter),
            };

            return View(report);
        }

        private string GetMemberFirstName(string memberId)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                var cmd = new SqlCommand("SELECT FirstName FROM Members WHERE ID = @MemberId", conn);
                cmd.Parameters.AddWithValue("@MemberId", memberId);
                conn.Open();
                return cmd.ExecuteScalar()?.ToString() ?? "N/A";
            }
        }
        private string GetMemberLastName(string memberId)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                var cmd = new SqlCommand("SELECT LastName FROM Members WHERE ID = @MemberId", conn);
                cmd.Parameters.AddWithValue("@MemberId", memberId);
                conn.Open();
                return cmd.ExecuteScalar()?.ToString() ?? "N/A";
            }
        }



        private string GetMemberStatus(string memberId)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                var cmd = new SqlCommand("SELECT Status FROM Members WHERE ID = @MemberId", conn);
                cmd.Parameters.AddWithValue("@MemberId", memberId);
                conn.Open();
                return cmd.ExecuteScalar()?.ToString() ?? "Unknown";
            }
        }

        private string GetMemberGroupInfo(string memberId)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                var cmd = new SqlCommand(@"
                    SELECT g.GroupName, g.Cycles, g.Duration
                    FROM Groups g
                    JOIN MemberGroups mg ON g.ID = mg.GroupID
                    WHERE mg.MemberID = @MemberId", conn);

                cmd.Parameters.AddWithValue("@MemberId", memberId);

                conn.Open();
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        var groupName = reader["GroupName"]?.ToString() ?? "No Group";
                        var cycles = reader["Cycles"] != DBNull.Value ? reader["Cycles"].ToString() : "0";
                        var duration = reader["Duration"] != DBNull.Value ? reader["Duration"].ToString() : "0";

                        return $"{groupName} - [Cycle ({cycles}/{duration})]";
                    }
                }
            }

            return "No Group - [Cycle (0/0)]";
        }


        private List<ContributionViewModel> GetMemberContributions(string memberId, DateTime? dateFrom, DateTime? dateTo, string statusFilter)
        {
            var contributions = new List<ContributionViewModel>();

            using (var conn = new SqlConnection(_connectionString))
            {
                // Base query
                var sql = @"
                            SELECT 
                                c.TransactionDate AS Date, 
                                c.ContributionAmount AS Amount, 
                                pm.Method AS PaymentMethod, 
                                c.ProofOfPaymentPath AS ProofOfPayment, 
                                c.Status AS Status,
                                c.PaidForCycle AS Cycle,
                                g.GroupName
                            FROM Contributions c
                            JOIN PaymentMethods pm ON c.PaymentMethodID = pm.ID
                            JOIN MemberGroups mg ON c.MemberGroupID = mg.ID
                            JOIN Groups g ON mg.GroupID = g.ID
                            WHERE mg.MemberID = @MemberId
                            ";

                // Date filtering
                if (dateFrom.HasValue && dateTo.HasValue)
                    sql += " AND c.TransactionDate BETWEEN @DateFrom AND @DateTo";
                else if (dateFrom.HasValue)
                    sql += " AND c.TransactionDate >= @DateFrom";
                else if (dateTo.HasValue)
                    sql += " AND c.TransactionDate <= @DateTo";

                // Status filtering
                if (!string.IsNullOrEmpty(statusFilter))
                    sql += " AND LTRIM(RTRIM(c.Status)) = @StatusFilter";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@MemberId", memberId);

                    if (dateFrom.HasValue) cmd.Parameters.AddWithValue("@DateFrom", dateFrom.Value.Date);
                    if (dateTo.HasValue) cmd.Parameters.AddWithValue("@DateTo", dateTo.Value.Date.AddDays(1).AddTicks(-1));

                    if (!string.IsNullOrEmpty(statusFilter))
                        cmd.Parameters.AddWithValue("@StatusFilter", statusFilter.Trim());

                    conn.Open();
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            contributions.Add(new ContributionViewModel
                            {
                                Date = Convert.ToDateTime(reader["Date"]),
                                Amount = Convert.ToDecimal(reader["Amount"]),
                                PaymentMethod = reader["PaymentMethod"].ToString(),
                                ProofOfPayment = reader["ProofOfPayment"].ToString(),
                                Status = reader["Status"].ToString(),
                                Cycle = reader["Cycle"].ToString(),
                                GroupName = reader["GroupName"].ToString()  
                            });
                        }
                    }
                }
            }

            return contributions;

        }

        public GraphResult GetGraphs(string memberId, DateTime? dateFrom, DateTime? dateTo, string statusFilter)
        {
            var result = new GraphResult();

            using (var conn = new SqlConnection(_connectionString))
            {
                var sql = @"
                    SELECT 
                        g.StartDate AS StartDate,
                        CASE g.FrequencyID
                            WHEN 1 THEN DATEADD(DAY, CAST(g.Duration AS INT), g.StartDate)      -- Daily
                            WHEN 2 THEN DATEADD(MONTH, CAST(g.Duration AS INT), g.StartDate)    -- Monthly
                            WHEN 3 THEN DATEADD(YEAR, CAST(g.Duration AS INT), g.StartDate)     -- Annually
                            WHEN 4 THEN DATEADD(DAY, CAST(g.Duration AS INT) * 7, g.StartDate)  -- Weekly
                        END AS EndDate,

                        (
                            SELECT 
                                FORMAT(c.TransactionDate, 'yyyy-MM-dd') AS [Date],
                                SUM(c.ContributionAmount) AS [Amount]
                            FROM Contributions c
                            WHERE c.MemberGroupID IN (
                                SELECT GroupID FROM MemberGroups WHERE MemberID = @MemberId
                            )
                            AND (@Status = '' OR c.Status = @Status)
                            AND (@DateFrom IS NULL OR c.TransactionDate >= @DateFrom)
                            AND (@DateTo IS NULL OR c.TransactionDate <= @DateTo)
                            GROUP BY FORMAT(c.TransactionDate, 'yyyy-MM-dd')
                            FOR JSON PATH
                        ) AS ContributionsJson,

                        (
                            SELECT 
                                FORMAT(p.TransactionDate, 'yyyy-MM-dd') AS [Date],
                                SUM(p.Amount) AS [Amount]
                            FROM Payouts p
                            WHERE p.MemberGroupID IN (
                                SELECT GroupID FROM MemberGroups WHERE MemberID = @MemberId
                            )
                            AND p.Status = 'Success'  -- only include successful payouts
                            AND (@DateFrom IS NULL OR p.TransactionDate >= @DateFrom)
                            AND (@DateTo IS NULL OR p.TransactionDate <= @DateTo)
                            GROUP BY FORMAT(p.TransactionDate, 'yyyy-MM-dd')
                            FOR JSON PATH
                        ) AS PayoutsJson
                    FROM Groups g
                    JOIN MemberGroups mg ON g.ID = mg.GroupID
                    WHERE mg.MemberID = @MemberId;
                ";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@MemberId", memberId);
                    cmd.Parameters.AddWithValue("@DateFrom", (object?)dateFrom ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@DateTo", (object?)dateTo ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Status", (object?)statusFilter ?? "");

                    conn.Open();
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            // Start and End Dates
                            if (!reader.IsDBNull(reader.GetOrdinal("StartDate")))
                                result.StartDate = reader.GetDateTime(reader.GetOrdinal("StartDate"));
                            if (!reader.IsDBNull(reader.GetOrdinal("EndDate")))
                                result.EndDate = reader.GetDateTime(reader.GetOrdinal("EndDate"));

                            // Contributions JSON
                            var contributionsJson = reader["ContributionsJson"]?.ToString() ?? "[]";
                            if (string.IsNullOrWhiteSpace(contributionsJson))
                                contributionsJson = "[]";

                            var contributions = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(contributionsJson);
                            if (contributions != null)
                            {
                                foreach (var item in contributions)
                                {
                                    if (item.ContainsKey("Date") && item.ContainsKey("Amount"))
                                    {
                                        var date = item["Date"].ToString();
                                        decimal amount = 0;

                                        if (item["Amount"] is JsonElement je && je.ValueKind == JsonValueKind.Number)
                                            amount = je.GetDecimal();
                                        else
                                            amount = Convert.ToDecimal(item["Amount"]);

                                        result.Contributions[date] = amount;
                                    }
                                }
                            }

                            // Payouts JSON
                            var payoutsJson = reader["PayoutsJson"]?.ToString() ?? "[]";
                            if (string.IsNullOrWhiteSpace(payoutsJson))
                                payoutsJson = "[]";

                            var payouts = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(payoutsJson);
                            if (payouts != null)
                            {
                                foreach (var item in payouts)
                                {
                                    if (item.ContainsKey("Date") && item.ContainsKey("Amount"))
                                    {
                                        var date = item["Date"].ToString();
                                        decimal amount = 0;

                                        if (item["Amount"] is JsonElement je && je.ValueKind == JsonValueKind.Number)
                                            amount = je.GetDecimal();
                                        else
                                            amount = Convert.ToDecimal(item["Amount"]);

                                        result.Payouts[date] = amount;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return result;
        }

        private decimal CalculateTotalContributions(string memberId, DateTime? dateFrom, DateTime? dateTo, string statusFilter)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                // Base SQL
                var sql = @"
                        SELECT ISNULL(SUM(ContributionAmount), 0)
                        FROM Contributions
                        WHERE Status = 'Success'
                        AND MemberGroupID IN (
                            SELECT ID FROM MemberGroups WHERE MemberID = @MemberId
                        )
            ";


                // Add date filters if provided
                if (dateFrom.HasValue && dateTo.HasValue)
                {
                    sql += " AND TransactionDate BETWEEN @DateFrom AND @DateTo";
                }
                else if (dateFrom.HasValue)
                {
                    sql += " AND TransactionDate >= @DateFrom";
                }
                else if (dateTo.HasValue)
                {
                    sql += " AND TransactionDate <= @DateTo";
                }

                // Add status filter if provided
                if (!string.IsNullOrEmpty(statusFilter))
                {
                    sql += " AND Status = @StatusFilter";
                }

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@MemberId", memberId);

                    if (dateFrom.HasValue) cmd.Parameters.AddWithValue("@DateFrom", dateFrom.Value.Date);
                    if (dateTo.HasValue) cmd.Parameters.AddWithValue("@DateTo", dateTo.Value.Date.AddDays(1).AddTicks(-1));
                    if (!string.IsNullOrEmpty(statusFilter)) cmd.Parameters.AddWithValue("@StatusFilter", statusFilter);

                    conn.Open();
                    return Convert.ToDecimal(cmd.ExecuteScalar() ?? 0);
                }
            }
        }

        private int CountMissedPayments(string memberId, DateTime? dateFrom, DateTime? dateTo, string statusFilter)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                // Base SQL
                var sql = @"
                    SELECT ISNULL(COUNT(*), 0)
                    FROM Contributions
                    WHERE MemberGroupID IN (
                        SELECT GroupID FROM MemberGroups WHERE MemberID = @MemberId
                    )
                ";

                // Only count missed payments (PenaltyAmount > 0)
                if (string.IsNullOrEmpty(statusFilter))
                {
                    sql += " AND PenaltyAmount > 0";
                }
                else if (statusFilter == "Success")
                {
                    sql += " AND Status = 'Success'";
                }
                else if (statusFilter == "Pending")
                {
                    sql += " AND Status = 'Pending'";
                }
                else if (statusFilter == "Fail")
                {
                    sql += " AND Status = 'Fail'";
                }

                // Add date filters if provided
                if (dateFrom.HasValue && dateTo.HasValue)
                {
                    sql += " AND TransactionDate BETWEEN @DateFrom AND @DateTo";
                }
                else if (dateFrom.HasValue)
                {
                    sql += " AND TransactionDate >= @DateFrom";
                }
                else if (dateTo.HasValue)
                {
                    sql += " AND TransactionDate <= @DateTo";
                }

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@MemberId", memberId);

                    if (dateFrom.HasValue) cmd.Parameters.AddWithValue("@DateFrom", dateFrom.Value.Date);
                    if (dateTo.HasValue) cmd.Parameters.AddWithValue("@DateTo", dateTo.Value.Date.AddDays(1).AddTicks(-1));

                    conn.Open();
                    return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
                }
            }
        }

        private int CountPenalties(string memberId, DateTime? dateFrom, DateTime? dateTo, string statusFilter)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                // Base SQL
                var sql = @"
                    SELECT ISNULL(COUNT(*), 0)
                    FROM Contributions
                    WHERE PenaltyAmount > 0
                    AND MemberGroupID IN (
                        SELECT GroupID FROM MemberGroups WHERE MemberID = @MemberId
                    )
                ";

                // Apply status filter if provided
                if (!string.IsNullOrEmpty(statusFilter))
                {
                    sql += " AND Status = @StatusFilter";
                }

                // Add date filters if provided
                if (dateFrom.HasValue && dateTo.HasValue)
                {
                    sql += " AND TransactionDate BETWEEN @DateFrom AND @DateTo";
                }
                else if (dateFrom.HasValue)
                {
                    sql += " AND TransactionDate >= @DateFrom";
                }
                else if (dateTo.HasValue)
                {
                    sql += " AND TransactionDate <= @DateTo";
                }

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@MemberId", memberId);

                    if (!string.IsNullOrEmpty(statusFilter))
                        cmd.Parameters.AddWithValue("@StatusFilter", statusFilter);

                    if (dateFrom.HasValue) 
                        cmd.Parameters.AddWithValue("@DateFrom", dateFrom.Value.Date);

                    if (dateTo.HasValue) 
                        cmd.Parameters.AddWithValue("@DateTo", dateTo.Value.Date.AddDays(1).AddTicks(-1));

                    conn.Open();
                    return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
                }
            }
        }


        // This is for Group Reports
        [Authorize(Roles = "Admin")]
        [HttpGet]
        public IActionResult GroupReport(int groupId, DateTime? dateFrom, DateTime? dateTo, string statusFilter)
        {
            // Fetch member summaries, optionally filtered by dates
            var memberSummaries = GetGroupMemberSummaries(groupId, dateFrom, dateTo, statusFilter);

            // Fetch contributions optionally filtered
            var contributions = GetGroupContributions(groupId, dateFrom, dateTo, statusFilter);
            var (totalCycles, contributionPerMember) = GetGroupCyclesAndContribution(groupId);

            var report = new Report
            {
                GroupId = groupId,
                GroupName = GetGroupName(groupId),
                Currency = GetGroupCurrency(groupId),
                TotalMembers = CountGroupMembers(groupId),
                GroupStartDate = GetGroupDateRangeString(groupId),
                ContributionFrequency = GetGroupFrequency(groupId),
                GroupTotalContributions = CalculateGroupTotalContributions(groupId, dateFrom, dateTo, statusFilter),
                Period = dateFrom.HasValue && dateTo.HasValue
                            ? $"{dateFrom:yyyy-MM-dd} to {dateTo:yyyy-MM-dd}"
                            : dateFrom.HasValue
                                ? $"From {dateFrom:yyyy-MM-dd}"
                                : dateTo.HasValue
                                    ? $"Up to {dateTo:yyyy-MM-dd}"
                                    : DateTime.Now.ToString("MMMM yyyy"),
                Contributions = contributions,
                MemberSummaries = memberSummaries,

                // Calculating total penalties in filtered period
                TotalGroupPenalties = memberSummaries.Count(m => m.Penalties > 0),
                TotalPenaltiesAmount = memberSummaries.Sum(m => m.Penalties),
                TotalCycles = totalCycles,
                ContributionPerMember = contributionPerMember
            };

            return View("GroupReport", report);
        }

        private string GetGroupName(int groupId)
        {
            using var conn = new SqlConnection(_connectionString);
            var cmd = new SqlCommand("SELECT GroupName FROM Groups WHERE ID = @GroupId", conn);
            cmd.Parameters.AddWithValue("@GroupId", groupId);
            conn.Open();
            return cmd.ExecuteScalar()?.ToString() ?? "Unknown Group";
        }

        private string GetGroupCurrency(int groupId)
        {
            using var conn = new SqlConnection(_connectionString);
            var cmd = new SqlCommand("SELECT c.Currency FROM Groups g INNER JOIN Currencies c ON g.CurrencyId = c.ID WHERE g.ID = @GroupId", conn);
            cmd.Parameters.AddWithValue("@GroupId", groupId);
            conn.Open();
            return cmd.ExecuteScalar()?.ToString() ?? "N/A";
        }

        private int CountGroupMembers(int groupId)
        {
            using var conn = new SqlConnection(_connectionString);
            var cmd = new SqlCommand("SELECT COUNT(*) FROM MemberGroups WHERE GroupID = @GroupId", conn);
            cmd.Parameters.AddWithValue("@GroupId", groupId);
            conn.Open();
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        private string GetGroupDateRangeString(int groupId)
        {
            using var conn = new SqlConnection(_connectionString);

            var sql = @"
                SELECT 
                    g.StartDate AS StartDate,
                    CASE g.FrequencyID
                        WHEN 1 THEN DATEADD(DAY, CAST(g.Duration AS INT), g.StartDate)      -- Daily
                        WHEN 2 THEN DATEADD(MONTH, CAST(g.Duration AS INT), g.StartDate)    -- Monthly
                        WHEN 3 THEN DATEADD(YEAR, CAST(g.Duration AS INT), g.StartDate)     -- Annually
                        WHEN 4 THEN DATEADD(DAY, CAST(g.Duration AS INT) * 7, g.StartDate)  -- Weekly
                    END AS EndDate
                FROM Groups g
                WHERE g.ID = @GroupId";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@GroupId", groupId);

            conn.Open();
            var reader = cmd.ExecuteReader();

            if (reader.Read())
            {
                var startDate = reader["StartDate"] != DBNull.Value ? Convert.ToDateTime(reader["StartDate"]).ToString("yyyy-MM-dd") : null;
                var endDate = reader["EndDate"] != DBNull.Value ? Convert.ToDateTime(reader["EndDate"]).ToString("yyyy-MM-dd") : null;

                if (!string.IsNullOrEmpty(startDate) && !string.IsNullOrEmpty(endDate))
                    return $"{startDate} - {endDate}";
            }

            return string.Empty;
        }

        private string GetGroupFrequency(int groupId)
        {
            using var conn = new SqlConnection(_connectionString);
            var cmd = new SqlCommand("SELECT f.FrequencyName FROM Groups g INNER JOIN Frequencies f ON g.FrequencyId = f.ID WHERE g.ID = @GroupId", conn);
            cmd.Parameters.AddWithValue("@GroupId", groupId);
            conn.Open();
            return cmd.ExecuteScalar()?.ToString() ?? "N/A";
        }
        private (int TotalCycles, decimal ContributionPerMember) GetGroupCyclesAndContribution(int groupId)
        {
            using var conn = new SqlConnection(_connectionString);

            var sql = @"SELECT 
                            ISNULL(Cycles, 0) AS TotalCycles,
                            CAST(ContributionAmount AS decimal(18,2)) AS ContributionPerMember
                        FROM Groups
                        WHERE ID = @GroupId";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@GroupId", groupId);

            _logger.LogInformation("Executing GetGroupCyclesAndContribution with GroupId={GroupId}", groupId);
            _logger.LogInformation("SQL Query: {SQL}", sql);

            conn.Open();
            using var reader = cmd.ExecuteReader();

            if (reader.Read())
            {
                int totalCycles = reader["TotalCycles"] != DBNull.Value ? Convert.ToInt32(reader["TotalCycles"]) : 0;
                decimal contributionPerMember = reader["ContributionPerMember"] != DBNull.Value ? Convert.ToDecimal(reader["ContributionPerMember"]) : 0;

                return (totalCycles, contributionPerMember);
            }

            return (0, 0);
        }


        private decimal CalculateGroupTotalContributions(int groupId, DateTime? dateFrom = null, DateTime? dateTo = null, string statusFilter = null)
        {
            using var conn = new SqlConnection(_connectionString);

            // Base query: always include Status = 'Success'
            var sql = @"SELECT ISNULL(SUM(ContributionAmount), 0) 
                        FROM Contributions 
                        WHERE MemberGroupID = @GroupId AND LTRIM(RTRIM(Status)) = 'Success' {0}";

            // Build optional filters
            var filters = new List<string>();

            if (dateFrom.HasValue && dateTo.HasValue)
            {
                filters.Add("TransactionDate BETWEEN @DateFrom AND @DateTo");
            }
            else if (dateFrom.HasValue)
            {
                filters.Add("TransactionDate >= @DateFrom");
            }
            else if (dateTo.HasValue)
            {
                filters.Add("TransactionDate <= @DateTo");
            }

            if (!string.IsNullOrEmpty(statusFilter) && statusFilter != "All")
            {
                filters.Add("LTRIM(RTRIM(Status)) = @StatusFilter");
            }

            var filterClause = filters.Count > 0 ? "AND " + string.Join(" AND ", filters) : "";
            sql = string.Format(sql, filterClause);

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@GroupId", groupId);
            if (dateFrom.HasValue) cmd.Parameters.AddWithValue("@DateFrom", dateFrom.Value.Date);
            if (dateTo.HasValue) cmd.Parameters.AddWithValue("@DateTo", dateTo.Value.Date.AddDays(1).AddTicks(-1));
            if (!string.IsNullOrEmpty(statusFilter) && statusFilter != "All") cmd.Parameters.AddWithValue("@StatusFilter", statusFilter.Trim());

            _logger.LogInformation("Executing CalculateGroupTotalContributions with GroupId={GroupId}, DateFrom={DateFrom}, DateTo={DateTo}, StatusFilter={StatusFilter}", 
                groupId, dateFrom, dateTo, statusFilter);
            _logger.LogInformation("SQL Query: {SQL}", sql);

            conn.Open();
            return Convert.ToDecimal(cmd.ExecuteScalar());
        }


        private List<ContributionViewModel> GetGroupContributions(int groupId, DateTime? dateFrom = null, DateTime? dateTo = null, string statusFilter = null)
        {
            var contributions = new List<ContributionViewModel>();

            using (var conn = new SqlConnection(_connectionString))
            {
                // Base query
                var sql = @"
                    SELECT 
                        c.TransactionDate AS Date, 
                        c.ContributionAmount AS Amount, 
                        pm.Method AS PaymentMethod, 
                        c.ProofOfPaymentPath AS ProofOfPayment, 
                        c.Status AS Status
                    FROM Contributions c
                    JOIN PaymentMethods pm ON c.PaymentMethodID = pm.ID
                    WHERE c.MemberGroupID = @GroupId
                ";

                // Date filtering
                if (dateFrom.HasValue && dateTo.HasValue)
                    sql += " AND c.TransactionDate BETWEEN @DateFrom AND @DateTo";
                else if (dateFrom.HasValue)
                    sql += " AND c.TransactionDate >= @DateFrom";
                else if (dateTo.HasValue)
                    sql += " AND c.TransactionDate <= @DateTo";

                // Status filtering
                if (!string.IsNullOrEmpty(statusFilter) && statusFilter != "All")
                    sql += " AND LTRIM(RTRIM(c.Status)) = @StatusFilter";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@GroupId", groupId);

                    if (dateFrom.HasValue)
                        cmd.Parameters.AddWithValue("@DateFrom", dateFrom.Value.Date);

                    if (dateTo.HasValue)
                        cmd.Parameters.AddWithValue("@DateTo", dateTo.Value.Date.AddDays(1).AddTicks(-1));

                    if (!string.IsNullOrEmpty(statusFilter) && statusFilter != "All")
                        cmd.Parameters.AddWithValue("@StatusFilter", statusFilter.Trim());

                    conn.Open();
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            contributions.Add(new ContributionViewModel
                            {
                                Date = Convert.ToDateTime(reader["Date"]),
                                Amount = Convert.ToDecimal(reader["Amount"]),
                                PaymentMethod = reader["PaymentMethod"].ToString(),
                                ProofOfPayment = reader["ProofOfPayment"].ToString(),
                                Status = reader["Status"].ToString()
                            });
                        }
                    }
                }
            }

            return contributions;
        }

        private List<GroupMemberSummary> GetGroupMemberSummaries(int groupId, DateTime? dateFrom = null, DateTime? dateTo = null, string statusFilter = null)
        {
            var summaries = new List<GroupMemberSummary>();
            using var conn = new SqlConnection(_connectionString);

            // Base query including Payouts
            var sql = @"
                SELECT 
                    m.FirstName + ' ' + m.LastName AS FullName,
                    ISNULL(SUM(CASE WHEN LTRIM(RTRIM(c.Status)) = 'Success' THEN c.ContributionAmount ELSE 0 END), 0) AS TotalPaid,
                    COUNT(CASE WHEN c.PenaltyAmount > 0 THEN 1 END) AS MissedPayments,
                    ISNULL(SUM(c.PenaltyAmount), 0) AS Penalties,
                    ISNULL(SUM(CASE WHEN LTRIM(RTRIM(p.Status)) = 'Success' THEN p.Amount ELSE 0 END), 0) AS TotalPayouts,
                    m.Status
                FROM Members m
                JOIN MemberGroups mg ON m.ID = mg.MemberID
                LEFT JOIN Contributions c ON mg.ID = c.MemberGroupID
                LEFT JOIN Payouts p ON mg.ID = p.MemberGroupID
                WHERE mg.GroupID = @GroupId
            ";

            // Build filters
            var filters = new List<string>();

            if (dateFrom.HasValue && dateTo.HasValue)
                filters.Add("(c.TransactionDate BETWEEN @DateFrom AND @DateTo OR p.TransactionDate BETWEEN @DateFrom AND @DateTo)");
            else if (dateFrom.HasValue)
                filters.Add("(c.TransactionDate >= @DateFrom OR p.TransactionDate >= @DateFrom)");
            else if (dateTo.HasValue)
                filters.Add("(c.TransactionDate <= @DateTo OR p.TransactionDate <= @DateTo)");

            if (!string.IsNullOrEmpty(statusFilter) && statusFilter != "All")
                filters.Add("LTRIM(RTRIM(c.Status)) = @StatusFilter");

            if (filters.Count > 0)
                sql += " AND " + string.Join(" AND ", filters);

            sql += " GROUP BY m.FirstName, m.LastName, m.Status";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@GroupId", groupId);

            if (dateFrom.HasValue)
                cmd.Parameters.AddWithValue("@DateFrom", dateFrom.Value.Date);
            if (dateTo.HasValue)
                cmd.Parameters.AddWithValue("@DateTo", dateTo.Value.Date.AddDays(1).AddTicks(-1));

            if (!string.IsNullOrEmpty(statusFilter) && statusFilter != "All")
                cmd.Parameters.AddWithValue("@StatusFilter", statusFilter.Trim());

            conn.Open();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                summaries.Add(new GroupMemberSummary
                {
                    FullName = reader["FullName"].ToString(),
                    TotalPaid = Convert.ToDecimal(reader["TotalPaid"]),
                    MissedPayments = Convert.ToInt32(reader["MissedPayments"]),
                    Penalties = Convert.ToDecimal(reader["Penalties"]),
                    TotalPayouts = Convert.ToDecimal(reader["TotalPayouts"]),
                    Status = reader["Status"].ToString()
                });
            }

            return summaries;
        }


    }
}