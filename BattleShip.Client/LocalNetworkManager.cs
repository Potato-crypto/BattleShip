using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Newtonsoft.Json;

namespace BattleShip.Client
{
    public class LocalNetworkManager : INetworkService
    {
        // События (из интерфейса)
        public event Action<string> OnMessageReceived;
        public event Action<bool> OnConnectionChanged;
        public event Action<GameStartMessage> OnGameStarted;
        public event Action<GameEndMessage> OnGameEnded;
        public event Action<ShootResultMessage> OnShootResult;
        public event Action<ShootMessage> OnOpponentShoot;
        public event Action<INetworkService.ChatMessage> OnChatMessage;
        public event Action<GameStateMessage> OnGameStateUpdated;
        public event Action<ErrorMessage> OnError;
        
        // Удаляем дублирующее событие OnChatMessageReceived
        // и заменяем его явной реализацией интерфейса
        
        // Свойства (из интерфейса)
        public bool IsConnected { get; private set; }
        public bool IsInGame { get; private set; }
        public string GameId { get; private set; }
        public string PlayerId { get; private set; }
        
        // Имитация сервера
        private Random _random = new Random();
        private Dictionary<string, GameSession> _activeGames = new Dictionary<string, GameSession>();
        private PlayerStats _playerStats = new PlayerStats();
        
        // Текущая игровая сессия
        private GameSession _currentSession;
        private string _playerName;
        
        // Для игры против компьютера
        private bool[,] _computerBoard;
        private List<ShipData> _computerShips;
        
        public LocalNetworkManager()
        {
            PlayerId = Guid.NewGuid().ToString().Substring(0, 8);
            InitializeComputerBoard();
        }
        
        private void InitializeComputerBoard()
        {
            _computerBoard = new bool[10, 10];
            _computerShips = new List<ShipData>();
        }
        
        public async Task ConnectAsync(string playerName)
        {
            await Task.Delay(500); // Имитация задержки сети
            
            _playerName = playerName;
            IsConnected = true;
            
            // Сообщаем об успешном подключении
            OnConnectionChanged?.Invoke(true);
            
            var message = new NetworkMessage
            {
                Type = "connected",
                Data = new { playerId = PlayerId, playerName = playerName },
                Timestamp = DateTime.Now
            };
            
            OnMessageReceived?.Invoke(JsonConvert.SerializeObject(message));
        }
        
        public async Task DisconnectAsync()
        {
            await Task.Delay(300);
            
            if (IsInGame)
            {
                await LeaveGameAsync();
            }
            
            IsConnected = false;
            OnConnectionChanged?.Invoke(false);
        }
        
        public async Task<string> CreateGameAsync(string gameMode)
        {
            await Task.Delay(300);
            
            GameId = Guid.NewGuid().ToString().Substring(0, 6).ToUpper();
            
            // Создаем новую игровую сессию
            _currentSession = new GameSession
            {
                GameId = GameId,
                Player1Id = PlayerId,
                Player1Name = _playerName,
                GameMode = gameMode,
                Status = "waiting",
                CreatedAt = DateTime.Now
            };
            
            _activeGames[GameId] = _currentSession;
            
            // Для игры против компьютера сразу создаем компьютерного противника
            if (gameMode == "computer")
            {
                _currentSession.Player2Id = "COMPUTER";
                _currentSession.Player2Name = "Компьютер";
                _currentSession.Status = "placing";
                
                // Расставляем корабли компьютера
                SetupComputerShips();
                
                // Уведомляем о начале игры
                await Task.Delay(1000);
                
                IsInGame = true;
                
                var startMessage = new GameStartMessage
                {
                    GameId = GameId,
                    OpponentName = "Компьютер",
                    PlayerRole = "first"
                };
                
                OnGameStarted?.Invoke(startMessage);
                
                // Обновляем состояние игры
                UpdateGameState();
            }
            
            return GameId;
        }
        
        public async Task<bool> JoinGameAsync(string gameId)
        {
            await Task.Delay(300);
            
            if (_activeGames.ContainsKey(gameId))
            {
                GameId = gameId;
                _currentSession = _activeGames[gameId];
                
                IsInGame = true;
                return true;
            }
            
            return false;
        }
        
        public async Task<bool> LeaveGameAsync()
        {
            await Task.Delay(300);
            
            if (_currentSession != null && _activeGames.ContainsKey(_currentSession.GameId))
            {
                _activeGames.Remove(_currentSession.GameId);
            }
            
            IsInGame = false;
            GameId = null;
            _currentSession = null;
            
            return true;
        }
        
        public async Task<bool> SendShipsPlacementAsync(List<ShipData> ships)
        {
            await Task.Delay(300);
            
            if (_currentSession == null) return false;
            
            // Сохраняем корабли игрока в сессии
            _currentSession.Player1Ships = ships;
            
            // Если игра против компьютера, начинаем игру
            if (_currentSession.GameMode == "computer")
            {
                _currentSession.Status = "playing";
                _currentSession.CurrentTurn = "player"; // Первый ход у игрока
                
                // Обновляем состояние
                UpdateGameState();
                
                // Отправляем подтверждение
                var message = new NetworkMessage
                {
                    Type = "ships_placed",
                    Data = new { success = true },
                    Timestamp = DateTime.Now,
                    GameId = GameId,
                    PlayerId = PlayerId
                };
                
                OnMessageReceived?.Invoke(JsonConvert.SerializeObject(message));
            }
            
            return true;
        }
        
        public async Task<bool> ShootAsync(int row, int col)
        {
            await Task.Delay(300); // Имитация задержки сети
            
            if (_currentSession == null || _currentSession.Status != "playing")
                return false;
            
            // Проверяем, ход ли игрока
            if (_currentSession.CurrentTurn != "player")
            {
                OnError?.Invoke(new ErrorMessage 
                { 
                    Code = "NOT_YOUR_TURN", 
                    Message = "Сейчас не ваш ход" 
                });
                return false;
            }
            
            // Проверяем, стреляли ли уже в эту клетку
            if (_currentSession.PlayerShots.Any(s => s.Row == row && s.Col == col))
            {
                var result = new ShootResultMessage
                {
                    Row = row,
                    Col = col,
                    Result = "already_shot",
                    NextTurn = "player",
                    RemainingShips = GetRemainingComputerShips()
                };
                
                OnShootResult?.Invoke(result);
                return false;
            }
            
            // Добавляем выстрел в историю
            _currentSession.PlayerShots.Add(new CellData { Row = row, Col = col });
            _playerStats.TotalShots++;
            
            // Проверяем попадание по кораблям компьютера
            bool isHit = false;
            ShipData hitShip = null;
            
            foreach (var ship in _computerShips)
            {
                foreach (var cell in ship.Cells)
                {
                    if (cell.Row == row && cell.Col == col)
                    {
                        isHit = true;
                        hitShip = ship;
                        
                        // Помечаем клетку как подбитую
                        if (ship.HitCells == null) ship.HitCells = new List<CellData>();
                        ship.HitCells.Add(cell);
                        
                        _playerStats.Hits++;
                        break;
                    }
                }
                if (isHit) break;
            }
            
            // Формируем результат
            var shootResult = new ShootResultMessage
            {
                Row = row,
                Col = col,
                Result = isHit ? "hit" : "miss",
                NextTurn = isHit ? "player" : "opponent",
                RemainingShips = GetRemainingComputerShips()
            };
            
            // Проверяем, потоплен ли корабль
            if (isHit && hitShip != null)
            {
                bool isSunk = hitShip.Cells.All(c => 
                    hitShip.HitCells?.Exists(h => h.Row == c.Row && h.Col == c.Col) == true);
                
                if (isSunk)
                {
                    shootResult.Result = "sunk";
                    shootResult.ShipSize = hitShip.Size;
                }
            }
            else if (!isHit)
            {
                _playerStats.Misses++;
            }
            
            // Отправляем результат игроку
            OnShootResult?.Invoke(shootResult);
            
            // Обновляем статистику точности
            _playerStats.Accuracy = _playerStats.TotalShots > 0 ? 
                (double)_playerStats.Hits / _playerStats.TotalShots * 100 : 0;
            
            // Проверяем условие победы
            if (GetRemainingComputerShips() == 0)
            {
                EndGame("player", "all_ships_sunk");
                return true;
            }
            
            // Если промахнулся, ход переходит к компьютеру
            if (!isHit)
            {
                _currentSession.CurrentTurn = "opponent";
                UpdateGameState();
                
                // Компьютер делает ход через 1 секунду
                await Task.Delay(1000);
                await ComputerShootAsync();
            }
            else
            {
                UpdateGameState();
            }
            
            return true;
        }
        
        public async Task SendChatMessageAsync(string message)
        {
            await Task.Delay(100); // Имитация задержки сети
            
            var responses = new[]
            {
                "Интересный ход!",
                "Удачи!",
                "Почти попал!",
                "Хорошая игра!",
                "Мне нравится твоя стратегия!",
                "Продолжай в том же духе!",
                "Отличный выстрел!",
                "У тебя хорошо получается!"
            };
            
            var response = responses[_random.Next(responses.Length)];
            
            OnChatMessage?.Invoke(new INetworkService.ChatMessage
            {
                Sender = "Компьютер",
                Message = response
            });
        }

        public Task<PlayerStats> GetPlayerStatsAsync()
        {
            return Task.FromResult(_playerStats);
        }
        
        private async Task ComputerShootAsync()
        {
            if (_currentSession == null || _currentSession.Status != "playing")
                return;
            
            // Ищем раненый корабль для добивания
            var woundedCells = _currentSession.ComputerShots
                .Where(s => _currentSession.Player1Ships.Any(ship => 
                    ship.Cells.Any(c => c.Row == s.Row && c.Col == s.Col) &&
                    !(ship.HitCells?.Any(h => h.Row == s.Row && h.Col == s.Col) == true)))
                .ToList();
            
            (int row, int col) target;
            
            if (woundedCells.Count > 0)
            {
                // Добиваем раненый корабль
                var lastHit = woundedCells.Last();
                var possibleTargets = new List<(int row, int col)>();
                
                // Проверяем соседние клетки
                var directions = new[] { (-1, 0), (1, 0), (0, -1), (0, 1) };
                foreach (var (dr, dc) in directions)
                {
                    int newRow = lastHit.Row + dr;
                    int newCol = lastHit.Col + dc;
                    
                    if (newRow >= 0 && newRow < 10 && newCol >= 0 && newCol < 10 &&
                        !_currentSession.ComputerShots.Any(s => s.Row == newRow && s.Col == newCol))
                    {
                        possibleTargets.Add((newRow, newCol));
                    }
                }
                
                target = possibleTargets.Count > 0 ? 
                    possibleTargets[_random.Next(possibleTargets.Count)] : 
                    GetRandomTarget();
            }
            else
            {
                // Случайный выстрел
                target = GetRandomTarget();
            }
            
            // Запоминаем выстрел компьютера
            _currentSession.ComputerShots.Add(new CellData { Row = target.row, Col = target.col });
            
            // Проверяем попадание по кораблям игрока
            bool isHit = false;
            ShipData hitShip = null;
            
            foreach (var ship in _currentSession.Player1Ships)
            {
                foreach (var cell in ship.Cells)
                {
                    if (cell.Row == target.row && cell.Col == target.col)
                    {
                        isHit = true;
                        hitShip = ship;
                        
                        if (ship.HitCells == null) ship.HitCells = new List<CellData>();
                        ship.HitCells.Add(cell);
                        break;
                    }
                }
                if (isHit) break;
            }
            
            // Уведомляем игрока о выстреле компьютера
            var opponentShoot = new ShootMessage
            {
                Row = target.row,
                Col = target.col
            };
            
            OnOpponentShoot?.Invoke(opponentShoot);
            
            // Проверяем потопление
            if (isHit && hitShip != null)
            {
                bool isSunk = hitShip.Cells.All(c => 
                    hitShip.HitCells?.Exists(h => h.Row == c.Row && h.Col == c.Col) == true);
            }
            
            // Проверяем поражение игрока
            int remainingPlayerShips = _currentSession.Player1Ships.Count(ship =>
                !ship.Cells.All(c => ship.HitCells?.Exists(h => 
                    h.Row == c.Row && h.Col == c.Col) == true));
            
            if (remainingPlayerShips == 0)
            {
                EndGame("opponent", "all_ships_sunk");
                return;
            }
            
            // Передаем ход игроку, если компьютер промахнулся
            if (!isHit)
            {
                _currentSession.CurrentTurn = "player";
            }
            else
            {
                // При попадании компьютер стреляет еще раз через 1 секунду
                await Task.Delay(1000);
                await ComputerShootAsync();
            }
            
            UpdateGameState();
        }
        
        private (int row, int col) GetRandomTarget()
        {
            // Получаем список еще не обстрелянных клеток
            var availableCells = new List<(int row, int col)>();
            
            for (int row = 0; row < 10; row++)
            {
                for (int col = 0; col < 10; col++)
                {
                    if (!_currentSession.ComputerShots.Any(s => s.Row == row && s.Col == col))
                    {
                        availableCells.Add((row, col));
                    }
                }
            }
            
            return availableCells.Count > 0 ? 
                availableCells[_random.Next(availableCells.Count)] : 
                (0, 0);
        }
        
        private int GetRemainingComputerShips()
        {
            return _computerShips.Count(ship =>
                !ship.Cells.All(c => ship.HitCells?.Exists(h => 
                    h.Row == c.Row && h.Col == c.Col) == true));
        }
        
        private void SetupComputerShips()
        {
            _computerShips.Clear();
            
            // Стандартный набор кораблей
            var shipSizes = new List<int> { 4, 3, 3, 2, 2, 2, 1, 1, 1, 1 };
            
            foreach (var size in shipSizes)
            {
                bool placed = false;
                int attempts = 0;
                
                while (!placed && attempts < 100)
                {
                    attempts++;
                    
                    bool horizontal = _random.Next(0, 2) == 0;
                    int row = _random.Next(0, horizontal ? 10 : 10 - size + 1);
                    int col = _random.Next(0, horizontal ? 10 - size + 1 : 10);
                    
                    // Проверяем возможность размещения
                    if (CanPlaceShip(row, col, size, horizontal))
                    {
                        var ship = new ShipData
                        {
                            Size = size,
                            IsHorizontal = horizontal,
                            Cells = new List<CellData>()
                        };
                        
                        for (int i = 0; i < size; i++)
                        {
                            int cellRow = horizontal ? row : row + i;
                            int cellCol = horizontal ? col + i : col;
                            
                            ship.Cells.Add(new CellData { Row = cellRow, Col = cellCol });
                            _computerBoard[cellRow, cellCol] = true;
                        }
                        
                        _computerShips.Add(ship);
                        placed = true;
                    }
                }
            }
        }
        
        private bool CanPlaceShip(int row, int col, int size, bool horizontal)
        {
            for (int i = 0; i < size; i++)
            {
                int cellRow = horizontal ? row : row + i;
                int cellCol = horizontal ? col + i : col;
                
                // Проверка границ
                if (cellRow < 0 || cellRow >= 10 || cellCol < 0 || cellCol >= 10)
                    return false;
                
                // Проверка самой клетки
                if (_computerBoard[cellRow, cellCol])
                    return false;
                
                // Проверка соседних клеток
                for (int dr = -1; dr <= 1; dr++)
                {
                    for (int dc = -1; dc <= 1; dc++)
                    {
                        int checkRow = cellRow + dr;
                        int checkCol = cellCol + dc;
                        
                        if (checkRow >= 0 && checkRow < 10 && checkCol >= 0 && checkCol < 10)
                        {
                            if (_computerBoard[checkRow, checkCol])
                                return false;
                        }
                    }
                }
            }
            
            return true;
        }
        
        private void UpdateGameState()
        {
            if (_currentSession == null) return;
            
            var state = new GameStateMessage
            {
                Status = _currentSession.Status,
                CurrentTurn = _currentSession.CurrentTurn,
                PlayerScore = _playerStats.Hits * 10,
                OpponentScore = _currentSession.ComputerShots.Count(s => 
                    _currentSession.Player1Ships.Any(ship => 
                        ship.Cells.Any(c => c.Row == s.Row && c.Col == s.Col))) * 10,
                RemainingTime = 0
            };
            
            OnGameStateUpdated?.Invoke(state);
        }
        
        private void EndGame(string winner, string reason)
        {
            if (_currentSession == null) return;
    
            _currentSession.Status = "finished";
    
            // Заполняем статистику
            _playerStats.TotalShots = _playerStats.Hits + _playerStats.Misses;
            if (_playerStats.TotalShots > 0)
            {
                _playerStats.Accuracy = (double)_playerStats.Hits / _playerStats.TotalShots * 100;
            }
    
            var endMessage = new GameEndMessage
            {
                Winner = winner,
                Reason = reason,
                Stats = _playerStats
            };
    
            OnGameEnded?.Invoke(endMessage);
            UpdateGameState();
        }
        
        // Вспомогательный класс для игровой сессии
        private class GameSession
        {
            public string GameId { get; set; }
            public string Player1Id { get; set; }
            public string Player1Name { get; set; }
            public string Player2Id { get; set; }
            public string Player2Name { get; set; }
            public string GameMode { get; set; }
            public string Status { get; set; } // waiting, placing, playing, finished
            public string CurrentTurn { get; set; } // player, opponent
            public DateTime CreatedAt { get; set; }
            
            public List<ShipData> Player1Ships { get; set; } = new List<ShipData>();
            public List<ShipData> Player2Ships { get; set; } = new List<ShipData>();
            
            public List<CellData> PlayerShots { get; set; } = new List<CellData>();
            public List<CellData> ComputerShots { get; set; } = new List<CellData>();
        }
    }
}