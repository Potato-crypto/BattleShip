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

            await _firebaseService.SaveGameAsync(game);

            return Ok(new
            {
                Success = true,
                GameId = game.Id,
                Message = $"Игра создана. Ждем второго игрока..."
            });
        }


        [HttpGet("{id}")]
        public async Task<IActionResult> GetGame(string id)
        {
            var game = await _firebaseService.GetGameAsync(id);
            if (game == null)
                return NotFound(new { Message = "Игра не найдена" });

            return Ok(game);
        }

        [HttpPost("{id}/join")]
        public async Task<IActionResult> JoinGame(string id, [FromBody] string playerName)
        {
            var game = await _firebaseService.GetGameAsync(id);
            if (game == null)
                return NotFound(new { Message = "Игра не найдена" });

            if (game.Status != "WaitingForPlayer")
                return BadRequest(new { Message = "Игра уже началась" });

            // Обновляем игру
            game.Player2Id = await _firebaseService.CreateTestUser();
            game.Status = "PlacingShips";
            await _firebaseService.UpdateGameAsync(game);

            return Ok(new
            {
                Success = true,
                Message = $"Игрок {playerName} присоединился к игре"
            });
        }

        [HttpPost("{id}/fire")]
        public async Task<IActionResult> Fire(string id, [FromBody] FireRequest request)
        {
            var game = await _firebaseService.GetGameAsync(id);
            if (game == null)
                return NotFound(new { Message = "Игра не найдена" });

            // Базовая логика выстрела (упрощённая)
            bool isHit = new Random().Next(0, 2) == 1; 

            // Сохраняем выстрел
            await _firebaseService.SaveShotAsync(id, request.PlayerId, request.X, request.Y, isHit);

            return Ok(new
            {
                Success = true,
                IsHit = isHit,
                Message = isHit ? "Попадание!" : "Мимо!"
            });
        }
    }


    public class FireRequest
    {
        public int X { get; set; }
        public int Y { get; set; }
        public string PlayerId { get; set; }
    }
}