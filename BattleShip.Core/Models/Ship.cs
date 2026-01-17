using System.Text.Json.Serialization;

namespace BattleShip.Core.Models
{
    public class Ship
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("size")]
        public int Size { get; set; }

        [JsonPropertyName("cellCoordinates")]
        public List<string> CellCoordinates { get; set; } = new();


        [JsonPropertyName("hits")]
        public int Hits { get; set; }

        [JsonPropertyName("isSunk")]
        public bool IsSunk { get; set; }
    }
}