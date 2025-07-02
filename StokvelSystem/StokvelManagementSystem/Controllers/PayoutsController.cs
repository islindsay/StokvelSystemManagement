//using Microsoft.AspNetCore.Authorization;
//using Microsoft.AspNetCore.Mvc;
//using Microsoft.AspNetCore.Mvc.Rendering;
//using Microsoft.Extensions.Configuration;
//using StokvelManagementSystem.Models;
//using System;
//using System.Collections.Generic;
//using System.Data.SqlClient;

//namespace StokvelManagementSystem.Controllers
//{
//    [Authorize]
//    public class PayoutsController : Controller
//    {
//        private readonly string _connectionString;

//        public PayoutsController(IConfiguration configuration)
//        {
//            _connectionString = configuration.GetConnectionString("DefaultConnection");
//        }

//        // GET: Payouts Dashboard
//        public ActionResult Index()
//        {
//            var model = new PayoutDashboardViewModel
//            {
//                ActiveSchedules = GetActiveSchedules(),
//                UpcomingCycles = GetUpcomingCycles()
//            };

//            return View(model);
//        }

//        // GET: Member Payouts List
//        public ActionResult MemberPayouts()
//        {
//            var payouts = GetMemberPayouts();
//            return View(payouts);
//        }

//        [Authorize(Roles = "Admin")]
//        public ActionResult Create()
//        {
//            var model = new PayoutCreateViewModel
//            {
//                Members = new SelectList(GetAllMembers(), "ID", "FullName"),
//                PayoutTypes = new SelectList(GetAllPayoutTypes(), "ID", "PayoutTypeName")
//            };
//            return View(model);
//        }

//        [HttpPost]
//        [Authorize(Roles = "Admin")]
//        [ValidateAntiForgeryToken]
//        public ActionResult Create(PayoutCreateViewModel model)
//        {
//            if (ModelState.IsValid)
//            {
//                CreatePayout(model);
//                return RedirectToAction(nameof(MemberPayouts));
//            }

//            // Repopulate dropdowns if validation fails
//            model.Members = new SelectList(GetAllMembers(), "ID", "FullName", model.MemberID);
//            model.PayoutTypes = new SelectList(GetAllPayoutTypes(), "ID", "PayoutTypeName", model.PayoutTypeID);
//            return View(model);
//        }

//        // Existing methods for schedules and cycles...
//        // Keep all the existing methods (GetActiveSchedules, GetUpcomingCycles, etc.)
//        // They can remain exactly as they are

//        // Add these new methods for member payouts
//        private List<MemberPayout> GetMemberPayouts()
//        {
//            var payouts = new List<MemberPayout>();

//            using (var connection = new SqlConnection(_connectionString))
//            using (var command = new SqlCommand(
//                @"SELECT p.*, m.FullName AS MemberName, pt.PayoutTypeName 
//                  FROM Payouts p
//                  JOIN Members m ON p.MemberID = m.ID
//                  JOIN PayoutTypes pt ON p.PayoutTypeID = pt.ID
//                  ORDER BY p.PayoutDate DESC", connection))
//            {
//                connection.Open();
//                using (var reader = command.ExecuteReader())
//                {
//                    while (reader.Read())
//                    {
//                        payouts.Add(new MemberPayout
//                        {
//                            ID = Convert.ToInt32(reader["ID"]),
//                            MemberID = Convert.ToInt32(reader["MemberID"]),
//                            MemberName = reader["MemberName"].ToString(),
//                            PayoutType = reader["PayoutTypeName"].ToString(),
//                            Amount = Convert.ToDecimal(reader["Amount"]),
//                            PayoutDate = Convert.ToDateTime(reader["PayoutDate"]),
//                            Notes = reader["Notes"]?.ToString()
//                        });
//                    }
//                }
//            }
//            return payouts;
//        }

//        private List<dynamic> GetAllMembers()
//        {
//            var members = new List<dynamic>();
//            using (var connection = new SqlConnection(_connectionString))
//            using (var command = new SqlCommand("SELECT ID, FullName FROM Members", connection))
//            {
//                connection.Open();
//                using (var reader = command.ExecuteReader())
//                {
//                    while (reader.Read())
//                    {
//                        members.Add(new
//                        {
//                            ID = Convert.ToInt32(reader["ID"]),
//                            FullName = reader["FullName"].ToString()
//                        });
//                    }
//                }
//            }
//            return members;
//        }

//        private void CreatePayout(PayoutCreateViewModel model)
//        {
//            using (var connection = new SqlConnection(_connectionString))
//            using (var command = new SqlCommand(
//                @"INSERT INTO Payouts 
//                  (MemberID, PayoutTypeID, Amount, PayoutDate, Notes)
//                  VALUES 
//                  (@MemberID, @PayoutTypeID, @Amount, @PayoutDate, @Notes)", connection))
//            {
//                command.Parameters.AddWithValue("@MemberID", model.MemberID);
//                command.Parameters.AddWithValue("@PayoutTypeID", model.PayoutTypeID);
//                command.Parameters.AddWithValue("@Amount", model.Amount);
//                command.Parameters.AddWithValue("@PayoutDate", model.PayoutDate);
//                command.Parameters.AddWithValue("@Notes", model.Notes ?? (object)DBNull.Value);

//                connection.Open();
//                command.ExecuteNonQuery();
//            }
//        }
//    }
//}