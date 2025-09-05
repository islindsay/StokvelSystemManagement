using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Data.SqlClient;
using StokvelManagementSystem.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace StokvelManagementSystem.Controllers
{
    public class ProfileController : Controller
    {
        private readonly string _connectionString;
        private readonly IConfiguration _configuration;

        public ProfileController(IConfiguration configuration)
        {
            _configuration = configuration;
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        private int GetLoggedInUserId()
        {
            var jwtCookie = Request.Cookies["jwt"];
            if (string.IsNullOrEmpty(jwtCookie))
                throw new Exception("User not logged in.");

            var handler = new JwtSecurityTokenHandler();
            var token = handler.ReadJwtToken(jwtCookie);

            var userIdClaim = token.Claims.FirstOrDefault(c => c.Type == "user_id");
            if (userIdClaim == null)
                throw new Exception("Invalid token: user_id missing.");

            return int.Parse(userIdClaim.Value);
        }

        private void LoadGenderDropdown()
        {
            var genderList = new List<Genders>();
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using var cmd = new SqlCommand("SELECT ID, Gender FROM Gender", conn);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    genderList.Add(new Genders
                    {
                        ID = Convert.ToInt32(reader["ID"]),
                        Gender = reader["Gender"].ToString()
                    });
                }
            }
            ViewBag.GenderList = new SelectList(genderList, "ID", "Gender");
        }

        [HttpGet]
        public IActionResult Index()
        {
            var jwtCookie = HttpContext.Request.Cookies["jwt"];
            if (string.IsNullOrEmpty(jwtCookie))
            {
                // Guest, redirect to login
                return RedirectToAction("Login", "Account"); // replace "Account" with your login controller if different
            }

            var handler = new JwtSecurityTokenHandler();
            JwtSecurityToken token;
            try
            {
                token = handler.ReadJwtToken(jwtCookie);
            }
            catch
            {
                // Invalid token, redirect to login
                return RedirectToAction("Login", "Account");
            }

            var uniqueNameClaim = token.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name || c.Type == "unique_name");
            if (uniqueNameClaim == null)
            {
                // No username claim, redirect
                return RedirectToAction("Login", "Account");
            }

            int userId = GetLoggedInUserId(); // your existing method
            LoadGenderDropdown();

            Member model;
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                string sql = @"
                    SELECT m.*, l.Username, l.NationalID AS LoginNationalID
                    FROM Members m
                    JOIN Logins l ON l.MemberID = m.ID
                    WHERE l.ID = @UserId";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@UserId", userId);

                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    model = new Member
                    {
                        FirstName = reader["FirstName"].ToString(),
                        MiddleName = reader["MiddleName"].ToString(),
                        LastName = reader["LastName"].ToString(),
                        DOB = Convert.ToDateTime(reader["DOB"]),
                        Phone = reader["Phone"].ToString(),
                        Email = reader["Email"].ToString(),
                        GenderID = Convert.ToInt32(reader["GenderID"]),
                        Address = reader["Address"].ToString(),
                        RegistrationDate = Convert.ToDateTime(reader["RegistrationDate"]),

                        Username = reader["Username"].ToString(),
                        NationalID = reader["LoginNationalID"].ToString(),

                        AccountNumber = reader["AccountNumber"] == DBNull.Value ? "" : reader["AccountNumber"].ToString(),
                        CVC = reader["CVC"] == DBNull.Value ? "" : reader["CVC"].ToString(),
                        Expiry = reader["Expiry"] == DBNull.Value ? "" : reader["Expiry"].ToString()
                    };
                }
                else
                {
                    model = new Member { NationalID = "" };
                }
            }

            return View(model);
        }

        [HttpPost]
        public IActionResult Index(Member model)
        {
            LoadGenderDropdown();

            if (!ModelState.IsValid)
                return View(model);

            int userId = GetLoggedInUserId();

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using var transaction = conn.BeginTransaction();
                try
                {
                    // 1️⃣ Update Members table including AccountNumber, CVC, Expiry
                    string updateMemberSql = @"
                        UPDATE Members
                        SET FirstName=@FirstName,
                            MiddleName=@MiddleName,
                            LastName=@LastName,
                            DOB=@DOB,
                            NationalID=@NationalID,
                            Phone=@Phone,
                            Email=@Email,
                            GenderID=@GenderID,
                            Address=@Address,
                            AccountNumber=@AccountNumber,
                            CVC=@CVC,
                            Expiry=@Expiry
                        WHERE ID = (SELECT MemberID FROM Logins WHERE ID=@UserId)";
                    
                    using var cmd = new SqlCommand(updateMemberSql, conn, transaction);
                    cmd.Parameters.AddWithValue("@FirstName", model.FirstName);
                    cmd.Parameters.AddWithValue("@MiddleName", (object)model.MiddleName ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@LastName", model.LastName);
                    cmd.Parameters.AddWithValue("@DOB", model.DOB);
                    cmd.Parameters.AddWithValue("@NationalID", model.NationalID);
                    cmd.Parameters.AddWithValue("@Phone", (object)model.Phone ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Email", model.Email);
                    cmd.Parameters.AddWithValue("@GenderID", model.GenderID);
                    cmd.Parameters.AddWithValue("@Address", (object)model.Address ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@AccountNumber", (object)model.AccountNumber ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@CVC", (object)model.CVC ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Expiry", (object)model.Expiry ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    cmd.ExecuteNonQuery();

                    // 2️⃣ Update Logins table (Username, NationalID)
                    string updateLoginSql = @"
                        UPDATE Logins
                        SET Username=@Username,
                            NationalID=@NationalID
                        WHERE ID=@UserId";
                    
                    using var cmdUpdateLogin = new SqlCommand(updateLoginSql, conn, transaction);
                    cmdUpdateLogin.Parameters.AddWithValue("@Username", model.Username);
                    cmdUpdateLogin.Parameters.AddWithValue("@NationalID", model.NationalID);
                    cmdUpdateLogin.Parameters.AddWithValue("@UserId", userId);
                    cmdUpdateLogin.ExecuteNonQuery();

                    // 3️⃣ Update password only if user entered a new one
                    if (!string.IsNullOrWhiteSpace(model.Password))
                    {
                        var saltBytes = new byte[16];
                        using var rng = new System.Security.Cryptography.RNGCryptoServiceProvider();
                        rng.GetBytes(saltBytes);
                        string salt = Convert.ToBase64String(saltBytes);
                        string hashedPassword = HashPassword(model.Password, salt);

                        string updatePasswordSql = @"
                            UPDATE Logins
                            SET PasswordHash=@PasswordHash,
                                PasswordSalt=@PasswordSalt
                            WHERE ID=@UserId";
                        
                        using var cmdPass = new SqlCommand(updatePasswordSql, conn, transaction);
                        cmdPass.Parameters.AddWithValue("@PasswordHash", hashedPassword);
                        cmdPass.Parameters.AddWithValue("@PasswordSalt", salt);
                        cmdPass.Parameters.AddWithValue("@UserId", userId);
                        cmdPass.ExecuteNonQuery();
                    }

                    // ✅ Commit transaction
                    transaction.Commit();

                    // ✅ Set HasBankDetails cookie if all 3 fields exist
                    if (!string.IsNullOrWhiteSpace(model.AccountNumber) &&
                        !string.IsNullOrWhiteSpace(model.CVC) &&
                        !string.IsNullOrWhiteSpace(model.Expiry))
                    {
                        Response.Cookies.Append("HasBankDetails", "true", new CookieOptions
                        {
                            HttpOnly = false,
                            Secure = true,
                            SameSite = SameSiteMode.Strict,
                            Expires = DateTime.UtcNow.AddYears(1)
                        });
                    }

                    ViewBag.SuccessMessage = "Profile updated successfully.";
                    return View(model);
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    ModelState.AddModelError("", "Error updating profile: " + ex.Message);
                    return View(model);
                }
            }
        }

        private string HashPassword(string password, string salt)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var combined = Encoding.UTF8.GetBytes(password + salt);
            var hash = sha.ComputeHash(combined);
            return Convert.ToBase64String(hash);
        }
    }
}
