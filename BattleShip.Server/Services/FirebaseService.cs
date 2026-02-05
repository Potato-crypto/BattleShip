using Firebase.Database;
using Firebase.Database.Query;
using BattleShip.Core.Models;
using BattleShip.Core.Enums;
using BattleShip.Server.Config;
using Microsoft.Extensions.Logging;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using System.Text.Json;

namespace BattleShip.Server.Services
{
    public class FirebaseService
    {
        private readonly FirebaseClient _firebaseClient;
        private readonly ILogger<FirebaseService> _logger;
        private readonly IConfiguration _configuration;

        public FirebaseService(IConfiguration configuration, ILogger<FirebaseService> logger)
        {
            _configuration = configuration;
            _logger = logger;

            try
            {
                // Проверяем файл
                var credentialPath = "firebase-credentials.json";
                var fullPath = Path.Combine(Directory.GetCurrentDirectory(), credentialPath);

                _logger.LogInformation($"🔍 Ищем файл по пути: {fullPath}");
                _logger.LogInformation($"📁 Существует ли файл: {File.Exists(fullPath)}");

                if (File.Exists(fullPath))
                {
                    _logger.LogInformation("✅ Файл firebase-credentials.json найден!");

                    // Читаем первые 100 символов для проверки
                    var fileContent = File.ReadAllText(fullPath);
                    _logger.LogInformation($"📄 Размер файла: {fileContent.Length} символов");
                    _logger.LogInformation($"🔑 Project ID: {GetProjectIdFromJson(fileContent)}");
                }
                else
                {
                    _logger.LogWarning("⚠️ Файл firebase-credentials.json НЕ найден!");
                    _logger.LogWarning("📁 Текущая директория: " + Directory.GetCurrentDirectory());

                    // Показываем какие файлы есть в директории
                    var files = Directory.GetFiles(Directory.GetCurrentDirectory());
                    _logger.LogWarning("📂 Файлы в директории:");
                    foreach (var file in files)
                    {
                        _logger.LogWarning($"   - {Path.GetFileName(file)}");
                    }
                }

                // Подключение к Firebase
                var databaseUrl = "https://seabattle-new-default-rtdb.asia-southeast1.firebasedatabase.app/";
                _logger.LogInformation($"🌐 Подключаемся к: {databaseUrl}");

                _firebaseClient = new FirebaseClient(databaseUrl, new FirebaseOptions
                {
                    AuthTokenAsyncFactory = () => Task.FromResult<string>(null)
                });

                _logger.LogInformation("✅ FirebaseService инициализирован");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Критическая ошибка инициализации Firebase");
                _firebaseClient = null;
            }
        }

        private string GetProjectIdFromJson(string json)
        {
            try
            {
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("project_id", out var projectId))
                    return projectId.GetString();
            }
            catch { }
            return "Не удалось прочитать";
        }

        private void InitializeFirebaseAdmin()
        {
            try
            {
                // Проверяем, не инициализировано ли уже
                if (FirebaseApp.DefaultInstance == null)
                {
                    var credentialPath = "firebase-credentials.json";
                    if (File.Exists(credentialPath))
                    {
                        FirebaseApp.Create(new AppOptions()
                        {
                            Credential = GoogleCredential.FromFile(credentialPath)
                        });
                        _logger.LogInformation("✅ Firebase Admin SDK инициализирован");
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ Файл firebase-credentials.json не найден");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Ошибка инициализации Firebase Admin SDK");
            }
        }

        // очистка базы
        public async Task ClearDatabaseAsync()
        {
            if (_firebaseClient == null) return;

            try
            {
                _logger.LogWarning("🧹 Начинаю очистку базы данных Firebase...");

                // Удаляем все данные
                await _firebaseClient.Child("").DeleteAsync();

                _logger.LogWarning("✅ База данных очищена!");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка очистки базы данных");
            }
        }

        // Оптимизированный поиск игр
        public async Task<List<Game>> FindWaitingGamesAsync()
        {
            if (_firebaseClient == null) return new List<Game>();

            try
            {
                // Используем OrderBy и StartAt для оптимизации запроса
                var games = await _firebaseClient
                    .Child("games")
                    .OrderBy("Status")
                    .StartAt((int)GameStatus.WaitingForPlayer)
                    .EndAt((int)GameStatus.WaitingForPlayer)
                    .OnceAsync<Game>();

                var waitingGames = games
                    .Where(game =>
                        game.Object.Status == GameStatus.WaitingForPlayer &&
                        string.IsNullOrEmpty(game.Object.Player2Id))
                    .Select(game =>
                    {
                        // Сохраняем ключ Firebase
                        game.Object.Id = game.Key;
                        return game.Object;
                    })
                    .ToList();

                _logger.LogInformation($"🔍 Найдено {waitingGames.Count} ожидающих игр");
                return waitingGames;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка поиска игр");
                return new List<Game>();
            }
        }

        //Оптимизированное сохранение
        public async Task<string> SaveGameAsync(Game game)
        {
            if (_firebaseClient == null) return null;

            try
            {
                // Генерируем ID если нет
                if (string.IsNullOrEmpty(game.Id))
                {
                    // Используем Push для Firebase-совместимого ID
                    var result = await _firebaseClient
                        .Child("games")
                        .PostAsync(game);

                    game.Id = result.Key;
                    _logger.LogInformation($"🆕 Создана новая игра с ID: {game.Id}");
                }
                else
                {
                    // Обновляем существующую
                    await _firebaseClient
                        .Child("games")
                        .Child(game.Id)
                        .PutAsync(game);
                }

                return game.Id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка сохранения игры");
                return null;
            }
        }

        // Оптимизированная загрузка игры
        public async Task<Game> GetGameAsync(string gameId)
        {
            if (_firebaseClient == null || string.IsNullOrEmpty(gameId))
                return null;

            try
            {
                var game = await _firebaseClient
                    .Child("games")
                    .Child(gameId)
                    .OnceSingleAsync<Game>();

                if (game != null)
                {
                    game.Id = gameId;
                    _logger.LogDebug($"🔄 Загружена игра: {gameId} ({game.Status})");

                    // Вместо этого просто инициализируем если нужно
                    game.Player1Board?.EnsureCellsInitialized();
                    game.Player2Board?.EnsureCellsInitialized();

                    // Только логируем состояние
                    int p1ShotCount = game.Player1Board?.Cells?.Count(c => c.WasShot) ?? 0;
                    int p2ShotCount = game.Player2Board?.Cells?.Count(c => c.WasShot) ?? 0;
                    _logger.LogDebug($"🎯 Состояние доски: P1 отстреляно={p1ShotCount}, P2 отстреляно={p2ShotCount}");
                }
                else
                {
                    _logger.LogWarning($"⚠️ Игра {gameId} не найдена");
                }

                return game;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Ошибка загрузки игры {gameId}");
                return null;
            }
        }

        // Обновляем только измененные поля
        // Обновляем игру с досками
        public async Task UpdateGameAsync(Game game)
        {
            if (_firebaseClient == null || string.IsNullOrEmpty(game.Id)) return;

            try
            {
                _logger.LogInformation($"💾 Обновление игры {game.Id} со всеми досками");

                // Обновляем всю игру
                await _firebaseClient
                    .Child("games")
                    .Child(game.Id)
                    .PutAsync(game);

                _logger.LogDebug($"💾 Игра обновлена: {game.Id} ({game.Status})");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Ошибка обновления игры {game.Id}");
            }
        }

        // Работа с досками через отдельные узлы
        public async Task UpdateBoardAsync(string gameId, string playerNumber, Board board)
        {
            if (_firebaseClient == null) return;

            try
            {
                // Сохраняем доску в отдельном узле для оптимизации
                var boardData = new
                {
                    Cells = board.Cells?.Select(c => new
                    {
                        c.X,
                        c.Y,
                        c.HasShip,
                        c.WasShot,
                        Status = (int)c.Status,
                        c.ShipId
                    }).ToList(),
                    Ships = board.Ships?.Select(s => new
                    {
                        s.Id,
                        s.Name,
                        s.Size,
                        s.Hits,
                        s.IsSunk,
                        s.CellCoordinates
                    }).ToList()
                };

                await _firebaseClient
                    .Child("boards")
                    .Child(gameId)
                    .Child($"player{playerNumber}")
                    .PutAsync(boardData);

                _logger.LogDebug($"✅ Доска игрока {playerNumber} сохранена");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Ошибка сохранения доски для игры {gameId}");
            }
        }

        // 🔥 Загрузка доски из отдельного узла
        public async Task<Board> GetBoardAsync(string gameId, string playerNumber)
        {
            if (_firebaseClient == null) return null;

            try
            {
                var boardData = await _firebaseClient
                    .Child("boards")
                    .Child(gameId)
                    .Child($"player{playerNumber}")
                    .OnceSingleAsync<dynamic>();

                if (boardData == null) return null;

                var board = new Board();

                // Восстанавливаем клетки
                if (boardData.Cells != null)
                {
                    board.Cells = new List<Cell>();
                    foreach (var cellData in boardData.Cells)
                    {
                        var cell = new Cell
                        {
                            X = cellData.X,
                            Y = cellData.Y,
                            HasShip = cellData.HasShip,
                            WasShot = cellData.WasShot,
                            Status = (CellStatus)cellData.Status,
                            ShipId = cellData.ShipId
                        };
                        board.Cells.Add(cell);
                    }
                }

                // Восстанавливаем корабли
                if (boardData.Ships != null)
                {
                    board.Ships = new List<Ship>();
                    foreach (var shipData in boardData.Ships)
                    {
                        var ship = new Ship
                        {
                            Id = shipData.Id,
                            Name = shipData.Name,
                            Size = shipData.Size,
                            Hits = shipData.Hits,
                            IsSunk = shipData.IsSunk,
                            CellCoordinates = shipData.CellCoordinates?.ToObject<List<string>>()
                        };
                        board.Ships.Add(ship);
                    }
                }

                board.RestoreCellShipReferences();
                return board;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Ошибка загрузки доски {gameId}/{playerNumber}");
                return null;
            }
        }

        //Сохранение хода с оптимизацией
        public async Task SaveShotAsync(string gameId, string playerId, int x, int y, bool isHit, bool isShipSunk = false)
        {
            if (_firebaseClient == null) return;

            try
            {
                var shot = new
                {
                    PlayerId = playerId,
                    X = x,
                    Y = y,
                    IsHit = isHit,
                    IsShipSunk = isShipSunk,
                    Timestamp = DateTime.UtcNow
                };

                // Используем Post для автоматического ID
                await _firebaseClient
                    .Child("shots")
                    .Child(gameId)
                    .PostAsync(shot);

                _logger.LogDebug($"🎯 Сохранен выстрел в игре {gameId}: ({x},{y}) -> {isHit}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка сохранения выстрела");
            }
        }

        // Получение истории выстрелов
        public async Task<List<dynamic>> GetShotsAsync(string gameId)
        {
            if (_firebaseClient == null) return new List<dynamic>();

            try
            {
                var shots = await _firebaseClient
                    .Child("shots")
                    .Child(gameId)
                    .OnceAsync<dynamic>();

                return shots
                    .Select(s => s.Object)
                    .OrderBy(s => s.Timestamp)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Ошибка загрузки выстрелов для игры {gameId}");
                return new List<dynamic>();
            }
        }

        // Очистка старых данных
        public async Task CleanupOldGames(int hoursOld = 24)
        {
            if (_firebaseClient == null) return;

            try
            {
                var cutoff = DateTime.UtcNow.AddHours(-hoursOld);

                var games = await _firebaseClient
                    .Child("games")
                    .OnceAsync<Game>();

                foreach (var game in games)
                {
                    if (game.Object.CreatedAt < cutoff &&
                        game.Object.Status != GameStatus.WaitingForPlayer)
                    {
                        // Удаляем игру и все связанные данные
                        await DeleteGameData(game.Key);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка очистки старых игр");
            }
        }

        private async Task DeleteGameData(string gameId)
        {
            try
            {
                // Удаляем все связанные данные игры
                await Task.WhenAll(
                    _firebaseClient.Child("games").Child(gameId).DeleteAsync(),
                    _firebaseClient.Child("boards").Child(gameId).DeleteAsync(),
                    _firebaseClient.Child("shots").Child(gameId).DeleteAsync(),
                    _firebaseClient.Child("chatMessages").Child(gameId).DeleteAsync()
                );

                _logger.LogInformation($"🗑️ Удалена старая игра: {gameId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Ошибка удаления игры {gameId}");
            }
        }

        public async Task<string> CreateTestUser()
        {
            return "player-" + Guid.NewGuid().ToString("N");
        }

        // Новый метод для проверки состояния базы
        public async Task<Dictionary<string, object>> GetDatabaseStats()
        {
            if (_firebaseClient == null)
                return new Dictionary<string, object> { ["error"] = "Firebase client not initialized" };

            try
            {
                var games = await _firebaseClient
                    .Child("games")
                    .OnceAsync<object>();

                var boards = await _firebaseClient
                    .Child("boards")
                    .OnceAsync<object>();

                var shots = await _firebaseClient
                    .Child("shots")
                    .OnceAsync<object>();

                return new Dictionary<string, object>
                {
                    ["total_games"] = games.Count,
                    ["total_boards"] = boards.Count,
                    ["total_shots"] = shots.Count,
                    ["waiting_games"] = games.Count(g =>
                        g.Object is Game game &&
                        game.Status == GameStatus.WaitingForPlayer),
                    ["database_url"] = FirebaseConfig.DatabaseUrl,
                    ["timestamp"] = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка получения статистики базы");
                return new Dictionary<string, object> { ["error"] = ex.Message };
            }
        }

        public async Task<string> AddToLobbyAsync(PlayerInLobby playerInLobby)
        {
            try
            {
                var playerId = playerInLobby.PlayerId;

                await _firebaseClient
                    .Child("lobby")
                    .Child(playerId)
                    .PutAsync(playerInLobby);

                _logger.LogInformation($"✅ Игрок {playerInLobby.PlayerName} добавлен в лобби");
                return playerId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка добавления в лобби");
                return null;
            }
        }

        public async Task<PlayerInLobby> FindOpponentInLobbyAsync(string currentPlayerName)
        {
            try
            {
                var players = await _firebaseClient
                    .Child("lobby")
                    .OnceAsync<PlayerInLobby>();

                // Ищем любого другого игрока
                var opponent = players
                    .Where(p => p.Object.PlayerName != currentPlayerName)
                    .OrderBy(p => p.Object.JoinedAt)
                    .Select(p => p.Object)
                    .FirstOrDefault();

                return opponent;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка поиска противника в лобби");
                return null;
            }
        }

        public async Task RemoveFromLobbyAsync(string playerId)
        {
            try
            {
                await _firebaseClient
                    .Child("lobby")
                    .Child(playerId)
                    .DeleteAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Ошибка удаления из лобби: {playerId}");
            }
        }

        public async Task<bool> IsPlayerInLobbyAsync(string playerId)
        {
            try
            {
                var player = await _firebaseClient
                    .Child("lobby")
                    .Child(playerId)
                    .OnceSingleAsync<object>();

                return player != null;
            }
            catch
            {
                return false;
            }
        }

        public async Task<Game> FindGameByPlayerIdAsync(string playerId)
        {
            try
            {
                var games = await _firebaseClient
                    .Child("games")
                    .OrderBy("Status")
                    .StartAt((int)GameStatus.Player1Turn)
                    .EndAt((int)GameStatus.Player2Turn)
                    .OnceAsync<Game>();

                var game = games
                    .Where(g => g.Object.Player1Id == playerId || g.Object.Player2Id == playerId)
                    .Select(g => g.Object)
                    .FirstOrDefault();

                return game;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка поиска игры по playerId");
                return null;
            }
        }

        public async Task<bool> HasOpponentLeft(string gameId, string myPlayerId)
        {
            // Здесь можно реализовать проверку через:
            // 1. Heartbeat от клиентов
            // 2. Время последнего действия
            // 3. Отдельную таблицу подключений

            // Пока упрощенная версия - проверяем время последнего обновления
            var game = await GetGameAsync(gameId);
            if (game == null) return false;

            var lastUpdated = game.UpdatedAt ?? game.CreatedAt;
            var timeSinceUpdate = DateTime.UtcNow - lastUpdated;

            // Если игра не обновлялась 30 секунд - считаем что противник вышел
            return timeSinceUpdate > TimeSpan.FromSeconds(30);
        }


        public async Task<PlayerInLobby> FindAndRemoveOpponentAsync(string currentPlayerName)
        {
            try
            {
                // Получаем всех в лобби
                var lobbyPlayers = await _firebaseClient
                    .Child("lobby")
                    .OnceAsync<PlayerInLobby>();

                var opponent = lobbyPlayers
                    .Where(p => p.Object.PlayerName != currentPlayerName)
                    .OrderBy(p => p.Object.JoinedAt)
                    .Select(p => new { Key = p.Key, Player = p.Object })
                    .FirstOrDefault();

                if (opponent != null)
                {
                    // Атомарно удаляем из лобби
                    await _firebaseClient
                        .Child("lobby")
                        .Child(opponent.Key)
                        .DeleteAsync();

                    _logger.LogInformation($"✅ Найден и удален противник: {opponent.Player.PlayerName}");
                    return opponent.Player;
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка атомарного поиска противника");
                return null;
            }
        }

        // Проверка активной игры у игрока
        public async Task<Game> GetActiveGameByPlayerIdAsync(string playerId)
        {
            try
            {
                var games = await _firebaseClient
                    .Child("games")
                    .OnceAsync<Game>();

                var activeGame = games
                    .Where(g => g.Object.Status != GameStatus.Player1Won &&
                               g.Object.Status != GameStatus.Player2Won &&
                               (g.Object.Player1Id == playerId || g.Object.Player2Id == playerId))
                    .Select(g =>
                    {
                        g.Object.Id = g.Key;
                        return g.Object;
                    })
                    .FirstOrDefault();

                return activeGame;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Ошибка поиска активной игры для {playerId}");
                return null;
            }
        }

        public async Task<List<Game>> GetAllGamesAsync()
        {
            if (_firebaseClient == null) return new List<Game>();

            try
            {
                var games = await _firebaseClient
                    .Child("games")
                    .OnceAsync<Game>();

                return games
                    .Select(game =>
                    {
                        game.Object.Id = game.Key;
                        return game.Object;
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка получения всех игр");
                return new List<Game>();
            }
        }

        private FirebaseClient GetDatabase()
        {
            return _firebaseClient;
        }

        public async Task SaveChatMessageAsync(ChatMessage message)
        {
            try
            {
                var db = GetDatabase();
                var messageRef = db.Child("chatMessages").Child(message.GameId).Child(message.Id);
                await messageRef.PutAsync(message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка сохранения сообщения чата");
            }
        }

        public async Task<List<ChatMessage>> GetChatMessagesAsync(string gameId, int limit = 50)
        {
            try
            {
                var db = GetDatabase();
                var messagesRef = db.Child("chatMessages").Child(gameId)
                    .OrderByKey()
                    .LimitToLast(limit);

                var snapshot = await messagesRef.OnceAsync<ChatMessage>();
                return snapshot.Select(x => x.Object).OrderBy(m => m.Timestamp).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка получения сообщений чата");
                return new List<ChatMessage>();
            }
        }

        // Обновляем игру целиком (со всеми досками)
        public async Task UpdateFullGameAsync(Game game)
        {
            if (_firebaseClient == null || string.IsNullOrEmpty(game.Id))
            {
                _logger.LogError("❌ Не могу обновить игру: FirebaseClient null или нет game.Id");
                return;
            }

            try
            {
                _logger.LogInformation($"💾 Сохранение полной игры: {game.Id}");

                // ✅ ДОБАВЬТЕ ЭТУ ПРОВЕРКУ СОГЛАСОВАННОСТИ:
                _logger.LogInformation($"🎮 Проверка согласованности Status и CurrentPlayerId:");
                _logger.LogInformation($"   Status={game.Status}, CurrentPlayerId={game.CurrentPlayerId}");
                _logger.LogInformation($"   Player1Id={game.Player1Id}, Player2Id={game.Player2Id}");

                // Автоматически исправляем несоответствие
                if (game.Status == GameStatus.Player1Turn && game.CurrentPlayerId != game.Player1Id)
                {
                    _logger.LogWarning($"⚠️ ИСПРАВЛЕНИЕ: Status=Player1Turn, но CurrentPlayerId={game.CurrentPlayerId}");
                    _logger.LogWarning($"   Меняю CurrentPlayerId на {game.Player1Id}");
                    game.CurrentPlayerId = game.Player1Id;
                }
                else if (game.Status == GameStatus.Player2Turn && game.CurrentPlayerId != game.Player2Id)
                {
                    _logger.LogWarning($"⚠️ ИСПРАВЛЕНИЕ: Status=Player2Turn, но CurrentPlayerId={game.CurrentPlayerId}");
                    _logger.LogWarning($"   Меняю CurrentPlayerId на {game.Player2Id}");
                    game.CurrentPlayerId = game.Player2Id;
                }
                else
                {
                    _logger.LogInformation($"✅ Status и CurrentPlayerId согласованы");
                }

                // Логируем важные поля
                _logger.LogInformation($"⏰ LastTurnTime={game.LastTurnTime}, UpdatedAt={game.UpdatedAt}");

                // Логируем состояние кораблей
                if (game.Player1Board?.Ships != null)
                {
                    foreach (var ship in game.Player1Board.Ships)
                    {
                        _logger.LogInformation($"🚢 P1 Корабль '{ship.Name}': Hits={ship.Hits}, IsSunk={ship.IsSunk}");
                    }
                }

                if (game.Player2Board?.Ships != null)
                {
                    foreach (var ship in game.Player2Board.Ships)
                    {
                        _logger.LogInformation($"🚢 P2 Корабль '{ship.Name}': Hits={ship.Hits}, IsSunk={ship.IsSunk}");
                    }
                }

                // Сохраняем всю игру
                await _firebaseClient
                    .Child("games")
                    .Child(game.Id)
                    .PutAsync(game);

                _logger.LogInformation($"✅ Полная игра сохранена: {game.Id}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Ошибка сохранения полной игры {game.Id}");
            }
        }


    }
}