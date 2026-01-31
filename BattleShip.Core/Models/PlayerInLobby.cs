using System.Text.Json.Serialization;

namespace BattleShip.Core.Models
{
    public class PlayerInLobby
    {
        [JsonPropertyName("playerId")]
        public string PlayerId { get; set; }

        [JsonPropertyName("playerName")]
        public string PlayerName { get; set; }

        [JsonPropertyName("board")]
        public Board Board { get; set; }

        [JsonPropertyName("joinedAt")]
        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    }
}