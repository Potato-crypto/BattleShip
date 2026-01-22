namespace BattleShip.Core.Models
{
    public class ChatMessage
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string GameId { get; set; }
        public string PlayerId { get; set; }
        public string PlayerName { get; set; }
        public string Message { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public bool IsSystemMessage { get; set; }
    }
}