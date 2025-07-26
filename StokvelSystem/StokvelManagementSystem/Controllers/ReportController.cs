using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StokvelManagementSystem.Models;
using System.Data.SqlClient;
using System.Security.Claims;

namespace StokvelManagementSystem.Controllers
{
    public class ReportsController : Controller
    {
        private readonly string _connectionString;
        private readonly IConfiguration _configuration;

        public ReportsController(IConfiguration configuration)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("DefaultConnection");
        }

        [Authorize]
        public IActionResult MemberReport()
        {
            var memberId = User.FindFirst("member_id")?.Value;
            if (string.IsNullOrEmpty(memberId))
            {
                return RedirectToAction("Login", "Account");
            }

            var report = new Report
            {
                FirstName = GetMemberFirstName(memberId),
                LastName = GetMemberLastName(memberId),
                CurrentStatus = GetMemberStatus(memberId),
                GroupName = GetMemberGroupName(memberId),
                Contributions = GetMemberContributions(memberId),
                Date = DateTime.Now,
                Period = DateTime.Now.ToString("MMMM yyyy"),
                TotalContributionsPaid = CalculateTotalContributions(memberId),
                TotalMissedPayments = CountMissedPayments(memberId),
                PenaltiesApplied = CountPenalties(memberId)
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

        private List<ContributionViewModel> GetMemberContributions(string memberId)
        {
            var contributions = new List<ContributionViewModel>();

            using (var conn = new SqlConnection(_connectionString))
            {
                var cmd = new SqlCommand(@" SELECT c.TransactionDate AS Date, c.ContributionAmount AS Amount, pm.Method AS PaymentMethod, c.ProofOfPaymentPath AS ProofOfPayment, CASE WHEN c.PenaltyAmount > 0 THEN 'Missed' ELSE 'Paid' END AS Status
                                            FROM Contributions c
                                            JOIN PaymentMethods pm ON c.PaymentMethodID = pm.ID
                                            WHERE c.MemberGroupID IN (
                                            SELECT GroupID FROM MemberGroups WHERE MemberID = @MemberId
                                            
                                            )",
                                     conn);

                cmd.Parameters.AddWithValue("@MemberId", memberId);
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
            return contributions;
        }

        private decimal CalculateTotalContributions(string memberId)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                var cmd = new SqlCommand(@"SELECT ISNULL (SUM(ContributionAmount),0)
                                           FROM Contributions
                                           WHERE MemberGroupID IN (SELECT GroupID FROM MemberGroups WHERE MemberID = @MemberId)",
                                     conn);
                cmd.Parameters.AddWithValue("@MemberId", memberId);
                conn.Open();
                return Convert.ToDecimal(cmd.ExecuteScalar() ?? 0);
            }
        }

        private int CountMissedPayments(string memberId)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                var cmd = new SqlCommand(@" SELECT ISNULL(COUNT(*), 0)FROM Contributions
                                            WHERE PenaltyAmount > 0
                                            AND MemberGroupID IN ( SELECT GroupID FROM MemberGroups WHERE MemberID = @MemberId)",
                                     conn);
                cmd.Parameters.AddWithValue("@MemberId", memberId);
                conn.Open();
                return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
            }
        }

        private int CountPenalties(string memberId)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                var cmd = new SqlCommand(@"SELECT ISNULL(COUNT(*), 0) FROM Contributions WHERE PenaltyAmount > 0 AND MemberGroupID IN (SELECT GroupID FROM MemberGroups WHERE MemberID = @MemberId)",
                 conn);
                cmd.Parameters.AddWithValue("@MemberId", memberId);
                conn.Open();
                return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
            }
        }


        // This is for Group Reports
        [Authorize(Roles = "Admin")]
        public IActionResult GroupReport(int groupId)
        {
            var memberSummaries = GetGroupMemberSummaries(groupId);
            var report = new Report
            
            {
                GroupName = GetGroupName(groupId),
                Currency = GetGroupCurrency(groupId),
                TotalMembers = CountGroupMembers(groupId),
                GroupStartDate = GetGroupStartDate(groupId),
                ContributionFrequency = GetGroupFrequency(groupId),
                GroupTotalContributions = CalculateGroupTotalContributions(groupId),
                Date = DateTime.Now,
                Period = DateTime.Now.ToString("MMMM yyyy"),
                Contributions = GetGroupContributions(groupId),
                MemberSummaries = memberSummaries,

                // Calculating the total number of my penalties and the amount of all penalties
                TotalGroupPenalties = memberSummaries.Count(m => m.Penalties > 0),      
                TotalPenaltiesAmount = memberSummaries.Sum(m => m.Penalties)     
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

        private decimal CalculateGroupTotalContributions(int groupId)
        {
            using var conn = new SqlConnection(_connectionString);
            var cmd = new SqlCommand(@"SELECT ISNULL(SUM(ContributionAmount), 0)
                                    FROM Contributions WHERE MemberGroupID = @GroupId", conn);
            cmd.Parameters.AddWithValue("@GroupId", groupId);
            conn.Open();
            return Convert.ToDecimal(cmd.ExecuteScalar());
        }

        private List<ContributionViewModel> GetGroupContributions(int groupId)
        {
            var contributions = new List<ContributionViewModel>();

            using var conn = new SqlConnection(_connectionString);
            var cmd = new SqlCommand(@"
                SELECT c.TransactionDate AS Date, c.ContributionAmount AS Amount, 
                    pm.Method AS PaymentMethod, c.ProofOfPaymentPath AS ProofOfPayment, 
                    CASE WHEN c.PenaltyAmount > 0 THEN 'Missed' ELSE 'Paid' END AS Status
                FROM Contributions c
                JOIN PaymentMethods pm ON c.PaymentMethodID = pm.ID
                WHERE c.MemberGroupID = @GroupId", conn);

            cmd.Parameters.AddWithValue("@GroupId", groupId);
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
        private List<GroupMemberSummary> GetGroupMemberSummaries(int groupId)
        {
            var summaries = new List<GroupMemberSummary>();
            using var conn = new SqlConnection(_connectionString);
            var cmd = new SqlCommand(@" SELECT m.FirstName + ' ' + m.LastName AS FullName,
                                        ISNULL(SUM(c.ContributionAmount), 0) AS TotalPaid,
                                        COUNT(CASE WHEN c.PenaltyAmount > 0 THEN 1 END) AS MissedPayments,
                                        ISNULL(SUM(c.PenaltyAmount), 0) AS Penalties, m.Status
                                        FROM Members m
                                        JOIN MemberGroups mg ON m.ID = mg.MemberID
                                        LEFT JOIN Contributions c ON mg.ID = c.MemberGroupID
                                        WHERE mg.GroupID = @GroupId
                                        GROUP BY m.FirstName, m.LastName, m.Status",
                                 conn);

            cmd.Parameters.AddWithValue("@GroupId", groupId);
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