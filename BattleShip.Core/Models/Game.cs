using System.Text.Json.Serialization;
using BattleShip.Core.Enums;

namespace BattleShip.Core.Models
{
    public class Game
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("player1Id")]
        public string Player1Id { get; set; }

        [JsonPropertyName("player2Id")]
        public string Player2Id { get; set; }

        [JsonPropertyName("status")]
        public GameStatus Status { get; set; }

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("player1Ready")]
        public bool Player1Ready { get; set; }

        [JsonPropertyName("player2Ready")]
        public bool Player2Ready { get; set; }

        [JsonPropertyName("player1Board")]
        public Board Player1Board { get; set; } = new Board();

        [JsonPropertyName("player2Board")]
        public Board Player2Board { get; set; } = new Board();

        [JsonPropertyName("currentPlayerId")]
        public string CurrentPlayerId { get; set; }

        [JsonPropertyName("turnTimeSeconds")]
        public int TurnTimeSeconds { get; set; } = 30;
    }
}