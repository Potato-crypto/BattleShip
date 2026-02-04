using BattleShip.Client.Services;
using BattleShip.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Net.Http.Json;
using System.Text.Json;

namespace BattleShip.Client
{
    public class CellState
    {
        public Brush BaseColor { get; set; }
        public bool IsHighlighted { get; set; }
        public bool HasShip { get; set; }
        public bool IsHit { get; set; }
        public bool IsMiss { get; set; }
        public bool IsSunk { get; set; }
        public bool IsPlacing { get; set; }
        public bool IsAroundSunk { get; set; }
        public bool WasShot => IsHit || IsMiss;

        public Brush GetCurrentColor()
        {
            // 1. Клетки вокруг потопленных кораблей - ПЕРВЫМИ!
            if (IsAroundSunk)
            {
                return Brushes.LightGray; // Более светлый цвет для ясности
            }

            // 2. Потом потопленные клетки
            if (IsSunk)
            {
                return Brushes.DarkRed;
            }

            // 3. Потом попадания
            if (IsHit)
            {
                return Brushes.Red;
            }

            // 4. Потом обычные промахи
            if (IsMiss)
            {
                return Brushes.LightBlue;
            }

            // 5. Подсветка
            if (IsHighlighted && BaseColor is SolidColorBrush baseBrush)
            {
                var color = baseBrush.Color;
                var highlightedColor = Color.FromArgb(
                    255,
                    (byte)Math.Min(color.R + 40, 255),
                    (byte)Math.Min(color.G + 40, 255),
                    (byte)Math.Min(color.B + 40, 255));
                return new SolidColorBrush(highlightedColor);
            }

            return BaseColor;
        }
    }

    public partial class GameWindow : Window
    {
        private const int GridSize = 10;
        private const int CellSize = 35;
        private int _unreadMessages = 0;
        private bool _isSearching = false;
        private GameLogic _gameLogic;
        private INetworkService _networkService;
        private Dictionary<string, Border> _playerCells = new Dictionary<string, Border>();
        private Dictionary<string, Border> _opponentCells = new Dictionary<string, Border>();
        private string _playerName;
        private string _gameId;

        public enum GameClientState
        {
            PlacingShips,
            SearchingGame,
            InGame,
            GameFinished
        }

        private GameClientState _currentState = GameClientState.PlacingShips;

        // Таймер хода - только один!
        private DispatcherTimer _turnTimer;
        private int _remainingTurnTime = 30;
        private bool _isMyTurn = false;

        private string _opponentName = "Соперник";
        private bool _isExitingFromGameOver = false;

        // Для отображения выстрелов
        private HashSet<string> _playerShots = new HashSet<string>();
        private HashSet<string> _opponentShots = new HashSet<string>();
        private HashSet<string> _hitsOnPlayer = new HashSet<string>();
        private HashSet<string> _hitsOnOpponent = new HashSet<string>();

        private bool _showingSpecialMessage = false;
        private DispatcherTimer _messageTimer;

        private HttpClient _httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:5214") };
        private string _playerId;

        public GameWindow()
        {
            InitializeComponent();

            _networkService = new ServerNetworkManager();
            _gameLogic = new GameLogic();
            _networkService.SetShipsCallback(() => _gameLogic.GetShipsForServer());

            SetupNetworkEvents();
            InitializeGameBoards();


            _networkService.OnMessageReceived += HandleNetworkMessage;

            // Инициализация таймера хода
            _turnTimer = new DispatcherTimer();
            _turnTimer.Interval = TimeSpan.FromSeconds(1);
            _turnTimer.Tick += TurnTimer_Tick;
            TurnTimerContainer.Visibility = Visibility.Collapsed;

            UpdateButtonsState();
            ConnectToServer();

            _messageTimer = new DispatcherTimer();
            _messageTimer.IsEnabled = false;

            ChatWindowControl.MessageSent += ChatWindowControl_MessageSent;
            ChatWindowControl.Closed += ChatWindowControl_Closed;
            ChatWindowControl.UnreadCountChanged += ChatWindowControl_UnreadCountChanged;
        }

        private JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private async void HandleNetworkMessage(string jsonMessage)
        {
            try
            {
                Console.WriteLine($"📥 Получено сетевое сообщение: {jsonMessage.Substring(0, Math.Min(200, jsonMessage.Length))}...");

                using JsonDocument doc = JsonDocument.Parse(jsonMessage);
                var root = doc.RootElement;

                if (root.TryGetProperty("type", out var typeProperty))
                {
                    string type = typeProperty.GetString();
                    Console.WriteLine($"   Тип сообщения: {type}");

                    await Dispatcher.Invoke(async () =>
                    {
                        if (type == "board_full_update")
                        {
                            // 🔥 УПРОЩЕННЫЙ ПАРСИНГ - используем Dictionary
                            try
                            {
                                var messageDict = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonMessage, _jsonOptions);

                                if (messageDict.TryGetValue("myBoard", out var myBoardObj) &&
                                    messageDict.TryGetValue("opponentBoard", out var opponentBoardObj))
                                {
                                    // Преобразуем в строку и парсим отдельно
                                    string myBoardJson = JsonSerializer.Serialize(myBoardObj, _jsonOptions);
                                    string opponentBoardJson = JsonSerializer.Serialize(opponentBoardObj, _jsonOptions);

                                    var myBoardData = JsonSerializer.Deserialize<MyBoard>(myBoardJson, _jsonOptions);
                                    var opponentBoardData = JsonSerializer.Deserialize<OpponentBoard>(opponentBoardJson, _jsonOptions);

                                    if (myBoardData != null && opponentBoardData != null)
                                    {
                                        Console.WriteLine($"🔄 Обновление UI из board_full_update");

                                        UpdateBoardsFromServer(new BoardResponse
                                        {
                                            MyBoard = myBoardData,
                                            OpponentBoard = opponentBoardData
                                        });

                                        Console.WriteLine($"✅ UI обновлен: {myBoardData.Cells?.Count ?? 0} своих клеток, " +
                                                        $"{opponentBoardData.Cells?.Count ?? 0} клеток противника");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"❌ Ошибка парсинга board_full_update: {ex.Message}");
                            }
                        }
                        else
                        {
                            Console.WriteLine($"📨 Другое сообщение типа: {type}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка обработки сетевого сообщения: {ex.Message}");
            }
        }


        private async Task LoadGameStateFromServer()
        {
            try
            {
                if (string.IsNullOrEmpty(_networkService.GameId) || string.IsNullOrEmpty(_playerId))
                {
                    Console.WriteLine("⚠️ LoadGameStateFromServer: Нет GameId или PlayerId");
                    return;
                }

                Console.WriteLine($"📡 LoadGameStateFromServer: Начинаем загрузку...");

                var url = $"/api/game/{_networkService.GameId}/board/{_playerId}";
                Console.WriteLine($"   URL: {url}");

                HttpResponseMessage response = null;
                try
                {
                    response = await _httpClient.GetAsync(url);
                }
                catch (HttpRequestException hrex)
                {
                    Console.WriteLine($"❌ HTTP ошибка: {hrex.Message}");
                    return;
                }

                Console.WriteLine($"   Статус ответа: {response.StatusCode}");

                if (response.IsSuccessStatusCode)
                {
                    string jsonContent = null;
                    try
                    {
                        jsonContent = await response.Content.ReadAsStringAsync();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ Ошибка чтения контента: {ex.Message}");
                        return;
                    }

                    Console.WriteLine($"📄 Получен JSON: {jsonContent.Length} символов");

                    // Проверим первые 300 символов
                    if (jsonContent.Length > 0)
                    {
                        string preview = jsonContent.Length > 300
                            ? jsonContent.Substring(0, 300) + "..."
                            : jsonContent;
                        Console.WriteLine($"   Предпросмотр: {preview}");
                    }

                    // Пробуем десериализовать
                    try
                    {
                        var boardState = JsonSerializer.Deserialize<BoardResponse>(
                            jsonContent,
                            new JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true
                            }
                        );

                        if (boardState != null && boardState.MyBoard?.Cells != null)
                        {
                            // Проверяем клетки вокруг потопленного корабля
                            Console.WriteLine("🔍 Проверяем клетки вокруг потопленных кораблей:");

                            var sunkCells = boardState.MyBoard.Cells
                                .Where(c => c.Status == "4" || c.Status == "Sunk")
                                .ToList();

                            Console.WriteLine($"   Потопленных клеток: {sunkCells.Count}");

                            foreach (var sunkCell in sunkCells)
                            {
                                Console.WriteLine($"   🚢 Потопленная клетка ({sunkCell.X},{sunkCell.Y}): " +
                                                 $"Status={sunkCell.Status}, HasShip={sunkCell.HasShip}");

                                // Проверяем клетки вокруг
                                for (int dx = -1; dx <= 1; dx++)
                                {
                                    for (int dy = -1; dy <= 1; dy++)
                                    {
                                        if (dx == 0 && dy == 0) continue;

                                        int nx = sunkCell.X + dx;
                                        int ny = sunkCell.Y + dy;

                                        if (nx >= 0 && nx < 10 && ny >= 0 && ny < 10)
                                        {
                                            var neighbor = boardState.MyBoard.Cells
                                                .FirstOrDefault(c => c.X == nx && c.Y == ny);

                                            if (neighbor != null)
                                            {
                                                string neighborStatus = ConvertCellStatus(neighbor.Status);

                                                if (neighborStatus == "Miss" && !neighbor.HasShip && neighbor.WasShot)
                                                {
                                                    Console.WriteLine($"      🎯 Клетка вокруг ({nx},{ny}): " +
                                                                    $"Status={neighbor.Status}->{neighborStatus}, " +
                                                                    $"HasShip={neighbor.HasShip}, WasShot={neighbor.WasShot}");
                                                }
                                            }
                                        }
                                    }
                                }
                            }

                            UpdateBoardsFromServer(boardState);
                        }
                        else
                        {
                            Console.WriteLine($"❌ Десериализованный объект null");
                        }
                    }
                    catch (JsonException jex)
                    {
                        Console.WriteLine($"❌ JsonException при десериализации: {jex.Message}");
                        Console.WriteLine($"❌ Path: {jex.Path}, LineNumber: {jex.LineNumber}");

                        // Попробуем десериализовать как динамический объект для отладки
                        try
                        {
                            var dynamicObj = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonContent);
                            Console.WriteLine($"🔍 Динамический объект ключи: {string.Join(", ", dynamicObj.Keys)}");
                        }
                        catch { }
                    }
                }
                else
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"❌ Ошибка HTTP {response.StatusCode}: {errorContent}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Исключение в LoadGameStateFromServer: {ex.Message}");
                Console.WriteLine($"❌ StackTrace: {ex.StackTrace}");
            }
        }

        private void UpdateBoardsFromServer(BoardResponse boardState)
        {
            try
            {
                Console.WriteLine($"🔄 UpdateBoardsFromServer: начало");

                // 1. Обновляем свое поле
                if (boardState.MyBoard?.Cells != null)
                {
                    Console.WriteLine($"   Обновляем свое поле: {boardState.MyBoard.Cells.Count} клеток");

                    foreach (var serverCell in boardState.MyBoard.Cells)
                    {
                        string cellKey = $"{serverCell.X},{serverCell.Y}";
                        if (_playerCells.TryGetValue(cellKey, out Border cell))
                        {
                            UpdateCellFromServerData(cell, serverCell, true);
                        }
                    }
                }

                // 2. Обновляем поле противника
                if (boardState.OpponentBoard?.Cells != null)
                {
                    Console.WriteLine($"   Обновляем поле противника: {boardState.OpponentBoard.Cells.Count} клеток");

                    foreach (var serverCell in boardState.OpponentBoard.Cells)
                    {
                        string cellKey = $"{serverCell.X},{serverCell.Y}";
                        if (_opponentCells.TryGetValue(cellKey, out Border cell))
                        {
                            UpdateCellFromServerData(cell, serverCell, false);
                        }
                    }
                }

                // 3. Обновляем визуально
                UpdatePlayerBoardVisual();
                UpdateOpponentBoardVisual();

                Console.WriteLine($"✅ UpdateBoardsFromServer: завершено");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка в UpdateBoardsFromServer: {ex.Message}");
            }
        }

        private void UpdateOpponentBoardVisual()
        {
            foreach (var kvp in _opponentCells)
            {
                var cell = kvp.Value;
                if (cell.Tag is Tuple<string, CellState> tag)
                {
                    var (_, cellState) = tag;
                    cell.Background = cellState.GetCurrentColor();
                    UpdateOpponentCellSymbol(cell, cellState);

                    // Обновляем доступность
                    cell.IsEnabled = !cellState.WasShot && !cellState.IsAroundSunk && _isMyTurn;
                    cell.Cursor = cell.IsEnabled ? Cursors.Hand : Cursors.Arrow;
                }
            }
        }

        private void UpdateCellFromServerData(Border cell, BoardCell serverCell, bool isPlayerCell)
        {
            try
            {
                if (cell.Tag is Tuple<string, CellState> tag)
                {
                    var (_, cellState) = tag;

                    // Получаем статус из серверных данных
                    string status = ConvertCellStatus(serverCell.Status);

                    // ОБНОВЛЯЕМ ВСЕ СТАТУСЫ ИЗ ДАННЫХ СЕРВЕРА
                    cellState.IsHit = status == "Hit" || status == "Sunk";
                    cellState.IsMiss = status == "Miss";
                    cellState.IsSunk = status == "Sunk";

                    // 🔥 ВАЖНО: Клетки вокруг потопленных кораблей
                    // Если это промах (Miss) и клетка была отстреляна, и нет корабля
                    cellState.IsAroundSunk = (status == "Miss") &&
                                            serverCell.WasShot &&
                                            !serverCell.HasShip;

                    // Обновляем информацию о корабле (только для своей доски)
                    if (isPlayerCell)
                    {
                        cellState.HasShip = serverCell.HasShip;
                    }
                    else
                    {
                        // Для противника всегда скрываем корабли
                        cellState.HasShip = false;
                    }

                    // Логирование для отладки
                    if (cellState.IsAroundSunk || cellState.IsSunk || cellState.IsHit)
                    {
                        Console.WriteLine($"   [{serverCell.X},{serverCell.Y}] Status={status}, " +
                                         $"HasShip={serverCell.HasShip}, WasShot={serverCell.WasShot}, " +
                                         $"IsHit={cellState.IsHit}, IsMiss={cellState.IsMiss}, " +
                                         $"IsSunk={cellState.IsSunk}, IsAroundSunk={cellState.IsAroundSunk}");
                    }

                    // Обновляем цвет
                    cell.Background = cellState.GetCurrentColor();

                    // Обновляем символ
                    if (isPlayerCell)
                        UpdatePlayerCellSymbol(cell, cellState);
                    else
                        UpdateOpponentCellSymbol(cell, cellState);

                    // Обновляем доступность (для поля противника)
                    if (!isPlayerCell)
                    {
                        cell.IsEnabled = !cellState.WasShot && !cellState.IsAroundSunk && _isMyTurn;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка в UpdateCellFromServerData: {ex.Message}");
            }
        }

        private string ConvertCellStatus(string statusNumber)
        {
            if (int.TryParse(statusNumber, out int statusInt))
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

            // Если пришла строка, а не число
            return statusNumber switch
            {
                "0" => "Empty",
                "2" => "Hit",
                "3" => "Miss",
                "4" => "Sunk",
                _ => statusNumber // Оставляем как есть
            };
        }


        private void TurnTimer_Tick(object sender, EventArgs e)
        {
            if (!_isMyTurn) return;

            if (_remainingTurnTime > 0)
            {
                _remainingTurnTime--;
                UpdateTimerDisplay();
            }
            else
            {
                _turnTimer.Stop();
                TimeExpired();
            }
        }

        private void UpdateTimerDisplay()
        {
            Dispatcher.Invoke(() =>
            {
                TurnTimeTextBlock.Text = $"{_remainingTurnTime} сек";

                if (_remainingTurnTime <= 10)
                {
                    TurnTimerContainer.Background = new SolidColorBrush(Color.FromRgb(231, 76, 60));
                    TurnTimeTextBlock.Foreground = Brushes.White;
                }
                else if (_remainingTurnTime <= 20)
                {
                    TurnTimerContainer.Background = new SolidColorBrush(Color.FromRgb(241, 196, 15));
                    TurnTimeTextBlock.Foreground = Brushes.Black;
                }
                else
                {
                    TurnTimerContainer.Background = new SolidColorBrush(Color.FromRgb(46, 204, 113));
                    TurnTimeTextBlock.Foreground = Brushes.White;
                }
            });
        }

        private void StartMyTurn()
        {
            Dispatcher.Invoke(() =>
            {
                _isMyTurn = true;
                _remainingTurnTime = 30;
                _turnTimer.Stop();
                _turnTimer.Start();

                TurnTimerContainer.Visibility = Visibility.Visible;
                UpdateTimerDisplay();

                EnableOpponentBoard(true);
                GameStatus.Text = "Ваш ход! Выберите клетку на поле противника";

                Console.WriteLine("✅ Таймер хода запущен");
            });
        }

        private void StopMyTurn()
        {
            Dispatcher.Invoke(() =>
            {
                _isMyTurn = false;
                _turnTimer.Stop();
                TurnTimerContainer.Visibility = Visibility.Collapsed;

                EnableOpponentBoard(false);
                GameStatus.Text = "Ход противника...";

                Console.WriteLine("⏸️ Таймер хода остановлен");
            });
        }

        private async void TimeExpired()
        {
            Console.WriteLine("⏰ Время хода истекло!");

            if (_currentState == GameClientState.InGame && await IsMyTurn())
            {
                await SetOpponentTurnAsync();
                ShowSpecialMessage("Время хода истекло!", 2000);
            }
        }

        private async Task SetOpponentTurnAsync()
        {
            StopMyTurn();

            if (_isMyTurn)
            {
                if (await IsMyTurn())
                {
                    await SetPlayerTurnAsync();
                    return;
                }
            }

            Dispatcher.Invoke(() =>
            {
                EnableOpponentBoard(false);
                GameStatus.Text = "Ход противника...";
            });

            if (_networkService is ServerNetworkManager serverManager)
            {
                await serverManager.ForceUpdateBoards();
            }
        }

        private async Task SetPlayerTurnAsync()
        {
            if (!_isMyTurn)
            {
                if (!await IsMyTurn())
                {
                    await SetOpponentTurnAsync();
                    return;
                }
            }

            StartMyTurn();

            if (_networkService is ServerNetworkManager serverManager)
            {
                await serverManager.ForceUpdateBoards();
            }
        }

        private async Task UpdateOpponentBoardAsync(bool forceReload = false)
        {
            try
            {
                if (forceReload)
                {
                    await LoadGameStateFromServer();
                }

                bool canShoot = await IsMyTurn() && _currentState == GameClientState.InGame;

                Dispatcher.Invoke(() =>
                {
                    foreach (var kvp in _opponentCells)
                    {
                        var cell = kvp.Value;
                        string cellKey = kvp.Key;

                        if (cell.Tag is Tuple<string, CellState> tag)
                        {
                            var (_, cellState) = tag;

                            cell.Background = cellState.GetCurrentColor();
                            UpdateOpponentCellSymbol(cell, cellState);

                            cell.IsEnabled = canShoot && !cellState.WasShot;
                            cell.Cursor = cell.IsEnabled ? Cursors.Hand : Cursors.Arrow;
                        }
                    }

                    Console.WriteLine($"🔄 Обновлено поле противника. Можно стрелять: {canShoot}");
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка обновления поля противника: {ex.Message}");
            }
        }

        private async Task UpdateAllBoardsAsync()
        {
            UpdatePlayerBoardVisual();
            await UpdateOpponentBoardAsync(true);
        }


        private void UpdateButtonsState()
        {
            switch (_currentState)
            {
                case GameClientState.PlacingShips:
                    RandomPlacementButton.IsEnabled = true;
                    ClearBoardButton.IsEnabled = true;
                    RandomOpponentButton.IsEnabled = _gameLogic.AllShipsPlaced;
                    PlayWithFriendButton.IsEnabled = _gameLogic.AllShipsPlaced;

                    RandomPlacementButton.Opacity = 1;
                    ClearBoardButton.Opacity = 1;
                    RandomOpponentButton.Opacity = _gameLogic.AllShipsPlaced ? 1 : 0.5;
                    PlayWithFriendButton.Opacity = _gameLogic.AllShipsPlaced ? 1 : 0.5;
                    break;

                case GameClientState.SearchingGame:
                case GameClientState.InGame:
                    RandomPlacementButton.IsEnabled = false;
                    ClearBoardButton.IsEnabled = false;
                    RandomOpponentButton.IsEnabled = false;
                    PlayWithFriendButton.IsEnabled = false;

                    RandomPlacementButton.Opacity = 0.5;
                    ClearBoardButton.Opacity = 0.5;
                    RandomOpponentButton.Opacity = 0.5;
                    PlayWithFriendButton.Opacity = 0.5;
                    break;

                case GameClientState.GameFinished:
                    RandomPlacementButton.IsEnabled = true;
                    ClearBoardButton.IsEnabled = true;
                    RandomOpponentButton.IsEnabled = true;
                    PlayWithFriendButton.IsEnabled = true;

                    RandomPlacementButton.Opacity = 1;
                    ClearBoardButton.Opacity = 1;
                    RandomOpponentButton.Opacity = 1;
                    PlayWithFriendButton.Opacity = 1;
                    break;
            }
        }

        private async Task SetGameStateAsync(GameClientState newState)
        {
            _currentState = newState;
            UpdateButtonsState();
            await UpdateAllBoardsAsync();

            switch (newState)
            {
                case GameClientState.PlacingShips:
                    GameStatus.Text = "Расставьте свои корабли";
                    EnablePlayerBoard(true);
                    EnableOpponentBoard(false);
                    StopMyTurn();
                    break;

                case GameClientState.SearchingGame:
                    GameStatus.Text = "🔍 Поиск случайного соперника...";
                    EnablePlayerBoard(false);
                    EnableOpponentBoard(false);
                    StopMyTurn();
                    break;

                case GameClientState.InGame:
                    GameStatus.Text = "Игра началась!";
                    EnablePlayerBoard(false);
                    await UpdateAllBoardsAsync();
                    StopMyTurn();
                    break;

                case GameClientState.GameFinished:
                    GameStatus.Text = "Игра окончена";
                    EnablePlayerBoard(false);
                    EnableOpponentBoard(false);
                    StopMyTurn();
                    break;
            }
        }

        private void ChatWindowControl_UnreadCountChanged(object sender, int count)
        {
            Dispatcher.Invoke(() =>
            {
                _unreadMessages = count;
                UpdateUnreadBadge();
            });
        }

        private void UpdateUnreadBadge()
        {
            if (_unreadMessages > 0)
            {
                UnreadBadge.Visibility = Visibility.Visible;
                UnreadCountText.Text = _unreadMessages > 9 ? "9+" : _unreadMessages.ToString();
            }
            else
            {
                UnreadBadge.Visibility = Visibility.Collapsed;
            }
        }

        private async void ConnectToServer()
        {
            _playerName = Application.Current.Properties.Contains("Username")
                ? Application.Current.Properties["Username"].ToString()
                : "Игрок";

            await _networkService.ConnectAsync(_playerName);
            _gameId = _networkService.GameId;
            _playerId = _networkService.PlayerId;
        }

        private void EnableOpponentBoard(bool enable)
        {
            foreach (var cell in _opponentCells.Values)
            {
                cell.IsEnabled = enable;
                cell.Cursor = enable ? Cursors.Hand : Cursors.Arrow;
            }
        }

        private void EnablePlayerBoard(bool enable)
        {
            foreach (var cell in _playerCells.Values)
            {
                cell.IsEnabled = enable;
            }
        }

        private void SetupNetworkEvents()
        {
            _networkService.OnConnectionChanged += (isConnected) =>
            {
                Dispatcher.Invoke(() =>
                {
                    ConnectionStatus.Text = isConnected ? "Подключено" : "Не подключено";
                    if (isConnected && _currentState == GameClientState.PlacingShips)
                    {
                        _ = SetGameStateAsync(GameClientState.PlacingShips);
                    }
                });
            };

            _networkService.OnGameStarted += async (startMessage) =>
            {
                await Dispatcher.Invoke(async () =>
                {
                    _opponentName = !string.IsNullOrEmpty(startMessage.OpponentName)
                        ? startMessage.OpponentName
                        : "Соперник";

                    GameStatus.Text = $"Игра началась! Противник: {_opponentName}";
                    ConnectionStatus.Text = "В игре";

                    _ = Task.Run(StartPeriodicUpdate);

                    await LoadGameStateFromServer();
                    await SetGameStateAsync(GameClientState.InGame);



                    await Task.Delay(1000);

                    bool isMyTurn = await IsMyTurn();
                    if (isMyTurn)
                    {
                        await SetPlayerTurnAsync();
                    }
                    else
                    {
                        await SetOpponentTurnAsync();
                    }

                    ChatWindowControl.AddSystemMessage($"Игра началась. Ваш соперник: {_opponentName}");
                    OpenChatButton.Visibility = Visibility.Visible;

                    _playerShots.Clear();
                    _hitsOnOpponent.Clear();
                });
            };

            _networkService.OnGameEnded += (endMessage) =>
            {
                Dispatcher.Invoke(async () =>
                {
                    await SetGameStateAsync(GameClientState.GameFinished);

                    OpenChatButton.Visibility = Visibility.Collapsed;
                    ChatWindowControl.Visibility = Visibility.Collapsed;

                    string resultMessage = endMessage.Winner == "player"
                        ? "Вы победили! Поздравляем!"
                        : "Вы проиграли. Попробуйте еще раз!";
                    ChatWindowControl.AddSystemMessage(resultMessage);

                    GameOverWindow gameOverWindow = new GameOverWindow(
                        endMessage.Winner,
                        _opponentName,
                        endMessage.Stats);

                    gameOverWindow.Owner = this;
                    bool? dialogResult = gameOverWindow.ShowDialog();

                    if (dialogResult == true)
                    {
                        if (gameOverWindow.PlayAgain)
                        {
                            ResetGameForNewRound();
                        }
                        else
                        {
                            _isExitingFromGameOver = true;
                            ReturnToMainMenu();
                        }
                    }
                });
            };

            _networkService.OnShootResult += async (result) =>
            {
                await Dispatcher.Invoke(async () =>
                {
                    Console.WriteLine($"=== UI: Получен результат выстрела ===");
                    Console.WriteLine($"Клетка: {result.Row},{result.Col}, Результат: {result.Result}");
                    Console.WriteLine($"Продолжает ход: {result.ContinueTurn}");

                    string cellKey = $"{result.Row},{result.Col}";
                    _playerShots.Add(cellKey);

                    // 1. Обновляем конкретную клетку для быстрой обратной связи
                    UpdateOpponentCellAfterShot(result.Row, result.Col, result.Result, result.ShipName);

                    // 2. ЗАГРУЖАЕМ ПОЛНОЕ СОСТОЯНИЕ С СЕРВЕРА
                    // Сервер уже пометил ВСЕ клетки вокруг потопленного корабля
                    await LoadGameStateFromServer();

                    // 3. Обновляем ВСЮ доску противника из данных сервера
                    await UpdateOpponentBoardAsync(true); // forceReload = true

                    if (result.Result == "sunk")
                    {
                        ShowSpecialMessage($"Вы потопили {result.ShipName}!", 3000);
                    }
                    else if (result.Result == "hit")
                    {
                        ShowSpecialMessage("Попадание!", 2000);
                    }
                    else if (result.Result == "miss")
                    {
                        ShowSpecialMessage("Промах!", 1500);
                    }

                    // 4. Проверяем, не окончена ли игра
                    if (result.IsGameOver)
                    {
                        Console.WriteLine("🏁 Игра окончена из OnShootResult!");
                        return;
                    }

                    if (result.ContinueTurn)
                    {
                        _remainingTurnTime = 30;
                        UpdateTimerDisplay();
                    }
                    else
                    {
                        StopMyTurn();
                        await SetOpponentTurnAsync();
                    }
                });
            };

            _networkService.OnOpponentShoot += async (shoot) =>
            {
                await Dispatcher.Invoke(async () =>
                {
                    string cellKey = $"{shoot.Row},{shoot.Col}";
                    Console.WriteLine($"=== UI: Выстрел противника в {cellKey} ===");

                    _opponentShots.Add(cellKey);

                    
                    Console.WriteLine("🔄 Загружаем состояние с сервера после выстрела противника...");
                    await LoadGameStateFromServer();

                    if (shoot.IsHit)
                    {
                        _hitsOnPlayer.Add(cellKey);
                        ShowSpecialMessage($"Противник попал в ваш корабль!", 3000);
                    }
                    else
                    {
                        ShowSpecialMessage("Противник промахнулся!", 2000);
                    }

                    await Task.Delay(1000);
                    await CheckAndUpdateTurn();
                });
            };

            _networkService.OnGameStateUpdated += async (state) =>
            {
                await Dispatcher.Invoke(async () =>
                {
                    if (_showingSpecialMessage) return;

                    Console.WriteLine($"=== UI: Обновление состояния игры ===");
                    Console.WriteLine($"Статус: {state.Status}, Ход: {state.CurrentTurn}");

                    if (state.Status == "playing")
                    {
                        if (state.CurrentTurn == "player" && !_isMyTurn)
                        {
                            await SetPlayerTurnAsync();
                        }
                        else if (state.CurrentTurn == "opponent" && _isMyTurn)
                        {
                            await SetOpponentTurnAsync();
                        }
                    }
                    else if (state.Status == "finished")
                    {
                        StopMyTurn();
                        EnableOpponentBoard(false);
                        EnablePlayerBoard(false);
                        GameStatus.Text = "Игра окончена";
                    }
                });
            };

            _networkService.OnError += (error) =>
            {
                Dispatcher.Invoke(() =>
                {
                    AlertService.ShowNetworkAlert(error);

                    if (error.Code == "SERVER_DISCONNECTED")
                    {
                        foreach (var cell in _opponentCells.Values)
                        {
                            cell.IsEnabled = false;
                        }

                        foreach (var cell in _playerCells.Values)
                        {
                            cell.IsEnabled = false;
                        }

                        GameStatus.Text = "Сервер недоступен. Игра завершена.";
                        StopMyTurn();

                        Task.Delay(5000).ContinueWith(_ =>
                        {
                            Dispatcher.Invoke(() =>
                            {
                                if (!_isExitingFromGameOver)
                                {
                                    ReturnToMainMenu();
                                }
                            });
                        });
                    }
                });
            };

            _networkService.OnOpponentDisconnected += (message) =>
            {
                Dispatcher.Invoke(() =>
                {
                    ChatWindowControl.AddSystemMessage($"⚠️ {message}");
                    ShowSpecialMessage(message, 5000);

                    foreach (var cell in _opponentCells.Values)
                    {
                        cell.IsEnabled = false;
                    }

                    GameStatus.Text = "Противник отключился";
                    StopMyTurn();
                });
            };

            _networkService.OnChatMessage += (chatMessage) =>
            {
                Dispatcher.Invoke(() =>
                {
                    string playerName = Application.Current.Properties.Contains("Username")
                        ? Application.Current.Properties["Username"].ToString()
                        : "Вы";

                    if (chatMessage.IsSystem)
                    {
                        ChatWindowControl.AddSystemMessage(chatMessage.Message);
                    }
                    else
                    {
                        bool isOwn = !chatMessage.IsFromOpponent;
                        string senderDisplayName = isOwn ? "Вы" : chatMessage.Sender;

                        ChatWindowControl.AddMessage(senderDisplayName, chatMessage.Message, isOwn);
                    }

                    if (ChatWindowControl.Visibility != Visibility.Visible &&
                        !chatMessage.IsSystem &&
                        chatMessage.IsFromOpponent)
                    {
                        ShowSpecialMessage($"Новое сообщение от {chatMessage.Sender}", 2000);
                    }
                });
            };
        }

        private async Task CheckAndUpdateTurn()
        {
            try
            {
                var gameState = await _networkService.GetUpdatedGameStateAsync();
                if (gameState != null && gameState.Status == "playing")
                {
                    if (gameState.CurrentTurn == "player")
                    {
                        await SetPlayerTurnAsync();
                    }
                    else
                    {
                        await SetOpponentTurnAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка проверки хода: {ex.Message}");
            }
        }

        private void UpdateOpponentCellAfterShot(int row, int col, string result, string shipName = "")
        {
            string cellKey = $"{row},{col}";

            if (!_opponentCells.TryGetValue(cellKey, out Border cell)) return;

            if (cell.Tag is Tuple<string, CellState> tag)
            {
                var (_, cellState) = tag;

                switch (result)
                {
                    case "hit":
                        cellState.IsHit = true;
                        cellState.IsMiss = false;
                        cell.Background = Brushes.Red;
                        _hitsOnOpponent.Add(cellKey);
                        ShowCellSymbol(cell, "✖", Brushes.White);
                        break;

                    case "sunk":
                        cellState.IsHit = true;
                        cellState.IsSunk = true;
                        cell.Background = Brushes.DarkRed;
                        _hitsOnOpponent.Add(cellKey);
                        ShowCellSymbol(cell, "☠", Brushes.White);

                        
                        Console.WriteLine($"💥 Потоплен корабль {shipName} на поле противника!");
                        break;

                    case "miss":
                        cellState.IsMiss = true;
                        cellState.IsHit = false;
                        cell.Background = Brushes.LightGray;
                        ShowCellSymbol(cell, "○", Brushes.DarkGray);
                        break;
                }

                cell.IsEnabled = false;
            }
        }

        private void UpdatePlayerBoardVisual()
        {
            foreach (var kvp in _playerCells)
            {
                var cell = kvp.Value;
                if (cell.Tag is Tuple<string, CellState> tag)
                {
                    var (_, cellState) = tag;
                    cell.Background = cellState.GetCurrentColor();
                    UpdatePlayerCellSymbol(cell, cellState);
                }
            }
        }

        private void UpdatePlayerCellSymbol(Border cell, CellState cellState)
        {
            if (cell.Child is TextBlock textBlock)
            {
                // 1. В первую очередь - клетки вокруг потопленных кораблей
                if (cellState.IsAroundSunk && !cellState.HasShip)
                {
                    textBlock.Text = "○";
                    textBlock.Foreground = Brushes.DarkGray;
                    textBlock.Visibility = Visibility.Visible;
                }
                // 2. Потом потопленные клетки кораблей
                else if (cellState.IsSunk && cellState.HasShip)
                {
                    textBlock.Text = "☠";
                    textBlock.Foreground = Brushes.White;
                    textBlock.Visibility = Visibility.Visible;
                }
                // 3. Попадания (но не потопленные)
                else if (cellState.IsHit && cellState.HasShip && !cellState.IsSunk)
                {
                    textBlock.Text = "✖";
                    textBlock.Foreground = Brushes.White;
                    textBlock.Visibility = Visibility.Visible;
                }
                // 4. Обычные промахи
                else if (cellState.IsMiss && !cellState.HasShip && !cellState.IsAroundSunk)
                {
                    textBlock.Text = "○";
                    textBlock.Foreground = Brushes.DarkGray;
                    textBlock.Visibility = Visibility.Visible;
                }
                else
                {
                    textBlock.Visibility = Visibility.Hidden;
                }
            }
        }

        private void ShowCellSymbol(Border cell, string symbol, Brush color)
        {
            if (cell.Child is TextBlock existingTextBlock)
            {
                existingTextBlock.Text = symbol;
                existingTextBlock.Foreground = color;
                existingTextBlock.Visibility = Visibility.Visible;
                existingTextBlock.HorizontalAlignment = HorizontalAlignment.Center;
                existingTextBlock.VerticalAlignment = VerticalAlignment.Center;
                existingTextBlock.FontSize = 16;
                existingTextBlock.FontWeight = FontWeights.Bold;
            }
            else
            {
                var textBlock = new TextBlock
                {
                    Text = symbol,
                    Foreground = color,
                    Visibility = Visibility.Visible,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 16,
                    FontWeight = FontWeights.Bold
                };
                cell.Child = textBlock;
            }
        }

        private void UpdateOpponentCellSymbol(Border cell, CellState cellState)
        {
            if (cell.Child is TextBlock textBlock)
            {
                // 1. Клетки вокруг потопленных кораблей противника
                if (cellState.IsAroundSunk && !cellState.HasShip)
                {
                    textBlock.Text = "○";
                    textBlock.Foreground = Brushes.DarkGray;
                    textBlock.Visibility = Visibility.Visible;
                    Console.WriteLine($"   ✅ Отображение IsAroundSunk на поле противника");
                }
                // 2. Потопленные клетки кораблей противника
                else if (cellState.IsSunk && cellState.HasShip)
                {
                    textBlock.Text = "☠";
                    textBlock.Foreground = Brushes.White;
                    textBlock.Visibility = Visibility.Visible;
                }
                // 3. Попадания по кораблям противника
                else if (cellState.IsHit && cellState.HasShip && !cellState.IsSunk)
                {
                    textBlock.Text = "✖";
                    textBlock.Foreground = Brushes.White;
                    textBlock.Visibility = Visibility.Visible;
                }
                // 4. Обычные промахи по противнику
                else if (cellState.IsMiss && !cellState.HasShip && !cellState.IsAroundSunk)
                {
                    textBlock.Text = "○";
                    textBlock.Foreground = Brushes.DarkGray;
                    textBlock.Visibility = Visibility.Visible;
                }
                else
                {
                    textBlock.Visibility = Visibility.Hidden;
                }
            }
        }

        private async Task<bool> IsMyTurn()
        {
            try
            {
                if (!_networkService.IsInGame)
                    return false;

                var gameState = await _networkService.GetUpdatedGameStateAsync();
                if (gameState == null)
                {
                    return _isMyTurn;
                }

                bool isMyTurn = gameState.CurrentTurn == "player";
                Console.WriteLine($"✅ Проверка хода: сервер говорит {isMyTurn}");
                return isMyTurn;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Ошибка проверки хода: {ex.Message}");
                return _isMyTurn;
            }
        }

        private async void ChatWindowControl_MessageSent(object sender, string message)
        {
            try
            {
                string playerName = Application.Current.Properties.Contains("Username")
                    ? Application.Current.Properties["Username"].ToString()
                    : "Вы";

                ChatWindowControl.AddMessage(playerName, message, isOwn: true);

                if (_networkService.IsInGame && _networkService.IsConnected)
                {
                    await _networkService.SendChatMessageAsync(message);
                }
                else
                {
                    ChatWindowControl.AddSystemMessage("Нет подключения к серверу. Сообщение не отправлено.");
                }
            }
            catch (Exception ex)
            {
                ChatWindowControl.AddSystemMessage($"Ошибка отправки: {ex.Message}");
            }
        }

        private void ChatWindowControl_Closed(object sender, EventArgs e)
        {
            ChatWindowControl.Visibility = Visibility.Collapsed;
        }

        private void OpenChatButton_Click(object sender, RoutedEventArgs e)
        {
            ChatWindowControl.Visibility = Visibility.Visible;
            ChatWindowControl.MarkAsRead();
            UpdateUnreadBadge();
        }

        private async void ResetGameForNewRound()
        {
            await SetGameStateAsync(GameClientState.PlacingShips);

            _gameLogic.ResetForNewGame();

            _playerShots.Clear();
            _opponentShots.Clear();
            _hitsOnPlayer.Clear();
            _hitsOnOpponent.Clear();

            UpdateYourBoard();

            foreach (var cell in _opponentCells.Values)
            {
                cell.Background = new SolidColorBrush(Color.FromRgb(40, 50, 60));
                cell.IsEnabled = false;
            }

            EnablePlayerBoard(true);

            OpenChatButton.Visibility = Visibility.Collapsed;
            ChatWindowControl.Visibility = Visibility.Collapsed;
            ChatWindowControl.ClearChat();

            UpdateShipsInfo();

            await _networkService.LeaveGameAsync();

            GameStatus.Text = "Новая игра! Расставьте корабли.";
            _unreadMessages = 0;
            UpdateUnreadBadge();
        }

        private void ReturnToMainMenu()
        {
            _networkService.LeaveGameAsync();
            _gameLogic.ClearBoard();
            _playerShots.Clear();
            _opponentShots.Clear();
            _hitsOnPlayer.Clear();
            _hitsOnOpponent.Clear();

            MainWindow mainWindow = new MainWindow();
            mainWindow.Show();
            this.Close();
            _unreadMessages = 0;
            UpdateUnreadBadge();
        }

        private void ShowSpecialMessage(string message, int durationMilliseconds)
        {
            if (_messageTimer != null)
            {
                _messageTimer.Stop();
                _messageTimer = null;
            }

            _showingSpecialMessage = true;
            GameStatus.Text = message;

            _messageTimer = new DispatcherTimer();
            _messageTimer.Interval = TimeSpan.FromMilliseconds(durationMilliseconds);
            _messageTimer.Tick += (s, e) =>
            {
                _messageTimer.Stop();
                _showingSpecialMessage = false;

                if (_currentState == GameClientState.InGame)
                {
                    GameStatus.Text = _isMyTurn ? "Ваш ход!" : "Ход противника...";
                }
                else
                {
                    GameStatus.Text = _gameLogic.GetCurrentShipInfo();
                }
            };

            _messageTimer.Start();
        }

        private void InitializeGameBoards()
        {
            InitializeBoard(YourBoardGrid, true);
            InitializeBoard(OpponentBoardGrid, false);
            UpdateShipsInfo();

            foreach (var cell in _opponentCells.Values)
            {
                cell.Background = new SolidColorBrush(Color.FromRgb(40, 50, 60));
                cell.IsEnabled = false;
                cell.Cursor = Cursors.Arrow;
            }

            UpdateYourBoard();
        }

        private void InitializeBoard(Grid boardGrid, bool isYourBoard)
        {
            boardGrid.Children.Clear();
            boardGrid.RowDefinitions.Clear();
            boardGrid.ColumnDefinitions.Clear();

            if (isYourBoard)
                _playerCells.Clear();
            else
                _opponentCells.Clear();

            for (int i = 0; i <= GridSize; i++)
            {
                boardGrid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(CellSize) });
                boardGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(CellSize) });
            }

            for (int col = 0; col < GridSize; col++)
            {
                TextBlock letter = new TextBlock
                {
                    Text = ((char)('А' + col)).ToString(),
                    Foreground = Brushes.White,
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetRow(letter, 0);
                Grid.SetColumn(letter, col + 1);
                boardGrid.Children.Add(letter);
            }

            for (int row = 0; row < GridSize; row++)
            {
                TextBlock number = new TextBlock
                {
                    Text = (row + 1).ToString(),
                    Foreground = Brushes.White,
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetRow(number, row + 1);
                Grid.SetColumn(number, 0);
                boardGrid.Children.Add(number);
            }

            for (int row = 0; row < GridSize; row++)
            {
                for (int col = 0; col < GridSize; col++)
                {
                    Border cell = CreateCell(row, col, isYourBoard);
                    Grid.SetRow(cell, row + 1);
                    Grid.SetColumn(cell, col + 1);
                    boardGrid.Children.Add(cell);

                    if (isYourBoard)
                    {
                        _playerCells[$"{row},{col}"] = cell;
                    }
                    else
                    {
                        _opponentCells[$"{row},{col}"] = cell;
                        cell.IsEnabled = false;
                    }
                }
            }
        }

        private Border CreateCell(int row, int col, bool isYourBoard)
        {
            var cellState = new CellState
            {
                HasShip = false,
                IsHit = false,
                IsMiss = false,
                IsPlacing = false,
                BaseColor = new SolidColorBrush(Color.FromRgb(40, 50, 60))
            };

            Border cell = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(79, 92, 110)),
                BorderThickness = new Thickness(1),
                Background = cellState.BaseColor,
                Tag = new Tuple<string, CellState>($"{row},{col}", cellState),
                Cursor = isYourBoard ? Cursors.Hand : Cursors.Arrow
            };

            if (isYourBoard)
            {
                cell.MouseLeftButtonDown += YourCell_MouseLeftButtonDown;
                cell.MouseRightButtonDown += YourCell_MouseRightButtonDown;
                cell.MouseEnter += Cell_MouseEnter;
                cell.MouseLeave += Cell_MouseLeave;
            }
            else
            {
                cell.MouseLeftButtonDown += OpponentCell_MouseLeftButtonDown;
                cell.MouseEnter += Cell_MouseEnter;
                cell.MouseLeave += Cell_MouseLeave;
            }

            return cell;
        }

        private void Cell_MouseEnter(object sender, MouseEventArgs e)
        {
            var cell = (Border)sender;

            if (cell.Tag is Tuple<string, CellState> tag)
            {
                var (_, cellState) = tag;
                cellState.IsHighlighted = true;
                cell.Background = cellState.GetCurrentColor();
            }
        }

        private void Cell_MouseLeave(object sender, MouseEventArgs e)
        {
            var cell = (Border)sender;

            if (cell.Tag is Tuple<string, CellState> tag)
            {
                var (_, cellState) = tag;
                cellState.IsHighlighted = false;
                cell.Background = cellState.GetCurrentColor();
            }
        }

        private void UpdateCellState(CellState cellState, int row, int col, string cellKey, bool isPlayerCell)
        {
            if (isPlayerCell)
            {
                bool isCurrentShipCell = _gameLogic.GetCurrentShipBeingPlacedCells()
                    .Any(c => c.row == row && c.col == col);
                bool hasShip = _gameLogic.GetPlayerShipCells()
                    .Any(c => c.row == row && c.col == col);

                cellState.IsPlacing = isCurrentShipCell;
                cellState.HasShip = hasShip;
                cellState.IsHit = _hitsOnPlayer.Contains(cellKey);
                cellState.IsMiss = _opponentShots.Contains(cellKey);

                if (isCurrentShipCell)
                {
                    cellState.BaseColor = new SolidColorBrush(Color.FromRgb(106, 137, 204));
                }
                else if (hasShip)
                {
                    cellState.BaseColor = Brushes.DarkGray;
                }
                else if (cellState.IsHit)
                {
                    cellState.BaseColor = Brushes.Red;
                }
                else if (cellState.IsMiss)
                {
                    cellState.BaseColor = Brushes.LightBlue;
                }
                else
                {
                    cellState.BaseColor = new SolidColorBrush(Color.FromRgb(40, 50, 60));
                }
            }
            else
            {
                cellState.HasShip = false;
                cellState.IsPlacing = false;
                cellState.IsHit = _hitsOnOpponent.Contains(cellKey);
                cellState.IsMiss = _playerShots.Contains(cellKey) && !_hitsOnOpponent.Contains(cellKey);

                if (cellState.IsHit)
                {
                    cellState.BaseColor = Brushes.Red;
                }
                else if (cellState.IsMiss)
                {
                    cellState.BaseColor = Brushes.LightGray;
                }
                else
                {
                    cellState.BaseColor = new SolidColorBrush(Color.FromRgb(40, 50, 60));
                }
            }
        }

        private void YourCell_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_currentState != GameClientState.PlacingShips) return;

            var cell = (Border)sender;

            if (cell.Tag is Tuple<string, CellState> tag)
            {
                var (coordsStr, _) = tag;
                var coords = coordsStr.Split(',');
                int row = int.Parse(coords[0]);
                int col = int.Parse(coords[1]);

                if (_gameLogic.TryPlaceShipCell(row, col))
                {
                    UpdateYourBoard();
                    UpdateShipsInfo();
                    UpdateButtonsState();

                    if (!_showingSpecialMessage)
                    {
                        GameStatus.Text = _gameLogic.GetCurrentShipInfo();
                    }
                }
                else
                {
                    ShowSpecialMessage("Нельзя поставить корабль здесь!", 2000);
                }
            }
        }

        private void YourCell_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_currentState != GameClientState.PlacingShips) return;

            var cell = (Border)sender;

            if (cell.Tag is Tuple<string, CellState> tag)
            {
                if (_gameLogic.IsPlacingShip())
                {
                    _gameLogic.CancelCurrentShipPlacement();
                }
                else
                {
                    _gameLogic.RemoveLastCell();
                }

                UpdateYourBoard();
                UpdateShipsInfo();
                UpdateButtonsState();
            }
        }

        private async void OpponentCell_MouseLeftButtonDown(object sender, RoutedEventArgs e)
        {
            if (_currentState != GameClientState.InGame) return;

            if (!await IsMyTurn())
            {
                ShowSpecialMessage("Сейчас не ваш ход!", 2000);
                return;
            }

            var cell = (Border)sender;

            if (cell.Tag is Tuple<string, CellState> tag)
            {
                var (coordsStr, cellState) = tag;

                if (cellState.WasShot || cellState.IsAroundSunk)
                {
                    ShowSpecialMessage("Сюда уже стреляли!", 2000);
                    return;
                }

                var coords = coordsStr.Split(',');
                int row = int.Parse(coords[0]);
                int col = int.Parse(coords[1]);

                bool success = await _networkService.ShootAsync(row, col);

                if (success)
                {

                    await Task.Delay(500);

                    if (_networkService is ServerNetworkManager serverManager)
                    {
                        await serverManager.ForceUpdateBoards();
                    }

                    cell.IsEnabled = false;
                }
            }
        }

        private async void StartPeriodicUpdate()
        {
            while (_currentState == GameClientState.InGame)
            {
                await Task.Delay(3000); // Обновляем каждые 3 секунды

                try
                {
                    if (_networkService is ServerNetworkManager serverManager)
                    {
                        await serverManager.ForceUpdateBoards();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Ошибка периодического обновления: {ex.Message}");
                }
            }
        }

        private void UpdateYourBoard()
        {
            foreach (var kvp in _playerCells)
            {
                var cell = kvp.Value;
                var coords = kvp.Key.Split(',');
                int row = int.Parse(coords[0]);
                int col = int.Parse(coords[1]);
                string cellKey = kvp.Key;

                if (cell.Tag is Tuple<string, CellState> tag)
                {
                    var (coordsStr, cellState) = tag;

                    UpdateCellState(cellState, row, col, coordsStr, true);
                    cell.Background = cellState.GetCurrentColor();
                    UpdatePlayerCellSymbol(cell, cellState);
                }
            }
        }

        private void UpdateShipsInfo()
        {
            int placed4 = 0, placed3 = 0, placed2 = 0, placed1 = 0;
            int total4 = 1, total3 = 2, total2 = 3, total1 = 4;

            foreach (var ship in _gameLogic.PlayerShips)
            {
                switch (ship.Size)
                {
                    case 4: if (ship.IsPlaced) placed4++; break;
                    case 3: if (ship.IsPlaced) placed3++; break;
                    case 2: if (ship.IsPlaced) placed2++; break;
                    case 1: if (ship.IsPlaced) placed1++; break;
                }
            }

            ShipsInfo.Text = $"Осталось расставить: {total4 - placed4}x4, {total3 - placed3}x3, {total2 - placed2}x2, {total1 - placed1}x1";
            GameStatus.Text = _gameLogic.GetCurrentShipInfo();
        }

        private void RandomPlacementButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentState != GameClientState.PlacingShips) return;

            _gameLogic.RandomlyPlaceShips();
            UpdateYourBoard();
            UpdateShipsInfo();
            UpdateButtonsState();
            ShowSpecialMessage("Корабли расставлены случайным образом!", 2000);
        }

        private async void ClearBoardButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentState != GameClientState.PlacingShips) return;

            _gameLogic.ClearBoard();
            _playerShots.Clear();
            _opponentShots.Clear();
            _hitsOnPlayer.Clear();
            _hitsOnOpponent.Clear();
            UpdateYourBoard();

            foreach (var cell in _opponentCells.Values)
            {
                cell.Background = new SolidColorBrush(Color.FromRgb(40, 50, 60));
                cell.IsEnabled = false;
            }

            UpdateShipsInfo();
            UpdateButtonsState();
            ShowSpecialMessage("Поле очищено. Начинайте расстановку заново.", 3000);
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (_networkService.IsInGame)
            {
                var result = MessageBox.Show("Вы в игре. Выйти из игры и вернуться в меню?",
                    "Подтверждение",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                    return;

                _networkService.LeaveGameAsync();
            }

            MainWindow mainWindow = new MainWindow();
            mainWindow.Show();
            this.Close();
        }

        private void PlayWithFriendButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "Режим игры с другом будет реализован позже",
                "Игра с другом",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private async void RandomOpponentButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isSearching)
            {
                CancelSearch();
                return;
            }

            if (!_gameLogic.AllShipsPlaced)
            {
                MessageBox.Show("Сначала расставьте все корабли!", "Внимание",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await SetGameStateAsync(GameClientState.SearchingGame);
            StartSearch();

            var gameId = await _networkService.CreateGameAsync("random");

            if (gameId != null)
            {
                CancelSearch();
                await SetGameStateAsync(GameClientState.InGame);
            }
            else
            {
                Console.WriteLine("⏳ В лобби, ждем противника...");
            }
        }

        private void StartSearch()
        {
            _isSearching = true;
            PlayWithFriendButton.Visibility = Visibility.Collapsed;
            RandomOpponentButton.Visibility = Visibility.Collapsed;
            OpenChatButton.Visibility = Visibility.Collapsed;
            SearchIndicator.Visibility = Visibility.Visible;
            GameStatus.Text = "🔍 Поиск случайного соперника...";
            ConnectionStatus.Text = "Поиск...";
        }

        private void CancelSearchButton_Click(object sender, RoutedEventArgs e)
        {
            CancelSearch();
        }

        private void CancelSearch()
        {
            _isSearching = false;
            PlayWithFriendButton.Visibility = Visibility.Visible;
            RandomOpponentButton.Visibility = Visibility.Visible;
            OpenChatButton.Visibility = Visibility.Collapsed;
            SearchIndicator.Visibility = Visibility.Collapsed;

            _ = SetGameStateAsync(GameClientState.PlacingShips);
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_isExitingFromGameOver)
            {
                base.OnClosing(e);
                return;
            }

            if (_networkService.IsInGame)
            {
                var result = MessageBox.Show("Вы в игре. Выйти из игры?",
                    "Подтверждение",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);  

                if (result != MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }

            base.OnClosing(e);
        }
    }
}