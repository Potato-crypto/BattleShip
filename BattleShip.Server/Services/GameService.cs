using BattleShip.Core.Enums;
using BattleShip.Core.Models;

namespace BattleShip.Server.Services
{
    public class GameService
    {
        public (bool isHit, bool isShipSunk, Ship sunkShip) CheckHitWithDetails(Board board, int x, int y)
        {
            Console.WriteLine($"🎯 CheckHitWithDetails в ({x},{y})");

            if (board == null) return (false, false, null);
            if (x < 0 || x >= 10 || y < 0 || y >= 10) return (false, false, null);

            var cell = board.GetCell(x, y);
            if (cell == null) return (false, false, null);

            Console.WriteLine($"🔍 Клетка: HasShip={cell.HasShip}, ShipId={cell.ShipId}");

            if (cell.WasShot)
            {
                Console.WriteLine("⚠️ Уже стреляли сюда");
                return (cell.Status == CellStatus.Hit || cell.Status == CellStatus.Sunk, false, null);
            }

            cell.WasShot = true;
            bool isHit = false;

            if (cell.HasShip)
            {
                Console.WriteLine($"🎯 ПОПАДАНИЕ (через HasShip) в ({x},{y})!");
                isHit = true;
            }
            else if (board.Ships != null)
            {
                var coord = $"{x},{y}";
                var ship = board.Ships.FirstOrDefault(s =>
                    s.CellCoordinates?.Contains(coord) == true);

                if (ship != null)
                {
                    Console.WriteLine($"🎯 ПОПАДАНИЕ (через координаты корабля) в ({x},{y})!");
                    isHit = true;
                    cell.HasShip = true;
                    cell.ShipId = ship.Id;
                }
            }

            bool isShipSunk = false;
            Ship sunkShip = null;

            if (isHit)
            {
                cell.Status = CellStatus.Hit;

                Ship hitShip = null;

                if (!string.IsNullOrEmpty(cell.ShipId))
                {
                    hitShip = board.Ships?.FirstOrDefault(s => s.Id == cell.ShipId);
                }

                if (hitShip == null)
                {
                    var coord = $"{x},{y}";
                    hitShip = board.Ships?.FirstOrDefault(s => s.CellCoordinates?.Contains(coord) == true);
                }

                if (hitShip != null)
                {
                    hitShip.Hits++;
                    Console.WriteLine($"🚢 Корабль '{hitShip.Name}': {hitShip.Hits}/{hitShip.Size}");

                    if (hitShip.Hits >= hitShip.Size)
                    {
                        hitShip.IsSunk = true;
                        sunkShip = hitShip;
                        isShipSunk = true;

                        Console.WriteLine($"💥 Корабль '{hitShip.Name}' ПОТОПЛЕН!");

                        // Помечаем клетки корабля как Sunk
                        MarkShipCellsAsSunk(board, hitShip);

                        // Помечаем клетки вокруг как Miss
                        MarkCellsAroundSunkShip(board, hitShip);
                    }
                }
            }
            else
            {
                cell.Status = CellStatus.Miss;
                Console.WriteLine($"❌ ПРОМАХ в ({x},{y})");
            }

            return (isHit, isShipSunk, sunkShip);
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
        private void MarkCellsAroundSunkShip(Board board, Ship ship)
        {
            if (ship.CellCoordinates == null) return;

            Console.WriteLine($"🎯 Помечаем клетки вокруг потопленного корабля '{ship.Name}'");

            foreach (var coord in ship.CellCoordinates)
            {
                var parts = coord.Split(',');
                if (parts.Length == 2 &&
                    int.TryParse(parts[0], out int x) &&
                    int.TryParse(parts[1], out int y))
                {
                    // Проверяем все 8 направлений вокруг клетки
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
                                var neighborCell = board.GetCell(nx, ny);
                                if (neighborCell != null && !neighborCell.WasShot)
                                {
                                    // Помечаем как промах (даже если там есть корабль!)
                                    neighborCell.WasShot = true;
                                    neighborCell.Status = CellStatus.Miss;
                                    Console.WriteLine($"   Клетка вокруг ({nx},{ny}) помечена как Miss");
                                }
                            }
                        }
                    }
                }
            }
        }

        public bool CheckHit(Board board, int x, int y)
        {
            var (isHit, _, _) = CheckHitWithDetails(board, x, y);
            return isHit;
        }

        public bool IsGameOver(Board board)
        {
            if (board?.Ships == null) return false;

            bool allSunk = board.Ships.All(s => s.IsSunk);

            Console.WriteLine($"🏁 Проверка конца игры: {board.Ships.Count(s => s.IsSunk)}/{board.Ships.Count} потоплено");

            return allSunk;
        }
    }
}