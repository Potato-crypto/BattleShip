// Hubs/GameHub.cs
using BattleShip.Core.Enums;
using BattleShip.Server.Services;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

namespace BattleShip.Server.Hubs
{
    public class GameHub : Hub
    {
        private readonly FirebaseService _firebaseService;
        private static readonly ConcurrentDictionary<string, PlayerConnectionInfo> _playerConnections = new();

        public GameHub(FirebaseService firebaseService)
        {
            _firebaseService = firebaseService;
        }

        private class PlayerConnectionInfo
        {
            public string GameId { get; set; }
            public string PlayerId { get; set; }
            public string ConnectionId { get; set; }
            public DateTime LastPing { get; set; }
        }

        public async Task JoinGame(string gameId, string playerId)
        {
            Console.WriteLine($"🎮 Игрок {playerId} подключился к игре {gameId}");

            var connectionInfo = new PlayerConnectionInfo
            {
                GameId = gameId,
                PlayerId = playerId,
                ConnectionId = Context.ConnectionId,
                LastPing = DateTime.UtcNow
            };

            _playerConnections[Context.ConnectionId] = connectionInfo;

            // Добавляем в группу игры для уведомлений
            await Groups.AddToGroupAsync(Context.ConnectionId, gameId);

            // Отправляем подтверждение
            await Clients.Caller.SendAsync("GameJoined", gameId, playerId);

            // Уведомляем оппонента (если есть)
            await NotifyOpponent(gameId, playerId, "opponent_connected");
        }

        public async Task Ping()
        {
            if (_playerConnections.TryGetValue(Context.ConnectionId, out var info))
            {
                info.LastPing = DateTime.UtcNow;
                await Clients.Caller.SendAsync("Pong");
            }
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            Console.WriteLine($"🎮 GameHub: Отключение {Context.ConnectionId}");

            if (_playerConnections.TryRemove(Context.ConnectionId, out var playerInfo))
            {
                Console.WriteLine($"⚠️ Игрок {playerInfo.PlayerId} отключился от игры {playerInfo.GameId}");

                // Уведомляем оппонента об отключении
                await NotifyOpponent(playerInfo.GameId, playerInfo.PlayerId, "opponent_disconnected");

                // Обновляем статус игры в Firebase
                await HandlePlayerDisconnection(playerInfo.GameId, playerInfo.PlayerId);

                // Удаляем из группы
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, playerInfo.GameId);
            }

            await base.OnDisconnectedAsync(exception);
        }

        private async Task NotifyOpponent(string gameId, string playerId, string eventType)
        {
            // Находим всех игроков в этой игре
            var playersInGame = _playerConnections.Values
                .Where(p => p.GameId == gameId)
                .ToList();

            // Ищем оппонента
            var opponent = playersInGame.FirstOrDefault(p => p.PlayerId != playerId);

            if (opponent != null)
            {
                Console.WriteLine($"📢 Уведомляем оппонента {opponent.PlayerId} о событии {eventType}");

                if (eventType == "opponent_disconnected")
                {
                    await Clients.Client(opponent.ConnectionId)
                        .SendAsync("OpponentDisconnected", "Противник отключился от игры");
                }
                else if (eventType == "opponent_connected")
                {
                    await Clients.Client(opponent.ConnectionId)
                        .SendAsync("OpponentReconnected", "Противник переподключился");
                }
            }
        }

        private async Task HandlePlayerDisconnection(string gameId, string disconnectedPlayerId)
        {
            try
            {
                var game = await _firebaseService.GetGameAsync(gameId);
                if (game == null) return;

                // Определяем кто остался в игре
                string remainingPlayerId = game.Player1Id == disconnectedPlayerId
                    ? game.Player2Id
                    : game.Player1Id;

                if (!string.IsNullOrEmpty(remainingPlayerId))
                {
                    // Помечаем игру как завершенную с победой оставшегося игрока
                    game.Status = remainingPlayerId == game.Player1Id
                        ? GameStatus.Player1Won
                        : GameStatus.Player2Won;

                    await _firebaseService.UpdateGameAsync(game);
                    Console.WriteLine($"🏆 Автоматическая победа присвоена игроку {remainingPlayerId}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка при обработке отключения: {ex.Message}");
            }
        }
    }
}