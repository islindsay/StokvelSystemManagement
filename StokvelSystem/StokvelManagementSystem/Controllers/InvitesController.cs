using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace StokvelManagementSystem.Controllers
{
    [Route("Invites")]
    public class InvitesController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<InvitesController> _logger;



        public InvitesController(IConfiguration configuration, ILogger<InvitesController> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        [HttpPost("AcceptJoinRequest/{requestId:int}")]
        public IActionResult AcceptJoinRequest(int requestId)
        {
            if (requestId <= 0)
                return BadRequest("Invalid request ID.");

            try
            {
                using (var conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
                {
                    conn.Open();

                    int memberId = 0;
                    int groupId = 0;

                    using (var getCmd = new SqlCommand("SELECT MemberID, GroupID FROM JoinRequests WHERE ID = @RequestId", conn))
                    {
                        getCmd.Parameters.AddWithValue("@RequestId", requestId);
                        using (var reader = getCmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                memberId = Convert.ToInt32(reader["MemberID"]);
                                groupId = Convert.ToInt32(reader["GroupID"]);
                            }
                            else
                            {
                                return NotFound("Join request not found.");
                            }
                        }
                    }

                    using (var updateCmd = new SqlCommand(@"UPDATE JoinRequests SET Status = 'Accepted' WHERE ID = @RequestID", conn))
                    {
                        updateCmd.Parameters.AddWithValue("@RequestID", requestId);
                        updateCmd.ExecuteNonQuery();
                    }

                    using (var insertCmd = new SqlCommand("INSERT INTO MemberGroups (MemberID, GroupID, RoleID) VALUES (@MemberID, @GroupID, 2)", conn))
                    {
                        insertCmd.Parameters.AddWithValue("@MemberID", memberId);
                        insertCmd.Parameters.AddWithValue("@GroupID", groupId);
                        insertCmd.ExecuteNonQuery();
                    }
                }

                return Ok("Join request accepted and member added to the group.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while accepting join request ID {RequestId}", requestId);
                return StatusCode(500, "Internal server error.");
            }
        }

        [HttpPost("RejectJoinRequest/{requestId:int}")]
        public IActionResult RejectJoinRequest(int requestId)
        {
            if (requestId <= 0)
                return BadRequest("Invalid request ID.");

            try
            {
                using (var conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
                {
                    conn.Open();

                    using (var updateCmd = new SqlCommand(@"UPDATE JoinRequests SET Status = 'Rejected' WHERE ID = @RequestID", conn))
                    {
                        updateCmd.Parameters.AddWithValue("@RequestID", requestId);
                        updateCmd.ExecuteNonQuery();
                    }
                }

                return Ok("Join request rejected.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while rejecting join request ID {RequestId}", requestId);
                return StatusCode(500, "Internal server error.");
            }
        }

        [HttpPost("DeleteJoinRequest/{requestId:int}")]
        public IActionResult DeleteJoinRequest(int requestId)
        {
            if (requestId <= 0)
                return BadRequest("Invalid request ID.");

            try
            {
                using (var conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
                {
                    conn.Open();

                    using (var deleteCmd = new SqlCommand("DELETE FROM JoinRequests WHERE ID = @RequestID AND Status = 'Pending'", conn))
                    {
                        deleteCmd.Parameters.AddWithValue("@RequestID", requestId);
                        int rowsAffected = deleteCmd.ExecuteNonQuery();

                        if (rowsAffected == 0)
                        {
                            // Check if the request exists to provide a more specific error
                            using (var checkCmd = new SqlCommand("SELECT COUNT(*) FROM JoinRequests WHERE ID = @RequestID", conn))
                            {
                                checkCmd.Parameters.AddWithValue("@RequestID", requestId);
                                bool exists = (int)checkCmd.ExecuteScalar() > 0;
                                if (exists)
                                {
                                    return BadRequest("Cannot delete this request. It may have already been processed.");
                                }
                                else
                                {
                                    return NotFound("Join request not found.");
                                }
                            }
                        }
                    }
                }

                return Ok("Join request deleted successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while deleting join request ID {RequestId}", requestId);
                return StatusCode(500, "Internal server error.");
            }
        }


        // 🔹 Action methods go here

        [HttpGet("GroupStats/{groupId}")]
        public IActionResult GroupStats(int groupId)
        {
            var (memberCount, memberLimit) = GetGroupMembershipStats(groupId);
            return Ok(new { memberCount, memberLimit });
        }


        private (int memberCount, int memberLimit) GetGroupMembershipStats(int groupId)
        {
        int memberCount = 0;
        int memberLimit = 0;

        using (var conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
        {
            conn.Open();

            using (var countCmd = new SqlCommand("SELECT COUNT(*) FROM MemberGroups WHERE GroupID = @GroupID", conn))
            {
                countCmd.Parameters.AddWithValue("@GroupID", groupId);
                memberCount = (int)countCmd.ExecuteScalar();
            }

            using (var limitCmd = new SqlCommand("SELECT MemberLimit FROM Groups WHERE ID = @GroupID", conn))
            {
                limitCmd.Parameters.AddWithValue("@GroupID", groupId);
                var result = limitCmd.ExecuteScalar();
                if (result != null && result != DBNull.Value)
                {
                    memberLimit = Convert.ToInt32(result);
                }
            }
        }

        return (memberCount, memberLimit);
    }
    }
}
