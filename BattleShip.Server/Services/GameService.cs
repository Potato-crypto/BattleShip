using BattleShip.Core.Enums;
using BattleShip.Core.Models;

namespace BattleShip.Server.Services
{
    public class GameService
    {
        // Проверка попадания по кораблю
        public bool CheckHit(Board board, int x, int y)
        {
            Console.WriteLine($"🎯 CheckHit в ({x},{y})");

            // Проверка входных данных
            if (board == null) return false;
            if (x < 0 || x >= 10 || y < 0 || y >= 10) return false;

            // Получаем клетку
            var cell = board.GetCell(x, y);
            if (cell == null) return false;

            Console.WriteLine($"🔍 Клетка: HasShip={cell.HasShip}, ShipId={cell.ShipId}");

            // Если уже стреляли
            if (cell.WasShot)
            {
                Console.WriteLine("⚠️ Уже стреляли сюда");
                return cell.Status == CellStatus.Hit || cell.Status == CellStatus.Sunk;
            }

            // Помечаем как простреленную
            cell.WasShot = true;

            // Проверяем попадание ДВУМЯ способами:
            bool isHit = false;

            // По HasShip и ShipId (если связи есть)
            if (cell.HasShip)
            {
                Console.WriteLine($"🎯 ПОПАДАНИЕ (через HasShip) в ({x},{y})!");
                isHit = true;
            }
            // По координатам в кораблях (если связи нет)
            else if (board.Ships != null)
            {
                var coord = $"{x},{y}";
                var ship = board.Ships.FirstOrDefault(s =>
                    s.CellCoordinates?.Contains(coord) == true);

                if (ship != null)
                {
                    Console.WriteLine($"🎯 ПОПАДАНИЕ (через координаты корабля) в ({x},{y})!");
                    isHit = true;

                    // Восстанавливаем связь
                    cell.HasShip = true;
                    cell.ShipId = ship.Id;
                }
            }

            // Обновляем статус клетки
            if (isHit)
            {
                cell.Status = CellStatus.Hit;

                // Находим корабль для обновления Hits
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
                        Console.WriteLine($"💥 Корабль '{hitShip.Name}' ПОТОПЛЕН!");

                        // Помечаем все клетки корабля как потопленные
                        MarkShipCellsAsSunk(board, hitShip);
                    }
                }
            }
            else
            {
                cell.Status = CellStatus.Miss;
                Console.WriteLine($"❌ ПРОМАХ в ({x},{y})");
            }

            return isHit;
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

        public bool IsGameOver(Board board)
        {
            return board.Ships.All(s => s.IsSunk);
        }
    }
}