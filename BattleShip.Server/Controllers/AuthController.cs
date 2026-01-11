using Microsoft.AspNetCore.Mvc;

namespace BattleShip.Server.Controllers
{
    public class AuthController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
