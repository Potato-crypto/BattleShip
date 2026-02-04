using BattleShip.Core.Enums;
using BattleShip.Core.Models;
using System.Text.Json.Serialization;

public class Board
{
    [JsonPropertyName("cells")]
    public List<Cell> Cells { get; set; }

    [JsonPropertyName("ships")]
    public List<Ship> Ships { get; set; }

    public Board()
    {
        
        Cells = new List<Cell>();
        Ships = new List<Ship>();
    }


    public void EnsureCellsInitialized()
    {
        if (Cells == null) Cells = new List<Cell>();

        // Если клеток нет или их не 100 - создаем
        if (Cells.Count != 100)
        {
            Cells.Clear();
            for (int x = 0; x < 10; x++)
            {
                for (int y = 0; y < 10; y++)
                {
                    Cells.Add(new Cell { X = x, Y = y });
                }
            }
            Console.WriteLine($"✅ Создано 100 клеток. Всего: {Cells.Count}");
        }
    }

    public void RestoreCellShipReferences()
    {
        Console.WriteLine($"🔄 Восстановление связей клеток в доске...");
        Console.WriteLine($"   Кораблей: {Ships?.Count ?? 0}");
        Console.WriteLine($"   Клеток: {Cells?.Count ?? 0}");

        if (Cells == null || Ships == null)
        {
            Console.WriteLine($"❌ Cells или Ships null!");
            return;
        }

        // 1. Очищаем связи с кораблями (но сохраняем WasShot!)
        foreach (var cell in Cells)
        {
            cell.HasShip = false;
            cell.ShipId = null;
            // НЕ трогаем cell.WasShot и cell.Status!
        }

        // 2. Восстанавливаем связи с кораблями
        foreach (var ship in Ships)
        {
            if (ship.CellCoordinates == null) continue;

            foreach (var coord in ship.CellCoordinates)
            {
                var parts = coord.Split(',');
                if (parts.Length == 2 &&
                    int.TryParse(parts[0], out int x) &&
                    int.TryParse(parts[1], out int y))
                {
                    var cell = GetCell(x, y);
                    if (cell != null)
                    {
                        cell.HasShip = true;
                        cell.ShipId = ship.Id;

                        // Если корабль потоплен, помечаем все его клетки как Sunk
                        if (ship.IsSunk)
                        {
                            cell.Status = CellStatus.Sunk;
                            cell.WasShot = true;
                        }

                        Console.WriteLine($"   ✅ Клетка ({x},{y}) → корабль '{ship.Name}', WasShot={cell.WasShot}, Status={cell.Status}");
                    }
                }
            }
        }

        // 3. Восстанавливаем статусы отстрелянных клеток
        // (если клетка была отстреляна, но не связана с кораблем - это Miss)
        foreach (var cell in Cells.Where(c => c.WasShot && !c.HasShip))
        {
            cell.Status = CellStatus.Miss;
        }

        int shipCellsCount = Cells.Count(c => c.HasShip);
        int shotCellsCount = Cells.Count(c => c.WasShot);
        Console.WriteLine($"📊 Клеток с кораблями: {shipCellsCount} (должно быть 20)");
        Console.WriteLine($"🎯 Прострелянных клеток: {shotCellsCount}");
    }

    public bool ValidateShipPlacement()
    {
        Console.WriteLine("🔍 Начинаем валидацию расстановки кораблей...");

        // Проверка 1: Должно быть 10 кораблей
        if (Ships?.Count != 10)
        {
            Console.WriteLine($"❌ Неправильное количество кораблей: {Ships?.Count ?? 0} вместо 10");
            return false;
        }

        // Проверка 2: Правильный набор кораблей (1x4, 2x3, 3x2, 4x1)
        var expectedShips = new Dictionary<int, int>
        {
            { 4, 1 }, // 1 корабль размером 4
            { 3, 2 }, // 2 корабля размером 3
            { 2, 3 }, // 3 корабля размером 2
            { 1, 4 }  // 4 корабля размером 1
        };

        var actualShips = Ships.GroupBy(s => s.Size)
                              .ToDictionary(g => g.Key, g => g.Count());

        foreach (var expected in expectedShips)
        {
            actualShips.TryGetValue(expected.Key, out int actualCount);
            if (actualCount != expected.Value)
            {
                Console.WriteLine($"❌ Неправильное количество кораблей размером {expected.Key}: {actualCount} вместо {expected.Value}");
                return false;
            }
        }

        // Проверка 3: Корабли не пересекаются и не касаются друг друга
        var occupiedCells = new HashSet<string>();
        var shipCells = new List<string>(); // Только клетки кораблей

        Console.WriteLine("📊 Проверка расположения кораблей...");

        foreach (var ship in Ships)
        {
            if (ship.CellCoordinates == null)
            {
                Console.WriteLine($"❌ Корабль {ship.Name} не имеет координат");
                return false;
            }

            Console.WriteLine($"   Корабль {ship.Name} (размер {ship.Size}):");

            // Проверяем координаты корабля
            List<(int x, int y)> coordinates = new List<(int x, int y)>();

            foreach (var coord in ship.CellCoordinates)
            {
                var parts = coord.Split(',');
                if (parts.Length != 2 ||
                    !int.TryParse(parts[0], out int x) ||
                    !int.TryParse(parts[1], out int y))
                {
                    Console.WriteLine($"❌ Неправильный формат координат: {coord}");
                    return false;
                }

                if (x < 0 || x >= 10 || y < 0 || y >= 10)
                {
                    Console.WriteLine($"❌ Координата вне доски: ({x},{y})");
                    return false;
                }

                var coordKey = $"{x},{y}";

                // Проверяем пересечение с другими кораблями
                if (shipCells.Contains(coordKey))
                {
                    Console.WriteLine($"❌ Пересечение кораблей в клетке ({x},{y})");
                    return false;
                }

                shipCells.Add(coordKey);
                coordinates.Add((x, y));
                Console.WriteLine($"     ✅ ({x},{y})");
            }

            // Проверяем что корабль расположен по прямой линии
            if (!IsShipInStraightLine(coordinates))
            {
                Console.WriteLine($"❌ Корабль {ship.Name} расположен не по прямой линии");
                return false;
            }
        }

        // Проверка 4: Корабли не касаются друг друга
        Console.WriteLine("📏 Проверка расстояния между кораблями...");

        foreach (var ship in Ships)
        {
            foreach (var coord in ship.CellCoordinates)
            {
                var parts = coord.Split(',');
                if (parts.Length == 2 &&
                    int.TryParse(parts[0], out int x) &&
                    int.TryParse(parts[1], out int y))
                {
                    // Проверяем все 8 соседних клеток
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            if (dx == 0 && dy == 0) continue; // Саму клетку не проверяем

                            int nx = x + dx;
                            int ny = y + dy;

                            if (nx >= 0 && nx < 10 && ny >= 0 && ny < 10)
                            {
                                string neighborKey = $"{nx},{ny}";

                                // Если соседняя клетка занята другим кораблем
                                if (shipCells.Contains(neighborKey))
                                {
                                    // Находим, какому кораблю принадлежит эта клетка
                                    var otherShip = Ships.FirstOrDefault(s =>
                                        s.CellCoordinates != null &&
                                        s.CellCoordinates.Contains(neighborKey));

                                    if (otherShip != null && !IsSameShip(ship, otherShip))
                                    {
                                        Console.WriteLine($"❌ Корабли касаются друг друга в клетках ({x},{y}) и ({nx},{ny})");
                                        return false;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        Console.WriteLine($"✅ Расстановка кораблей валидна!");
        return true;
    }

    private bool IsShipInStraightLine(List<(int x, int y)> coordinates)
    {
        if (coordinates.Count <= 1) return true;

        // Сортируем координаты
        var sorted = coordinates.OrderBy(c => c.x).ThenBy(c => c.y).ToList();

        // Проверяем горизонтальное расположение
        bool isHorizontal = sorted.All(c => c.x == sorted[0].x);
        bool isVertical = sorted.All(c => c.y == sorted[0].y);

        if (!isHorizontal && !isVertical)
        {
            Console.WriteLine($"   ❌ Корабль не по прямой линии: клетки в разных строках и столбцах");
            return false;
        }

        if (isHorizontal)
        {
            // Проверяем что столбцы идут подряд
            var cols = sorted.Select(c => c.y).OrderBy(y => y).ToList();
            for (int i = 1; i < cols.Count; i++)
            {
                if (cols[i] != cols[i - 1] + 1)
                {
                    Console.WriteLine($"   ❌ Горизонтальный корабль: столбцы не идут подряд");
                    return false;
                }
            }
            Console.WriteLine($"   ✅ Горизонтальный корабль, строка {sorted[0].x}, столбцы {string.Join(",", cols)}");
        }
        else
        {
            // Проверяем что строки идут подряд
            var rows = sorted.Select(c => c.x).OrderBy(x => x).ToList();
            for (int i = 1; i < rows.Count; i++)
            {
                if (rows[i] != rows[i - 1] + 1)
                {
                    Console.WriteLine($"   ❌ Вертикальный корабль: строки не идут подряд");
                    return false;
                }
            }
            Console.WriteLine($"   ✅ Вертикальный корабль, столбец {sorted[0].y}, строки {string.Join(",", rows)}");
        }

        return true;
    }

    private bool IsSameShip(Ship ship1, Ship ship2)
    {
        return ship1.Id == ship2.Id;
    }

    public void InitializeBoard(List<Ship> ships = null)
    {
        Console.WriteLine($"🎯 Инициализация доски...");

        // Создаем 100 клеток если их нет
        EnsureCellsInitialized();

        // Очищаем все клетки
        foreach (var cell in Cells)
        {
            cell.HasShip = false;
            cell.ShipId = null;
            cell.WasShot = false;
            cell.Status = CellStatus.Empty;
        }

        // Если переданы корабли - размещаем их
        if (ships != null && ships.Any())
        {
            Console.WriteLine($"   Размещаем {ships.Count} кораблей от клиента");

            
            Ships.Clear();
            Ships.AddRange(ships);

            RestoreCellShipReferences();
        }
        else if (Ships != null && Ships.Any())
        {
            // Если корабли уже есть в объекте - просто восстанавливаем ссылки
            Console.WriteLine($"   Восстанавливаем {Ships.Count} существующих кораблей");
            RestoreCellShipReferences();
        }
    }

    public Cell GetCell(int x, int y)
    {
        return Cells?.FirstOrDefault(c => c.X == x && c.Y == y);
    }
}