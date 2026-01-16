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

            if (board == null)
            {
                Console.WriteLine("❌ Board is null!");
                return false;
            }

            if (x < 0 || x >= 10 || y < 0 || y >= 10)
                return false;

            var cell = board.GetCell(x, y);
            if (cell == null)
            {
                Console.WriteLine($"❌ Клетка ({x},{y}) не найдена!");
                return false;
            }

            Console.WriteLine($"🔍 Клетка: HasShip={cell.HasShip}, WasShot={cell.WasShot}, ShipId={cell.ShipId}");

            if (cell.WasShot)
            {
                Console.WriteLine("❌ Уже стреляли сюда");
                return false;
            }

            cell.WasShot = true;

            if (cell.HasShip && !string.IsNullOrEmpty(cell.ShipId))
            {
                Console.WriteLine($"🎯 ПОПАДАНИЕ в ({x},{y})!");
                cell.Status = CellStatus.Hit;

                // Находим корабль по ShipId
                var ship = board.Ships?.FirstOrDefault(s => s.Id == cell.ShipId);

                if (ship != null)
                {
                    ship.Hits++;
                    Console.WriteLine($"🚢 Корабль '{ship.Name}': {ship.Hits}/{ship.Size} попаданий");

                    if (ship.Hits >= ship.Size)
                    {
                        ship.IsSunk = true;
                        Console.WriteLine($"💥 Корабль '{ship.Name}' ПОТОПЛЕН!");
                    }
                }
                else
                {
                    Console.WriteLine($"⚠️ Корабль с ID {cell.ShipId} не найден");
                }

                return true;
            }

            cell.Status = CellStatus.Miss;
            Console.WriteLine($"❌ ПРОМАХ в ({x},{y})");
            return false;
        }

        // Случайная расстановка кораблей
        public void PlaceShipsRandomly(Board board)
        {
            Console.WriteLine($"🚢 Начинаем расстановку кораблей...");

            // ✅ Гарантируем что есть 100 клеток
            board.EnsureCellsInitialized();

            // Очищаем предыдущие корабли
            board.Ships?.Clear();
            if (board.Ships == null) board.Ships = new List<Ship>();

            var shipsToPlace = new[]
            {
        new Ship { Size = 4, Name = "Линкор" },
        new Ship { Size = 3, Name = "Крейсер" },
        new Ship { Size = 3, Name = "Крейсер" },
        new Ship { Size = 2, Name = "Эсминец" },
        new Ship { Size = 2, Name = "Эсминец" },
        new Ship { Size = 2, Name = "Эсминец" },
        new Ship { Size = 1, Name = "Катер" },
        new Ship { Size = 1, Name = "Катер" },
        new Ship { Size = 1, Name = "Катер" },
        new Ship { Size = 1, Name = "Катер" }
    };

            var random = new Random();
            int placedCount = 0;

            foreach (var ship in shipsToPlace)
            {
                bool placed = false;
                int attempts = 0;

                while (!placed && attempts < 100)
                {
                    attempts++;
                    bool isHorizontal = random.Next(0, 2) == 0;
                    int startX = random.Next(0, isHorizontal ? 10 - ship.Size : 10);
                    int startY = random.Next(0, isHorizontal ? 10 : 10 - ship.Size);

                    if (CanPlaceShip(board, startX, startY, ship.Size, isHorizontal))
                    {
                        PlaceShip(board, ship, startX, startY, isHorizontal);
                        placed = true;
                        placedCount++;
                    }
                }
            }

            Console.WriteLine($"🎯 Всего размещено: {placedCount} кораблей");
            Console.WriteLine($"📊 В списке Ships: {board.Ships?.Count ?? 0} кораблей");
            Console.WriteLine($"🧮 Клеток на доске: {board.Cells?.Count ?? 0}");

            // Восстанавливаем связи сразу
            board.RestoreCellShipReferences();
        }

        private bool CanPlaceShip(Board board, int startX, int startY, int size, bool isHorizontal)
        {
            for (int i = 0; i < size; i++)
            {
                int x = isHorizontal ? startX + i : startX;
                int y = isHorizontal ? startY : startY + i;

                if (x >= 10 || y >= 10) return false;

                var cell = board.GetCell(x, y);
                if (cell == null || cell.HasShip) return false;

                // Проверяем соседние клетки
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int nx = x + dx;
                        int ny = y + dy;

                        if (nx >= 0 && nx < 10 && ny >= 0 && ny < 10)
                        {
                            var neighborCell = board.GetCell(nx, ny);
                            if (neighborCell != null && neighborCell.HasShip) return false;
                        }
                    }
                }
            }
            return true;
        }

        private void PlaceShip(Board board, Ship ship, int startX, int startY, bool isHorizontal)
        {
            // Очищаем координаты
            ship.CellCoordinates?.Clear();
            if (ship.CellCoordinates == null)
                ship.CellCoordinates = new List<string>();

            for (int i = 0; i < ship.Size; i++)
            {
                int x = isHorizontal ? startX + i : startX;
                int y = isHorizontal ? startY : startY + i;

                var cell = board.GetCell(x, y);
                if (cell != null)
                {
                    cell.HasShip = true;
                    ship.CellCoordinates.Add($"{x},{y}");

                    Console.WriteLine($"   Клетка ({x},{y}) → корабль '{ship.Name}'");
                }
            }

            board.Ships.Add(ship);

            Console.WriteLine($"✅ Корабль '{ship.Name}' размещен. ID: {ship.Id}");
        }

        public bool IsGameOver(Board board)
        {
            return board.Ships.All(s => s.IsSunk);
        }
    }
}