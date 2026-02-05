    using Microsoft.AspNetCore.Mvc;
    using BattleShip.Core.Models;
    using BattleShip.Server.Services;
    using System.Collections.Concurrent;

    namespace BattleShip.Server.Controllers
    {
        [ApiController]
        [Route("api/[controller]")]
        public class ChatController : ControllerBase
        {
            private readonly FirebaseService _firebaseService;
            private static readonly ConcurrentDictionary<string, List<ChatMessage>> _gameMessages = new();

            public ChatController(FirebaseService firebaseService)
            {
                _firebaseService = firebaseService;
            }

            // Отправка сообщения (резервный метод, если SignalR не работает)
            [HttpPost("send")]
            public async Task<IActionResult> SendMessage([FromBody] ChatMessage message)
            {
                try
                {
                    if (string.IsNullOrEmpty(message.GameId) || string.IsNullOrEmpty(message.Message))
                        return BadRequest(new { Success = false, Message = "Неверные данные" });

                    // Сохраняем в памяти
                    if (!_gameMessages.ContainsKey(message.GameId))
                        _gameMessages[message.GameId] = new List<ChatMessage>();

                    _gameMessages[message.GameId].Add(message);

                    // Также сохраняем в Firebase для истории
                    await _firebaseService.SaveChatMessageAsync(message);

                    return Ok(new { Success = true, MessageId = message.Id });
                }
                catch (Exception ex)
                {
                    return StatusCode(500, new { Success = false, Message = ex.Message });
                }
            }


            [HttpGet("{gameId}/messages")]
            public IActionResult GetMessages(string gameId, [FromQuery] int limit = 50)
            {
                try
                {
                    if (_gameMessages.TryGetValue(gameId, out var messages))
                    {
                        return Ok(new
                        {
                            Success = true,
                            Messages = messages
                                .OrderByDescending(m => m.Timestamp)
                                .Take(limit)
                                .OrderBy(m => m.Timestamp) // Возвращаем в хронологическом порядке
                        });
                    }

                    return Ok(new { Success = true, Messages = new List<ChatMessage>() });
                }
                catch (Exception ex)
                {
                    return StatusCode(500, new { Success = false, Message = ex.Message });
                }
            }
        }
    }