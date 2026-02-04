using BattleShip.Core.Enums;
using BattleShip.Core.Models;
using System.Collections.Concurrent;

namespace BattleShip.Server.Services
{
    public class HeartbeatService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<HeartbeatService> _logger;

        private readonly ConcurrentDictionary<string, DateTime> _playerHeartbeats
            = new ConcurrentDictionary<string, DateTime>();

        public HeartbeatService(IServiceProvider serviceProvider, ILogger<HeartbeatService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("❤️ HeartbeatService запущен");

            // Ждем 10 секунд после старта сервера
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckInactivePlayersAsync();
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); // Проверка каждые 10 секунд
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Ошибка в HeartbeatService");
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); // Пауза при ошибке
                }
            }
        }

        public void UpdateHeartbeat(string playerId)
        {
            _playerHeartbeats[playerId] = DateTime.UtcNow;
            _logger.LogDebug($"❤️ Heartbeat обновлен для игрока {playerId}");
        }

        public bool IsPlayerActive(string playerId, int timeoutSeconds = 45)
        {
            if (!_playerHeartbeats.TryGetValue(playerId, out var lastHeartbeat))
            {
                _logger.LogDebug($"❤️ Игрок {playerId} не найден в heartbeat");
                return false;
            }

            var timeSinceLastHeartbeat = DateTime.UtcNow - lastHeartbeat;
            bool isActive = timeSinceLastHeartbeat <= TimeSpan.FromSeconds(timeoutSeconds);

            _logger.LogDebug($"❤️ Игрок {playerId}: последний heartbeat {timeSinceLastHeartbeat.TotalSeconds:F1} сек назад, активен={isActive}");

            return isActive;
        }

        private async Task CheckInactivePlayersAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var firebaseService = scope.ServiceProvider.GetRequiredService<FirebaseService>();

            // Получаем только активные игры (не завершенные)
            var activeGames = await GetActiveGamesAsync(firebaseService);

            _logger.LogInformation($"❤️ Проверка {activeGames.Count} активных игр");

            foreach (var game in activeGames)
            {
                try
                {
                    await CheckGameActivityAsync(game, firebaseService);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"❌ Ошибка проверки активности игры {game.Id}");
                }
            }
        }

        private async Task<List<Game>> GetActiveGamesAsync(FirebaseService firebaseService)
        {
            try
            {
                var games = await firebaseService.GetAllGamesAsync();

                // Игры, которые еще не завершены и не прерваны
                var activeGames = games
                    .Where(g => g.Status != GameStatus.Player1Won &&
                               g.Status != GameStatus.Player2Won &&
                               g.Status != GameStatus.Aborted &&
                               g.Status != GameStatus.Draw &&
                               g.Status != GameStatus.Abandoned)
                    .ToList();

                return activeGames;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка получения активных игр");
                return new List<Game>();
            }
        }

        private async Task CheckGameActivityAsync(Game game, FirebaseService firebaseService)
        {
            // Проверяем активность обоих игроков
            var player1Active = IsPlayerActive(game.Player1Id, 60); // 60 секунд для heartbeat
            var player2Active = IsPlayerActive(game.Player2Id, 60);

            _logger.LogDebug($"❤️ Игра {game.Id}: P1 активен={player1Active}, P2 активен={player2Active}");

            // Если оба игрока неактивны слишком долго
            if (!player1Active && !player2Active)
            {
                var lastHeartbeat1 = _playerHeartbeats.TryGetValue(game.Player1Id, out var hb1) ? hb1 : DateTime.MinValue;
                var lastHeartbeat2 = _playerHeartbeats.TryGetValue(game.Player2Id, out var hb2) ? hb2 : DateTime.MinValue;

                var timeSince1 = DateTime.UtcNow - lastHeartbeat1;
                var timeSince2 = DateTime.UtcNow - lastHeartbeat2;

                // Если оба неактивны больше 5 минут - отменяем игру
                if (timeSince1 > TimeSpan.FromMinutes(5) && timeSince2 > TimeSpan.FromMinutes(5))
                {
                    game.Status = GameStatus.Aborted;
                    game.EndedAt = DateTime.UtcNow;

                    _logger.LogInformation($"❌ Игра {game.Id} отменена: оба игрока неактивны >5 минут");

                    await firebaseService.UpdateFullGameAsync(game);
                }
            }
            // Если один игрок неактивен
            else if (!player1Active || !player2Active)
            {
                var inactivePlayerId = !player1Active ? game.Player1Id : game.Player2Id;
                var activePlayerId = player1Active ? game.Player1Id : game.Player2Id;

                var lastHeartbeat = _playerHeartbeats.TryGetValue(inactivePlayerId, out var hb) ? hb : DateTime.MinValue;
                var timeSinceLastHeartbeat = DateTime.UtcNow - lastHeartbeat;

                // Если игрок неактивен больше 2 минут - присваиваем победу
                if (timeSinceLastHeartbeat > TimeSpan.FromMinutes(2))
                {
                    if (!player1Active)
                    {
                        game.Status = GameStatus.Player2Won;
                        game.WinnerId = game.Player2Id;
                        game.LoserId = game.Player1Id;
                    }
                    else
                    {
                        game.Status = GameStatus.Player1Won;
                        game.WinnerId = game.Player1Id;
                        game.LoserId = game.Player2Id;
                    }

                    game.OpponentDisconnected = true;
                    game.EndedAt = DateTime.UtcNow;

                    _logger.LogInformation($"🏆 Игра {game.Id}: игрок {activePlayerId} победил (оппонент отключился)");

                    await firebaseService.UpdateFullGameAsync(game);
                }
            }
        }

        public async Task<List<string>> GetInactivePlayersAsync(int timeoutSeconds = 60)
        {
            return _playerHeartbeats
                .Where(kvp => DateTime.UtcNow - kvp.Value > TimeSpan.FromSeconds(timeoutSeconds))
                .Select(kvp => kvp.Key)
                .ToList();
        }
    }
}