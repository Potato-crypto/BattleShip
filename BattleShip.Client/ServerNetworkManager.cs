using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Linq;
using Newtonsoft.Json;
using BattleShip.Core.Models;

namespace BattleShip.Client
{
    public class ServerNetworkManager : INetworkService
    {
        // Реализация событий интерфейса
        public event Action<string> OnMessageReceived;
        public event Action<bool> OnConnectionChanged;
        public event Action<GameStartMessage> OnGameStarted;
        public event Action<GameEndMessage> OnGameEnded;
        public event Action<ShootResultMessage> OnShootResult;
        public event Action<ShootMessage> OnOpponentShoot;
        public event Action<INetworkService.ChatMessage> OnChatMessage;
        public event Action<GameStateMessage> OnGameStateUpdated;
        public event Action<ErrorMessage> OnError;

        // Свойства
        public bool IsConnected { get; private set; }
        public bool IsInGame { get; private set; }
        public string GameId { get; private set; }
        public string PlayerId { get; private set; }

        private readonly HttpClient _httpClient;
        private string _playerName;
        private System.Threading.Timer _gameStatePollingTimer;

        private System.Threading.Timer _waitingTimer;
        private Action<string> _showStatusCallback; 

        // Настройки сервера
        private const string BaseUrl = "http://localhost:5214";

        public ServerNetworkManager()
        {
            _httpClient = new HttpClient { BaseAddress = new Uri(BaseUrl) };
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        }

        public async Task ConnectAsync(string playerName)
        {
            try
            {
                _playerName = playerName;

                // Проверяем что сервер доступен
                var testResponse = await _httpClient.GetAsync("/api/game/test");
                if (!testResponse.IsSuccessStatusCode)
                {
                    throw new Exception("Сервер недоступен");
                }

                IsConnected = true;
                OnConnectionChanged?.Invoke(true);

                // Генерируем или получаем PlayerId
                PlayerId = $"player-{Guid.NewGuid():N}";

                OnMessageReceived?.Invoke(JsonConvert.SerializeObject(new
                {
                    type = "connected",
                    data = new { playerId = PlayerId, playerName },
                    timestamp = DateTime.Now
                }));
            }
            catch (Exception ex)
            {
                OnError?.Invoke(new ErrorMessage
                {
                    Code = "CONNECTION_ERROR",
                    Message = $"Не удалось подключиться: {ex.Message}"
                });
            }
        }

        public async Task DisconnectAsync()
        {
            IsConnected = false;
            IsInGame = false;
            GameId = null;

            // Останавливаем все таймеры
            _waitingTimer?.Dispose();
            _waitingTimer = null;

            _gameStatePollingTimer?.Dispose();
            _gameStatePollingTimer = null;

            OnConnectionChanged?.Invoke(false);
        }

        public async Task<string> CreateGameAsync(string gameMode)
        {
            try
            {
                var request = new { playerName = _playerName };
                var response = await _httpClient.PostAsJsonAsync("/api/game/find-game", request);

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"Ошибка сервера: {response.StatusCode}");
                }

                var result = await response.Content.ReadFromJsonAsync<ServerFindGameResponse>();

                GameId = result.GameId;
                PlayerId = result.PlayerId;
                IsInGame = true;

                // Если мы Player1 (создатель игры) - ждем второго игрока
                if (result.IsPlayer1)
                {
                    ShowStatusMessage("🎮 Ожидание второго игрока...");
                    StartWaitingForOpponent(); // ← ЗАПУСКАЕМ ОЖИДАНИЕ
                }
                else
                {
                    // Мы Player2 (присоединились) - игра начинается
                    ShowStatusMessage("✅ Присоединились к игре!");

                    OnGameStarted?.Invoke(new GameStartMessage
                    {
                        GameId = GameId,
                        OpponentName = "Игрок",
                        PlayerRole = "second"
                    });
                }

                return GameId;
            }
            catch (Exception ex)
            {
                ShowStatusMessage($"❌ Ошибка: {ex.Message}");
                return null;
            }
        }

        private void StartWaitingForOpponent()
        {
            // Останавливаем предыдущий таймер если был
            _waitingTimer?.Dispose();

            ShowStatusMessage("🎮 Ожидание второго игрока...");

            // Опрашиваем сервер каждые 3 секунды
            _waitingTimer = new System.Threading.Timer(async _ =>
            {
                try
                {
                    // Используем эндпоинт GET /api/game/{id}
                    var response = await _httpClient.GetAsync($"/api/game/{GameId}");

                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();

                        // Простая проверка на наличие Player2Id в JSON
                        if (json.Contains("Player2Id") && !json.Contains("\"Player2Id\":null"))
                        {
                            // Второй игрок присоединился!
                            ShowStatusMessage("✅ Второй игрок найден!");

                            // Запускаем событие начала игры
                            OnGameStarted?.Invoke(new GameStartMessage
                            {
                                GameId = GameId,
                                OpponentName = "Соперник",
                                PlayerRole = "first"
                            });

                            // Останавливаем таймер ожидания
                            _waitingTimer?.Dispose();
                            _waitingTimer = null;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка опроса ожидания: {ex.Message}");
                }
            }, null, 0, 3000); // каждые 3 секунды
        }

        private void ShowStatusMessage(string message)
        {
            
            OnMessageReceived?.Invoke($"Status: {message}");
        }

        public async Task<bool> JoinGameAsync(string gameId)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync($"/api/game/{gameId}/join", _playerName);

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"Не удалось присоединиться: {response.StatusCode}");
                }

                var result = await response.Content.ReadFromJsonAsync<ServerJoinGameResponse>();

                GameId = gameId;
                PlayerId = result.Player2Id; // Второй игрок получает Player2Id
                IsInGame = true;

                // Запускаем опрос состояния игры
                StartGameStatePolling();

                return true;
            }
            catch (Exception ex)
            {
                OnError?.Invoke(new ErrorMessage
                {
                    Code = "JOIN_GAME_ERROR",
                    Message = ex.Message
                });
                return false;
            }
        }

        public Task<bool> LeaveGameAsync()
        {
            
            IsInGame = false;
            GameId = null;

            _gameStatePollingTimer?.Dispose();
            _gameStatePollingTimer = null;

            return Task.FromResult(true);
        }

        public async Task<bool> SendShipsPlacementAsync(List<ShipData> ships)
        {
            try
            {
                // Конвертируем ShipData в вашу модель Ship
                var serverShips = ships.Select(s => new ServerShip
                {
                    Id = $"ship-{Guid.NewGuid()}",
                    Name = GetShipName(s.Size),
                    Size = s.Size,
                    CellCoordinates = s.Cells.Select(c => $"{c.Row},{c.Col}").ToList(),
                    Hits = 0,
                    IsSunk = false
                }).ToList();

                // Используем ваш эндпоинт /{id}/ready
                var request = new ServerReadyRequest
                {
                    PlayerId = PlayerId,
                    Ships = serverShips
                };

                var response = await _httpClient.PostAsJsonAsync($"/api/game/{GameId}/ready", request);

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"Ошибка при отправке кораблей: {response.StatusCode}");
                }

                // После расстановки кораблей начинаем следить за состоянием
                StartGameStatePolling();

                return true;
            }
            catch (Exception ex)
            {
                OnError?.Invoke(new ErrorMessage
                {
                    Code = "SHIPS_PLACEMENT_ERROR",
                    Message = ex.Message
                });
                return false;
            }
        }

        public async Task<bool> ShootAsync(int row, int col)
        {
            try
            {
                var request = new ServerFireRequest
                {
                    PlayerId = PlayerId,
                    X = row,
                    Y = col
                };

                Console.WriteLine($"=== ShootAsync: Отправка выстрела ({row},{col}) ===");

                var response = await _httpClient.PostAsJsonAsync($"/api/game/{GameId}/fire", request);

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"❌ Ошибка выстрела: {response.StatusCode}");
                    throw new Exception($"Ошибка выстрела: {response.StatusCode}");
                }

                // Читаем как JSON строку
                var json = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Ответ на выстрел: {json}");

                var result = JsonConvert.DeserializeObject<ServerFireResponse>(json);

                if (result == null)
                {
                    Console.WriteLine("❌ Ошибка десериализации ответа");
                    return false;
                }

                Console.WriteLine($"Результат: IsHit={result.IsHit}, IsShipSunk={result.IsShipSunk}, CellStatus={result.CellStatus}");

                // Создаем результат с ВСЕМИ полями
                var shootResult = new ShootResultMessage
                {
                    Row = row,
                    Col = col,
                    Result = result.IsShipSunk ? "sunk" : (result.IsHit ? "hit" : "miss"),
                    ShipSize = result.ShipSize,
                    ShipName = result.ShipName,
                    CellStatus = result.CellStatus,
                    NextTurn = result.IsHit ? "player" : "opponent",
                    RemainingShips = 10
                };

                // Отправляем результат
                OnShootResult?.Invoke(shootResult);

                // ВАЖНО: Немедленно опрашиваем обновленное состояние
                await Task.Delay(300); // Небольшая задержка для обновления сервера
                await PollGameStateAsync(); // Принудительное обновление

                Console.WriteLine($"=== ShootAsync завершен успешно ===");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка в ShootAsync: {ex.Message}");
                OnError?.Invoke(new ErrorMessage
                {
                    Code = "SHOOT_ERROR",
                    Message = ex.Message
                });
                return false;
            }
        }

        public async Task SendChatMessageAsync(string message)
        {
            // Ваш сервер пока не поддерживает чат
            // Можно имитировать или добавить позже
            OnChatMessage?.Invoke(new INetworkService.ChatMessage
            {
                Sender = _playerName,
                Message = message
            });
        }

        public Task<PlayerStats> GetPlayerStatsAsync()
        {
            // Пока возвращаем заглушку
            return Task.FromResult(new PlayerStats
            {
                Hits = 0,
                Misses = 0,
                Accuracy = 0,
                TotalShots = 0
            });
        }

        private void StartGameStatePolling()
        {
            // Останавливаем предыдущий таймер
            _gameStatePollingTimer?.Dispose();

            // Запускаем опрос состояния игры каждые 2 секунды
            _gameStatePollingTimer = new System.Threading.Timer(async _ =>
            {
                await PollGameStateAsync();
            }, null, 0, 2000);
        }

        private async Task PollGameStateAsync()
        {
            if (string.IsNullOrEmpty(GameId) || string.IsNullOrEmpty(PlayerId))
                return;

            try
            {
                Console.WriteLine($"=== PollGameStateAsync: Опрос состояния игры {GameId} ===");

                // Используем эндпоинт /{id}/player/{playerId}
                var response = await _httpClient.GetAsync($"/api/game/{GameId}/player/{PlayerId}");

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"❌ Ошибка запроса: {response.StatusCode}");
                    return;
                }

                // Читаем как строку для отладки
                var json = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Получен JSON: {json.Substring(0, Math.Min(500, json.Length))}...");

                var gameState = JsonConvert.DeserializeObject<ServerPlayerViewResponse>(json);

                if (gameState == null)
                {
                    Console.WriteLine("❌ Ошибка десериализации gameState");
                    return;
                }

                Console.WriteLine($"Статус игры: {gameState.GameStatus}, Мой ход: {gameState.IsMyTurn}");

                // КОНВЕРТИРУЕМ в GameStateMessage С ДЕТАЛЯМИ
                var stateMessage = new GameStateMessage
                {
                    Status = ConvertGameStatus(gameState.GameStatus),
                    CurrentTurn = gameState.IsMyTurn ? "player" : "opponent",
                    PlayerScore = 0,
                    OpponentScore = 0,
                    RemainingTime = 0,
                    GameStateJson = json
                };

                // ВЫЗЫВАЕМ СОБЫТИЕ С ДЕТАЛЬНЫМ СОСТОЯНИЕМ
                OnGameStateUpdated?.Invoke(stateMessage);

                // ОБРАБАТЫВАЕМ ВЫСТРЕЛЫ ПРОТИВНИКА НА ОСНОВЕ СОСТОЯНИЯ КЛЕТОК
                if (gameState.MyBoard?.Cells != null)
                {
                    Console.WriteLine($"Обработка {gameState.MyBoard.Cells.Count} клеток своего поля");

                    foreach (var cell in gameState.MyBoard.Cells)
                    {
                        // Если клетка была прострелена - это мог быть выстрел противника
                        if (cell.WasShot)
                        {
                            Console.WriteLine($"Клетка ({cell.X},{cell.Y}) была прострелена. Статус: {cell.Status}");

                            // Создаем событие выстрела противника
                            var shootMessage = new ShootMessage
                            {
                                Row = cell.X,
                                Col = cell.Y,
                                Timestamp = DateTime.Now,
                                IsHit = cell.Status == "Hit" || cell.Status == "Sunk"
                            };

                            // Отправляем событие
                            OnOpponentShoot?.Invoke(shootMessage);
                        }
                    }
                }

                // ПРОВЕРЯЕМ НАЧАЛО ИГРЫ
                if (gameState.GameStatus == "Player1Turn" || gameState.GameStatus == "Player2Turn")
                {
                    Console.WriteLine($"Игра началась! Статус: {gameState.GameStatus}");

                    var opponentName = !string.IsNullOrEmpty(gameState.OpponentName)
                        ? gameState.OpponentName
                        : (gameState.OpponentId?.Contains("test-user") == true ? "Игрок" : "Соперник");

                    var startMessage = new GameStartMessage
                    {
                        GameId = GameId,
                        OpponentName = opponentName,
                        PlayerRole = gameState.IsMyTurn ? "first" : "second"
                    };

                    OnGameStarted?.Invoke(startMessage);
                }

                // ПРОВЕРЯЕМ КОНЕЦ ИГРЫ
                if (gameState.GameStatus == "Player1Won" || gameState.GameStatus == "Player2Won")
                {
                    Console.WriteLine($"Игра окончена! Победитель: {gameState.GameStatus}");

                    var winner = gameState.GameStatus == "Player1Won" ? "player1" : "player2";
                    var isPlayerWinner = (winner == "player1" && PlayerId == gameState.MyPlayerId) ||
                                        (winner == "player2" && PlayerId == gameState.OpponentId);

                    var endMessage = new GameEndMessage
                    {
                        Winner = isPlayerWinner ? "player" : "opponent",
                        Reason = "all_ships_sunk",
                        Stats = new PlayerStats
                        {
                            Hits = gameState.MyBoard?.Ships?.Sum(s => s.Hits) ?? 0,
                            Misses = (gameState.MyBoard?.Cells?.Count(c => c.WasShot && c.Status == "Miss") ?? 0) +
                                    (gameState.OpponentBoard?.Cells?.Count(c => c.WasShot && c.Status == "Miss") ?? 0),
                            TotalShots = (gameState.MyBoard?.Cells?.Count(c => c.WasShot) ?? 0) +
                                        (gameState.OpponentBoard?.Cells?.Count(c => c.WasShot) ?? 0)
                        }
                    };

                    // Рассчитываем точность
                    if (endMessage.Stats.TotalShots > 0)
                    {
                        endMessage.Stats.Accuracy = (int)((float)endMessage.Stats.Hits / endMessage.Stats.TotalShots * 100);
                    }

                    OnGameEnded?.Invoke(endMessage);
                }

                // ИНФОРМАЦИЯ О КОРАБЛЯХ
                if (gameState.MyBoard?.Ships != null)
                {
                    int sunkShips = gameState.MyBoard.Ships.Count(s => s.IsSunk);
                    int remainingShips = gameState.MyBoard.Ships.Count - sunkShips;
                    Console.WriteLine($"Мои корабли: {sunkShips} потоплено, {remainingShips} осталось");
                }

                if (gameState.OpponentBoard != null)
                {
                    Console.WriteLine($"Корабли противника: {gameState.OpponentBoard.ShipsSunk} потоплено, {gameState.OpponentBoard.ShipsRemaining} осталось");
                }

                Console.WriteLine($"=== PollGameStateAsync завершен ===");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка опроса состояния: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");
            }
        }

        private string ConvertGameStatus(string serverStatus)
        {
            return serverStatus switch
            {
                "WaitingForPlayer" or "PlacingShips" => "placing",
                "Player1Turn" or "Player2Turn" => "playing",
                "Player1Won" or "Player2Won" => "finished",
                _ => "waiting"
            };
        }

        private string GetShipName(int size)
        {
            return size switch
            {
                4 => "Линкор",
                3 => "Крейсер",
                2 => "Эсминец",
                1 => "Катер",
                _ => "Корабль"
            };
        }

        public async Task<GameStateMessage> GetUpdatedGameStateAsync()
        {
            try
            {
                // Используем эндпоинт /{id}/player/{playerId}
                var response = await _httpClient.GetAsync($"/api/game/{GameId}/player/{PlayerId}");

                if (!response.IsSuccessStatusCode)
                    return null;

                var json = await response.Content.ReadAsStringAsync();

                var gameState = JsonConvert.DeserializeObject<ServerPlayerViewResponse>(json);

                return new GameStateMessage
                {
                    Status = ConvertGameStatus(gameState.GameStatus),
                    CurrentTurn = gameState.IsMyTurn ? "player" : "opponent"
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка получения состояния: {ex.Message}");
                return null;
            }
        }

        // Вспомогательные классы для десериализации ответов сервера
        private class ServerFindGameResponse
        {
            public bool Success { get; set; }
            public string GameId { get; set; }
            public string PlayerId { get; set; }
            public bool IsPlayer1 { get; set; }
            public string GameStatus { get; set; }
            public string Message { get; set; }
        }

        private class ServerJoinGameResponse
        {
            public bool Success { get; set; }
            public string GameId { get; set; }
            public string Status { get; set; }
            public string Player1Id { get; set; }
            public string Player2Id { get; set; }
            public string Message { get; set; }
        }

        private class ServerReadyRequest
        {
            public string PlayerId { get; set; }
            public List<ServerShip> Ships { get; set; }
        }

        private class ServerShip
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public int Size { get; set; }
            public List<string> CellCoordinates { get; set; }
            public int Hits { get; set; }
            public bool IsSunk { get; set; }
        }

        private class ServerFireRequest
        {
            public int X { get; set; }
            public int Y { get; set; }
            public string PlayerId { get; set; }
        }

        private class ServerFireResponse
        {
            public bool Success { get; set; }
            public bool IsHit { get; set; }
            public bool IsGameOver { get; set; }
            public string GameStatus { get; set; }
            public string CurrentPlayerId { get; set; }
            public string NextPlayer { get; set; }
            public string Message { get; set; }

            public bool IsShipSunk { get; set; }
            public int ShipSize { get; set; }
            public string ShipName { get; set; }
            public string CellStatus { get; set; } 
        }

        private class ServerPlayerViewResponse
        {
            public string GameId { get; set; }
            public string GameStatus { get; set; }
            public bool IsMyTurn { get; set; }
            public string MyPlayerId { get; set; }
            public string OpponentId { get; set; }
            public string OpponentName { get; set; }
            public PlayerBoardResponse MyBoard { get; set; }
            public OpponentBoardResponse OpponentBoard { get; set; }
        }

        private class PlayerBoardResponse
        {
            public List<CellResponse> Cells { get; set; }
            public List<ShipResponse> Ships { get; set; }
        }

        private class CellResponse
        {
            public int X { get; set; }
            public int Y { get; set; }
            public bool HasShip { get; set; }
            public bool WasShot { get; set; }
            public string Status { get; set; } // "Empty", "Hit", "Miss", "Sunk"
            public bool IsSunkShip { get; set; }
        }

        private class ShipResponse
        {
            public string Name { get; set; }
            public int Size { get; set; }
            public bool IsSunk { get; set; }
            public int Hits { get; set; }
            public List<string> Cells { get; set; }
        }

        private class OpponentBoardResponse
        {
            public List<HiddenCellResponse> Cells { get; set; }
            public int ShipsSunk { get; set; }
            public int ShipsRemaining { get; set; }
        }

        private class HiddenCellResponse
        {
            public int X { get; set; }
            public int Y { get; set; }
            public bool WasShot { get; set; }
            public string Status { get; set; }
            public bool ShowSunk { get; set; }
        }
    }
}