using Microsoft.AspNetCore.Mvc;
using BattleShip.Core.Models;
using BattleShip.Server.Services;

namespace BattleShip.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GameController : ControllerBase
    {
        private readonly FirebaseService _firebaseService;

        public GameController(FirebaseService firebaseService)
        {
            _firebaseService = firebaseService;
        }

        [HttpGet("test")]
        public IActionResult Test()
        {
            return Ok(new
            {
                Message = "BattleShip Server работает!",
                Time = DateTime.Now,
                Project = "Морской бой",
                Team = "3 разработчика"
            });
        }

        [HttpPost("create")]
        public async Task<IActionResult> CreateGame([FromBody] string playerName)
        {
            var game = new Game
            {
                Player1Id = await _firebaseService.CreateTestUser(),
                Status = "WaitingForPlayer"
            };

            return Ok(new
            {
                Success = true,
                GameId = game.Id,
                Message = $"Игра создана. Ждем второго игрока..."
            });
        }
    }
}