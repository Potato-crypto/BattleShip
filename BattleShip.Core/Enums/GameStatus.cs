namespace BattleShip.Core.Enums
{
    public enum GameStatus
    {
        WaitingForPlayer,   // Ожидание второго игрока
        PlacingShips,       // Оба игрока присоединились, расставляют корабли
        Player1Ready,       // Player1 расставил корабли
        Player2Ready,       // Player2 расставил корабли  
        Player1Turn,        // Оба готовы, игра началась
        Player2Turn,
        Player1Won,
        Player2Won,
        Draw,
        Abandoned
    }
}