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
        private readonly ILogger<GroupsController> _logger;

        public RegistrationController(IConfiguration configuration, ILogger<GroupsController> logger)
        {
            _configuration = configuration;
            _connectionString = configuration.GetConnectionString("DefaultConnection");
            _logger = logger;
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
        //[ValidateAntiForgeryToken]
    
        public IActionResult Index(Member model, bool create)
        {

            LoadGenderDropdown();

            if (!ModelState.IsValid)
            {
                var errors = string.Join("; ", ModelState.Values
                                            .SelectMany(v => v.Errors)
                                            .Select(e => e.ErrorMessage));

                _logger.LogWarning("Model validation errors: " + errors);

                return View(model);

            }
            if (model.Password != model.ConfirmPassword)
            {
                ModelState.AddModelError("", "Passwords do not match.");
                _logger.LogWarning("Passwords do not match.");
                return View(model);
            }


            int newMemberId;
            int newLoginId;

            try
            {
                using (SqlConnection connection = new SqlConnection(_connectionString))
                {
                    connection.Open();
                    using (SqlTransaction transaction = connection.BeginTransaction())
                    {
                        try
                        {
                          // Step 0: Check if member already exists
               // Check NationalID
                string checkNationalId = "SELECT COUNT(*) FROM Members WHERE NationalID = @NationalID";
                using (SqlCommand cmd = new SqlCommand(checkNationalId, connection, transaction))
                {
                    cmd.Parameters.AddWithValue("@NationalID", model.NationalID);
                    if ((int)cmd.ExecuteScalar() > 0)
                        ModelState.AddModelError("NationalID", "National ID already exists.");
                }

                // Check Email
                string checkEmail = "SELECT COUNT(*) FROM Members WHERE Email = @Email";
                using (SqlCommand cmd = new SqlCommand(checkEmail, connection, transaction))
                {
                    cmd.Parameters.AddWithValue("@Email", model.Email);
                    if ((int)cmd.ExecuteScalar() > 0)
                        ModelState.AddModelError("Email", "Email already exists.");
                }

                // Check Phone
                if (!string.IsNullOrEmpty(model.Phone))
                {
                    string checkPhone = "SELECT COUNT(*) FROM Members WHERE Phone = @Phone";
                    using (SqlCommand cmd = new SqlCommand(checkPhone, connection, transaction))
                    {
                        cmd.Parameters.AddWithValue("@Phone", model.Phone);
                        if ((int)cmd.ExecuteScalar() > 0)
                            ModelState.AddModelError("Phone", "Phone already exists.");
                    }
                }
                
                  // Check Username
                if (!string.IsNullOrEmpty(model.Username))
                {
                    string checkUsername = "SELECT COUNT(*) FROM Logins WHERE Username = @Username";
                    using (SqlCommand cmd = new SqlCommand(checkUsername, connection, transaction))
                    {
                        cmd.Parameters.AddWithValue("@Username", model.Username);
                        if ((int)cmd.ExecuteScalar() > 0)
                            ModelState.AddModelError("Username", "Username already exists.");
                    }
                }

                // If any errors, return immediately
                            if (!ModelState.IsValid)
                            {
                                // MVC: return View(model);
                                // API: return BadRequest(ModelState);
                                return View(model);
                            }



                        // Step 1: Insert into Members
                        string memberQuery = @"
                            INSERT INTO Members (FirstName, MiddleName, LastName, DOB, NationalID, Phone, Email, GenderID, Address, RegistrationDate, Status)
                            VALUES (@FirstName, @MiddleName, @LastName, @DOB, @NationalID, @Phone, @Email, @GenderID, @Address, @RegistrationDate, @Status);
                            SELECT CAST(scope_identity() AS int)";

                        using (SqlCommand memberCommand = new SqlCommand(memberQuery, connection, transaction))
                        {
                            memberCommand.Parameters.AddWithValue("@FirstName", model.FirstName);
                            memberCommand.Parameters.AddWithValue("@MiddleName", (object)model.MiddleName ?? DBNull.Value);
                            memberCommand.Parameters.AddWithValue("@LastName", model.LastName);
                            memberCommand.Parameters.AddWithValue("@DOB", model.DOB);
                            memberCommand.Parameters.AddWithValue("@NationalID", model.NationalID);
                            memberCommand.Parameters.AddWithValue("@Phone", (object)model.Phone ?? DBNull.Value);
                            memberCommand.Parameters.AddWithValue("@Email", model.Email);
                            memberCommand.Parameters.AddWithValue("@GenderID", model.GenderID);
                            memberCommand.Parameters.AddWithValue("@Address", (object)model.Address ?? DBNull.Value);
                            memberCommand.Parameters.AddWithValue("@RegistrationDate", DateTime.Now);
                            memberCommand.Parameters.AddWithValue("@Status", 1);

                            newMemberId = (int)memberCommand.ExecuteScalar();
                        }


                            // Step 2: Create login credentials
                            var saltBytes = new byte[16];
                            using (var rng = new System.Security.Cryptography.RNGCryptoServiceProvider())
                            {
                                rng.GetBytes(saltBytes);
                            }
                            string salt = Convert.ToBase64String(saltBytes);
                            string hashedPassword = HashPassword(model.Password, salt);

                            // Step 3: Insert into Logins and get the new Login ID
                            string loginQuery = @"
                                INSERT INTO Logins (Username, PasswordHash, PasswordSalt, MemberID, NationalID)
                                VALUES (@Username, @PasswordHash, @PasswordSalt, @MemberID, @NationalID);
                                SELECT CAST(scope_identity() AS int)";

                            using (SqlCommand loginCommand = new SqlCommand(loginQuery, connection, transaction))
                            {
                                loginCommand.Parameters.AddWithValue("@Username", model.Username);
                                loginCommand.Parameters.AddWithValue("@PasswordHash", hashedPassword);
                                loginCommand.Parameters.AddWithValue("@PasswordSalt", salt);
                                loginCommand.Parameters.AddWithValue("@NationalID", model.NationalID);
                                loginCommand.Parameters.AddWithValue("@MemberID", newMemberId);
                                newLoginId = (int)loginCommand.ExecuteScalar();
                            }

                            // Step 4: Update the Members table with the new UserID from Logins
                            string updateMemberQuery = "UPDATE Members SET UserID = @UserID WHERE ID = @MemberID";
                            using (SqlCommand updateCommand = new SqlCommand(updateMemberQuery, connection, transaction))
                            {
                                updateCommand.Parameters.AddWithValue("@UserID", newLoginId);
                                updateCommand.Parameters.AddWithValue("@MemberID", newMemberId);
                                updateCommand.ExecuteNonQuery();
                            }

                            transaction.Commit();
                        }
                        catch (Exception error)
                        {
                            _logger.LogInformation("DB Error: " + error.Message);
                            ModelState.AddModelError("", "Error saving member: " + error.Message);
                            transaction.Rollback();
                            throw;
                        }
                    }
                }
                string token = GenerateJwtToken(newMemberId, newLoginId, model.Username, model.FirstName, model.NationalID);

                bool isAdmin = CheckIsAdmin(newLoginId);

                // Store the token in a cookie (or return it however your frontend expects)
                HttpContext.Response.Cookies.Append("jwt", token, new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Strict,
                    Expires = DateTime.UtcNow.AddHours(2)
                });

                if (create)
                {
                    // Redirect to group creation screen for admins
                    return RedirectToAction("ListGroups", "Groups", new { showCreate = true });
                }

                return RedirectToAction("ListGroups", "Groups", new { showCreate = true });

            }
            catch (Exception ex)
            {
                _logger.LogInformation("DB Error: " + ex.ToString());
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

        private string GenerateJwtToken(int memberId, int userId, string username, string firstname, string nationalId)
        {

            _logger.LogInformation("Generating JWT Token with values: MemberId={MemberId}, UserId={UserId}, Username={Username}, FirstName={FirstName}, NationalID={NationalID}",
            memberId, userId, username, firstname, nationalId);

            var key = Encoding.ASCII.GetBytes(_configuration["Jwt:Key"]);
            var tokenHandler = new JwtSecurityTokenHandler();

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, memberId.ToString()),
                new Claim(ClaimTypes.Name, username),
                new Claim(ClaimTypes.GivenName, firstname),
                new Claim(ClaimTypes.Role, "Admin"),

                new Claim("member_id", memberId.ToString()),
                new Claim("national_id", nationalId),
                new Claim("user_id", userId.ToString()) // ✅ THIS IS WHAT YOU NEED
            };

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddHours(2),
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            _logger.LogInformation("Generated JWT Token: {Token}", token);
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
