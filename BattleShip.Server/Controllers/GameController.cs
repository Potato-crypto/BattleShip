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
        private readonly SessionService _sessionService;        
        private readonly MatchmakingService _matchmakingService;
        private readonly GameService _gameService;
        private readonly ILogger<GameController> _logger;

        public GameController(
            FirebaseService firebaseService,
            GameService gameService,
            SessionService sessionService,        
            MatchmakingService matchmakingService,
            ILogger<GameController> logger)
        {
            _firebaseService = firebaseService;
            _gameService = gameService;
            _sessionService = sessionService;        
            _matchmakingService = matchmakingService;
            _logger = logger;
        }

        [HttpPost("{gameId}/ready-to-start")]
        public async Task<IActionResult> ReadyToStart(string gameId, [FromBody] ReadyToStartRequest request)
        {
            _logger.LogInformation($"✅ Игрок {request.PlayerName} готов начать игру {gameId}");

            var game = await _firebaseService.GetGameAsync(gameId);
            if (game == null)
                return NotFound(new { Message = "Игра не найдена" });

            // Валидация сессии
            if (!await _sessionService.ValidateSessionAsync(request.SessionId, request.PlayerId))
            {
                return Unauthorized(new { Message = "Невалидная сессия" });
            }

            // Проверяем что игрок участник игры
            if (game.Player1Id != request.PlayerId && game.Player2Id != request.PlayerId)
            {
                return Unauthorized(new { Message = "Вы не участник этой игры" });
            }

            // Устанавливаем флаг готовности
            if (game.Player1Id == request.PlayerId)
            {
                game.Player1Ready = true;
                _logger.LogInformation($"✅ Игрок 1 ({game.Player1Name}) готов");
            }
            else
            {
                game.Player2Ready = true;
                _logger.LogInformation($"✅ Игрок 2 ({game.Player2Name}) готов");
            }

            // Проверяем оба ли игрока готовы
            if (game.Player1Ready && game.Player2Ready)
            {
                // НАЧИНАЕМ ИГРУ!
                game.Status = DetermineFirstTurn(game); // Выбираем случайного первого игрока
                game.CurrentPlayerId = game.Status == GameStatus.Player1Turn
                    ? game.Player1Id
                    : game.Player2Id;
                game.LastTurnTime = DateTime.UtcNow;

                _logger.LogInformation($"🎲 Игра {gameId} началась! Первый ход у: {game.CurrentPlayerId}");
            }

            await _firebaseService.UpdateGameAsync(game);

            return Ok(new
            {
                Success = true,
                GameStatus = game.Status.ToString(),
                IsGameStarted = game.Status == GameStatus.Player1Turn || game.Status == GameStatus.Player2Turn,
                CurrentPlayerId = game.CurrentPlayerId,
                Player1Ready = game.Player1Ready,
                Player2Ready = game.Player2Ready,
                Message = game.Player1Ready && game.Player2Ready
                    ? "Игра началась!"
                    : "Ожидаем готовности противника..."
            });
        }

        private GameStatus DetermineFirstTurn(Game game)
        {
            // Случайный выбор кто ходит первым
            return new Random().Next(0, 2) == 0
                ? GameStatus.Player1Turn
                : GameStatus.Player2Turn;
        }

        // Игрок готов к поиску противника
        [HttpPost("ready-for-matchmaking")]
        public async Task<IActionResult> ReadyForMatchmaking([FromBody] ReadyForMatchmakingRequest request)
        {
            _logger.LogInformation($"🎮 Игрок {request.PlayerName} готов к поиску противника");

            // 1. Валидация сессии
            if (!await _sessionService.ValidateSessionAsync(request.SessionId, request.PlayerId))
            {
                return Unauthorized(new { Message = "Невалидная сессия" });
            }

            // 2. Проверяем что все корабли расставлены
            if (request.Ships == null || request.Ships.Count != 10)
            {
                return BadRequest(new
                {
                    Success = false,
                    Message = "Должно быть расставлено 10 кораблей"
                });
            }

            // 3. Создаем доску игрока
            var playerBoard = new Board();
            playerBoard.InitializeBoard(request.Ships);

            // 4. Проверяем правильность расстановки
            if (!playerBoard.ValidateShipPlacement())
            {
                return BadRequest(new
                {
                    Success = false,
                    Message = "Некорректная расстановка кораблей"
                });
            }

            // 5. Сохраняем корабли в сессии
            var session = await _sessionService.GetSessionAsync(request.SessionId);
            if (session != null)
            {
                session.Ships = request.Ships;
                await _sessionService.UpdateSessionAsync(session);
            }

            // 6. Ищем матч через MatchmakingService
            var matchmakingResult = await _matchmakingService.FindMatchAsync(
                request.PlayerName,
                request.PlayerId,
                request.SessionId, 
                playerBoard
            );

            if (!matchmakingResult.Success)
            {
                return BadRequest(new { Message = matchmakingResult.Error });
            }

            if (matchmakingResult.GameFound)
            {
                var game = matchmakingResult.Game;
                bool isPlayer1 = game.Player1Id == request.PlayerId;

                return Ok(new
                {
                    Success = true,
                    GameFound = true,
                    GameId = game.Id,
                    PlayerId = request.PlayerId,
                    OpponentName = isPlayer1 ? game.Player2Name : game.Player1Name,
                    GameStatus = game.Status.ToString(),
                    IsMyTurn = false, // Игра еще не началась
                    Message = $"Найден противник: {(isPlayer1 ? game.Player2Name : game.Player1Name)}"
                });
            }
            else
            {
                return Ok(new
                {
                    Success = true,
                    GameFound = false,
                    InLobby = true,
                    PlayerId = request.PlayerId,
                    Message = "Ожидаем противника...",
                    WaitEndpoint = $"/api/Game/wait-for-opponent/{request.SessionId}"
                });
            }
        }

        // Ожидание противника в лобби
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
            game.OpponentDisconnected = false;

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
            try
            {
                _logger.LogInformation($"🔥 Выстрел: игра={id}, игрок={request.PlayerId}, x={request.X}, y={request.Y}");

                // 1. Загружаем игру
                var game = await _firebaseService.GetGameAsync(id);
                if (game == null)
                    return NotFound(new { Success = false, Message = "Игра не найдена" });

                // 2. Логируем состояние игры для отладки
                _logger.LogInformation($"🎮 Состояние игры: Status={game.Status}, LastTurnTime={game.LastTurnTime}");
                _logger.LogInformation($"🎮 Игроки: P1={game.Player1Name}({game.Player1Id}), P2={game.Player2Name}({game.Player2Id})");

                // 3. Проверяем статус игры
                if (game.Status == GameStatus.Player1Won || game.Status == GameStatus.Player2Won)
                    return BadRequest(new { Success = false, Message = "Игра уже завершена" });

                if (game.Status != GameStatus.Player1Turn && game.Status != GameStatus.Player2Turn)
                    return BadRequest(new { Success = false, Message = "Игра не началась" });

                // 4. Проверяем очередь хода
                bool isPlayer1Turn = game.Status == GameStatus.Player1Turn;
                if ((isPlayer1Turn && request.PlayerId != game.Player1Id) ||
                    (!isPlayer1Turn && request.PlayerId != game.Player2Id))
                {
                    _logger.LogWarning($"⚠️ Не очередь хода! Сейчас ходит: {(isPlayer1Turn ? game.Player1Name : game.Player2Name)}");
                    return BadRequest(new
                    {
                        Success = false,
                        Message = "Сейчас не ваш ход",
                        CurrentPlayer = isPlayer1Turn ? game.Player1Name : game.Player2Name
                    });
                }

                // 5. Определяем цель
                bool isPlayer1Shooting = request.PlayerId == game.Player1Id;
                Board targetBoard = isPlayer1Shooting ? game.Player2Board : game.Player1Board;

                _logger.LogInformation($"🎯 Цель: {(isPlayer1Shooting ? "P2 (противник)" : "P1 (противник)")}");

                // 6. Проверяем таймер хода (только если установлен)
                if (game.LastTurnTime.HasValue)
                {
                    var timeSinceLastTurn = DateTime.UtcNow - game.LastTurnTime.Value;
                    _logger.LogInformation($"⏰ Прошло времени: {timeSinceLastTurn.TotalSeconds:F1} секунд");

                    if (timeSinceLastTurn > TimeSpan.FromSeconds(30))
                    {
                        _logger.LogWarning($"⏰ Время хода истекло ({timeSinceLastTurn.TotalSeconds:F1} сек)");

                        // Меняем ход
                        game.Status = game.Status == GameStatus.Player1Turn
                            ? GameStatus.Player2Turn
                            : GameStatus.Player1Turn;
                        game.CurrentPlayerId = game.Status == GameStatus.Player1Turn
                            ? game.Player1Id
                            : game.Player2Id;
                        game.LastTurnTime = DateTime.UtcNow;

                        await _firebaseService.UpdateFullGameAsync(game);

                        return BadRequest(new
                        {
                            Success = false,
                            Message = "Время хода истекло",
                            TurnChanged = true,
                            CurrentPlayerId = game.CurrentPlayerId
                        });
                    }
                }
                else
                {
                    _logger.LogInformation("⏰ LastTurnTime не установлен, устанавливаем сейчас");
                    game.LastTurnTime = DateTime.UtcNow;
                }

                // 7. Проверяем клетку
                var cell = targetBoard.GetCell(request.X, request.Y);
                if (cell == null)
                    return BadRequest(new { Success = false, Message = "Неверные координаты" });

                _logger.LogInformation($"🎯 Клетка ({request.X},{request.Y}): HasShip={cell.HasShip}, WasShot={cell.WasShot}");

                if (cell.WasShot)
                {
                    // Находим информацию о корабле для лучшего сообщения
                    string shipInfo = "";
                    if (cell.HasShip && !string.IsNullOrEmpty(cell.ShipId))
                    {
                        var ship = targetBoard.Ships?.FirstOrDefault(s => s.Id == cell.ShipId);
                        shipInfo = ship != null ? $" (корабль '{ship.Name}')" : "";
                    }

                    return BadRequest(new
                    {
                        Success = false,
                        Message = $"Вы уже стреляли в эту клетку{shipInfo}",
                        CellStatus = cell.Status.ToString(),
                        WasHit = cell.Status == CellStatus.Hit || cell.Status == CellStatus.Sunk
                    });
                }

                // 8. Выполняем выстрел через GameService
                _logger.LogInformation("🎯 Выполняем выстрел через GameService...");
                var (isHit, isShipSunk, hitShip) = _gameService.CheckHitWithDetails(targetBoard, request.X, request.Y);

                _logger.LogInformation($"🎯 Результат: isHit={isHit}, isShipSunk={isShipSunk}, shipName={hitShip?.Name}");

                // 9. Сохраняем выстрел в историю
                await _firebaseService.SaveShotAsync(id, request.PlayerId, request.X, request.Y, isHit, isShipSunk);

                // 10. Обновляем доску в памяти игры
                if (isPlayer1Shooting)
                {
                    game.Player2Board = targetBoard;
                }
                else
                {
                    game.Player1Board = targetBoard;
                }

                // 11. Сохраняем обновленную доску в Firebase
                await _firebaseService.UpdateBoardAsync(id, isPlayer1Shooting ? "player2" : "player1", targetBoard);

                // 12. Проверяем конец игры
                bool isGameOver = _gameService.IsGameOver(targetBoard);
                _logger.LogInformation($"🏁 Проверка конца игры: {isGameOver}");

                // 13. Обрабатываем результат выстрела
                if (isGameOver)
                {
                    // КОНЕЦ ИГРЫ - ПОБЕДА
                    game.Status = request.PlayerId == game.Player1Id
                        ? GameStatus.Player1Won
                        : GameStatus.Player2Won;
                    game.WinnerId = request.PlayerId;
                    game.LoserId = request.PlayerId == game.Player1Id ? game.Player2Id : game.Player1Id;
                    game.EndedAt = DateTime.UtcNow;
                    game.OpponentDisconnected = false;
                    game.Surrender = false;

                    _logger.LogInformation($"🏆 Игра окончена! Победил: {request.PlayerId}");
                }
                else
                {
                    if (isHit)
                    {
                        // 🔥 ПОПАЛ - продолжает ходить
                        _logger.LogInformation($"🎯 Попадание! Игрок {request.PlayerId} продолжает ход");

                        if (isShipSunk && hitShip != null)
                        {
                            _logger.LogInformation($"💥 Потоплен корабль '{hitShip.Name}' (размер: {hitShip.Size})!");

                            // Дополнительная обработка потопленного корабля
                            _gameService.MarkCellsAroundSunkShip(targetBoard, hitShip);

                            // Сохраняем доску снова после обработки потопления
                            await _firebaseService.UpdateBoardAsync(id, isPlayer1Shooting ? "player2" : "player1", targetBoard);

                            // Обновляем доску в памяти
                            if (isPlayer1Shooting)
                                game.Player2Board = targetBoard;
                            else
                                game.Player1Board = targetBoard;
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

                // 14. Обновляем время последнего хода
                game.LastTurnTime = DateTime.UtcNow;
                game.UpdatedAt = DateTime.UtcNow;

                // 15. Сохраняем полную игру в Firebase
                _logger.LogInformation("💾 Сохраняем полную игру в Firebase...");
                await _firebaseService.UpdateFullGameAsync(game);

                // 16. Подготавливаем ответ
                var response = new
                {
                    Success = true,
                    IsHit = isHit,
                    IsShipSunk = isShipSunk,
                    ShipName = hitShip?.Name ?? "",
                    ShipSize = hitShip?.Size ?? 0,
                    IsGameOver = isGameOver,
                    GameStatus = game.Status.ToString(),
                    CurrentPlayerId = game.CurrentPlayerId,
                    CurrentPlayerName = game.CurrentPlayerId == game.Player1Id
                        ? game.Player1Name
                        : game.Player2Name,
                    ContinueTurn = isHit, // true если игрок продолжает ход
                    Message = isGameOver ? "🎉 Игра окончена! Вы победили!" :
                             isShipSunk ? $"💥 Потоплен {hitShip?.Name}!" :
                             isHit ? $"🎯 Попадание в {hitShip?.Name}!" : "❌ Мимо!",
                    Coordinates = new { X = request.X, Y = request.Y },
                    HitShipInfo = hitShip != null ? new
                    {
                        hitShip.Name,
                        hitShip.Size,
                        hitShip.Hits,
                        Remaining = hitShip.Size - hitShip.Hits,
                        IsSunk = hitShip.IsSunk
                    } : null
                };

                _logger.LogInformation($"✅ Выстрел обработан: {response.Message}");
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Критическая ошибка в методе Fire");
                return StatusCode(500, new
                {
                    Success = false,
                    Message = "Внутренняя ошибка сервера",
                    Error = ex.Message
                });
            }
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

            bool fixedInconsistency = false;

            if (game.Status == GameStatus.Player1Turn && game.CurrentPlayerId != game.Player1Id)
            {
                _logger.LogWarning($"⚠️ Обнаружено несоответствие в GetGameStatus:");
                _logger.LogWarning($"   Status=Player1Turn, но CurrentPlayerId={game.CurrentPlayerId}");
                _logger.LogWarning($"   Исправляю: устанавливаю CurrentPlayerId={game.Player1Id}");
                game.CurrentPlayerId = game.Player1Id;
                fixedInconsistency = true;
            }
            else if (game.Status == GameStatus.Player2Turn && game.CurrentPlayerId != game.Player2Id)
            {
                _logger.LogWarning($"⚠️ Обнаружено несоответствие в GetGameStatus:");
                _logger.LogWarning($"   Status=Player2Turn, но CurrentPlayerId={game.CurrentPlayerId}");
                _logger.LogWarning($"   Исправляю: устанавливаю CurrentPlayerId={game.Player2Id}");
                game.CurrentPlayerId = game.Player2Id;
                fixedInconsistency = true;
            }

            // Если исправили несоответствие - сохраняем игру
            if (fixedInconsistency)
            {
                await _firebaseService.UpdateFullGameAsync(game);
                isMyTurn = game.CurrentPlayerId == playerId; // Пересчитываем
            }

            // Проверяем не вышел ли противник
            var heartbeatService = HttpContext.RequestServices.GetService<HeartbeatService>();
            bool opponentLeft = false;

            if (heartbeatService != null)
            {
                var opponentId = isPlayer1 ? game.Player2Id : game.Player1Id;
                opponentLeft = !heartbeatService.IsPlayerActive(opponentId);
            }

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

        [HttpPost("create-session")]
        public async Task<IActionResult> CreateSession([FromBody] CreateSessionRequest request)
        {
            if (string.IsNullOrEmpty(request.PlayerName))
            {
                return BadRequest(new { Message = "Имя игрока обязательно" });
            }

            var session = await _sessionService.CreateSessionAsync(request.PlayerName);

            return Ok(new SessionResponse
            {
                Success = true,
                SessionId = session.SessionId,
                PlayerId = session.PlayerId,
                Message = "Сессия создана"
            });
        }

        [HttpGet("validate-session/{sessionId}/{playerId}")]
        public async Task<IActionResult> ValidateSession(string sessionId, string playerId)
        {
            var isValid = await _sessionService.ValidateSessionAsync(sessionId, playerId);

            if (!isValid)
            {
                return Unauthorized(new { Message = "Невалидная сессия" });
            }

            // Проверяем есть ли активная игра
            var session = await _sessionService.GetSessionAsync(sessionId);
            if (!string.IsNullOrEmpty(session.CurrentGameId))
            {
                var game = await _firebaseService.GetGameAsync(session.CurrentGameId);
                if (game != null)
                {
                    return Ok(new
                    {
                        Success = true,
                        HasActiveGame = true,
                        GameId = game.Id,
                        GameStatus = game.Status.ToString(),
                        Message = "Возвращаемся в активную игру"
                    });
                }
            }

            return Ok(new
            {
                Success = true,
                HasActiveGame = false,
                Message = "Сессия валидна"
            });
        }

        [HttpPost("heartbeat")]
        public async Task<IActionResult> Heartbeat([FromBody] HeartbeatRequest request)
        {
            try
            {
                _logger.LogDebug($"❤️ Heartbeat от игрока {request.PlayerId}");

                // Обновляем heartbeat через сервис
                var heartbeatService = HttpContext.RequestServices.GetService<HeartbeatService>();
                if (heartbeatService != null)
                {
                    heartbeatService.UpdateHeartbeat(request.PlayerId);
                }

                // Обновляем активность в сессии
                var session = await _sessionService.GetSessionByPlayerIdAsync(request.PlayerId);
                if (session != null)
                {
                    session.LastActivity = DateTime.UtcNow;
                    await _sessionService.UpdateSessionAsync(session);
                }

                return Ok(new
                {
                    Success = true,
                    Timestamp = DateTime.UtcNow,
                    Message = "Heartbeat получен",
                    PlayerId = request.PlayerId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Ошибка обработки heartbeat для {request.PlayerId}");
                return StatusCode(500, new { Success = false, Message = "Ошибка heartbeat" });
            }
        }

        public class HeartbeatRequest
        {
            public string PlayerId { get; set; }
        }
    }

    public class ReadyForMatchmakingRequest
    {
        public string SessionId { get; set; } 
        public string PlayerId { get; set; }
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

    public class ReadyToStartRequest
    {
        public string SessionId { get; set; }
        public string PlayerId { get; set; }
        public string PlayerName { get; set; }
    }
}