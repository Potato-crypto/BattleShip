using Microsoft.AspNetCore.Mvc;

namespace BattleShip.Server.Controllers
{
    public class LobbyController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
