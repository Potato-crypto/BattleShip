using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Timers;  
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

        // Приватные поля
        private string _sessionId;
        private string _playerName;
        private HttpClient _httpClient;
        private System.Timers.Timer _gameStatePollTimer;  // Явное указание
        private System.Timers.Timer _heartbeatTimer;       // Явное указание
        private Func<List<ShipData>> _getShipsCallback;

        // Константы
        private const string BaseUrl = "http://localhost:5214";
        private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };


        private HubConnection _chatHubConnection;
        private HubConnection _gameHubConnection;
        private bool _isSignalRConnected = false;

        public ServerNetworkManager()
        {
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(BaseUrl),
                Timeout = TimeSpan.FromSeconds(30)
            };
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        }

        public void SetShipsCallback(Func<List<ShipData>> getShipsCallback)
        {
            _getShipsCallback = getShipsCallback;
        }

        #region Подключение/Отключение

        public async Task ConnectAsync(string playerName)
        {
            try
            {
                Console.WriteLine($"🔄 Подключение к серверу как '{playerName}'...");
                _playerName = playerName;

                // Проверяем доступность сервера
                var testResponse = await _httpClient.GetAsync("/api/Game/test");
                if (!testResponse.IsSuccessStatusCode)
                    throw new Exception($"Сервер недоступен: {testResponse.StatusCode}");

                // Создаем сессию
                var sessionRequest = new CreateSessionRequest { PlayerName = playerName };
                var sessionResponse = await _httpClient.PostAsJsonAsync("/api/Game/create-session", sessionRequest);
                sessionResponse.EnsureSuccessStatusCode();

                var sessionResult = await sessionResponse.Content.ReadFromJsonAsync<SessionResponse>(_jsonOptions);
                if (sessionResult == null || !sessionResult.Success)
                    throw new Exception("Не удалось создать сессию");

                _sessionId = sessionResult.SessionId;
                PlayerId = sessionResult.PlayerId;

                Console.WriteLine($"✅ Сессия создана: SessionId={_sessionId}, PlayerId={PlayerId}");

                // Подключаемся к SignalR хабам
                await ConnectToSignalR();

                // Запускаем heartbeat
                StartHeartbeat();

                IsConnected = true;
                OnConnectionChanged?.Invoke(true);

                OnMessageReceived?.Invoke(JsonSerializer.Serialize(new
                {
                    Type = "connected",
                    Data = new { PlayerId = PlayerId, PlayerName = playerName },
                    Timestamp = DateTime.Now
                }));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка подключения: {ex.Message}");
                OnError?.Invoke(new ErrorMessage
                {
                    Code = "CONNECTION_ERROR",
                    Message = $"Ошибка подключения: {ex.Message}"
                });
            }
        }

        private async Task ConnectToSignalR()
        {
            try
            {
                Console.WriteLine("🔗 Подключение к SignalR хабам...");

                // Создаем подключение к ChatHub
                _chatHubConnection = new HubConnectionBuilder()
                    .WithUrl($"{BaseUrl}/chathub")
                    .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) })
                    .Build();

                // Подписываемся на события чата
                _chatHubConnection.On<string>("ReceiveSystemMessage",
                    (message) =>
                    {
                        Console.WriteLine($"💬 [Система] {message}");
                        OnChatMessage?.Invoke(new INetworkService.ChatMessage
                        {
                            Sender = "[Система]",
                            Message = message,
                            IsSystem = true
                        });
                    });

                _chatHubConnection.On<string, string>("ReceiveMessage",
                    (senderId, message) =>
                    {
                        Console.WriteLine($"💬 {senderId}: {message}");
                        OnChatMessage?.Invoke(new INetworkService.ChatMessage
                        {
                            Sender = senderId,
                            Message = message,
                            IsSystem = false
                        });
                    });

                // Создаем подключение к GameHub
                _gameHubConnection = new HubConnectionBuilder()
                    .WithUrl($"{BaseUrl}/gamehub")
                    .WithAutomaticReconnect()
                    .Build();

                // Подписываемся на события игры
                _gameHubConnection.On<string>("OpponentDisconnected",
                    (message) =>
                    {
                        Console.WriteLine($"⚠️ {message}");
                        OnOpponentDisconnected?.Invoke(message);
                    });

                _gameHubConnection.On<string>("OpponentReconnected",
                    (message) =>
                    {
                        Console.WriteLine($"✅ {message}");
                        OnMessageReceived?.Invoke(JsonSerializer.Serialize(new
                        {
                            Type = "opponent_reconnected",
                            Message = message,
                            Timestamp = DateTime.Now
                        }));
                    });

                // Начинаем подключение
                await _chatHubConnection.StartAsync();
                await _gameHubConnection.StartAsync();

                _isSignalRConnected = true;
                Console.WriteLine("✅ SignalR подключен");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка подключения SignalR: {ex.Message}");
                _isSignalRConnected = false;
            }
        }

        public async Task DisconnectAsync()
        {
            try
            {
                if (IsInGame)
                {
                    await LeaveGameAsync();
                }

                if (_chatHubConnection != null)
                {
                    await _chatHubConnection.StopAsync();
                    await _chatHubConnection.DisposeAsync();
                }

                if (_gameHubConnection != null)
                {
                    await _gameHubConnection.StopAsync();
                    await _gameHubConnection.DisposeAsync();
                }

                _isSignalRConnected = false;

                StopAllTimers();
                IsConnected = false;
                OnConnectionChanged?.Invoke(false);

                Console.WriteLine("✅ Отключение завершено");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка отключения: {ex.Message}");
            }
        }

        #endregion

        #region Создание и поиск игры

        public async Task<string> CreateGameAsync(string gameMode)
        {
            try
            {
                Console.WriteLine($"🎮 Создание игры: {gameMode}");

                if (_getShipsCallback == null)
                    throw new Exception("Callback для получения кораблей не установлен");

                var shipsData = _getShipsCallback();
                if (shipsData == null || shipsData.Count != 10)
                    throw new Exception("Должно быть расставлено 10 кораблей");

                var serverShips = ConvertToServerShips(shipsData);

                var request = new ReadyForMatchmakingRequest
                {
                    SessionId = _sessionId,
                    PlayerId = PlayerId,
                    PlayerName = _playerName,
                    Ships = serverShips
                };

                Console.WriteLine($"📤 Отправка кораблей на сервер...");
                var response = await _httpClient.PostAsJsonAsync("/api/Game/ready-for-matchmaking", request, _jsonOptions);
                response.EnsureSuccessStatusCode();

                var result = await response.Content.ReadFromJsonAsync<ReadyForMatchmakingResponse>(_jsonOptions);

                if (!result.Success)
                    throw new Exception(result.Message);

                if (result.GameFound)
                {
                    // Игра найдена
                    GameId = result.GameId;
                    IsInGame = true;

                    Console.WriteLine($"✅ Игра создана: {GameId} против {result.OpponentName}");

                    // 🔥 ДОБАВЛЯЕМ: Автоматически подтверждаем готовность для СЕБЯ
                    await ConfirmReadyToStart();

                    // 🔥 ДОБАВЛЯЕМ: Ждем пока игра действительно начнется
                    await WaitForGameToActuallyStart();

                    // Запускаем опрос состояния игры
                    StartGameStatePolling();

                    OnGameStarted?.Invoke(new GameStartMessage
                    {
                        GameId = GameId,
                        OpponentName = result.OpponentName,
                        PlayerRole = result.IsMyTurn ? "first" : "second"
                    });

                    return GameId;
                }
                else
                {
                    // В лобби, ожидаем противника
                    Console.WriteLine($"⏳ В лобби, ожидаем противника...");

                    // Запускаем ожидание
                    _ = StartWaitingInLobby(PlayerId);

                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка создания игры: {ex.Message}");
                OnError?.Invoke(new ErrorMessage
                {
                    Code = "CREATE_GAME_ERROR",
                    Message = $"Ошибка создания игры: {ex.Message}"
                });
                return null;
            }
        }

        // 🔥 НОВЫЙ МЕТОД: Ждем пока игра действительно начнется
        private async Task WaitForGameToActuallyStart()
        {
            Console.WriteLine("⏳ Ожидание начала игры...");

            int attempts = 0;
            while (attempts < 30) // Ждем до 60 секунд
            {
                try
                {
                    var gameState = await GetGameStateAsync();
                    if (gameState != null &&
                        (gameState.GameStatus == "Player1Turn" || gameState.GameStatus == "Player2Turn"))
                    {
                        Console.WriteLine($"🎮 Игра началась! Статус: {gameState.GameStatus}");
                        return;
                    }

                    // Также проверяем через ready-to-start
                    var readyResponse = await _httpClient.GetAsync($"/api/Game/{GameId}/status/{PlayerId}");
                    if (readyResponse.IsSuccessStatusCode)
                    {
                        var status = await readyResponse.Content.ReadFromJsonAsync<dynamic>();
                        if (status?.gameStatus?.ToString() == "Player1Turn" ||
                            status?.gameStatus?.ToString() == "Player2Turn")
                        {
                            Console.WriteLine($"🎮 Игра началась через статус!");
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Ошибка проверки начала игры: {ex.Message}");
                }

                await Task.Delay(2000);
                attempts++;
            }

            Console.WriteLine("⚠️ Время ожидания начала игры истекло");
        }

        private async Task ConfirmReadyToStart()
        {
            try
            {
                var request = new ReadyToStartRequest
                {
                    SessionId = _sessionId,
                    PlayerId = PlayerId,
                    PlayerName = _playerName
                };

                var response = await _httpClient.PostAsJsonAsync($"/api/game/{GameId}/ready-to-start", request, _jsonOptions);
                response.EnsureSuccessStatusCode();

                var result = await response.Content.ReadFromJsonAsync<ReadyToStartResponse>(_jsonOptions);
                Console.WriteLine($"✅ Готовность подтверждена: {result?.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Ошибка подтверждения готовности: {ex.Message}");
            }
        }

        private async Task StartWaitingInLobby(string playerId)
        {
            Console.WriteLine($"⏳ Начало ожидания в лобби...");

            int maxAttempts = 30; // 60 секунд максимум
            for (int i = 0; i < maxAttempts; i++)
            {
                try
                {
                    Console.WriteLine($"📡 Проверка лобби ({i + 1}/{maxAttempts})...");

                    var response = await _httpClient.GetAsync($"/api/Game/wait-for-opponent/{playerId}");
                    if (!response.IsSuccessStatusCode)
                        continue;

                    var json = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<ReadyForMatchmakingResponse>(json, _jsonOptions);

                    if (result?.GameFound == true)
                    {
                        // Нашли игру!
                        GameId = result.GameId;
                        IsInGame = true;

                        Console.WriteLine($"✅ Противник найден! Игра: {GameId}");

                        // Присоединяемся к чату игры через SignalR
                        await JoinGameChat();

                        // Присоединяемся к GameHub
                        await JoinGameHub();

                        StartGameStatePolling();

                        OnGameStarted?.Invoke(new GameStartMessage
                        {
                            GameId = GameId,
                            OpponentName = result.OpponentName,
                            PlayerRole = result.IsMyTurn ? "first" : "second"
                        });

                        return;
                    }

                    if (result?.Message?.Contains("вышел из лобби") == true)
                    {
                        Console.WriteLine($"❌ Вышли из лобби");
                        break;
                    }

                    if (i == maxAttempts - 1)
                    {
                        Console.WriteLine($"❌ Время ожидания истекло");
                        OnError?.Invoke(new ErrorMessage
                        {
                            Code = "MATCHMAKING_TIMEOUT",
                            Message = "Не удалось найти противника"
                        });
                    }

                    await Task.Delay(2000); // Ждем 2 секунды
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Ошибка проверки лобби: {ex.Message}");
                    await Task.Delay(2000);
                }
            }
        }

        private async Task JoinGameChat()
        {
            if (_chatHubConnection?.State == HubConnectionState.Connected && !string.IsNullOrEmpty(GameId))
            {
                try
                {
                    
                    await _chatHubConnection.InvokeAsync("JoinGameChat", GameId, PlayerId, _playerName);
                    Console.WriteLine($"💬 Присоединились к чату игры {GameId} как {_playerName}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Ошибка присоединения к чату: {ex.Message}");
                }
            }
        }

        private async Task JoinGameHub()
        {
            if (_gameHubConnection?.State == HubConnectionState.Connected && !string.IsNullOrEmpty(GameId))
            {
                try
                {
                    await _gameHubConnection.InvokeAsync("JoinGame", GameId, PlayerId);
                    Console.WriteLine($"🎮 Присоединились к GameHub игры {GameId}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Ошибка присоединения к GameHub: {ex.Message}");
                }
            }
        }

        public Task<bool> JoinGameAsync(string gameId)
        {
            // Этот метод используется для присоединения к игре по ID
            return Task.FromResult(false);
        }

        public async Task<bool> LeaveGameAsync()
        {
            try
            {
                if (!string.IsNullOrEmpty(GameId))
                {
                    Console.WriteLine($"🚪 Выход из игры {GameId}...");

                    // Если игра не завершена, сдаемся
                    var gameState = await GetGameStateAsync();
                    if (gameState?.GameStatus != "finished")
                    {
                        await Surrender();
                    }

                    GameId = null;
                }

                IsInGame = false;
                StopGameStatePolling();

                Console.WriteLine($"✅ Выход из игры завершен");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка выхода из игры: {ex.Message}");
                return false;
            }
        }

        private async Task Surrender()
        {
            try
            {
                var request = new SurrenderRequest { PlayerId = PlayerId };
                var response = await _httpClient.PostAsJsonAsync($"/api/game/{GameId}/surrender", request, _jsonOptions);
                response.EnsureSuccessStatusCode();

                Console.WriteLine($"🏳️  Сдались в игре {GameId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Ошибка при сдаче: {ex.Message}");
            }
        }

        #endregion

        #region Выстрелы и игровые действия

        public async Task<bool> ShootAsync(int row, int col)
        {
            try
            {
                if (!IsInGame || string.IsNullOrEmpty(GameId))
                    throw new Exception("Не в игре");

                Console.WriteLine($"🎯 Выстрел в ({row}, {col})");

                var request = new FireRequest
                {
                    PlayerId = PlayerId,
                    X = row,
                    Y = col
                };

                var response = await _httpClient.PostAsJsonAsync($"/api/Game/{GameId}/fire", request, _jsonOptions);

                if (!response.IsSuccessStatusCode)
                {
                    var errorJson = await response.Content.ReadAsStringAsync();
                    var error = JsonSerializer.Deserialize<ErrorResponse>(errorJson, _jsonOptions);

                    OnError?.Invoke(new ErrorMessage
                    {
                        Code = "SHOOT_ERROR",
                        Message = error?.Message ?? "Ошибка выстрела"
                    });
                    return false;
                }

                var result = await response.Content.ReadFromJsonAsync<FireResponse>(_jsonOptions);

                if (!result.Success)
                {
                    OnError?.Invoke(new ErrorMessage
                    {
                        Code = "SHOOT_FAILED",
                        Message = result.Message
                    });
                    return false;
                }

                Console.WriteLine($"✅ Результат: Попадание={result.IsHit}, Потоплен={result.IsShipSunk}, Продолжает ход={result.ContinueTurn}");
                Console.WriteLine($"✅ Сообщение от сервера: {result.Message}");

                await SendBoardUpdateToUI();

                //  ОБНОВИТЬ ВСЮ ДОСКУ С СЕРВЕРА
                Console.WriteLine("🔄 Запрашиваем обновленную доску с сервера...");
                await UpdateOpponentBoardFromServer();

                // Также обновим и свою доску (на случай если противник уже стрелял)
                await UpdateMyBoardFromServer();

                // Создаем сообщение о результате для клиента
                var shootResult = new ShootResultMessage
                {
                    Row = row,
                    Col = col,
                    Result = result.IsShipSunk ? "sunk" : (result.IsHit ? "hit" : "miss"),
                    ShipSize = result.ShipSize,
                    ShipName = result.ShipName,
                    IsGameOver = result.IsGameOver,
                    ContinueTurn = result.ContinueTurn,
                    NextTurn = result.ContinueTurn ? "player" : "opponent",
                    CellStatus = result.IsShipSunk ? "sunk" : (result.IsHit ? "hit" : "miss"),
                    Message = result.Message,
                    GameStatus = result.GameStatus,
                    CurrentPlayerId = result.CurrentPlayerId
                };

                Console.WriteLine($"📤 Отправляем результат в UI: Result={shootResult.Result}, ContinueTurn={shootResult.ContinueTurn}");

                // Отправляем результат выстрела
                OnShootResult?.Invoke(shootResult);

                // Если игра окончена, делаем финальный опрос состояния
                if (result.IsGameOver)
                {
                    Console.WriteLine("🏁 Игра окончена! Обновляем финальное состояние...");
                    await Task.Delay(1000);
                    await PollGameStateAsync();

                    // Отправляем сообщение о конце игры
                    OnGameEnded?.Invoke(new GameEndMessage
                    {
                        Winner = result.CurrentPlayerId == PlayerId ? "player" : "opponent",
                        Reason = "all_ships_sunk",
                        Stats = new PlayerStats()
                    });
                }
                else if (!result.ContinueTurn)
                {
                    // Если ход переходит противнику - обновляем состояние
                    Console.WriteLine("🔄 Ход переходит противнику, обновляем состояние...");
                    await Task.Delay(500);
                    await PollGameStateAsync();

                    // Уведомляем UI о смене хода
                    OnGameStateUpdated?.Invoke(new GameStateMessage
                    {
                        Status = "playing",
                        CurrentTurn = "opponent",
                        GameStateJson = JsonSerializer.Serialize(new
                        {
                            GameId,
                            PlayerId,
                            OpponentTurn = true,
                            Message = "Ход противника"
                        })
                    });
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка при выстреле: {ex.Message}");
                Console.WriteLine($"❌ StackTrace: {ex.StackTrace}");
                OnError?.Invoke(new ErrorMessage
                {
                    Code = "SHOOT_EXCEPTION",
                    Message = $"Ошибка при выстреле: {ex.Message}"
                });
                return false;
            }
        }


        public async Task ForceUpdateBoards()
        {
            await SendBoardUpdateToUI();
        }
        private async Task UpdateOpponentBoardFromServer()
        {
            try
            {
                if (string.IsNullOrEmpty(GameId) || string.IsNullOrEmpty(PlayerId))
                    return;

                Console.WriteLine("📡 Запрашиваем состояние доски противника...");
                var response = await _httpClient.GetAsync($"/api/Game/{GameId}/board/{PlayerId}");

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"⚠️ Не удалось получить доску: {response.StatusCode}");
                    return;
                }

                var boardData = await response.Content.ReadFromJsonAsync<BoardResponse>(_jsonOptions);

                if (boardData != null)
                {
                    Console.WriteLine($"✅ Получена доска: {boardData.OpponentBoard?.Cells?.Count} клеток противника");

                    // Отправляем данные в UI через сериализованное сообщение
                    var updateMessage = new
                    {
                        type = "opponent_board_update", 
                        gameId = GameId,
                        playerId = PlayerId,
                        cells = boardData.OpponentBoard?.Cells, 
                        timestamp = DateTime.Now
                    };

                    OnMessageReceived?.Invoke(JsonSerializer.Serialize(updateMessage, _jsonOptions));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Ошибка обновления доски противника: {ex.Message}");
            }
        }

        // Обновление своей доски с сервера
        private async Task UpdateMyBoardFromServer()
        {
            try
            {
                if (string.IsNullOrEmpty(GameId) || string.IsNullOrEmpty(PlayerId))
                    return;

                Console.WriteLine("📡 Запрашиваем состояние своей доски...");
                var response = await _httpClient.GetAsync($"/api/Game/{GameId}/board/{PlayerId}");

                if (!response.IsSuccessStatusCode)
                    return;

                var boardData = await response.Content.ReadFromJsonAsync<BoardResponse>(_jsonOptions);

                if (boardData != null && boardData.MyBoard != null)
                {
                    Console.WriteLine($"✅ Получена своя доска: {boardData.MyBoard.Cells?.Count} клеток, {boardData.MyBoard.Ships?.Count} кораблей");

                    // Отправляем данные в UI
                    var updateMessage = new
                    {
                        type = "my_board_update", 
                        gameId = GameId,
                        playerId = PlayerId,
                        cells = boardData.MyBoard.Cells, 
                        ships = boardData.MyBoard.Ships, 
                        timestamp = DateTime.Now
                    };

                    OnMessageReceived?.Invoke(JsonSerializer.Serialize(updateMessage, _jsonOptions));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Ошибка обновления своей доски: {ex.Message}");
            }
        }

        public async Task<bool> SendShipsPlacementAsync(List<ShipData> ships)
        {
            // Этот метод вызывается при повторной отправке кораблей

            return await Task.FromResult(true);
        }

        #endregion

        #region Опрос состояния игры


        public async Task<bool> SurrenderAsync(string playerId)
        {
            try
            {
                if (!IsInGame || string.IsNullOrEmpty(GameId))
                    return false;

                var request = new SurrenderRequest { PlayerId = playerId };

                var response = await _httpClient.PostAsJsonAsync(
                    $"/api/game/{GameId}/surrender",
                    request,
                    _jsonOptions);

                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"🏳️ Игрок {playerId} сдался в игре {GameId}");
                    IsInGame = false;
                    GameId = null;
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка при сдаче: {ex.Message}");
                return false;
            }
        }
        private void StartGameStatePolling()
        {
            StopGameStatePolling();

            _gameStatePollTimer = new System.Timers.Timer(2000); // Опрос каждые 2 секунды
            _gameStatePollTimer.Elapsed += async (sender, e) => await PollGameStateAsync();
            _gameStatePollTimer.AutoReset = true;
            _gameStatePollTimer.Start();

            Console.WriteLine("📡 Запущен опрос состояния игры");
        }

        private void StopGameStatePolling()
        {
            if (_gameStatePollTimer != null)
            {
                _gameStatePollTimer.Stop();
                _gameStatePollTimer.Dispose();
                _gameStatePollTimer = null;
            }
        }


        public async Task<BoardResponse> GetBoardState()
        {
            try
            {
                if (string.IsNullOrEmpty(GameId) || string.IsNullOrEmpty(PlayerId))
                    return null;

                var response = await _httpClient.GetAsync($"/api/Game/{GameId}/board/{PlayerId}");

                if (!response.IsSuccessStatusCode)
                    return null;

                return await response.Content.ReadFromJsonAsync<BoardResponse>(_jsonOptions);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка получения состояния доски: {ex.Message}");
                return null;
            }
        }

        private async Task PollGameStateAsync()
        {
            if (!IsInGame || string.IsNullOrEmpty(GameId) || string.IsNullOrEmpty(PlayerId))
                return;

            try
            {
                var gameState = await GetGameStateAsync();
                if (gameState == null)
                    return;

                await SendBoardUpdateToUI();

                // Проверяем, не окончена ли игра
                if (gameState.IsGameOver || gameState.OpponentLeft)
                {
                    Console.WriteLine($"🏁 Игра окончена: {gameState.Message}");

                    OnGameEnded?.Invoke(new GameEndMessage
                    {
                        Winner = gameState.WinnerId == PlayerId ? "player" : "opponent",
                        Reason = gameState.OpponentLeft ? "opponent_disconnected" : "all_ships_sunk",
                        Stats = new PlayerStats()
                    });

                    IsInGame = false;
                    StopGameStatePolling();
                    return;
                }

                // Обновляем состояние игры
                var stateMessage = new GameStateMessage
                {
                    Status = ConvertGameStatus(gameState.GameStatus),
                    CurrentTurn = gameState.IsMyTurn ? "player" : "opponent",
                    PlayerScore = 0,
                    OpponentScore = 0,
                    RemainingTime = 0
                };

                OnGameStateUpdated?.Invoke(stateMessage);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Ошибка опроса состояния: {ex.Message}");
            }
        }

        private async Task SendBoardUpdateToUI()
        {
            try
            {
                if (string.IsNullOrEmpty(GameId) || string.IsNullOrEmpty(PlayerId))
                    return;

                Console.WriteLine("📡 Загружаем состояние досок для отправки в UI...");

                var response = await _httpClient.GetAsync($"/api/Game/{GameId}/board/{PlayerId}");

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"⚠️ Не удалось получить доски: {response.StatusCode}");
                    return;
                }

                var boardData = await response.Content.ReadFromJsonAsync<BoardResponse>(_jsonOptions);

                if (boardData != null)
                {
                    // ПРЕОБРАЗУЕМ ДАННЫЕ ДЛЯ UI
                    var uiBoardData = new
                    {
                        type = "board_full_update",
                        myBoard = ConvertToUIBoard(boardData.MyBoard, true),
                        opponentBoard = ConvertToUIBoard(boardData.OpponentBoard, false),
                        gameId = GameId,
                        playerId = PlayerId,
                        timestamp = DateTime.Now
                    };

                    var json = JsonSerializer.Serialize(uiBoardData, _jsonOptions);
                    Console.WriteLine($"📤 Отправляем JSON в UI...");
                    OnMessageReceived?.Invoke(json);

                    Console.WriteLine($"✅ Отправлено обновление досок в UI");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Ошибка отправки обновления досок: {ex.Message}");
            }
        }

        private dynamic ConvertToUIBoard(dynamic board, bool isMyBoard)
        {
            if (board == null) return null;

            var cells = new List<object>();

            if (isMyBoard && board is MyBoard myBoard)
            {
                // Преобразуем клетки своей доски
                foreach (var cell in myBoard.Cells)
                {
                    cells.Add(new
                    {
                        x = cell.X,
                        y = cell.Y,
                        hasShip = cell.HasShip,
                        wasShot = cell.WasShot,
                        status = GetStatusString(cell.Status),
                        isSunk = cell.Status == "4" || cell.Status == "Sunk"
                    });
                }
            }
            else if (!isMyBoard && board is OpponentBoard opponentBoard)
            {
                // Преобразуем клетки доски противника
                foreach (var cell in opponentBoard.Cells)
                {
                    cells.Add(new
                    {
                        x = cell.X,
                        y = cell.Y,
                        hasShip = false, // Для доски противника всегда скрываем корабли
                        wasShot = cell.WasShot,
                        status = GetStatusString(cell.Status),
                        isSunk = cell.Status == "4" || cell.Status == "Sunk"
                    });
                }
            }

            return new
            {
                cells = cells,
                shipsSunk = isMyBoard ? 0 : (board as OpponentBoard)?.ShipsSunk ?? 0,
                shipsRemaining = isMyBoard ? 0 : (board as OpponentBoard)?.ShipsRemaining ?? 0
            };
        }

        private string GetStatusString(string status)
        {
            if (int.TryParse(status, out int statusInt))
            {
                return statusInt switch
                {
                    0 => "Empty",
                    2 => "Hit",
                    3 => "Miss",
                    4 => "Sunk",
                    _ => "Empty"
                };
            }
            return status;
        }

        private async Task UpdateBothBoardsFromServer()
        {
            try
            {
                if (string.IsNullOrEmpty(GameId) || string.IsNullOrEmpty(PlayerId))
                    return;

                Console.WriteLine("📡 Получение текущего состояния досок...");
                var response = await _httpClient.GetAsync($"/api/Game/{GameId}/board/{PlayerId}");

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"⚠️ Не удалось получить доски: {response.StatusCode}");
                    return;
                }

                var boardData = await response.Content.ReadFromJsonAsync<BoardResponse>(_jsonOptions);

                if (boardData != null)
                {
                    // Создаем сообщение об обновлении для UI
                    var updateMessage = new
                    {
                        type = "board_full_update", 
                        myBoard = boardData.MyBoard, 
                        opponentBoard = boardData.OpponentBoard, 
                        gameId = GameId,
                        playerId = PlayerId,
                        timestamp = DateTime.Now
                    };

                    var json = JsonSerializer.Serialize(updateMessage, _jsonOptions);
                    Console.WriteLine($"📤 Отправляем JSON: {json}"); // Для отладки
                    OnMessageReceived?.Invoke(json);

                    Console.WriteLine($"✅ Отправлено полное обновление досок в UI");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Ошибка обновления досок: {ex.Message}");
            }
        }

        private async Task<GameStatusResponse> GetGameStateAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync($"/api/Game/{GameId}/status/{PlayerId}");
                if (!response.IsSuccessStatusCode)
                    return null;

                return await response.Content.ReadFromJsonAsync<GameStatusResponse>(_jsonOptions);
            }
            catch
            {
                return null;
            }
        }

        public async Task<GameStateMessage> GetUpdatedGameStateAsync()
        {
            var gameState = await GetGameStateAsync();
            if (gameState == null)
                return null;

            return new GameStateMessage
            {
                Status = ConvertGameStatus(gameState.GameStatus),
                CurrentTurn = gameState.IsMyTurn ? "player" : "opponent"
            };
        }

        #endregion

        #region Чат

        // На клиенте добавьте ссылку на BattleShip.Core
        public async Task SendChatMessageAsync(string message)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(message) || string.IsNullOrEmpty(GameId))
                    return;

                if (_chatHubConnection?.State == HubConnectionState.Connected)
                {
                    await _chatHubConnection.InvokeAsync("SendMessage", message);
                    Console.WriteLine($"💬 Сообщение отправлено через SignalR: {message}");
                }
                else
                {
                    Console.WriteLine($"⚠️ SignalR не подключен, отправляем через REST API...");

                    var chatMessage = new BattleShip.Core.Models.ChatMessage
                    {
                        GameId = GameId,
                        PlayerId = PlayerId,
                        PlayerName = _playerName,
                        Message = message,
                        Timestamp = DateTime.UtcNow,
                        IsSystemMessage = false
                    };

                    var response = await _httpClient.PostAsJsonAsync($"/api/chat/send", chatMessage, _jsonOptions);

                    if (response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"💬 Сообщение отправлено через REST API: {message}");
                    }
                    else
                    {
                        Console.WriteLine($"❌ Ошибка отправки сообщения: {response.StatusCode}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Ошибка отправки сообщения: {ex.Message}");
            }
        }

        public async Task<List<INetworkService.ChatHistoryItem>> GetChatHistoryAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(GameId))
                    return new List<INetworkService.ChatHistoryItem>();

                Console.WriteLine($"📜 Загружаем историю чата для игры {GameId}...");

                // 🔥 МЕТОД 1: Через REST API (ChatController)
                var response = await _httpClient.GetAsync($"/api/chat/{GameId}/messages");

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"⚠️ Не удалось получить историю чата: {response.StatusCode}");
                    return new List<INetworkService.ChatHistoryItem>();
                }

                var result = await response.Content.ReadFromJsonAsync<ChatHistoryResponse>(_jsonOptions);

                if (result?.Success != true || result.Messages == null)
                {
                    Console.WriteLine("⚠️ Пустая история чата");
                    return new List<INetworkService.ChatHistoryItem>();
                }

                Console.WriteLine($"📜 Загружено {result.Messages.Count} сообщений");

                // Преобразуем сообщения сервера в клиентскую модель
                var chatHistory = new List<INetworkService.ChatHistoryItem>();

                foreach (var serverMessage in result.Messages)
                {
                    // Определяем, наше ли это сообщение
                    bool isOwn = serverMessage.PlayerId == PlayerId;
                    string senderName;

                    if (serverMessage.IsSystemMessage)
                    {
                        senderName = "Система";
                    }
                    else if (isOwn)
                    {
                        senderName = "Вы";
                    }
                    else
                    {
                        // Если это сообщение оппонента - используем его имя
                        senderName = !string.IsNullOrEmpty(serverMessage.PlayerName)
                            ? serverMessage.PlayerName
                            : "Соперник";
                    }

                    chatHistory.Add(new INetworkService.ChatHistoryItem
                    {
                        SenderName = senderName,
                        Message = serverMessage.Message,
                        Timestamp = serverMessage.Timestamp,
                        IsSystem = serverMessage.IsSystemMessage,
                        IsOwn = isOwn
                    });
                }

                return chatHistory;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка получения истории чата: {ex.Message}");
                return new List<INetworkService.ChatHistoryItem>();
            }
        }

        private class ChatHistoryResponse
        {
            public bool Success { get; set; }
            public List<BattleShip.Core.Models.ChatMessage> Messages { get; set; }
        }

        #endregion

        #region Heartbeat

        private void StartHeartbeat()
        {
            _heartbeatTimer = new System.Timers.Timer(15000); // Каждые 15 секунд
            _heartbeatTimer.Elapsed += async (sender, e) => await SendHeartbeatAsync();
            _heartbeatTimer.AutoReset = true;
            _heartbeatTimer.Start();
        }

        private async Task SendHeartbeatAsync()
        {
            try
            {
                var request = new HeartbeatRequest { PlayerId = PlayerId };
                await _httpClient.PostAsJsonAsync("/api/Game/heartbeat", request, _jsonOptions);
            }
            catch
            {
                // Игнорируем ошибки heartbeat
            }
        }

        private void StopAllTimers()
        {
            StopGameStatePolling();

            if (_heartbeatTimer != null)
            {
                _heartbeatTimer.Stop();
                _heartbeatTimer.Dispose();
                _heartbeatTimer = null;
            }
        }

        #endregion

        #region Вспомогательные методы

        private List<Ship> ConvertToServerShips(List<ShipData> clientShips)
        {
            var serverShips = new List<Ship>();

            foreach (var clientShip in clientShips)
            {
                var serverShip = new Ship
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = GetShipName(clientShip.Size),
                    Size = clientShip.Size,
                    CellCoordinates = clientShip.Cells.Select(c => $"{c.Row},{c.Col}").ToList(),
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

        private string ConvertGameStatus(string serverStatus)
        {
            return serverStatus switch
            {
                "WaitingForPlayer" or "PlacingShips" or "WaitingForStart" => "placing",
                "Player1Turn" or "Player2Turn" => "playing",
                "Player1Won" or "Player2Won" or "Draw" or "Abandoned" => "finished",
                _ => "waiting"
            };
        }

        #endregion

        #region Статистика

        public Task<PlayerStats> GetPlayerStatsAsync()
        {
            // TODO: Реализовать получение статистики с сервера
            return Task.FromResult(new PlayerStats
            {
                Hits = 0,
                Misses = 0,
                Accuracy = 0,
                TotalShots = 0
            });
        }

        #endregion

        #region Модели ответов сервера

        // Модели для десериализации ответов сервера
        private class SessionResponse
        {
            public bool Success { get; set; }
            public string SessionId { get; set; }
            public string PlayerId { get; set; }
            public string Message { get; set; }
        }

        private class ReadyForMatchmakingResponse
        {
            public bool Success { get; set; }
            public bool GameFound { get; set; }
            public bool InLobby { get; set; }
            public string GameId { get; set; }
            public string PlayerId { get; set; }
            public string OpponentName { get; set; }
            public string GameStatus { get; set; }
            public bool IsMyTurn { get; set; }
            public string Message { get; set; }
        }

        private class ReadyToStartResponse
        {
            public bool Success { get; set; }
            public string GameStatus { get; set; }
            public bool IsGameStarted { get; set; }
            public string CurrentPlayerId { get; set; }
            public bool Player1Ready { get; set; }
            public bool Player2Ready { get; set; }
            public string Message { get; set; }
        }

        private class FireResponse
        {
            public bool Success { get; set; }
            public bool IsHit { get; set; }
            public bool IsShipSunk { get; set; }
            public string ShipName { get; set; }
            public int ShipSize { get; set; }
            public bool IsGameOver { get; set; }
            public string GameStatus { get; set; }
            public string CurrentPlayerId { get; set; }
            public bool ContinueTurn { get; set; }
            public string Message { get; set; }
        }

        private class GameStatusResponse
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

        private class ErrorResponse
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            public string Code { get; set; }
        }

        private class HeartbeatRequest
        {
            public string PlayerId { get; set; }
        }

        #endregion
    }
}