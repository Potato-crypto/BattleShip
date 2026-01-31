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

                    // Восстанавливаем связи
                    game.Player1Board?.RestoreCellShipReferences();
                    game.Player2Board?.RestoreCellShipReferences();
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
        public async Task UpdateGameAsync(Game game)
        {
            if (_firebaseClient == null || string.IsNullOrEmpty(game.Id)) return;

            try
            {
                // Вместо сохранения всей игры, обновляем только нужные поля
                var updates = new Dictionary<string, object>
                {
                    ["Status"] = game.Status,
                    ["CurrentPlayerId"] = game.CurrentPlayerId,
                    ["Player1Ready"] = game.Player1Ready,
                    ["Player2Ready"] = game.Player2Ready,
                    ["UpdatedAt"] = DateTime.UtcNow
                };

                // Обновляем только если изменились
                if (!string.IsNullOrEmpty(game.Player2Id))
                {
                    updates["Player2Id"] = game.Player2Id;
                }

                // Используем Patch для частичного обновления
                await _firebaseClient
                    .Child("games")
                    .Child(game.Id)
                    .PatchAsync(updates);

                _logger.LogDebug($"💾 Обновлена игра: {game.Id} ({game.Status})");
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

        public async Task<string> AddToLobbyAsync(string playerName, Board board)
        {
            try
            {
                var playerId = "player-" + Guid.NewGuid();

                var playerData = new PlayerInLobby
                {
                    PlayerId = playerId,
                    PlayerName = playerName,
                    Board = board,
                    JoinedAt = DateTime.UtcNow
                };

                await _firebaseClient
                    .Child("lobby")
                    .Child(playerId)
                    .PutAsync(playerData);

                _logger.LogInformation($"✅ Игрок {playerName} добавлен в лобби");
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


    }
}