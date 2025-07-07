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
        // if (!isAdmin)
        //     return Unauthorized("You are not authorized as admin.");

        var token = GenerateJwtToken(loginUser.ID, loginUser.Username, loginUser.FirstName);

        Response.Cookies.Append("jwt", token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true, 
            SameSite = SameSiteMode.Strict,
            Expires = DateTime.UtcNow.AddHours(2)
        });

            // Store isAdmin (less secure, so HttpOnly = false to allow view access)
        Response.Cookies.Append("isAdmin", isAdmin.ToString().ToLower(), new CookieOptions
        {
            HttpOnly = false, // allow access in Razor View (only do this for non-sensitive flags!)
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

        string query = @"SELECT l.ID, l.Username, l.PasswordHash, l.PasswordSalt, m.FirstName
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
            string firstName = reader["FirstName"].ToString();

            string inputHash = HashPassword(password, storedSalt);
           
            Console.WriteLine($"StoredHash: {storedHash}");
            Console.WriteLine($"InputHash: {inputHash}");

            if (inputHash == storedHash)
            {
                return new LoginUser { ID = id, Username = username, FirstName = firstName };
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


    private string GenerateJwtToken(int memberId, string username, string firstname)
    {
        var key = Encoding.ASCII.GetBytes(_configuration["Jwt:Key"]);
        var tokenHandler = new JwtSecurityTokenHandler();

        var claims = new[]
        {
        new Claim(ClaimTypes.NameIdentifier, memberId.ToString()), // Use MemberID here
        new Claim(ClaimTypes.Name, username),
        new Claim(ClaimTypes.GivenName, firstname),
        new Claim(ClaimTypes.Role, "Admin")
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
        public string FirstName{ get; set; }
    }
}
