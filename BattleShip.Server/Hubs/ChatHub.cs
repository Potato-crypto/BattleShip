// Hubs/ChatHub.cs
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

namespace BattleShip.Server.Hubs
{
    public class ChatHub : Hub
    {
        private static readonly ConcurrentDictionary<string, (string gameId, string playerId, string playerName)>
            _connections = new();

        // Добавляем логгер для отладки
        private readonly ILogger<ChatHub> _logger;

        public ChatHub(ILogger<ChatHub> logger)
        {
            _logger = logger;
        }

        public override async Task OnConnectedAsync()
        {
            _logger.LogInformation($"🔗 ChatHub: Подключен {Context.ConnectionId}");
            await base.OnConnectedAsync();
        }

        // Улучшенный метод подключения к чату игры
        public async Task JoinGameChat(string gameId, string playerId, string playerName)
        {
            try
            {
                _logger.LogInformation($"💬 Игрок {playerName} ({playerId}) присоединяется к чату игры {gameId}");

                // Сохраняем информацию о подключении
                _connections[Context.ConnectionId] = (gameId, playerId, playerName);

                // Добавляем в группу игры
                await Groups.AddToGroupAsync(Context.ConnectionId, gameId);

                // Отправляем подтверждение клиенту
                await Clients.Caller.SendAsync("JoinedChat", gameId);

                // Отправляем системное сообщение всем в игре
                await Clients.Group(gameId).SendAsync("ReceiveSystemMessage",
                    $"{playerName} присоединился к чату");

                _logger.LogInformation($"✅ Игрок {playerName} успешно присоединился к чату игры {gameId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Ошибка при присоединении к чату игры {gameId}");
                throw;
            }
        }

        // Метод отправки сообщения
        public async Task SendMessageToGame(string gameId, string message)
        {
            try
            {
                if (!_connections.TryGetValue(Context.ConnectionId, out var connectionInfo))
                {
                    _logger.LogWarning($"⚠️ Неизвестное подключение: {Context.ConnectionId}");
                    return;
                }

                var (storedGameId, playerId, playerName) = connectionInfo;

                // Проверяем, что игрок в нужной игре
                if (storedGameId != gameId)
                {
                    _logger.LogWarning($"⚠️ Игрок {playerName} пытается отправить сообщение не в свою игру");
                    return;
                }

                _logger.LogInformation($"💬 Игра {gameId}: {playerName}: {message}");

                // Отправляем сообщение всем участникам игры
                await Clients.Group(gameId).SendAsync("ReceiveMessage",
                    new
                    {
                        PlayerId = playerId,
                        PlayerName = playerName,
                        Message = message,
                        Timestamp = DateTime.UtcNow
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Ошибка отправки сообщения в игру {gameId}");
            }
        }

        // Метод отправки системного сообщения
        public async Task SendSystemMessage(string gameId, string message)
        {
            await Clients.Group(gameId).SendAsync("ReceiveSystemMessage", message);
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            try
            {
                _logger.LogInformation($"🔗 ChatHub: Отключен {Context.ConnectionId}");

                // Получаем информацию об отключающемся игроке
                if (_connections.TryRemove(Context.ConnectionId, out var connectionInfo))
                {
                    var (gameId, _, playerName) = connectionInfo;

                    // Удаляем из группы
                    await Groups.RemoveFromGroupAsync(Context.ConnectionId, gameId);

                    // Уведомляем других игроков об отключении
                    await Clients.Group(gameId).SendAsync("ReceiveSystemMessage",
                        $"{playerName} покинул чат");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка при отключении от чата");
            }

            await base.OnDisconnectedAsync(exception);
        }

        // Метод для проверки подключения
        public async Task<bool> IsConnectedToGame(string gameId, string playerId)
        {
            var connection = _connections.Values.FirstOrDefault(c =>
                c.gameId == gameId && c.playerId == playerId);
            return connection != default;
        }
    }
}