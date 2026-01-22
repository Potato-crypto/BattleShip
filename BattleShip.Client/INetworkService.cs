using System;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace BattleShip.Client
{
    public interface INetworkService
    {
        // События
        event Action<string> OnMessageReceived;
        event Action<bool> OnConnectionChanged;
        event Action<GameStartMessage> OnGameStarted;
        event Action<GameEndMessage> OnGameEnded;
        event Action<ShootResultMessage> OnShootResult;
        event Action<ShootMessage> OnOpponentShoot;
        event Action<ChatMessage> OnChatMessage;  
        event Action<GameStateMessage> OnGameStateUpdated;
        event Action<ErrorMessage> OnError;
        event Action<string> OnOpponentDisconnected;


        // Статусы
        bool IsConnected { get; }
        bool IsInGame { get; }
        string GameId { get; }
        string PlayerId { get; }
        
        // Методы подключения
        Task ConnectAsync(string playerName);
        Task DisconnectAsync();
        
        // Игровые методы
        Task<string> CreateGameAsync(string gameMode);
        Task<bool> JoinGameAsync(string gameId);
        Task<bool> LeaveGameAsync();
        
        Task<bool> SendShipsPlacementAsync(List<ShipData> ships);
        Task<bool> ShootAsync(int row, int col);
        
        // Чат
        Task SendChatMessageAsync(string message);
        // Удаляем дублирующее событие OnChatMessageReceived
        
        // Статистика
        Task<PlayerStats> GetPlayerStatsAsync();

        public class ChatMessage
        {
            public string Sender { get; set; }
            public string Message { get; set; }
            public bool IsSystem { get; set; }        
            public bool IsFromOpponent { get; set; }  
            public DateTime Timestamp { get; set; }   
        }

    }
}
