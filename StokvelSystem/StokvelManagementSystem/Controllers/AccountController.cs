using System.Data.SqlClient;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using StokvelManagementSystem.Models;

public class AccountController : Controller
{
    private readonly IConfiguration _configuration;
    private readonly string _connectionString;

    public AccountController(IConfiguration configuration) 
    {
        _configuration = configuration;
        _connectionString = _configuration.GetConnectionString("DefaultConnection");
    }

    [HttpGet]
    public IActionResult Login()
    {
        return View(); 
    }

    [HttpPost]
    public IActionResult Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
            return BadRequest("Invalid login data.");

        var loginUser = ValidateUser(model.Username, model.Password);
        if (loginUser == null)
            return Unauthorized("Invalid username or password.");

        bool isAdmin = CheckIsAdmin(loginUser.ID);

        // 🔎 Retrieve MemberID from DB
        int memberId;
        using (var conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
        {
            conn.Open();
            using (var cmd = new SqlCommand("SELECT ID, AccountNumber, CVC, Expiry FROM Members WHERE UserID = @UserId", conn))
            {
                cmd.Parameters.AddWithValue("@UserId", loginUser.ID);
                using (var reader = cmd.ExecuteReader())
                {
                    if (!reader.Read())
                        return Unauthorized("No member associated with this user.");

                    memberId = Convert.ToInt32(reader["ID"]);
                    var hasBankDetails = !string.IsNullOrWhiteSpace(reader["AccountNumber"]?.ToString())
                                        && !string.IsNullOrWhiteSpace(reader["CVC"]?.ToString())
                                        && !string.IsNullOrWhiteSpace(reader["Expiry"]?.ToString());

                    // 🍪 Set a cookie to indicate missing bank details
                    Response.Cookies.Append("HasBankDetails", hasBankDetails.ToString().ToLower(), new CookieOptions
                    {
                        HttpOnly = false,
                        Secure = true,
                        SameSite = SameSiteMode.Strict,
                        Expires = DateTime.UtcNow.AddHours(2)
                    });
                }
            }
        }

        // 🔐 Include MemberID in token
        var token = GenerateJwtToken(memberId, loginUser.ID, loginUser.Username, loginUser.FirstName, loginUser.NationalID);

        Response.Cookies.Append("jwt", token, new CookieOptions 
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTime.UtcNow.AddHours(2)
        });

        Response.Cookies.Append("isAdmin", isAdmin.ToString().ToLower(), new CookieOptions
        {
            HttpOnly = false,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTime.UtcNow.AddHours(2)
        });

        return RedirectToAction("Index", "Home");
    }


    [HttpPost]
    public IActionResult Logout()
    {
        Response.Cookies.Delete("jwt");
        return RedirectToAction("Login");
    }


    private LoginUser ValidateUser(string username, string password)
        {
       
        using var conn = new SqlConnection(_connectionString);
        conn.Open();

        string query = @"SELECT l.ID, l.Username, l.PasswordHash, l.PasswordSalt, l.NationalID, m.FirstName
                     FROM Logins l
                     JOIN Members m ON l.MemberID = m.ID
                     WHERE l.Username = @Username";
        using var cmd = new SqlCommand(query, conn);
        cmd.Parameters.AddWithValue("@Username", username);

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            string storedHash = reader["PasswordHash"].ToString();
            string storedSalt = reader["PasswordSalt"].ToString();
            int id = Convert.ToInt32(reader["ID"]);
            string nationalId = reader["NationalID"].ToString();
            string firstName = reader["FirstName"].ToString();

            string inputHash = HashPassword(password, storedSalt);

            Console.WriteLine($"StoredHash: {storedHash}");
            Console.WriteLine($"InputHash: {inputHash}");

            if (inputHash == storedHash)
            {
                return new LoginUser 
                { 
                    ID = id, 
                    Username = username, 
                    FirstName = firstName,
                    NationalID = nationalId  // ✅ Add this
                };
            }

        }

        return null;
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
    var key = Encoding.ASCII.GetBytes(_configuration["Jwt:Key"]);
    var tokenHandler = new JwtSecurityTokenHandler();

    var claims = new[]
    {
        new Claim(ClaimTypes.NameIdentifier, memberId.ToString()),
        new Claim(ClaimTypes.Name, username),                     
        new Claim(ClaimTypes.GivenName, firstname),                
        new Claim(ClaimTypes.Role, "Admin"),                      

        new Claim("user_id", userId.ToString()),
        new Claim("member_id", memberId.ToString()),
        new Claim("national_id", nationalId)
    };

    var tokenDescriptor = new SecurityTokenDescriptor
    {
        Subject = new ClaimsIdentity(claims),
        Expires = DateTime.UtcNow.AddDays(7),
        SigningCredentials = new SigningCredentials(
            new SymmetricSecurityKey(key),
            SecurityAlgorithms.HmacSha256Signature)
    };

    var token = tokenHandler.CreateToken(tokenDescriptor);
    return tokenHandler.WriteToken(token);
}

    private class LoginUser
    {
        public int ID { get; set; }
        public string Username { get; set; }
        public string NationalID { get; set; }
        public string FirstName{ get; set; }
    }
}
