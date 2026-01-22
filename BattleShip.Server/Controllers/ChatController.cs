using Microsoft.AspNetCore.Mvc;
using BattleShip.Core.Models;
using BattleShip.Server.Services;

namespace BattleShip.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ChatController : ControllerBase
    {
        private readonly FirebaseService _firebaseService;

        public ChatController(FirebaseService firebaseService)
        {
            _firebaseService = firebaseService;
        }

        [HttpGet("{gameId}/history")]
        public async Task<IActionResult> GetChatHistory(string gameId)
        {   
            try
            {
                var history = await _firebaseService.GetChatHistoryAsync(gameId);
                return Ok(history);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка получения истории чата: {ex.Message}");
                return StatusCode(500, new { Message = "Ошибка загрузки истории" });
            }
        }

        [HttpPost("{gameId}/save")]
        public async Task<IActionResult> SaveMessage(string gameId, [FromBody] ChatMessage message)
        {
            if (message == null || string.IsNullOrEmpty(message.Message))
                return BadRequest(new { Message = "Пустое сообщение" });

            try
            {
                // Сохраняем в Firebase для истории
                await _firebaseService.SaveChatMessageAsync(gameId, message);
                return Ok(new { Success = true, MessageId = message.Id });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка сохранения сообщения: {ex.Message}");
                return StatusCode(500, new { Message = "Ошибка сохранения" });
            }
        }
    }
}