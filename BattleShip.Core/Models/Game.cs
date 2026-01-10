namespace BattleShip.Core.Models
{
    public class Game
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Player1Id { get; set; }
        public string Player2Id { get; set; }
        public string Status { get; set; } = "Waiting";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}