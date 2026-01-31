using Microsoft.AspNetCore.Mvc;
using BattleShip.Core.Models;
using BattleShip.Server.Services;
using BattleShip.Core.Enums;

namespace BattleShip.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GameController : ControllerBase
    {
        private readonly FirebaseService _firebaseService;
        private readonly GameService _gameService;
        private readonly ILogger<GameController> _logger;

        public GameController(
            FirebaseService firebaseService,
            GameService gameService,
            ILogger<GameController> logger)
        {
            _firebaseService = firebaseService;
            _gameService = gameService;
            _logger = logger;
        }

        // 🔥 НОВАЯ ЛОГИКА: Игрок готов к поиску противника
        [HttpPost("ready-for-matchmaking")]
        public async Task<IActionResult> ReadyForMatchmaking([FromBody] ReadyForMatchmakingRequest request)
        {
            _logger.LogInformation($"🎮 Игрок {request.PlayerName} готов к поиску противника");

            // 1. Проверяем что все корабли расставлены
            if (request.Ships == null || request.Ships.Count != 10)
            {
                return BadRequest(new
                {
                    Success = false,
                    Message = "Должно быть расставлено 10 кораблей"
                });
            }

            // 2. Создаем доску игрока
            var playerBoard = new Board();
            playerBoard.InitializeBoard(request.Ships);

            // 3. Проверяем правильность расстановки
            if (!playerBoard.ValidateShipPlacement())
            {
                return BadRequest(new
                {
                    Success = false,
                    Message = "Некорректная расстановка кораблей"
                });
            }

            // 4. Ищем игрока в лобби
            var opponent = await _firebaseService.FindOpponentInLobbyAsync(request.PlayerName);

            if (opponent != null)
            {
                // НАШЛИ ПРОТИВНИКА! Создаем игру
                _logger.LogInformation($"✅ Найден противник: {opponent.PlayerName}");

                var game = new Game
                {
                    Id = Guid.NewGuid().ToString(),
                    Player1Id = opponent.PlayerId,
                    Player2Id = await _firebaseService.CreateTestUser(), // Текущий игрок
                    Player1Name = opponent.PlayerName,
                    Player2Name = request.PlayerName,
                    Status = GameStatus.PlacingShips,
                    CreatedAt = DateTime.UtcNow
                };

                // Сохраняем доски
                game.Player1Board = opponent.Board; // Доска противника (уже расставлена)
                game.Player2Board = playerBoard;    // Доска текущего игрока

                // Устанавливаем кто первый ходит (случайно)
                game.CurrentPlayerId = new Random().Next(0, 2) == 0
                    ? game.Player1Id
                    : game.Player2Id;

                game.Status = game.CurrentPlayerId == game.Player1Id
                    ? GameStatus.Player1Turn
                    : GameStatus.Player2Turn;

                await _firebaseService.SaveGameAsync(game);

                // Удаляем противника из лобби
                await _firebaseService.RemoveFromLobbyAsync(opponent.PlayerId);

                return Ok(new
                {
                    Success = true,
                    GameId = game.Id,
                    PlayerId = game.Player2Id,
                    OpponentName = opponent.PlayerName,
                    GameStatus = game.Status.ToString(),
                    IsMyTurn = game.CurrentPlayerId == game.Player2Id,
                    Message = $"Игра началась против {opponent.PlayerName}!"
                });
            }
            else
            {
                // НЕ НАШЛИ - добавляем себя в лобби
                _logger.LogInformation($"⏳ Игрок {request.PlayerName} добавлен в лобби");

                var playerId = await _firebaseService.AddToLobbyAsync(
                    request.PlayerName,
                    playerBoard
                );

                return Ok(new
                {
                    Success = true,
                    PlayerId = playerId,
                    InLobby = true,
                    Message = "Ожидаем противника...",
                    WaitEndpoint = $"/api/Game/wait-for-opponent/{playerId}"
                });
            }
        }

        // 🔥 Ожидание противника в лобби
        [HttpGet("wait-for-opponent/{playerId}")]
        public async Task<IActionResult> WaitForOpponent(string playerId)
        {
            _logger.LogInformation($"⏳ Ожидание для игрока {playerId}");

            int maxAttempts = 30; // 30 * 2 сек = 1 минута
            for (int i = 0; i < maxAttempts; i++)
            {
                // Проверяем нашли ли мы игру
                var game = await _firebaseService.FindGameByPlayerIdAsync(playerId);

                if (game != null)
                {
                    // Игра найдена!
                    bool isPlayer1 = game.Player1Id == playerId;

                    return Ok(new
                    {
                        Success = true,
                        GameFound = true,
                        GameId = game.Id,
                        PlayerId = playerId,
                        OpponentName = isPlayer1 ? game.Player2Name : game.Player1Name,
                        GameStatus = game.Status.ToString(),
                        IsMyTurn = game.CurrentPlayerId == playerId,
                        Message = $"Игра началась против {(isPlayer1 ? game.Player2Name : game.Player1Name)}!"
                    });
                }

                // Проверяем не вышел ли игрок из лобби
                var inLobby = await _firebaseService.IsPlayerInLobbyAsync(playerId);
                if (!inLobby)
                {
                    return Ok(new
                    {
                        Success = false,
                        GameFound = false,
                        Message = "Вы вышли из лобби"
                    });
                }

                await Task.Delay(2000); // Ждем 2 секунды
            }

            // Время вышло
            await _firebaseService.RemoveFromLobbyAsync(playerId);

            return Ok(new
            {
                Success = false,
                GameFound = false,
                Timeout = true,
                Message = "Время ожидания истекло"
            });
        }

        // 🔥 Выход из лобби
        [HttpPost("cancel-matchmaking")]
        public async Task<IActionResult> CancelMatchmaking([FromBody] string playerId)
        {
            await _firebaseService.RemoveFromLobbyAsync(playerId);

            return Ok(new
            {
                Success = true,
                Message = "Поиск отменен"
            });
        }

        // 🔥 Выход из игры (с присвоением поражения)
        [HttpPost("{gameId}/surrender")]
        public async Task<IActionResult> Surrender(string gameId, [FromBody] SurrenderRequest request)
        {
            _logger.LogInformation($"🏳️ Игрок {request.PlayerId} сдается в игре {gameId}");

            var game = await _firebaseService.GetGameAsync(gameId);
            if (game == null)
                return NotFound(new { Message = "Игра не найдена" });

            // Определяем кто сдается
            bool isPlayer1 = request.PlayerId == game.Player1Id;

            if (!isPlayer1 && request.PlayerId != game.Player2Id)
                return Unauthorized(new { Message = "Вы не участник этой игры" });

            // Присваиваем победу противнику
            if (isPlayer1)
            {
                game.Status = GameStatus.Player2Won;
                _logger.LogInformation($"🏆 Победа игроку {game.Player2Name} (игрок {game.Player1Name} сдался)");
            }
            else
            {
                game.Status = GameStatus.Player1Won;
                _logger.LogInformation($"🏆 Победа игроку {game.Player1Name} (игрок {game.Player2Name} сдался)");
            }

            game.EndedAt = DateTime.UtcNow;
            game.WinnerId = isPlayer1 ? game.Player2Id : game.Player1Id;
            game.LoserId = request.PlayerId;
            game.Surrender = true;

            await _firebaseService.UpdateGameAsync(game);

            return Ok(new
            {
                Success = true,
                GameStatus = game.Status.ToString(),
                Winner = isPlayer1 ? game.Player2Name : game.Player1Name,
                Loser = isPlayer1 ? game.Player1Name : game.Player2Name,
                Message = "Игра завершена"
            });
        }

        // Fire 
        [HttpPost("{id}/fire")]
        public async Task<IActionResult> Fire(string id, [FromBody] FireRequest request)
        {
            _logger.LogInformation($"🔥 Выстрел: игра={id}, игрок={request.PlayerId}, x={request.X}, y={request.Y}");

            var game = await _firebaseService.GetGameAsync(id);
            if (game == null)
                return NotFound(new { Message = "Игра не найдена" });

            // Проверяем статус игры
            if (game.Status == GameStatus.Player1Won || game.Status == GameStatus.Player2Won)
                return BadRequest(new { Message = "Игра уже завершена" });

            if (game.Status != GameStatus.Player1Turn && game.Status != GameStatus.Player2Turn)
                return BadRequest(new { Message = "Игра не началась" });



            // Проверяем очередь хода
            bool isPlayer1Turn = game.Status == GameStatus.Player1Turn;
            if ((isPlayer1Turn && request.PlayerId != game.Player1Id) ||
                (!isPlayer1Turn && request.PlayerId != game.Player2Id))
            {
                return BadRequest(new
                {
                    Success = false,
                    Message = "Сейчас не ваш ход",
                    CurrentPlayer = isPlayer1Turn ? game.Player1Name : game.Player2Name
                });
            }

            bool isPlayer1Shooting = request.PlayerId == game.Player1Id;
            Board targetBoard = isPlayer1Shooting ? game.Player2Board : game.Player1Board;

            if (game.LastTurnTime.HasValue)
            {
                var timeSinceLastTurn = DateTime.UtcNow - game.LastTurnTime.Value;
                if (timeSinceLastTurn > TimeSpan.FromSeconds(30))
                {
                    // Время истекло - передаем ход противнику
                    game.Status = game.Status == GameStatus.Player1Turn
                        ? GameStatus.Player2Turn
                        : GameStatus.Player1Turn;
                    game.CurrentPlayerId = game.Status == GameStatus.Player1Turn
                        ? game.Player1Id
                        : game.Player2Id;
                    game.LastTurnTime = DateTime.UtcNow;

                    await _firebaseService.UpdateGameAsync(game);

                    return BadRequest(new
                    {
                        Success = false,
                        Message = "Время хода истекло",
                        TurnChanged = true,
                        CurrentPlayerId = game.CurrentPlayerId
                    });
                }
            }

            // Проверяем что клетка еще не обстреляна
            var cell = targetBoard.GetCell(request.X, request.Y);
            if (cell != null && cell.WasShot)
            {
                return BadRequest(new
                {
                    Success = false,
                    Message = "Вы уже стреляли в эту клетку",
                    CellStatus = cell.Status.ToString()
                });
            }

            // Проверяем попадание
            var (isHit, isShipSunk, sunkShip) = _gameService.CheckHitWithDetails(targetBoard, request.X, request.Y);

            // Сохраняем выстрел
            await _firebaseService.SaveShotAsync(id, request.PlayerId, request.X, request.Y, isHit, isShipSunk);
            await _firebaseService.UpdateBoardAsync(id, isPlayer1Shooting ? "player2" : "player1", targetBoard);

            bool isGameOver = _gameService.IsGameOver(targetBoard);

            // 1. Если игра окончена - фиксируем победу
            // 2. Если попал (даже если потопил корабль) - продолжает ходить
            // 3. Если промахнулся - ход переходит противнику

            if (isGameOver)
            {
                // КОНЕЦ ИГРЫ
                game.Status = request.PlayerId == game.Player1Id
                    ? GameStatus.Player1Won
                    : GameStatus.Player2Won;
                game.WinnerId = request.PlayerId;
                game.LoserId = request.PlayerId == game.Player1Id ? game.Player2Id : game.Player1Id;
                game.EndedAt = DateTime.UtcNow;

                _logger.LogInformation($"🏆 Игра окончена! Победил: {request.PlayerId}");
            }
            else
            {
                if (isHit)
                {
                    // 🔥 ПОПАЛ - продолжает ходить (НЕ МЕНЯЕМ ИГРОКА)
                    _logger.LogInformation($"🎯 Попадание! Игрок {request.PlayerId} продолжает ход");

                    if (isShipSunk)
                    {
                        _logger.LogInformation($"💥 Потоплен {sunkShip?.Name}!");

                        // Помечаем клетки вокруг потопленного корабля как промахи
                        _gameService.MarkCellsAroundSunkShip(targetBoard, sunkShip);
                    }
                }
                else
                {
                    // 🔥 ПРОМАХ - меняем игрока
                    game.Status = game.Status == GameStatus.Player1Turn
                        ? GameStatus.Player2Turn
                        : GameStatus.Player1Turn;

                    game.CurrentPlayerId = game.Status == GameStatus.Player1Turn
                        ? game.Player1Id
                        : game.Player2Id;

                    _logger.LogInformation($"🔄 Промах! Ход переходит к {game.CurrentPlayerId}");
                }
            }

            game.LastTurnTime = DateTime.UtcNow;
            await _firebaseService.UpdateGameAsync(game);

            await _firebaseService.UpdateGameAsync(game);

            return Ok(new
            {
                Success = true,
                IsHit = isHit,
                IsShipSunk = isShipSunk,
                ShipName = sunkShip?.Name ?? "",
                ShipSize = sunkShip?.Size ?? 0,
                IsGameOver = isGameOver,
                GameStatus = game.Status.ToString(),
                CurrentPlayerId = game.CurrentPlayerId,
                CurrentPlayerName = game.CurrentPlayerId == game.Player1Id
                    ? game.Player1Name
                    : game.Player2Name,
                ContinueTurn = isHit, // true если игрок продолжает ход
                Message = isGameOver ? "Игра окончена!" :
                         isShipSunk ? $"Потоплен {sunkShip?.Name}!" :
                         isHit ? "Попадание!" : "Мимо!"
            });
        }

        // Проверка статуса игры (для постоянного опроса с клиента)
        [HttpGet("{gameId}/status/{playerId}")]
        public async Task<IActionResult> GetGameStatus(string gameId, string playerId)
        {
            var game = await _firebaseService.GetGameAsync(gameId);
            if (game == null)
                return NotFound(new { Message = "Игра не найдена" });

            if (playerId != game.Player1Id && playerId != game.Player2Id)
                return Unauthorized(new { Message = "Вы не участник этой игры" });

            bool isPlayer1 = playerId == game.Player1Id;
            bool isMyTurn = game.CurrentPlayerId == playerId;

            // Проверяем не вышел ли противник
            var opponentLeft = await _firebaseService.HasOpponentLeft(gameId, playerId);

            if (opponentLeft)
            {
                // Противник вышел - присваиваем победу
                game.Status = playerId == game.Player1Id
                    ? GameStatus.Player1Won
                    : GameStatus.Player2Won;
                game.WinnerId = playerId;
                game.LoserId = playerId == game.Player1Id ? game.Player2Id : game.Player1Id;
                game.EndedAt = DateTime.UtcNow;
                game.OpponentDisconnected = true;

                await _firebaseService.UpdateGameAsync(game);
            }

            return Ok(new
            {
                GameId = gameId,
                GameStatus = game.Status.ToString(),
                IsMyTurn = isMyTurn,
                IsGameOver = game.Status == GameStatus.Player1Won ||
                            game.Status == GameStatus.Player2Won,
                WinnerId = game.WinnerId,
                PlayerId = playerId,
                OpponentName = isPlayer1 ? game.Player2Name : game.Player1Name,
                OpponentLeft = opponentLeft,
                MyBoardShipsRemaining = isPlayer1
                    ? game.Player1Board.Ships.Count(s => !s.IsSunk)
                    : game.Player2Board.Ships.Count(s => !s.IsSunk),
                OpponentBoardShipsRemaining = isPlayer1
                    ? game.Player2Board.Ships.Count(s => !s.IsSunk)
                    : game.Player1Board.Ships.Count(s => !s.IsSunk),
                Message = opponentLeft ? "Противник вышел из игры. Вы победили!" :
                         isMyTurn ? "Ваш ход!" : "Ход противника..."
            });
        }

        // Получение состояния доски (с скрытием неотстрелянных клеток противника)
        [HttpGet("{gameId}/board/{playerId}")]
        public async Task<IActionResult> GetBoard(string gameId, string playerId)
        {
            var game = await _firebaseService.GetGameAsync(gameId);
            if (game == null)
                return NotFound(new { Message = "Игра не найдена" });

            if (playerId != game.Player1Id && playerId != game.Player2Id)
                return Unauthorized(new { Message = "Вы не участник этой игры" });

            bool isPlayer1 = playerId == game.Player1Id;

            var myBoard = isPlayer1 ? game.Player1Board : game.Player2Board;
            var opponentBoard = isPlayer1 ? game.Player2Board : game.Player1Board;

            // Создаем безопасное представление доски противника
            var safeOpponentCells = new List<object>();
            foreach (var cell in opponentBoard.Cells)
            {
                // Показываем только отстрелянные клетки
                if (cell.WasShot)
                {
                    safeOpponentCells.Add(new
                    {
                        cell.X,
                        cell.Y,
                        cell.Status,
                        WasShot = true,
                        HasShip = cell.Status == CellStatus.Hit || cell.Status == CellStatus.Sunk,
                        IsSunk = cell.Status == CellStatus.Sunk
                    });
                }
                else
                {
                    // Неотстрелянные клетки скрываем
                    safeOpponentCells.Add(new
                    {
                        cell.X,
                        cell.Y,
                        Status = CellStatus.Empty,
                        WasShot = false,
                        HasShip = false,
                        IsSunk = false
                    });
                }
            }

            return Ok(new
            {
                MyBoard = new
                {
                    Cells = myBoard.Cells.Select(c => new
                    {
                        c.X,
                        c.Y,
                        c.HasShip,
                        c.WasShot,
                        c.Status,
                        IsSunk = c.Status == CellStatus.Sunk
                    }),
                    Ships = myBoard.Ships.Select(s => new
                    {
                        s.Name,
                        s.Size,
                        s.IsSunk,
                        s.Hits,
                        Remaining = s.Size - s.Hits
                    })
                },
                OpponentBoard = new
                {
                    Cells = safeOpponentCells,
                    ShipsSunk = opponentBoard.Ships.Count(s => s.IsSunk),
                    ShipsRemaining = opponentBoard.Ships.Count(s => !s.IsSunk)
                }
            });
        }

        [HttpGet("test")]
        public IActionResult Test()
        {
            return Ok(new
            {
                Message = "BattleShip Server работает!",
                Timestamp = DateTime.UtcNow,
                Version = "1.0",
                Status = "OK"
            });
        }
    }

    public class ReadyForMatchmakingRequest
    {
        public string PlayerName { get; set; }
        public List<Ship> Ships { get; set; }
    }

    public class SurrenderRequest
    {
        public string PlayerId { get; set; }
    }

    public class FireRequest
    {
        public int X { get; set; }
        public int Y { get; set; }
        public string PlayerId { get; set; }
    }
}