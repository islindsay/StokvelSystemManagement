using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StokvelManagementSystem.Models;
using System.Data.SqlClient;
using System.Security.Claims;
using Microsoft.Extensions.Logging;

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
        public IActionResult MemberReport(int groupId, DateTime? dateFrom, DateTime? dateTo)
        {
            var memberId = User.FindFirst("member_id")?.Value;
            if (string.IsNullOrEmpty(memberId))
            {
                return RedirectToAction("Login", "Account");
            }

            string period;
            if (dateFrom.HasValue && dateTo.HasValue)
            {
                period = $"{dateFrom:yyyy-MM-dd} to {dateTo:yyyy-MM-dd}";
            }
            else if (dateFrom.HasValue)
            {
                period = $"From {dateFrom:yyyy-MM-dd} to {DateTime.Now:yyyy-MM-dd}";
            }
            else if (dateTo.HasValue)
            {
                period = $"Up to {dateTo:yyyy-MM-dd}";
            }
            else
            {
                period = "All time";
            }

            var report = new Report
            {
                FirstName = GetMemberFirstName(memberId),
                LastName = GetMemberLastName(memberId),
                CurrentStatus = GetGroupStatus(groupId),
                GroupName = GetMemberGroupName(memberId),
                Contributions = GetMemberContributions(memberId, dateFrom, dateTo),
                Date = DateTime.Now,
                Period = period,
                TotalContributionsPaid = CalculateTotalContributions(memberId, dateFrom, dateTo),
                TotalMissedPayments = CountMissedPayments(memberId, dateFrom, dateTo),
                PenaltiesApplied = CountPenalties(memberId, dateFrom, dateTo),
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



        private string GetGroupStatus(int groupId)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                var cmd = new SqlCommand("SELECT Status FROM Groups WHERE ID = @groupId", conn);
                cmd.Parameters.AddWithValue("@groupId", groupId); // ✅ fixed: parameter name matches SQL
                conn.Open();
                return cmd.ExecuteScalar()?.ToString() ?? "Unknown";
            }
        }


        private string GetMemberGroupName(string memberId)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                var cmd = new SqlCommand(@"
                    SELECT g.GroupName 
                    FROM Groups g
                    JOIN MemberGroups mg ON g.ID = mg.GroupID
                    WHERE mg.MemberID = @MemberId", conn);
                cmd.Parameters.AddWithValue("@MemberId", memberId);
                conn.Open();
                return cmd.ExecuteScalar()?.ToString() ?? "No Group";
            }
        }

        private List<ContributionViewModel> GetMemberContributions(string memberId, DateTime? dateFrom, DateTime? dateTo)
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
                        CASE WHEN c.PenaltyAmount > 0 THEN 'Missed' ELSE 'Paid' END AS Status
                    FROM Contributions c
                    JOIN PaymentMethods pm ON c.PaymentMethodID = pm.ID
                    WHERE c.MemberGroupID IN (
                        SELECT GroupID FROM MemberGroups WHERE MemberID = @MemberId
                    )
                ";

                // Add date filtering
                if (dateFrom.HasValue && dateTo.HasValue)
                {
                    sql += " AND c.TransactionDate BETWEEN @DateFrom AND @DateTo";
                }
                else if (dateFrom.HasValue)
                {
                    sql += " AND c.TransactionDate >= @DateFrom";
                }
                else if (dateTo.HasValue)
                {
                    sql += " AND c.TransactionDate <= @DateTo";
                }

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@MemberId", memberId);
                    if (dateFrom.HasValue) cmd.Parameters.AddWithValue("@DateFrom", dateFrom.Value.Date);
                    if (dateTo.HasValue) cmd.Parameters.AddWithValue("@DateTo", dateTo.Value.Date.AddDays(1).AddTicks(-1)); // include the full end day

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

        private decimal CalculateTotalContributions(string memberId, DateTime? dateFrom, DateTime? dateTo)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                // Base SQL
                var sql = @"
                    SELECT ISNULL(SUM(ContributionAmount), 0)
                    FROM Contributions
                    WHERE MemberGroupID IN (
                        SELECT GroupID FROM MemberGroups WHERE MemberID = @MemberId
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

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@MemberId", memberId);

                    if (dateFrom.HasValue) cmd.Parameters.AddWithValue("@DateFrom", dateFrom.Value.Date);
                    if (dateTo.HasValue) cmd.Parameters.AddWithValue("@DateTo", dateTo.Value.Date.AddDays(1).AddTicks(-1));

                    conn.Open();
                    return Convert.ToDecimal(cmd.ExecuteScalar() ?? 0);
                }
            }
        }


        private int CountMissedPayments(string memberId, DateTime? dateFrom, DateTime? dateTo)
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


        private int CountPenalties(string memberId, DateTime? dateFrom, DateTime? dateTo)
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



        // This is for Group Reports
        [Authorize(Roles = "Admin")]
        [HttpGet]
        public IActionResult GroupReport(int groupId, DateTime? dateFrom, DateTime? dateTo)
        {
            // Fetch member summaries, optionally filtered by dates
            var memberSummaries = GetGroupMemberSummaries(groupId, dateFrom, dateTo);

            // Fetch contributions optionally filtered
            var contributions = GetGroupContributions(groupId, dateFrom, dateTo);
            var (totalCycles, contributionPerMember) = GetGroupCyclesAndContribution(groupId);

            var report = new Report
            {
                GroupId = groupId,
                GroupName = GetGroupName(groupId),
                Currency = GetGroupCurrency(groupId),
                TotalMembers = CountGroupMembers(groupId),
                GroupStartDate = GetGroupStartDate(groupId),
                ContributionFrequency = GetGroupFrequency(groupId),
                GroupTotalContributions = CalculateGroupTotalContributions(groupId, dateFrom, dateTo),
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

        private DateTime? GetGroupStartDate(int groupId)
        {
            using var conn = new SqlConnection(_connectionString);
            var cmd = new SqlCommand("SELECT StartDate FROM Groups WHERE ID = @GroupId", conn);
            cmd.Parameters.AddWithValue("@GroupId", groupId);
            conn.Open();

            var result = cmd.ExecuteScalar();

            if (result == DBNull.Value || result == null)
                return null;

            return Convert.ToDateTime(result);
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



        private decimal CalculateGroupTotalContributions(int groupId, DateTime? dateFrom = null, DateTime? dateTo = null)
        {
            using var conn = new SqlConnection(_connectionString);

            // Base query
            var sql = @"SELECT ISNULL(SUM(ContributionAmount), 0) 
                        FROM Contributions 
                        WHERE MemberGroupID = @GroupId {0}";

            // Build date filter
            string dateFilter = "";
            if (dateFrom.HasValue && dateTo.HasValue)
            {
                // Extend dateTo to include the end of the day
                var endOfDay = dateTo.Value.Date.AddDays(1).AddTicks(-1);
                dateFilter = "AND TransactionDate BETWEEN @DateFrom AND @DateTo";
            }
            else if (dateFrom.HasValue)
            {
                dateFilter = "AND TransactionDate >= @DateFrom";
            }
            else if (dateTo.HasValue)
            {
                var endOfDay = dateTo.Value.Date.AddDays(1).AddTicks(-1);
                dateFilter = "AND TransactionDate <= @DateTo";
            }

            sql = string.Format(sql, dateFilter);

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@GroupId", groupId);
            if (dateFrom.HasValue) cmd.Parameters.AddWithValue("@DateFrom", dateFrom.Value);
            if (dateTo.HasValue) cmd.Parameters.AddWithValue("@DateTo", dateTo.Value.Date.AddDays(1).AddTicks(-1));

            _logger.LogInformation("Executing CalculateGroupTotalContributions with GroupId={GroupId}, DateFrom={DateFrom}, DateTo={DateTo}", groupId, dateFrom, dateTo);
            _logger.LogInformation("SQL Query: {SQL}", sql);

            conn.Open();
            return Convert.ToDecimal(cmd.ExecuteScalar());
        }


        private List<ContributionViewModel> GetGroupContributions(int groupId, DateTime? dateFrom = null, DateTime? dateTo = null)
            {
                var contributions = new List<ContributionViewModel>();

                using var conn = new SqlConnection(_connectionString);

                // Base query
                var sql = @"
                    SELECT c.TransactionDate AS Date, 
                        c.ContributionAmount AS Amount, 
                        pm.Method AS PaymentMethod, 
                        c.ProofOfPaymentPath AS ProofOfPayment, 
                        CASE WHEN c.PenaltyAmount > 0 THEN 'Missed' ELSE 'Paid' END AS Status
                    FROM Contributions c
                    JOIN PaymentMethods pm ON c.PaymentMethodID = pm.ID
                    WHERE c.MemberGroupID = @GroupId
                        {0}"; // placeholder for date filter

                // Build date filter
                var dateFilter = "";
            if (dateFrom.HasValue && dateTo.HasValue)
            {
                // Extend dateTo to the end of the day
                var endOfDay = dateTo.Value.Date.AddDays(1).AddTicks(-1);
                dateFilter = "AND c.TransactionDate BETWEEN @DateFrom AND @DateTo";
                _logger.LogInformation("dateFrom={DateFrom}, dateTo={DateTo}", dateFrom.Value, endOfDay);
                _logger.LogInformation("SQL: {SQL}", sql);
                _logger.LogInformation("Parameters: @GroupId={GroupId}, @DateFrom={DateFrom}, @DateTo={DateTo}", groupId, dateFrom, dateTo);

                }
            else if (dateFrom.HasValue)
            {
                dateFilter = "AND c.TransactionDate >= @DateFrom";
                _logger.LogInformation("dateFrom={DateFrom}", dateFrom.Value);
            }
            else if (dateTo.HasValue)
            {
                var endOfDay = dateTo.Value.Date.AddDays(1).AddTicks(-1);
                dateFilter = "AND c.TransactionDate <= @DateTo";
                _logger.LogInformation("dateTo={DateTo}", endOfDay);
            }


                sql = string.Format(sql, dateFilter);

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@GroupId", groupId);
                if (dateFrom.HasValue) cmd.Parameters.AddWithValue("@DateFrom", dateFrom.Value);
                if (dateTo.HasValue) cmd.Parameters.AddWithValue("@DateTo", dateTo.Value.Date.AddDays(1).AddTicks(-1));


                conn.Open();
                using var reader = cmd.ExecuteReader();
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

                return contributions;
            }

        private List<GroupMemberSummary> GetGroupMemberSummaries(int groupId, DateTime? dateFrom = null, DateTime? dateTo = null)
        {
            var summaries = new List<GroupMemberSummary>();
            using var conn = new SqlConnection(_connectionString);

            // Base query
            var sql = @"
                SELECT 
                    m.FirstName + ' ' + m.LastName AS FullName,
                    ISNULL(SUM(c.ContributionAmount), 0) AS TotalPaid,
                    COUNT(CASE WHEN c.PenaltyAmount > 0 THEN 1 END) AS MissedPayments,
                    ISNULL(SUM(c.PenaltyAmount), 0) AS Penalties,
                    m.Status
                FROM Members m
                JOIN MemberGroups mg ON m.ID = mg.MemberID
                LEFT JOIN Contributions c ON mg.ID = c.MemberGroupID
                    {0}  -- date filter placeholder
                WHERE mg.GroupID = @GroupId
                GROUP BY m.FirstName, m.LastName, m.Status";

            // Build date filter conditions
            var dateFilter = "";
            if (dateFrom.HasValue && dateTo.HasValue)
                dateFilter = "AND c.TransactionDate BETWEEN @DateFrom AND @DateTo";
            else if (dateFrom.HasValue)
                dateFilter = "AND c.TransactionDate >= @DateFrom";
            else if (dateTo.HasValue)
                dateFilter = "AND c.TransactionDate <= @DateTo";

            // Inject filter into query
            sql = string.Format(sql, dateFilter);

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@GroupId", groupId);
            if (dateFrom.HasValue) cmd.Parameters.AddWithValue("@DateFrom", dateFrom.Value);
            if (dateTo.HasValue) cmd.Parameters.AddWithValue("@DateTo", dateTo.Value);

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
                    Status = reader["Status"].ToString()
                });
            }

            return summaries;
        }


    }
}