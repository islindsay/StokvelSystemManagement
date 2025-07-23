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
    }
}