namespace BattleShip.Core.Enums
{
    public enum CellStatus
    {
        Empty,   // Пустая клетка
        Ship,    // Корабль (не повреждён)
        Hit,     // Попадание
        Miss,    // Промах
        Sunk     // Корабль потоплен
    }
}