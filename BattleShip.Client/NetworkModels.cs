using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BattleShip.Client
{
    // Базовый класс для всех сетевых сообщений
    public class NetworkMessage
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }
        
        [JsonPropertyName("data")]
        public object Data { get; set; }
        
        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }
        
        [JsonPropertyName("gameId")]
        public string GameId { get; set; }
        
        [JsonPropertyName("playerId")]
        public string PlayerId { get; set; }
    }

    // Сообщение для подключения к игре
    public class ConnectMessage
    {
        [JsonPropertyName("playerName")]
        public string PlayerName { get; set; }
        
        [JsonPropertyName("gameMode")]
        public string GameMode { get; set; } // "random", "friend", "computer"
        
        [JsonPropertyName("roomId")]
        public string RoomId { get; set; }
    }

    // Сообщение о расстановке кораблей
    public class ShipsPlacementMessage
    {
        [JsonPropertyName("ships")]
        public List<ShipData> Ships { get; set; }
    }

    public class ShipData
    {
        [JsonPropertyName("size")]
        public int Size { get; set; }

        [JsonPropertyName("cells")]
        public List<CellData> Cells { get; set; }

        [JsonPropertyName("isHorizontal")]
        public bool IsHorizontal { get; set; }

        [JsonPropertyName("hitCells")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] 
        public List<CellData> HitCells { get; set; } = new List<CellData>();
    }

    public class CellData
    {
        [JsonPropertyName("row")]
        public int Row { get; set; }
        
        [JsonPropertyName("col")]
        public int Col { get; set; }
    }

    // Сообщение о выстреле
    public class ShootMessage
    {
        [JsonPropertyName("row")]
        public int Row { get; set; }
        
        [JsonPropertyName("col")]
        public int Col { get; set; }
        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonPropertyName("isHit")]
        public bool IsHit { get; set; }
    }

    // Результат выстрела
    public class ShootResultMessage
    {
        [JsonPropertyName("isGameOver")]
        public bool IsGameOver { get; set; }

        [JsonPropertyName("continueTurn")]
        public bool ContinueTurn { get; set; }

        [JsonPropertyName("gameStatus")]
        public string GameStatus { get; set; }

        [JsonPropertyName("currentPlayerId")]
        public string CurrentPlayerId { get; set; }

        [JsonPropertyName("row")]
        public int Row { get; set; }
        
        [JsonPropertyName("col")]
        public int Col { get; set; }
        
        [JsonPropertyName("result")]
        public string Result { get; set; } // "hit", "miss", "sunk", "already_shot", "invalid"
        
        [JsonPropertyName("shipSize")]
        public int ShipSize { get; set; } // Размер потопленного корабля (если потоплен)
        
        [JsonPropertyName("nextTurn")]
        public string NextTurn { get; set; } // "player" или "opponent"
        
        [JsonPropertyName("remainingShips")]
        public int RemainingShips { get; set; }

        public string Message { get; set; }

        public string ShipName { get; set; }
        public string CellStatus { get; set; } 
    }

    // Состояние игры
    public class GameStateMessage
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } // "waiting", "placing", "playing", "finished"
        
        [JsonPropertyName("currentTurn")]
        public string CurrentTurn { get; set; }
        
        [JsonPropertyName("playerScore")]
        public int PlayerScore { get; set; }
        
        [JsonPropertyName("opponentScore")]
        public int OpponentScore { get; set; }
        
        [JsonPropertyName("remainingTime")]
        public int RemainingTime { get; set; }

        [JsonPropertyName("gameStateJson")]
        public string GameStateJson { get; set; }
    }

    // Сообщение о начале игры
    public class GameStartMessage
    {
        [JsonPropertyName("opponentName")]
        public string OpponentName { get; set; }
        
        [JsonPropertyName("playerRole")]
        public string PlayerRole { get; set; } // "first" или "second"
        
        [JsonPropertyName("gameId")]
        public string GameId { get; set; }
    }

    // Сообщение о завершении игры
    public class GameEndMessage
    {
        [JsonPropertyName("winner")]
        public string Winner { get; set; }
        
        [JsonPropertyName("reason")]
        public string Reason { get; set; } // "all_ships_sunk", "timeout", "surrender"
        
        [JsonPropertyName("playerStats")]
        public PlayerStats Stats { get; set; }
    }

    public class PlayerStats
    {
        [JsonPropertyName("hits")]
        public int Hits { get; set; }
        
        [JsonPropertyName("misses")]
        public int Misses { get; set; }
        
        [JsonPropertyName("accuracy")]
        public double Accuracy { get; set; }
        
        [JsonPropertyName("totalShots")]
        public int TotalShots { get; set; }
    }

    // Сообщение об ошибке
    public class ErrorMessage
    {
        [JsonPropertyName("code")]
        public string Code { get; set; }
        
        [JsonPropertyName("message")]
        public string Message { get; set; }
    }

    // Сообщение чата
    public class ClientChatMessage
    {
        [JsonPropertyName("sender")]
        public string Sender { get; set; }

        [JsonPropertyName("text")]
        public string Text { get; set; }

        [JsonPropertyName("isSystem")]
        public bool IsSystem { get; set; }
    }

    // Модели для нового сервера
    public class CreateSessionRequest
    {
        public string PlayerName { get; set; }
    }

    public class CreateSessionResponse
    {
        public bool Success { get; set; }
        public string SessionId { get; set; }
        public string PlayerId { get; set; }
        public string Message { get; set; }
    }

    public class ReadyForMatchmakingRequest
    {
        public string SessionId { get; set; }
        public string PlayerId { get; set; }
        public string PlayerName { get; set; }
        public List<BattleShip.Core.Models.Ship> Ships { get; set; }
    }

    public class ReadyForMatchmakingResponse
    {
        public bool Success { get; set; }
        public bool GameFound { get; set; }
        public bool InLobby { get; set; }
        public string GameId { get; set; }
        public string PlayerId { get; set; }
        public string OpponentName { get; set; }
        public string GameStatus { get; set; }
        public bool IsMyTurn { get; set; }
        public string Message { get; set; }
    }

    public class ReadyToStartRequest
    {
        public string SessionId { get; set; }
        public string PlayerId { get; set; }
        public string PlayerName { get; set; }
    }

    public class ReadyToStartResponse
    {
        public bool Success { get; set; }
        public string GameStatus { get; set; }
        public bool IsGameStarted { get; set; }
        public string CurrentPlayerId { get; set; }
        public bool Player1Ready { get; set; }
        public bool Player2Ready { get; set; }
        public string Message { get; set; }
    }

    public class FireRequest
    {
        public string PlayerId { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
    }

    public class FireResponse
    {
        public bool Success { get; set; }
        public bool IsHit { get; set; }
        public bool IsShipSunk { get; set; }
        public string ShipName { get; set; }
        public int ShipSize { get; set; }
        public bool IsGameOver { get; set; }
        public string GameStatus { get; set; }
        public string CurrentPlayerId { get; set; }
        public bool ContinueTurn { get; set; }
        public string Message { get; set; }
    }

    public class GameStatusResponse
    {
        public string GameId { get; set; }
        public string GameStatus { get; set; }
        public bool IsMyTurn { get; set; }
        public bool IsGameOver { get; set; }
        public string WinnerId { get; set; }
        public string PlayerId { get; set; }
        public string OpponentName { get; set; }
        public bool OpponentLeft { get; set; }
        public int MyBoardShipsRemaining { get; set; }
        public int OpponentBoardShipsRemaining { get; set; }
        public string Message { get; set; }
    }

    public class SurrenderRequest
    {
        public string PlayerId { get; set; }
    }

    public class BoardResponse
    {
        [JsonPropertyName("myBoard")]
        public MyBoard MyBoard { get; set; }

        [JsonPropertyName("opponentBoard")]
        public OpponentBoard OpponentBoard { get; set; }
    }

    public class MyBoard
    {
        [JsonPropertyName("cells")]
        public List<BoardCell> Cells { get; set; }

        [JsonPropertyName("ships")]
        public List<BoardShip> Ships { get; set; }
    }

    public class OpponentBoard
    {
        [JsonPropertyName("cells")]
        public List<BoardCell> Cells { get; set; }

        [JsonPropertyName("shipsSunk")]
        public int ShipsSunk { get; set; }

        [JsonPropertyName("shipsRemaining")]
        public int ShipsRemaining { get; set; }
    }

    public class BoardCell
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
        [JsonConverter(typeof(StatusConverter))] // Добавьте конвертер
        public string Status { get; set; }

        [JsonPropertyName("isSunk")]
        public bool IsSunk { get; set; }
    }

    // Конвертер для статуса
    public class StatusConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number)
            {
                var number = reader.GetInt32();
                return number switch
                {
                    0 => "Empty",
                    2 => "Hit",
                    3 => "Miss",
                    4 => "Sunk",
                    _ => "Empty"
                };
            }
            else if (reader.TokenType == JsonTokenType.String)
            {
                return reader.GetString();
            }

            return "Empty";
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value);
        }
    }

    public class BoardShip
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("size")]
        public int Size { get; set; }

        [JsonPropertyName("isSunk")]
        public bool IsSunk { get; set; }

        [JsonPropertyName("hits")]
        public int Hits { get; set; }

        [JsonPropertyName("remaining")]
        public int Remaining { get; set; }
    }
}
