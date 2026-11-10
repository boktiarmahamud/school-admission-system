using Microsoft.AspNetCore.Mvc;

namespace SchoolAdmission.Api.Controllers
{
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
