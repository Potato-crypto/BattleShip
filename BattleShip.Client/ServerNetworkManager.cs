using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Linq;
using Newtonsoft.Json;
using BattleShip.Core.Models;
using Microsoft.AspNetCore.SignalR.Client;

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
        public event Action<string> OnOpponentDisconnected;

        // Свойства
        public bool IsConnected { get; private set; }
        public bool IsInGame { get; private set; }
        public string GameId { get; private set; }
        public string PlayerId { get; private set; }

        private readonly HttpClient _httpClient;
        private string _playerName;
        private System.Threading.Timer _gameStatePollingTimer;

        // ДОБАВЛЕНО: Флаг для предотвращения повторных алертов об отключении
        private bool _serverDisconnectAlertShown = false;

        // ✅ ДОБАВЛЕНО: Callback для отправки кораблей (будет установлен из GameWindow)
        private Func<List<ShipData>> _getShipsCallback;

        // Настройки сервера
        private const string BaseUrl = "http://localhost:5214";

        public ServerNetworkManager()
        {
            _httpClient = new HttpClient { BaseAddress = new Uri(BaseUrl) };
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        }

        // ✅ ДОБАВЛЕНО: Метод для установки callback получения кораблей
        public void SetShipsCallback(Func<List<ShipData>> getShipsCallback)
        {
            _getShipsCallback = getShipsCallback;
        }

        public async Task ConnectAsync(string playerName)
        {
            try
            {
                _playerName = playerName;

                // 1. Проверяем HTTP сервер 
                var testResponse = await _httpClient.GetAsync("/api/Game/test");

                if (!testResponse.IsSuccessStatusCode)
                {
                    // Получаем подробную информацию об ошибке
                    var errorContent = await testResponse.Content.ReadAsStringAsync();
                    Console.WriteLine($"❌ Сервер недоступен. Статус: {testResponse.StatusCode}");
                    Console.WriteLine($"❌ Ответ: {errorContent}");

                    throw new Exception($"HTTP сервер недоступен. Статус: {testResponse.StatusCode}");
                }

                // Проверяем ответ
                var testResult = await testResponse.Content.ReadAsStringAsync();
                Console.WriteLine($"✅ Сервер доступен: {testResult}");

                // Генерируем временный PlayerId (сервер создаст настоящий при создании игры)
                PlayerId = Guid.NewGuid().ToString();

                IsConnected = true;
                OnConnectionChanged?.Invoke(true);

                OnMessageReceived?.Invoke(JsonConvert.SerializeObject(new
                {
                    type = "connected",
                    data = new { playerId = PlayerId, playerName },
                    timestamp = DateTime.Now
                }));
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"❌ Ошибка HTTP: {ex.Message}");
                Console.WriteLine($"❌ Проверьте: {BaseUrl}/api/Game/test");

                OnError?.Invoke(new ErrorMessage
                {
                    Code = "HTTP_CONNECTION_ERROR",
                    Message = $"Не удалось подключиться к серверу: {ex.Message}. Проверьте что сервер запущен."
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Общая ошибка: {ex.Message}");

                OnError?.Invoke(new ErrorMessage
                {
                    Code = "CONNECTION_ERROR",
                    Message = $"Ошибка подключения: {ex.Message}"
                });
            }
        }

        public async Task DisconnectAsync()
        {
            IsConnected = false;
            IsInGame = false;
            GameId = null;

            // Сбрасываем флаг при явном отключении
            _serverDisconnectAlertShown = false;

            // Останавливаем все таймеры
            _gameStatePollingTimer?.Dispose();
            _gameStatePollingTimer = null;

            OnConnectionChanged?.Invoke(false);
        }

        public async Task<string> CreateGameAsync(string gameMode)
        {
            try
            {
                Console.WriteLine($"=== CreateGameAsync: {gameMode} ===");
                Console.WriteLine($"Имя игрока: {_playerName}");

                // ПРОВЕРКА: Есть ли callback для получения кораблей?
                if (_getShipsCallback == null)
                {
                    throw new Exception("Не установлен callback для получения кораблей. Вызовите SetShipsCallback() перед началом игры.");
                }

                // Получаем корабли от клиента через callback
                var shipsData = _getShipsCallback();
                if (shipsData == null || shipsData.Count == 0)
                {
                    throw new Exception("Корабли не расставлены!");
                }

                // КОНВЕРТИРУЕМ ShipData в Ship для сервера
                var serverShips = ConvertToServerShips(shipsData);

                // ИСПОЛЬЗУЕМ НОВЫЙ ЭНДПОИНТ: ready-for-matchmaking
                var request = new
                {
                    playerName = _playerName,
                    ships = serverShips
                };

                Console.WriteLine($"URL: {BaseUrl}/api/Game/ready-for-matchmaking");
                var jsonRequest = JsonConvert.SerializeObject(request, Formatting.Indented);
                Console.WriteLine($"Запрос: {jsonRequest}");

                var response = await _httpClient.PostAsJsonAsync("/api/Game/ready-for-matchmaking", request);

                Console.WriteLine($"Статус ответа: {response.StatusCode}");

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Ошибка: {errorContent}");

                    OnError?.Invoke(new ErrorMessage
                    {
                        Code = "MATCHMAKING_ERROR",
                        Message = $"Ошибка поиска игры: {errorContent}"
                    });

                    return null;
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"✅ Ответ сервера: {responseContent}");

                var result = JsonConvert.DeserializeObject<dynamic>(responseContent);

                if (result == null)
                {
                    Console.WriteLine("❌ Ошибка десериализации ответа");
                    throw new Exception("Неверный ответ сервера");
                }

                bool success = result.success;

                if (success)
                {
                    // Проверяем, сразу ли нашли игру или попали в лобби
                    bool inLobby = result.inLobby ?? false;

                    if (!inLobby)
                    {
                        // НЕМЕДЛЕННО НАШЛИ ПРОТИВНИКА
                        GameId = result.gameId;
                        PlayerId = result.playerId;
                        IsInGame = true;

                        Console.WriteLine($"🎮 Немедленно начали игру: {GameId}");

                        // Запускаем опрос состояния игры
                        StartGameStatePolling();

                        // Отправляем событие начала игры
                        OnGameStarted?.Invoke(new GameStartMessage
                        {
                            GameId = GameId,
                            OpponentName = result.opponentName?.ToString() ?? "Соперник",
                            PlayerRole = result.isMyTurn?.ToString() == "true" ? "first" : "second"
                        });

                        return GameId;
                    }
                    else
                    {
                        // ✅ ПОПАЛИ В ЛОББИ - начинаем ожидание
                        PlayerId = result.playerId.ToString();
                        Console.WriteLine($"⏳ Добавлены в лобби с PlayerId: {PlayerId}");

                        // ✅ Запускаем ожидание в лобби
                        _ = StartWaitingInLobby(PlayerId);

                        return null; // Игры еще нет, вернем null
                    }
                }
                else
                {
                    Console.WriteLine("❌ Не удалось создать игру");
                    OnError?.Invoke(new ErrorMessage
                    {
                        Code = "MATCHMAKING_FAILED",
                        Message = result.message?.ToString() ?? "Не удалось найти игру"
                    });
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка в CreateGameAsync: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");

                OnError?.Invoke(new ErrorMessage
                {
                    Code = "CREATE_GAME_ERROR",
                    Message = $"Ошибка создания игры: {ex.Message}"
                });

                return null;
            }
        }

        // ✅ ДОБАВЛЕНО: Метод ожидания в лобби
        private async Task StartWaitingInLobby(string playerId)
        {
            Console.WriteLine($"⏳ Начинаем ожидание в лобби для PlayerId: {playerId}");

            int maxAttempts = 30; // 30 * 2 сек = 1 минута
            for (int i = 0; i < maxAttempts; i++)
            {
                try
                {
                    Console.WriteLine($"Попытка {i + 1}/{maxAttempts} проверки лобби...");

                    var response = await _httpClient.GetAsync($"/api/Game/wait-for-opponent/{playerId}");

                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        var result = JsonConvert.DeserializeObject<dynamic>(json);

                        bool gameFound = result.gameFound ?? false;

                        if (gameFound)
                        {
                            // ✅ НАШЛИ ИГРУ!
                            GameId = result.gameId;
                            PlayerId = result.playerId;
                            IsInGame = true;

                            Console.WriteLine($"✅ Нашлась игра: {GameId}");

                            // ✅ Запускаем опрос состояния игры
                            StartGameStatePolling();

                            // ✅ Отправляем событие начала игры
                            OnGameStarted?.Invoke(new GameStartMessage
                            {
                                GameId = GameId,
                                OpponentName = result.opponentName?.ToString() ?? "Соперник",
                                PlayerRole = result.isMyTurn?.ToString() == "true" ? "first" : "second"
                            });

                            return;
                        }
                        else if (result.timeout != null && (bool)result.timeout)
                        {
                            Console.WriteLine("❌ Время ожидания истекло");
                            OnError?.Invoke(new ErrorMessage
                            {
                                Code = "MATCHMAKING_TIMEOUT",
                                Message = "Время поиска противника истекло"
                            });
                            break;
                        }
                        else if (result.message != null)
                        {
                            Console.WriteLine($"Сообщение от сервера: {result.message}");
                        }
                    }

                    await Task.Delay(2000); // Ждем 2 секунды
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка опроса лобби: {ex.Message}");
                }
            }

            Console.WriteLine("❌ Не удалось найти противника");
            OnError?.Invoke(new ErrorMessage
            {
                Code = "NO_OPPONENT_FOUND",
                Message = "Не удалось найти противника"
            });
        }

        // ✅ ДОБАВЛЕНО: Конвертация ShipData в Ship для сервера
        private List<Ship> ConvertToServerShips(List<ShipData> shipDataList)
        {
            var serverShips = new List<Ship>();

            foreach (var shipData in shipDataList)
            {
                var serverShip = new Ship
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = GetShipName(shipData.Size),
                    Size = shipData.Size,
                    CellCoordinates = shipData.Cells.Select(c => $"{c.Row},{c.Col}").ToList(),
                    Hits = 0,
                    IsSunk = false
                };

                serverShips.Add(serverShip);
            }

            return serverShips;
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

            // Сбрасываем флаг при выходе из игры
            _serverDisconnectAlertShown = false;

            _gameStatePollingTimer?.Dispose();
            _gameStatePollingTimer = null;

            return Task.FromResult(true);
        }

        public async Task<bool> SendShipsPlacementAsync(List<ShipData> ships)
        {
            try
            {
                // ✅ Уже отправляем корабли через ready-for-matchmaking
                // Этот метод может быть использован для повторной отправки
                var serverShips = ConvertToServerShips(ships);

                var request = new ServerReadyRequest
                {
                    PlayerId = PlayerId,
                    Ships = serverShips.Select(s => new ServerShip
                    {
                        Id = s.Id,
                        Name = s.Name,
                        Size = s.Size,
                        CellCoordinates = s.CellCoordinates,
                        Hits = s.Hits,
                        IsSunk = s.IsSunk
                    }).ToList()
                };

                var response = await _httpClient.PostAsJsonAsync($"/api/game/{GameId}/ready", request);

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"Ошибка при отправке кораблей: {response.StatusCode}");
                }

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
                // ✅ Отправка выстрела на сервер
                var request = new
                {
                    playerId = PlayerId,
                    x = row,
                    y = col
                };

                Console.WriteLine($"=== ShootAsync: Отправка выстрела ({row},{col}) ===");

                var response = await _httpClient.PostAsJsonAsync($"/api/game/{GameId}/fire", request);

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"❌ Ошибка выстрела: {response.StatusCode}");

                    var errorJson = await response.Content.ReadAsStringAsync();
                    var errorResponse = JsonConvert.DeserializeObject<ServerErrorResponse>(errorJson);

                    OnError?.Invoke(new ErrorMessage
                    {
                        Code = errorResponse?.Code ?? "SHOOT_ERROR",
                        Message = errorResponse?.Message ?? "Ошибка выстрела"
                    });

                    return false;
                }

                var json = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"✅ Ответ на выстрел: {json}");

                var result = JsonConvert.DeserializeObject<ServerFireResponse>(json);

                if (result == null)
                {
                    Console.WriteLine("❌ Ошибка десериализации ответа");
                    return false;
                }

                Console.WriteLine($"Результат: IsHit={result.IsHit}, IsShipSunk={result.IsShipSunk}, ContinueTurn={result.ContinueTurn}");

                // ✅ Создаем результат
                var shootResult = new ShootResultMessage
                {
                    Row = row,
                    Col = col,
                    Result = result.IsShipSunk ? "sunk" : (result.IsHit ? "hit" : "miss"),
                    ShipSize = result.ShipSize,
                    ShipName = result.ShipName,
                    CellStatus = result.CellStatus,
                    NextTurn = result.ContinueTurn ? "player" : "opponent", // ✅ Исправлено
                    ContinueTurn = result.ContinueTurn,
                    IsGameOver = result.IsGameOver
                };

                OnShootResult?.Invoke(shootResult);

                // ✅ Если игра окончена - опросим состояние
                if (result.IsGameOver)
                {
                    Console.WriteLine("🏆 Игра окончена! Опрашиваем состояние...");
                    await Task.Delay(500);
                    await PollGameStateAsync();
                }

                Console.WriteLine($"=== ShootAsync завершен успешно ===");
                return true;
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"❌ Ошибка соединения с сервером: {ex.Message}");

                if (!_serverDisconnectAlertShown)
                {
                    _serverDisconnectAlertShown = true;

                    OnError?.Invoke(new ErrorMessage
                    {
                        Code = "SERVER_DISCONNECTED",
                        Message = "Сервер недоступен"
                    });
                }

                return false;
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
            if (string.IsNullOrWhiteSpace(message))
                return;

            try
            {
                // ✅ Простая реализация через HTTP
                var chatMessage = new
                {
                    GameId = GameId,
                    PlayerId = PlayerId,
                    PlayerName = _playerName,
                    Message = message,
                    Timestamp = DateTime.UtcNow,
                    IsSystemMessage = false
                };

                var response = await _httpClient.PostAsJsonAsync($"/api/chat/{GameId}/save", chatMessage);

                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"💬 Сообщение отправлено: {message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка отправки сообщения: {ex.Message}");
            }
        }

        public Task<PlayerStats> GetPlayerStatsAsync()
        {
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

                // ✅ Используем эндпоинт /{id}/status/{playerId}
                var response = await _httpClient.GetAsync($"/api/game/{GameId}/status/{PlayerId}");

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"❌ Ошибка запроса статуса: {response.StatusCode}");
                    return;
                }

                var json = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Получен JSON статуса: {json}");

                var status = JsonConvert.DeserializeObject<ServerGameStatusResponse>(json);

                if (status == null)
                {
                    Console.WriteLine("❌ Ошибка десериализации статуса");
                    return;
                }

                Console.WriteLine($"Статус игры: {status.GameStatus}, Мой ход: {status.IsMyTurn}");

                // ✅ КОНЕЦ ИГРЫ - оппонент вышел
                if (status.OpponentLeft)
                {
                    Console.WriteLine($"🏆 Оппонент вышел! Вы победили!");

                    OnGameEnded?.Invoke(new GameEndMessage
                    {
                        Winner = "player",
                        Reason = "opponent_disconnected",
                        Stats = new PlayerStats()
                    });

                    return;
                }

                // ✅ КОНЕЦ ИГРЫ - обычная победа
                if (status.IsGameOver)
                {
                    Console.WriteLine($"🏆 Игра окончена! Победитель: {status.WinnerId}");

                    bool isPlayerWinner = status.WinnerId == PlayerId;

                    OnGameEnded?.Invoke(new GameEndMessage
                    {
                        Winner = isPlayerWinner ? "player" : "opponent",
                        Reason = "all_ships_sunk",
                        Stats = new PlayerStats()
                    });

                    return;
                }

                // ✅ ОБНОВЛЯЕМ СОСТОЯНИЕ
                var stateMessage = new GameStateMessage
                {
                    Status = ConvertGameStatus(status.GameStatus),
                    CurrentTurn = status.IsMyTurn ? "player" : "opponent",
                    PlayerScore = 0,
                    OpponentScore = 0,
                    RemainingTime = 0
                };

                OnGameStateUpdated?.Invoke(stateMessage);

            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка опроса состояния: {ex.Message}");
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

        public async Task<GameStateMessage> GetUpdatedGameStateAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(GameId) || string.IsNullOrEmpty(PlayerId))
                    return null;

                var response = await _httpClient.GetAsync($"/api/game/{GameId}/status/{PlayerId}");

                if (!response.IsSuccessStatusCode)
                    return null;

                var json = await response.Content.ReadAsStringAsync();
                var status = JsonConvert.DeserializeObject<ServerGameStatusResponse>(json);

                return new GameStateMessage
                {
                    Status = ConvertGameStatus(status.GameStatus),
                    CurrentTurn = status.IsMyTurn ? "player" : "opponent"
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка получения состояния: {ex.Message}");
                return null;
            }
        }

        // Вспомогательные классы для десериализации ответов сервера
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

        private class ServerFireResponse
        {
            public bool Success { get; set; }
            public bool IsHit { get; set; }
            public bool IsShipSunk { get; set; }
            public string ShipName { get; set; }
            public int ShipSize { get; set; }
            public string CellStatus { get; set; }
            public bool IsGameOver { get; set; }
            public string GameStatus { get; set; }
            public string CurrentPlayerId { get; set; }
            public string NextPlayer { get; set; }
            public bool ContinueTurn { get; set; }
            public string Message { get; set; }
        }

        private class ServerErrorResponse
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            public string Code { get; set; }
        }

        private class ServerGameStatusResponse
        {
            public string GameId { get; set; }
            public string GameStatus { get; set; }
            public bool IsMyTurn { get; set; }
            public bool IsGameOver { get; set; }
            public string WinnerId { get; set; }
            public string PlayerId { get; set; }
            public string OpponentName { get; set; }
            public bool OpponentLeft { get; set; }
            public int MyBoardShipsRemaining { get; set; }
            public int OpponentBoardShipsRemaining { get; set; }
            public string Message { get; set; }
        }
    }
}