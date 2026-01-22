// Hubs/ChatHub.cs
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

namespace BattleShip.Server.Hubs
{
    public class ChatHub : Hub
    {
        // Храним связь ConnectionId -> GameId
        private static readonly ConcurrentDictionary<string, string> _connectionToGame = new();
        // Храним связь ConnectionId -> PlayerId
        private static readonly ConcurrentDictionary<string, string> _connectionToPlayer = new();

        public override async Task OnConnectedAsync()
        {
            Console.WriteLine($"🔗 ChatHub: Подключен {Context.ConnectionId}");
            await base.OnConnectedAsync();
        }

        public async Task JoinGameChat(string gameId, string playerId)
        {
            Console.WriteLine($"💬 Игрок {playerId} присоединяется к чату игры {gameId}");

            // Сохраняем связь
            _connectionToGame[Context.ConnectionId] = gameId;
            _connectionToPlayer[Context.ConnectionId] = playerId;

            // Добавляем в группу игры
            await Groups.AddToGroupAsync(Context.ConnectionId, gameId);

            // Отправляем системное сообщение
            await Clients.Group(gameId).SendAsync("ReceiveSystemMessage",
                $"{playerId} присоединился к чату");
        }

        public async Task SendMessage(string message)
        {
            if (!_connectionToGame.TryGetValue(Context.ConnectionId, out var gameId) ||
                !_connectionToPlayer.TryGetValue(Context.ConnectionId, out var playerId))
            {
                Console.WriteLine("❌ Не удалось найти игру/игрока для сообщения");
                return;
            }

            Console.WriteLine($"💬 Игра {gameId}: {playerId}: {message}");

            // Отправляем сообщение всем в группе (всем игрокам этой игры)
            await Clients.Group(gameId).SendAsync("ReceiveMessage", playerId, message);
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            Console.WriteLine($"🔗 ChatHub: Отключен {Context.ConnectionId}");

            // Удаляем из группы при отключении
            if (_connectionToGame.TryRemove(Context.ConnectionId, out var gameId))
            {
                if (_connectionToPlayer.TryRemove(Context.ConnectionId, out var playerId))
                {
                    // Уведомляем других игроков об отключении
                    await Clients.Group(gameId).SendAsync("ReceiveSystemMessage",
                        $"{playerId} покинул чат");
                }

                // Удаляем из группы
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, gameId);
            }

            await base.OnDisconnectedAsync(exception);
        }
    }
}