using System.Text.Json.Serialization;

namespace BattleShip.Core.Models
{
    public class PlayerSession
    {
        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("playerId")]
        public string PlayerId { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("playerName")]
        public string PlayerName { get; set; }

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("lastActivity")]
        public DateTime LastActivity { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("currentGameId")]
        public string CurrentGameId { get; set; }

        [JsonPropertyName("isInLobby")]
        public bool IsInLobby { get; set; }

        [JsonPropertyName("ships")]
        public List<Ship> Ships { get; set; }
    }

    public class CreateSessionRequest
    {
        public string PlayerName { get; set; }
    }

    public class SessionResponse
    {
        public bool Success { get; set; }
        public string SessionId { get; set; }
        public string PlayerId { get; set; }
        public string Message { get; set; }
    }
}