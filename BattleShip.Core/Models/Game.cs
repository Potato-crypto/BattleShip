// В Game.cs добавьте:
using BattleShip.Core.Enums;
using System.Text.Json.Serialization;

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

        [JsonPropertyName("player1Name")]
        public string Player1Name { get; set; }

        [JsonPropertyName("player2Name")]
        public string Player2Name { get; set; }

        [JsonPropertyName("status")]
        public GameStatus Status { get; set; }

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("updatedAt")]
        public DateTime? UpdatedAt { get; set; }

        [JsonPropertyName("endedAt")]
        public DateTime? EndedAt { get; set; }

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

        [JsonPropertyName("winnerId")]
        public string WinnerId { get; set; }

        [JsonPropertyName("loserId")]
        public string LoserId { get; set; }

        [JsonPropertyName("surrender")]
        public bool Surrender { get; set; }

        [JsonPropertyName("opponentDisconnected")]
        public bool OpponentDisconnected { get; set; }

        [JsonPropertyName("lastTurnTime")]
        public DateTime? LastTurnTime { get; set; }

    }
}