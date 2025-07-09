using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Data.SqlClient;
using StokvelManagementSystem.Models;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Data;
using System.Diagnostics;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace StokvelManagementSystem.Controllers
{
    public class RegistrationController : Controller
    {
        private readonly string _connectionString;
        private readonly IConfiguration _configuration;
        public RegistrationController(IConfiguration configuration)
        {
            _configuration = configuration;
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        private void LoadGenderDropdown()
        {
            List<Genders> genderList = new List<Genders>();
            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                string query = "SELECT ID, Gender FROM Gender";
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    connection.Open();
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            genderList.Add(new Genders
                            {
                                ID = Convert.ToInt32(reader["ID"]),
                                Gender = reader["Gender"].ToString()
                            });
                        }
                    }
                }
            }
            ViewBag.GenderList = new SelectList(genderList, "ID", "Gender");
        }

        public IActionResult Index()
        {
            LoadGenderDropdown();
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Index(Member model, string actionType)
        {
           
            LoadGenderDropdown();

            if (!ModelState.IsValid)
            {
                var errors = string.Join("; ", ModelState.Values
                                            .SelectMany(v => v.Errors)
                                            .Select(e => e.ErrorMessage));

                Debug.WriteLine("Model validation errors: " + errors);

                return View(model);

            }
            if (model.Password != model.ConfirmPassword)
            {
                ModelState.AddModelError("", "Passwords do not match.");
                return View(model);
            }


            int newMemberId;

            try
            {
                using (SqlConnection connection = new SqlConnection(_connectionString))
                {
                    string query = @"
                INSERT INTO Members (FirstName, MiddleName, LastName, DOB, NationalID, Phone, Email, GenderID, Address, RegistrationDate, StatusID)
                VALUES (@FirstName, @MiddleName, @LastName, @DOB, @NationalID, @Phone, @Email, @GenderID, @Address, @RegistrationDate, @StatusID);
                SELECT CAST(scope_identity() AS int)";

                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@FirstName", model.FirstName);
                        command.Parameters.AddWithValue("@MiddleName", (object)model.MiddleName ?? DBNull.Value);
                        command.Parameters.AddWithValue("@LastName", model.LastName);
                        command.Parameters.AddWithValue("@DOB", model.DOB);
                        command.Parameters.AddWithValue("@NationalID", model.NationalID);
                        command.Parameters.AddWithValue("@Phone", (object)model.Phone ?? DBNull.Value);
                        command.Parameters.AddWithValue("@Email", model.Email);
                        command.Parameters.AddWithValue("@GenderID", model.GenderID);
                        command.Parameters.AddWithValue("@Address", (object)model.Address ?? DBNull.Value);
                        command.Parameters.AddWithValue("@RegistrationDate", DateTime.Now);
                        command.Parameters.AddWithValue("@StatusID", 1);

                        connection.Open();
                        newMemberId = (int)command.ExecuteScalar();
                    }

                }
                var saltBytes = new byte[16];
                using (var rng = new System.Security.Cryptography.RNGCryptoServiceProvider())
                {
                    rng.GetBytes(saltBytes);
                }
                string salt = Convert.ToBase64String(saltBytes);
                string hashedPassword = HashPassword(model.Password, salt);
                using (SqlConnection connection = new SqlConnection(_connectionString))
                {
                    string loginQuery = @"
                INSERT INTO Logins (Username, PasswordHash, PasswordSalt, MemberID, NationalID)
                VALUES (@Username, @PasswordHash, @PasswordSalt, @MemberID, @NationalID)";

                    using (SqlCommand cmd = new SqlCommand(loginQuery, connection))
                    {
                        cmd.Parameters.AddWithValue("@Username", model.Username);
                        cmd.Parameters.AddWithValue("@PasswordHash", hashedPassword);
                        cmd.Parameters.AddWithValue("@PasswordSalt", salt);
                        cmd.Parameters.AddWithValue("@NationalID", model.NationalID);
                        cmd.Parameters.AddWithValue("@MemberID", newMemberId);

                        connection.Open();
                        cmd.ExecuteNonQuery();
                    }
                }
                string token = GenerateJwtToken(newMemberId, model.Username, model.FirstName, model.NationalID); // Add this method (see below)

                bool isAdmin = CheckIsAdmin(newMemberId);

                // Store the token in a cookie (or return it however your frontend expects)
                HttpContext.Response.Cookies.Append("jwt", token, new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Strict,
                    Expires = DateTime.UtcNow.AddHours(2)
                });


                if (isAdmin)
                {
                    return RedirectToAction("ListGroups", "Groups", new { memberId = newMemberId, showCreate = true });
                }
                else
                {

                    return RedirectToAction("ListGroups", "Groups", new { memberId = newMemberId, showJoinedTab = true });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("DB Error: " + ex.ToString());
                ModelState.AddModelError("", "Error saving member: " + ex.Message);
                return View(model);
            }
        }
        private string HashPassword(string password, string salt)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var combined = Encoding.UTF8.GetBytes(password + salt);
            var hash = sha.ComputeHash(combined);
            return Convert.ToBase64String(hash);
        }

        private bool CheckIsAdmin(int userId)
        {
            
            using var conn = new SqlConnection(_connectionString);
            conn.Open();

            string query = @" SELECT COUNT(*) FROM MemberGroups WHERE MemberID = (SELECT MemberID FROM Logins WHERE ID = @UserId ) AND RoleID = 1"; 
            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@UserId", userId);

            int count = (int)cmd.ExecuteScalar();
            return count > 0;
        }

        private string GenerateJwtToken(int memberId, string username, string firstname, string nationalId)
        {
            var key = Encoding.ASCII.GetBytes(_configuration["Jwt:Key"]);
            var tokenHandler = new JwtSecurityTokenHandler();

            var claims = new[]
            {
        new Claim(ClaimTypes.NameIdentifier, memberId.ToString()),
        new Claim(ClaimTypes.Name, username),
        new Claim(ClaimTypes.GivenName, firstname),
        new Claim(ClaimTypes.Role, "Admin"),
        new Claim("member_id", memberId.ToString()),
        new Claim("national_id", nationalId)
    };

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddHours(2),
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }




        [HttpGet]
        public IActionResult Search(string query)
        {
            List<Member> results = new List<Member>();

            if (string.IsNullOrWhiteSpace(query))
            {
                ModelState.AddModelError("", "Please enter a NationalID to search.");
                return View("SearchResults", results);
            }

            try
            {
                using (SqlConnection connection = new SqlConnection(_connectionString))
                {
                    string sql = @"
    SELECT m.*, g.Gender AS GenderText
    FROM Members m
    LEFT JOIN Gender g ON m.GenderID = g.ID    WHERE m.NationalID = @NationalID";

                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        command.Parameters.Add("@NationalID", SqlDbType.VarChar, 11).Value = query.Trim();
                        connection.Open();

                        Console.WriteLine($"Executing search for NationalID: '{query.Trim()}'");

                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                Console.WriteLine("Match found: " + reader["NationalID"]);
                                results.Add(new Member
                                {
                                    FirstName = reader["FirstName"].ToString(),
                                    MiddleName = reader["MiddleName"].ToString(),
                                    LastName = reader["LastName"].ToString(),
                                    DOB = Convert.ToDateTime(reader["DOB"]),
                                    NationalID = reader["NationalID"].ToString(),
                                    Phone = reader["Phone"].ToString(),
                                    Email = reader["Email"].ToString(),
                                    GenderID = Convert.ToInt32(reader["GenderID"]),
                                    GenderText = reader["GenderText"] == DBNull.Value ? "Not Set" : reader["GenderText"].ToString(),
                                    RegistrationDate = Convert.ToDateTime(reader["RegistrationDate"]),
                                    Address= reader["Address"].ToString(),
                                  
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Error fetching search results: " + ex.Message);
            }

            if (results.Count == 0)
            {
                Console.WriteLine("No results found for query: " + query);
            }

            return View("SearchResults", results);
        }

    }
}
