using BattleShip.Core.Enums;
using BattleShip.Core.Models;

namespace BattleShip.Server.Services
{
    public class GameService
    {

        private readonly ILogger<GameService> _logger;

        public GameService(ILogger<GameService> logger)
        {
            _logger = logger;
        }


        public (bool isHit, bool isShipSunk, Ship hitShip) CheckHitWithDetails(Board board, int x, int y)
        {
            try
            {
                Console.WriteLine($"🎯 CheckHitWithDetails в ({x},{y})");

                if (board == null) return (false, false, null);
                if (x < 0 || x >= 10 || y < 0 || y >= 10) return (false, false, null);

                var cell = board.GetCell(x, y);
                if (cell == null) return (false, false, null);

                Console.WriteLine($"🔍 Клетка: HasShip={cell.HasShip}, ShipId={cell.ShipId}, WasShot={cell.WasShot}");

                // Если уже стреляли - возвращаем информацию
                if (cell.WasShot)
                {
                    Console.WriteLine("⚠️ Уже стреляли сюда");

                    // Находим корабль
                    Ship existingShip = FindShipForCell(board, x, y, cell.ShipId);
                    return (cell.Status == CellStatus.Hit || cell.Status == CellStatus.Sunk,
                            existingShip?.IsSunk ?? false,
                            existingShip);
                }

                // Помечаем как отстрелянную
                cell.WasShot = true;

                // Находим корабль для этой клетки
                Ship hitShip = FindShipForCell(board, x, y, cell.ShipId);
                bool isHit = hitShip != null;

                if (isHit)
                {
                    // Обновляем клетку
                    cell.HasShip = true;
                    cell.Status = CellStatus.Hit;
                    if (hitShip != null && string.IsNullOrEmpty(cell.ShipId))
                        cell.ShipId = hitShip.Id;

                    // Обновляем корабль
                    hitShip.Hits++;
                    Console.WriteLine($"🚢 Корабль '{hitShip.Name}': {hitShip.Hits}/{hitShip.Size}");

                    // Проверяем потопление
                    bool isShipSunk = hitShip.Hits >= hitShip.Size;
                    if (isShipSunk)
                    {
                        hitShip.IsSunk = true;
                        Console.WriteLine($"💥 Корабль '{hitShip.Name}' ПОТОПЛЕН!");

                        // Помечаем все клетки корабля как потопленные
                        MarkShipCellsAsSunk(board, hitShip);
                    }

                    return (true, isShipSunk, hitShip);
                }
                else
                {
                    // Промах
                    cell.Status = CellStatus.Miss;
                    Console.WriteLine($"❌ ПРОМАХ в ({x},{y})");
                    return (false, false, null);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка в CheckHitWithDetails: {ex.Message}");
                return (false, false, null);
            }
        }

        private Ship FindShipForCell(Board board, int x, int y, string cellShipId)
        {
            // 1. Ищем по ShipId в клетке
            if (!string.IsNullOrEmpty(cellShipId))
            {
                var shipById = board.Ships?.FirstOrDefault(s => s.Id == cellShipId);
                if (shipById != null) return shipById;
            }

            // 2. Ищем по координатам
            var coord = $"{x},{y}";
            var shipByCoord = board.Ships?.FirstOrDefault(s =>
                s.CellCoordinates?.Contains(coord) == true);

            return shipByCoord;
        }

        private void MarkShipCellsAsSunk(Board board, Ship ship)
        {
            if (ship.CellCoordinates == null) return;

            foreach (var coord in ship.CellCoordinates)
            {
                var parts = coord.Split(',');
                if (parts.Length == 2 &&
                    int.TryParse(parts[0], out int x) &&
                    int.TryParse(parts[1], out int y))
                {
                    var cell = board.GetCell(x, y);
                    if (cell != null)
                    {
                        cell.Status = CellStatus.Sunk;
                    }
                }
            }
        }

        // Помечает клетки вокруг потопленного корабля
        public void MarkCellsAroundSunkShip(Board board, Ship sunkShip)
        {
            if (sunkShip?.CellCoordinates == null) return;

            _logger.LogInformation($"🎯 Помечаем клетки вокруг потопленного {sunkShip.Name}");

            HashSet<(int, int)> cellsToMark = new HashSet<(int, int)>();

            // 1. Собираем ВСЕ клетки вокруг ВСЕХ палуб
            foreach (var coord in sunkShip.CellCoordinates)
            {
                var parts = coord.Split(',');
                if (parts.Length == 2 &&
                    int.TryParse(parts[0], out int x) &&
                    int.TryParse(parts[1], out int y))
                {
                    // Проверяем все 8 направлений вокруг КАЖДОЙ клетки корабля
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            int nx = x + dx;
                            int ny = y + dy;

                            // Пропускаем саму клетку корабля
                            if (dx == 0 && dy == 0) continue;

                            // Проверяем границы доски
                            if (nx >= 0 && nx < 10 && ny >= 0 && ny < 10)
                            {
                                cellsToMark.Add((nx, ny));
                            }
                        }
                    }
                }
            }

            // 2. Помечаем все собранные клетки
            foreach (var (x, y) in cellsToMark)
            {
                var neighborCell = board.GetCell(x, y);
                if (neighborCell != null && !neighborCell.WasShot)
                {
                    // Помечаем ТОЛЬКО если там НЕТ корабля
                    // и эта клетка НЕ принадлежит какому-либо кораблю
                    if (!neighborCell.HasShip)
                    {
                        neighborCell.WasShot = true;
                        neighborCell.Status = CellStatus.Miss;
                        _logger.LogDebug($"   Клетка ({x},{y}) помечена как Miss");
                    }
                    else
                    {
                        // Если это другая часть того же потопленного корабля или другого корабля
                        // Проверяем, не является ли это той же палубой потопленного корабля
                        bool isSameSunkShipCell = false;
                        foreach (var coord in sunkShip.CellCoordinates)
                        {
                            var parts = coord.Split(',');
                            if (parts.Length == 2 &&
                                int.TryParse(parts[0], out int sx) &&
                                int.TryParse(parts[1], out int sy))
                            {
                                if (sx == x && sy == y)
                                {
                                    isSameSunkShipCell = true;
                                    break;
                                }
                            }
                        }

                        if (!isSameSunkShipCell)
                        {
                            _logger.LogDebug($"   Клетка ({x},{y}) содержит другой корабль - не помечаем");
                        }
                    }
                }
            }
        }

        public bool IsGameOver(Board board)
        {
            if (board?.Ships == null)
            {
                Console.WriteLine("🏁 Проверка конца игры: board или Ships null");
                return false;
            }

            Console.WriteLine($"🏁 Проверка конца игры для доски с {board.Ships.Count} кораблями");

            int sunkCount = 0;
            foreach (var ship in board.Ships)
            {
                Console.WriteLine($"   Корабль '{ship.Name}': Size={ship.Size}, Hits={ship.Hits}, IsSunk={ship.IsSunk}");
                if (ship.IsSunk) sunkCount++;
            }

            Console.WriteLine($"🏁 Потоплено: {sunkCount}/{board.Ships.Count}");

            bool allSunk = board.Ships.All(s => s.IsSunk);

            if (allSunk)
            {
                Console.WriteLine("🎉 ВСЕ КОРАБЛИ ПОТОПЛЕНЫ! Игра окончена!");
            }

            return allSunk;
        }
    }
}