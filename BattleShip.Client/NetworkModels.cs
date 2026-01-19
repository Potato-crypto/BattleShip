using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace BattleShip.Client
{
    // Базовый класс для всех сетевых сообщений
    public class NetworkMessage
    {
        [JsonProperty("type")]
        public string Type { get; set; }
        
        [JsonProperty("data")]
        public object Data { get; set; }
        
        [JsonProperty("timestamp")]
        public DateTime Timestamp { get; set; }
        
        [JsonProperty("gameId")]
        public string GameId { get; set; }
        
        [JsonProperty("playerId")]
        public string PlayerId { get; set; }
    }

    // Сообщение для подключения к игре
    public class ConnectMessage
    {
        [JsonProperty("playerName")]
        public string PlayerName { get; set; }
        
        [JsonProperty("gameMode")]
        public string GameMode { get; set; } // "random", "friend", "computer"
        
        [JsonProperty("roomId")]
        public string RoomId { get; set; }
    }

    // Сообщение о расстановке кораблей
    public class ShipsPlacementMessage
    {
        [JsonProperty("ships")]
        public List<ShipData> Ships { get; set; }
    }

    public class ShipData
    {
        [JsonProperty("size")]
        public int Size { get; set; }
        
        [JsonProperty("cells")]
        public List<CellData> Cells { get; set; }
        
        [JsonProperty("isHorizontal")]
        public bool IsHorizontal { get; set; }
        
        [JsonProperty("hitCells", NullValueHandling = NullValueHandling.Ignore)]
        public List<CellData> HitCells { get; set; } = new List<CellData>();
    }

    public class CellData
    {
        [JsonProperty("row")]
        public int Row { get; set; }
        
        [JsonProperty("col")]
        public int Col { get; set; }
    }

    // Сообщение о выстреле
    public class ShootMessage
    {
        [JsonProperty("row")]
        public int Row { get; set; }
        
        [JsonProperty("col")]
        public int Col { get; set; }
        [JsonProperty("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonProperty("isHit")]
        public bool IsHit { get; set; }
    }

    // Результат выстрела
    public class ShootResultMessage
    {
        [JsonProperty("row")]
        public int Row { get; set; }
        
        [JsonProperty("col")]
        public int Col { get; set; }
        
        [JsonProperty("result")]
        public string Result { get; set; } // "hit", "miss", "sunk", "already_shot", "invalid"
        
        [JsonProperty("shipSize")]
        public int ShipSize { get; set; } // Размер потопленного корабля (если потоплен)
        
        [JsonProperty("nextTurn")]
        public string NextTurn { get; set; } // "player" или "opponent"
        
        [JsonProperty("remainingShips")]
        public int RemainingShips { get; set; }

        public string ShipName { get; set; }
        public string CellStatus { get; set; } 
    }

    // Состояние игры
    public class GameStateMessage
    {
        [JsonProperty("status")]
        public string Status { get; set; } // "waiting", "placing", "playing", "finished"
        
        [JsonProperty("currentTurn")]
        public string CurrentTurn { get; set; }
        
        [JsonProperty("playerScore")]
        public int PlayerScore { get; set; }
        
        [JsonProperty("opponentScore")]
        public int OpponentScore { get; set; }
        
        [JsonProperty("remainingTime")]
        public int RemainingTime { get; set; }

        [JsonProperty("gameStateJson")]
        public string GameStateJson { get; set; }
    }

    // Сообщение о начале игры
    public class GameStartMessage
    {
        [JsonProperty("opponentName")]
        public string OpponentName { get; set; }
        
        [JsonProperty("playerRole")]
        public string PlayerRole { get; set; } // "first" или "second"
        
        [JsonProperty("gameId")]
        public string GameId { get; set; }
    }

    // Сообщение о завершении игры
    public class GameEndMessage
    {
        [JsonProperty("winner")]
        public string Winner { get; set; }
        
        [JsonProperty("reason")]
        public string Reason { get; set; } // "all_ships_sunk", "timeout", "surrender"
        
        [JsonProperty("playerStats")]
        public PlayerStats Stats { get; set; }
    }

    public class PlayerStats
    {
        [JsonProperty("hits")]
        public int Hits { get; set; }
        
        [JsonProperty("misses")]
        public int Misses { get; set; }
        
        [JsonProperty("accuracy")]
        public double Accuracy { get; set; }
        
        [JsonProperty("totalShots")]
        public int TotalShots { get; set; }
    }

    // Сообщение об ошибке
    public class ErrorMessage
    {
        [JsonProperty("code")]
        public string Code { get; set; }
        
        [JsonProperty("message")]
        public string Message { get; set; }
    }

    // Сообщение чата
    public class ChatMessage
    {
        [JsonProperty("sender")]
        public string Sender { get; set; }
        
        [JsonProperty("text")]
        public string Text { get; set; }
        
        [JsonProperty("isSystem")]
        public bool IsSystem { get; set; }
        
    }
}
