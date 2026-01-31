// Controllers/AdminController.cs
using Microsoft.AspNetCore.Mvc;
using BattleShip.Server.Services;

namespace BattleShip.Server.Controllers
{
    [ApiController]
    [Route("api/admin")]
    public class AdminController : ControllerBase
    {
        private readonly FirebaseService _firebaseService;
        private readonly ILogger<AdminController> _logger;

        public AdminController(FirebaseService firebaseService, ILogger<AdminController> logger)
        {
            _firebaseService = firebaseService;
            _logger = logger;
        }

        [HttpPost("clear-database")]
        public async Task<IActionResult> ClearDatabase()
        {
            try
            {
                await _firebaseService.ClearDatabaseAsync();
                return Ok(new { message = "База данных очищена", timestamp = DateTime.UtcNow });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка очистки базы");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("stats")]
        public async Task<IActionResult> GetStats()
        {
            try
            {
                var stats = await _firebaseService.GetDatabaseStats();
                return Ok(stats);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("cleanup-old-games")]
        public async Task<IActionResult> CleanupOldGames([FromQuery] int hours = 1)
        {
            try
            {
                await _firebaseService.CleanupOldGames(hours);
                return Ok(new { message = $"Удалены игры старше {hours} часов" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}