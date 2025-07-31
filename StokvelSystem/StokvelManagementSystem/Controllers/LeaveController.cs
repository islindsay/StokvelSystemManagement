using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Data.SqlClient;

namespace StokvelManagementSystem.Controllers
{
    [Route("Leave")]
    public class LeaveController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<LeaveController> _logger;

        public LeaveController(IConfiguration configuration, ILogger<LeaveController> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        [HttpPost("Request")]
        public IActionResult RequestLeave(int memberId, int groupId)
        {
            if (memberId <= 0 || groupId <= 0)
                return BadRequest("Invalid member ID or group ID.");

            try
            {
                using (var conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
                {
                    conn.Open();

                    // Check if a pending leave request already exists
                    using (var checkCmd = new SqlCommand(
                        "SELECT COUNT(*) FROM LeaveRequests WHERE MemberID = @MemberID AND GroupID = @GroupID AND Status = 'Pending'", conn))
                    {
                        checkCmd.Parameters.AddWithValue("@MemberID", memberId);
                        checkCmd.Parameters.AddWithValue("@GroupID", groupId);
                        int count = (int)checkCmd.ExecuteScalar();

                        if (count > 0)
                        {
                            return BadRequest("A leave request is already pending.");
                        }
                    }

                    // Insert the new leave request
                    using (var insertCmd = new SqlCommand(
                        "INSERT INTO LeaveRequests (MemberID, GroupID, Status, RequestedDate) VALUES (@MemberID, @GroupID, 'Pending', @RequestedDate)", conn))
                    {
                        insertCmd.Parameters.AddWithValue("@MemberID", memberId);
                        insertCmd.Parameters.AddWithValue("@GroupID", groupId);
                        insertCmd.Parameters.AddWithValue("@RequestedDate", DateTime.UtcNow);
                        insertCmd.ExecuteNonQuery();
                    }
                }

                return Ok("Leave request submitted successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting leave request.");
                return StatusCode(500, "An error occurred while processing your request.");
            }
        }

        [HttpPost("ApproveLeaveRequest")]
        public IActionResult ApproveLeaveRequest(int requestId)
        {
            try
            {
                using (var conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
                {
                    conn.Open();

                    int memberId = 0;
                    int groupId = 0;

                    // Get member and group IDs from the leave request
                    using (var getCmd = new SqlCommand("SELECT MemberID, GroupID FROM LeaveRequests WHERE ID = @RequestId", conn))
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
                                return NotFound("Leave request not found.");
                            }
                        }
                    }

                    // Update leave request status
                    using (var updateCmd = new SqlCommand("UPDATE LeaveRequests SET Status = 'Accepted' WHERE ID = @RequestId", conn))
                    {
                        updateCmd.Parameters.AddWithValue("@RequestId", requestId);
                        updateCmd.ExecuteNonQuery();
                    }

                    // Remove member from group
                    using (var deleteCmd = new SqlCommand("DELETE FROM MemberGroups WHERE MemberID = @MemberID AND GroupID = @GroupID", conn))
                    {
                        deleteCmd.Parameters.AddWithValue("@MemberID", memberId);
                        deleteCmd.Parameters.AddWithValue("@GroupID", groupId);
                        deleteCmd.ExecuteNonQuery();
                    }
                }

                return Ok("Leave request approved and member removed from group.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error approving leave request {RequestId}", requestId);
                return StatusCode(500, "Internal server error.");
            }
        }

        [HttpPost("RejectLeaveRequest")]
        public IActionResult RejectLeaveRequest(int requestId)
        {
            try
            {
                using (var conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
                {
                    conn.Open();

                    using (var cmd = new SqlCommand("UPDATE LeaveRequests SET Status = 'Rejected' WHERE ID = @RequestId", conn))
                    {
                        cmd.Parameters.AddWithValue("@RequestId", requestId);
                        int rows = cmd.ExecuteNonQuery();

                        if (rows == 0)
                        {
                            return NotFound("Leave request not found.");
                        }
                    }
                }

                return Ok("Leave request rejected.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error rejecting leave request {RequestId}", requestId);
                return StatusCode(500, "Internal server error.");
            }
        }

        [HttpDelete("DeleteLeaveRequest")]
        public IActionResult DeleteLeaveRequest(int requestId)
        {
            try
            {
                using (var conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
                {
                    conn.Open();

                    var deleteSql = @"DELETE FROM LeaveRequests 
                                    WHERE ID = @RequestId AND Status = 'Pending'";

                    using (var deleteCmd = new SqlCommand(deleteSql, conn))
                    {
                        deleteCmd.Parameters.AddWithValue("@RequestId", requestId);
                        int rowsAffected = deleteCmd.ExecuteNonQuery();

                        if (rowsAffected == 0)
                        {
                            return NotFound("Pending leave request not found or already processed.");
                        }
                    }
                }

                return Ok("Pending leave request deleted.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting leave request {RequestId}", requestId);
                return StatusCode(500, "Internal server error.");
            }
        }



    }
}
