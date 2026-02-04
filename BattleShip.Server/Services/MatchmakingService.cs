using BattleShip.Core.Enums;
using BattleShip.Core.Models;
using System.Collections.Concurrent;

namespace BattleShip.Server.Services
{
    public class MatchmakingService
    {
        private readonly FirebaseService _firebaseService;
        private readonly SessionService _sessionService;
        private readonly ILogger<MatchmakingService> _logger;

        private readonly ConcurrentDictionary<string, DateTime> _searchingPlayers
            = new ConcurrentDictionary<string, DateTime>();
        private readonly SemaphoreSlim _matchmakingLock = new SemaphoreSlim(1, 1);

        public MatchmakingService(
            FirebaseService firebaseService,
            SessionService sessionService,
            ILogger<MatchmakingService> logger)
        {
            _firebaseService = firebaseService;
            _sessionService = sessionService;
            _logger = logger;
        }

        public async Task<MatchmakingResult> FindMatchAsync(
            string playerName,
            string playerId,
            string sessionId,
            Board playerBoard)
        {
            await _matchmakingLock.WaitAsync();
            try
            {
                _logger.LogInformation($"🎮 Игрок {playerName} ищет противника");

                // 1. Проверяем есть ли уже активная игра
                var activeGame = await _firebaseService.GetActiveGameByPlayerIdAsync(playerId);
                if (activeGame != null)
                {
                    _logger.LogInformation($"🎯 У игрока {playerName} уже есть активная игра: {activeGame.Id}");
                    return new MatchmakingResult
                    {
                        Success = true,
                        GameFound = true,
                        Game = activeGame,
                        Message = "Возвращаемся в существующую игру"
                    };
                }

                // 2. Атомарно ищем противника в лобби
                var opponent = await _firebaseService.FindAndRemoveOpponentAsync(playerName);

                if (opponent != null)
                {
                    // НАШЛИ ПРОТИВНИКА! Создаем игру
                    _logger.LogInformation($"✅ Найден противник: {opponent.PlayerName}");

                    var game = await CreateGameAsync(
                        opponent.PlayerId, opponent.PlayerName, opponent.Board,
                        playerId, playerName, playerBoard,
                        opponent.SessionId, sessionId 
                    );

                    return new MatchmakingResult
                    {
                        Success = true,
                        GameFound = true,
                        Game = game,
                        Message = $"Игра началась против {opponent.PlayerName}!"
                    };
                }
                else
                {
                    // НЕ НАШЛИ - добавляем в лобби
                    var playerInLobby = new PlayerInLobby
                    {
                        PlayerId = playerId,
                        PlayerName = playerName,
                        Board = playerBoard,
                        JoinedAt = DateTime.UtcNow
                    };

                    var lobbyKey = await _firebaseService.AddToLobbyAsync(playerInLobby);

                    _logger.LogInformation($"⏳ Игрок {playerName} добавлен в лобби (ключ: {lobbyKey})");

                    return new MatchmakingResult
                    {
                        Success = true,
                        GameFound = false,
                        InLobby = true,
                        LobbyKey = lobbyKey,
                        Message = "Ожидаем противника..."
                    };
                }
            }
            finally
            {
                _matchmakingLock.Release();
            }
        }

        private async Task<Game> CreateGameAsync(
            string player1Id, string player1Name, Board player1Board,
            string player2Id, string player2Name, Board player2Board,
            string player1SessionId = null, string player2SessionId = null) 
        {
            var game = new Game
            {
                Id = Guid.NewGuid().ToString(),
                Player1Id = player1Id,
                Player1Name = player1Name,
                Player2Id = player2Id,
                Player2Name = player2Name,
                Status = GameStatus.WaitingForStart,
                CreatedAt = DateTime.UtcNow,
                Player1Board = player1Board,
                Player2Board = player2Board,
                Player1Ready = false, 
                Player2Ready = false  
            };

            await _firebaseService.SaveGameAsync(game);

            // Обновляем сессии игроков с sessionId
            var session1 = !string.IsNullOrEmpty(player1SessionId)
                ? await _sessionService.GetSessionAsync(player1SessionId)
                : await _sessionService.GetSessionByPlayerIdAsync(player1Id);

            var session2 = !string.IsNullOrEmpty(player2SessionId)
                ? await _sessionService.GetSessionAsync(player2SessionId)
                : await _sessionService.GetSessionByPlayerIdAsync(player2Id);

            if (session1 != null)
            {
                session1.CurrentGameId = game.Id;
                session1.IsInLobby = false;
                await _sessionService.UpdateSessionAsync(session1);
            }

            if (session2 != null)
            {
                session2.CurrentGameId = game.Id;
                session2.IsInLobby = false;
                await _sessionService.UpdateSessionAsync(session2);
            }

            _logger.LogInformation($"🎲 Создана игра {game.Id}: {player1Name} vs {player2Name}");

            return game;
        }

        public async Task<bool> CancelMatchmaking(string playerId)
        {
            try
            {
                await _firebaseService.RemoveFromLobbyAsync(playerId);

                var session = await _sessionService.GetSessionByPlayerIdAsync(playerId);
                if (session != null)
                {
                    session.IsInLobby = false;
                    await _sessionService.UpdateSessionAsync(session);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Ошибка отмены поиска для {playerId}");
                return false;
            }
        }
    }

    public class MatchmakingResult
    {
        public bool Success { get; set; }
        public bool GameFound { get; set; }
        public bool InLobby { get; set; }
        public Game Game { get; set; }
        public string LobbyKey { get; set; }
        public string Message { get; set; }
        public string Error { get; set; }
    }
}