using System.Text.Json.Serialization;
using BattleShip.Core.Enums;

namespace BattleShip.Core.Models
{
    public class Cell
    {
        [JsonPropertyName("x")]
        public int X { get; set; }

        [JsonPropertyName("y")]
        public int Y { get; set; }

        [JsonPropertyName("hasShip")]
        public bool HasShip { get; set; }

        [JsonPropertyName("wasShot")]
        public bool WasShot { get; set; }

        [JsonPropertyName("status")]
        public CellStatus Status { get; set; } = CellStatus.Empty;

        [JsonPropertyName("shipId")]
        public string ShipId { get; set; }
    }
}