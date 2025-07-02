using Microsoft.AspNetCore.Mvc;

namespace StokvelManagementSystem.Controllers
{
    public class ReportController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
