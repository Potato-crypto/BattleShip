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
        Console.WriteLine($"   Клеток до: {Cells?.Count ?? 0}");

        if (Cells == null || Ships == null)
        {
            Console.WriteLine($"❌ Cells или Ships null!");
            return;
        }

        var wasShotCells = new Dictionary<string, bool>();
        foreach (var cell in Cells)
        {
            string key = $"{cell.X},{cell.Y}";
            wasShotCells[key] = cell.WasShot;
        }

        foreach (var cell in Cells)
        {
            cell.HasShip = false;
            cell.ShipId = null;
        }

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

                        string key = $"{x},{y}";
                        if (wasShotCells.ContainsKey(key))
                        {
                            cell.WasShot = wasShotCells[key];
                        }

                        if (cell.WasShot)
                        {
                            cell.Status = cell.HasShip ? CellStatus.Hit : CellStatus.Miss;
                        }

                        Console.WriteLine($"   ✅ Клетка ({x},{y}) → корабль '{ship.Name}', WasShot={cell.WasShot}");
                    }
                }
            }
        }

        int shipCellsCount = Cells.Count(c => c.HasShip);
        int shotCellsCount = Cells.Count(c => c.WasShot);
        Console.WriteLine($"📊 Клеток с кораблями: {shipCellsCount} (должно быть 20)");
        Console.WriteLine($"🎯 Прострелянных клеток: {shotCellsCount}");
    }

    public Cell GetCell(int x, int y)
    {
        return Cells?.FirstOrDefault(c => c.X == x && c.Y == y);
    }
}