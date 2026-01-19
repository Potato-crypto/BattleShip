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

        public GameController(FirebaseService firebaseService, GameService gameService)
        {
            _firebaseService = firebaseService;
            _gameService = gameService; 
        }

        [HttpPost("find-game")]
        public async Task<IActionResult> FindGame([FromBody] FindGameRequest request)
        {
            Console.WriteLine($"🔍 Поиск игры для: {request.PlayerName}");

            // 1. Сначала ищем существующую игру в ожидании
            var waitingGames = await _firebaseService.FindWaitingGamesAsync();

            if (waitingGames.Any())
            {
                // Нашли ожидающую игру - присоединяемся
                var existingGame = waitingGames.First();
                existingGame.Player2Id = await _firebaseService.CreateTestUser();
                existingGame.Status = GameStatus.PlacingShips;

                Console.WriteLine($"✅ Нашлась ожидающая игра {existingGame.Id}");

                await _firebaseService.UpdateGameAsync(existingGame);

                return Ok(new
                {
                    Success = true,
                    GameId = existingGame.Id,
                    PlayerId = existingGame.Player2Id, // Игрок становится Player2
                    IsPlayer1 = false, // Это второй игрок
                    GameStatus = existingGame.Status.ToString(),
                    Message = "Присоединились к существующей игре!"
                });
            }
            else
            {
                // Не нашли - создаем новую игру
                var game = new Game
                {
                    Player1Id = await _firebaseService.CreateTestUser(),
                    Status = GameStatus.WaitingForPlayer, // Ожидание второго игрока
                    Player1Ready = false,
                    Player2Ready = false
                };

                // Создаем доски
                game.Player1Board = new Board();
                game.Player2Board = new Board();

                await _firebaseService.SaveGameAsync(game);

                Console.WriteLine($"🆕 Создана новая игра {game.Id}");

                return Ok(new
                {
                    Success = true,
                    GameId = game.Id,
                    PlayerId = game.Player1Id, // Игрок становится Player1
                    IsPlayer1 = true,
                    GameStatus = game.Status.ToString(),
                    Message = "Игра создана. Ожидаем второго игрока..."
                });
            }
        }




        [HttpGet("test")]
        public IActionResult Test()
        {
            return Ok(new
            {
                Message = "BattleShip Server работает!",
                Time = DateTime.Now,
                Project = "Морской бой",
                Team = "3 разработчика"
            });
        }


        [HttpPost("{id}/ready")]
        public async Task<IActionResult> PlayerReady(string id, [FromBody] SimpleReadyRequest request)
        {
            var game = await _firebaseService.GetGameAsync(id);
            if (game == null)
                return NotFound(new { Message = "Игра не найдена" });

            bool isPlayer1 = request.PlayerId == game.Player1Id;
            bool isPlayer2 = request.PlayerId == game.Player2Id;

            if (!isPlayer1 && !isPlayer2)
                return Unauthorized(new { Message = "Вы не участник этой игры" });

            var playerBoard = isPlayer1 ? game.Player1Board : game.Player2Board;

            if (request.Ships != null && request.Ships.Any())
            {
                Console.WriteLine($"💾 Сохраняем {request.Ships.Count} кораблей от игрока {(isPlayer1 ? "1" : "2")}");

                
                playerBoard.InitializeBoard(request.Ships);

                
                int shipCells = playerBoard.Cells.Count(c => c.HasShip);
                Console.WriteLine($"✅ После инициализации: {shipCells} клеток с кораблями");

                foreach (var ship in playerBoard.Ships)
                {
                    Console.WriteLine($"   🚢 {ship.Name}: {ship.CellCoordinates?.Count ?? 0} клеток");
                }
            }

            if (isPlayer1)
                game.Player1Ready = true;
            else
                game.Player2Ready = true;

            bool bothReady = game.Player1Ready && game.Player2Ready;

            if (bothReady)
            {
                game.Status = GameStatus.Player1Turn;
                game.CurrentPlayerId = game.Player1Id;
                Console.WriteLine($"🎮 Оба игрока готовы! Игра начинается!");
            }

            
            await _firebaseService.UpdateGameAsync(game);

            return Ok(new
            {
                Success = true,
                GameStatus = game.Status.ToString(),
                BothReady = bothReady,
                Player = isPlayer1 ? "Player1" : "Player2",
                ShipsPlaced = playerBoard.Ships?.Count ?? 0,
                ShipCells = playerBoard.Cells?.Count(c => c.HasShip) ?? 0,
                Message = "Корабли расставлены!"
            });
        }

        [HttpPost("{id}/place-ships-manual")]
        public async Task<IActionResult> PlaceShipsManual(string id, [FromBody] PlaceShipsManualRequest request)
        {
            var game = await _firebaseService.GetGameAsync(id);
            if (game == null)
                return NotFound(new { Message = "Игра не найдена" });

            // Проверяем что игра в фазе расстановки
            if (game.Status != GameStatus.PlacingShips &&
                game.Status != GameStatus.Player1Ready &&
                game.Status != GameStatus.Player2Ready)
                return BadRequest(new { Message = "Не время для расстановки кораблей" });

            bool isPlayer1 = request.PlayerId == game.Player1Id;
            bool isPlayer2 = request.PlayerId == game.Player2Id;

            if (!isPlayer1 && !isPlayer2)
                return Unauthorized(new { Message = "Вы не участник этой игры" });

            var targetBoard = isPlayer1 ? game.Player1Board : game.Player2Board;

            // Очищаем старые корабли
            targetBoard.Ships?.Clear();

            // Добавляем новые корабли от фронтенда
            if (request.Ships != null)
            {
                foreach (var ship in request.Ships)
                {
                    // Проверяем что корабль можно разместить
                    if (ValidateShipPlacement(targetBoard, ship))
                    {
                        targetBoard.Ships.Add(ship);
                    }
                    else
                    {
                        return BadRequest(new { Message = $"Невозможно разместить корабль {ship.Name}" });
                    }
                }

                // Восстанавливаем связи
                targetBoard.RestoreCellShipReferences();
            }

            await _firebaseService.UpdateGameAsync(game);

            return Ok(new
            {
                Success = true,
                ShipsPlaced = targetBoard.Ships.Count,
                Message = "Корабли успешно расставлены"
            });
        }

        private bool ValidateShipPlacement(Board board, Ship ship)
        {
            // Проверка что корабль помещается на доске
            // и не пересекается с другими кораблями
            // (нужно реализовать)
            return true;
        }



        [HttpGet("{id}")]
        public async Task<IActionResult> GetGame(string id)
        {
            var game = await _firebaseService.GetGameAsync(id);
            if (game == null)
                return NotFound(new { Message = "Игра не найдена" });

            
            return Ok(new
            {
                game.Id,
                game.Player1Id,
                game.Player2Id,
                game.Status,
                game.CurrentPlayerId,
                Player1Board = new
                {
                    ShipsCount = game.Player1Board.Ships?.Count ?? 0,
                    CellsCount = game.Player1Board.Cells?.Count ?? 0
                },
                Player2Board = new
                {
                    ShipsCount = game.Player2Board.Ships?.Count ?? 0,
                    CellsCount = game.Player2Board.Cells?.Count ?? 0
                },
                game.CreatedAt
            });
        }

        [HttpGet("{id}/validate")]
        public async Task<IActionResult> ValidateGame(string id)
        {
            var game = await _firebaseService.GetGameAsync(id);
            if (game == null)
                return NotFound(new { Message = "Игра не найдена" });

            var issues = new List<string>();

            // Проверка Player1Board
            if (game.Player1Board?.Cells?.Count != 100)
                issues.Add($"Player1Board: {game.Player1Board?.Cells?.Count ?? 0} клеток вместо 100");

            if (game.Player1Board?.Ships?.Count != 10)
                issues.Add($"Player1Board: {game.Player1Board?.Ships?.Count ?? 0} кораблей вместо 10");

            var p1ShipCells = game.Player1Board?.Cells?.Count(c => c.HasShip) ?? 0;
            if (p1ShipCells != 20) // 4+3+3+2+2+2+1+1+1+1 = 20
                issues.Add($"Player1Board: {p1ShipCells} клеток с кораблями вместо 20");

            // Проверка Player2Board
            if (game.Player2Board?.Cells?.Count != 100)
                issues.Add($"Player2Board: {game.Player2Board?.Cells?.Count ?? 0} клеток вместо 100");

            if (game.Player2Board?.Ships?.Count != 10)
                issues.Add($"Player2Board: {game.Player2Board?.Ships?.Count ?? 0} кораблей вместо 10");

            var p2ShipCells = game.Player2Board?.Cells?.Count(c => c.HasShip) ?? 0;
            if (p2ShipCells != 20)
                issues.Add($"Player2Board: {p2ShipCells} клеток с кораблями вместо 20");

            return Ok(new
            {
                GameId = id,
                Status = game.Status.ToString(),
                Player1Id = game.Player1Id,
                Player2Id = game.Player2Id,
                Player1Board = new
                {
                    Cells = game.Player1Board?.Cells?.Count ?? 0,
                    Ships = game.Player1Board?.Ships?.Count ?? 0,
                    ShipCells = game.Player1Board?.Cells?.Count(c => c.HasShip) ?? 0,
                    HasNullCells = game.Player1Board?.Cells?.Any(c => c == null) ?? false
                },
                Player2Board = new
                {
                    Cells = game.Player2Board?.Cells?.Count ?? 0,
                    Ships = game.Player2Board?.Ships?.Count ?? 0,
                    ShipCells = game.Player2Board?.Cells?.Count(c => c.HasShip) ?? 0,
                    HasNullCells = game.Player2Board?.Cells?.Any(c => c == null) ?? false
                },
                Issues = issues,
                IsValid = issues.Count == 0
            });
        }

        [HttpPost("{id}/join")]
        public async Task<IActionResult> JoinGame(string id, [FromBody] string playerName)
        {
            var game = await _firebaseService.GetGameAsync(id);
            if (game == null)
                return NotFound(new { Message = "Игра не найдена" });

            if (game.Status != GameStatus.WaitingForPlayer)
                return BadRequest(new { Message = "Игра уже началась" });

            if (game.Player1Board == null) game.Player1Board = new Board();
            if (game.Player2Board == null) game.Player2Board = new Board();

            // Присоединяем второго игрока
            game.Player2Id = await _firebaseService.CreateTestUser();
            game.Status = GameStatus.PlacingShips;

            await _firebaseService.UpdateGameAsync(game);

            return Ok(new
            {
                Success = true,
                GameId = id,
                Status = game.Status.ToString(),
                Player1Id = game.Player1Id,
                Player2Id = game.Player2Id,
                Message = $"Игрок {playerName} присоединился. Расставляйте корабли!"
            });
        }

        [HttpPost("{id}/fire")]
        public async Task<IActionResult> Fire(string id, [FromBody] FireRequest request)
        {
            Console.WriteLine($"🔥 Fire запрос: gameId={id}, playerId={request.PlayerId}, x={request.X}, y={request.Y}");

            var game = await _firebaseService.GetGameAsync(id);
            if (game == null)
            {
                Console.WriteLine($"❌ Игра {id} не найдена");
                return NotFound(new { Message = "Игра не найдена" });
            }

            Console.WriteLine($"📊 Статус игры: {game.Status}, CurrentPlayer: {game.CurrentPlayerId}");
            Console.WriteLine($"👤 Player1: {game.Player1Id}, Player2: {game.Player2Id}");

            // Проверяем чей сейчас ход
            if (game.Status != GameStatus.Player1Turn && game.Status != GameStatus.Player2Turn)
            {
                Console.WriteLine($"❌ Игра не в активной фазе. Статус: {game.Status}");
                return BadRequest(new { Message = "Игра не в активной фазе" });
            }

            // Проверяем, может ли этот игрок стрелять сейчас
            bool isPlayer1Turn = game.Status == GameStatus.Player1Turn;
            if ((isPlayer1Turn && request.PlayerId != game.Player1Id) ||
                (!isPlayer1Turn && request.PlayerId != game.Player2Id))
            {
                Console.WriteLine($"❌ Не очередь игрока. Ход: {(isPlayer1Turn ? "Player1" : "Player2")}, Стреляет: {request.PlayerId}");
                return BadRequest(new { Message = "Сейчас не ваш ход" });
            }

            bool isPlayer1Shooting = request.PlayerId == game.Player1Id;
            Board targetBoard = isPlayer1Shooting ? game.Player2Board : game.Player1Board;
            string targetPlayerNumber = isPlayer1Shooting ? "2" : "1";

            Console.WriteLine($"🎯 Стреляет игрок {(isPlayer1Shooting ? "1" : "2")} в доску игрока {targetPlayerNumber}");

            var cell = targetBoard.GetCell(request.X, request.Y);
            if (cell != null && cell.WasShot)
            {
                Console.WriteLine($"❌ Уже стреляли в клетку ({request.X},{request.Y})!");
                return BadRequest(new
                {
                    Message = "Вы уже стреляли в эту клетку!",
                    Status = cell.Status.ToString()
                });
            }

            // Используем новый метод с деталями
            var (isHit, isShipSunk, sunkShip) = _gameService.CheckHitWithDetails(targetBoard, request.X, request.Y);

            Console.WriteLine($"🎯 Результат: isHit={isHit}, isShipSunk={isShipSunk}");

            if (sunkShip != null)
            {
                Console.WriteLine($"💥 Потоплен корабль: {sunkShip.Name} ({sunkShip.Size} клеток)");
            }

            await _firebaseService.SaveShotAsync(id, request.PlayerId, request.X, request.Y, isHit);
            await _firebaseService.UpdateBoardAsync(id, targetPlayerNumber, targetBoard);

            bool isGameOver = _gameService.IsGameOver(targetBoard);

            // ИСПРАВЛЕНО: Правильная логика смены хода по правилам морского боя
            if (!isGameOver)
            {
                if (isHit)
                {
                    // ПОПАДАНИЕ (даже если потопил корабль) - продолжает ходить
                    Console.WriteLine($"🎯 ПОПАДАНИЕ! Игрок продолжает ход.");
                    // Статус игры НЕ меняем, CurrentPlayerId НЕ меняем
                }
                else
                {
                    // ПРОМАХ - меняем ход
                    game.Status = game.Status == GameStatus.Player1Turn
                        ? GameStatus.Player2Turn
                        : GameStatus.Player1Turn;

                    game.CurrentPlayerId = game.Status == GameStatus.Player1Turn
                        ? game.Player1Id
                        : game.Player2Id;

                    Console.WriteLine($"🔄 ПРОМАХ! Смена хода. Новый статус: {game.Status}");
                }
            }
            else
            {
                // КОНЕЦ ИГРЫ
                game.Status = request.PlayerId == game.Player1Id
                    ? GameStatus.Player1Won
                    : GameStatus.Player2Won;
                Console.WriteLine($"🏆 ПОБЕДА! Победитель: {game.Status}");
            }

            await _firebaseService.UpdateGameAsync(game);
            Console.WriteLine($"✅ Игра обновлена в Firebase");

            // ИСПРАВЛЕНО: NextPlayer теперь всегда "player" при попадании, "opponent" при промахе
            string nextPlayer = "unknown";
            if (isGameOver)
            {
                nextPlayer = "game_over";
            }
            else if (isHit)
            {
                nextPlayer = "same_player"; // Тот же игрок продолжает
            }
            else
            {
                nextPlayer = game.Status == GameStatus.Player1Turn ? "player1" : "player2";
            }

            // Добавляем всю информацию для клиента
            return Ok(new
            {
                Success = true,
                IsHit = isHit,
                IsShipSunk = isShipSunk,
                ShipSize = sunkShip?.Size ?? 0,
                ShipName = sunkShip?.Name ?? "",
                CellStatus = cell?.Status.ToString() ?? "Empty", // "Hit", "Miss", "Sunk"
                IsGameOver = isGameOver,
                GameStatus = game.Status.ToString(),
                CurrentPlayerId = game.CurrentPlayerId,
                NextPlayer = nextPlayer, // ИСПРАВЛЕНО: понятное значение
                Message = isShipSunk ?
                    $"Потоплен корабль {sunkShip?.Name}!" :
                    (isHit ? "Попадание!" : "Мимо!")
            });
        }

        [HttpGet("{id}/wait")]
        public async Task<IActionResult> WaitForGame(string id, [FromQuery] string playerId)
        {
            Console.WriteLine($"⏳ Ожидание игры {id} для игрока {playerId}");

            int maxAttempts = 30; // 30 * 2 секунды = 1 минута
            for (int i = 0; i < maxAttempts; i++)
            {
                var game = await _firebaseService.GetGameAsync(id);

                if (game == null)
                    return NotFound(new { Message = "Игра не найдена" });

                // Проверяем статус
                if (game.Status == GameStatus.Player1Turn || game.Status == GameStatus.Player2Turn)
                {
                    // Игра началась!
                    return Ok(new
                    {
                        GameStarted = true,
                        GameStatus = game.Status.ToString(),
                        CurrentPlayerId = game.CurrentPlayerId,
                        Message = "Игра началась!"
                    });
                }

                // Проверяем присоединился ли второй игрок
                if (game.Status == GameStatus.PlacingShips && !string.IsNullOrEmpty(game.Player2Id))
                {
                    return Ok(new
                    {
                        GameStarted = false,
                        GameStatus = game.Status.ToString(),
                        Player1Id = game.Player1Id,
                        Player2Id = game.Player2Id,
                        Message = "Второй игрок присоединился. Расставляйте корабли!"
                    });
                }

                await Task.Delay(2000); // Ждем 2 секунды
                Console.WriteLine($"   Попытка {i + 1}/{maxAttempts}...");
            }

            return Ok(new
            {
                GameStarted = false,
                Timeout = true,
                Message = "Время ожидания истекло"
            });
        }


        [HttpGet("{id}/player/{playerId}")]
        public async Task<IActionResult> GetPlayerView(string id, string playerId)
        {
            var game = await _firebaseService.GetGameAsync(id);
            if (game == null)
                return NotFound(new { Message = "Игра не найдена" });

            if (playerId != game.Player1Id && playerId != game.Player2Id)
                return Unauthorized(new { Message = "Вы не участник этой игры" });

            bool isPlayer1 = playerId == game.Player1Id;

            var myBoard = isPlayer1 ? game.Player1Board : game.Player2Board;
            var opponentBoard = isPlayer1 ? game.Player2Board : game.Player1Board;

            // Создаем скрытое представление доски противника
            var hiddenOpponentCells = new List<object>();
            foreach (var cell in opponentBoard.Cells)
            {
                hiddenOpponentCells.Add(new
                {
                    cell.X,
                    cell.Y,
                    cell.WasShot,
                    Status = cell.WasShot ? cell.Status : CellStatus.Empty,
                    // Можно показать потопленные корабли
                    ShowSunk = cell.WasShot && cell.HasShip &&
                              opponentBoard.Ships.Any(s =>
                                  s.CellCoordinates.Contains($"{cell.X},{cell.Y}") && s.IsSunk)
                });
            }

            return Ok(new
            {
                GameId = id,
                GameStatus = game.Status.ToString(),
                IsMyTurn = game.CurrentPlayerId == playerId,
                MyPlayerId = playerId,
                OpponentId = isPlayer1 ? game.Player2Id : game.Player1Id,
                Player1Ready = game.Player1Ready,
                Player2Ready = game.Player2Ready,
                WaitingFor = game.Player1Ready ? "Player2" : "Player1",

                // Моя доска
                MyBoard = new
                {
                    Cells = myBoard.Cells.Select(c => new
                    {
                        c.X,
                        c.Y,
                        c.HasShip,
                        c.WasShot,
                        c.Status,
                        //  Показываем потопленные корабли
                        IsSunkShip = c.HasShip && myBoard.Ships.Any(s =>
                            s.CellCoordinates.Contains($"{c.X},{c.Y}") && s.IsSunk)
                    }),
                    Ships = myBoard.Ships.Select(s => new
                    {
                        s.Name,
                        s.Size,
                        s.IsSunk,
                        Hits = s.Hits,
                        Cells = s.CellCoordinates
                    })
                },

                // Доска противника
                OpponentBoard = new
                {
                    Cells = hiddenOpponentCells,
                    ShipsSunk = opponentBoard.Ships.Count(s => s.IsSunk),
                    ShipsRemaining = opponentBoard.Ships.Count(s => !s.IsSunk),
                    // Дополнительная информация о потопленных кораблях
                    SunkShips = opponentBoard.Ships.Where(s => s.IsSunk)
                        .Select(s => new { s.Name, s.Size })
                }
            });
        }
    }

    public class SimpleReadyRequest
    {
        public string PlayerId { get; set; }
        public List<Ship> Ships { get; set; }
    }

    public class PlayerReadyRequest
    {
        public string PlayerId { get; set; }
        public List<Ship> Ships { get; set; } // Корабли от фронтенда
    }

    public class FindGameRequest
    {
        public string PlayerName { get; set; }
    }

    public class PlaceShipsRequest
    {
        public string PlayerId { get; set; }
    }

    public class FireRequest
    {
        public int X { get; set; }
        public int Y { get; set; }
        public string PlayerId { get; set; }
    }

    public class PlaceShipsManualRequest
    {
        public string PlayerId { get; set; }
        public List<Ship> Ships { get; set; }
    }

}