using FirebaseAdmin;
using Firebase.Database;
using Google.Apis.Auth.OAuth2;
using BattleShip.Core.Models;
using Firebase.Database.Query;
using BattleShip.Core.Enums;

namespace BattleShip.Server.Services
{
    public class FirebaseService
    {
        private readonly FirebaseClient _firebaseClient;

        public FirebaseService(IConfiguration configuration)
        {
            try
            {
                // Инициализация Firebase App
                var credentialPath = "firebase-credentials.json";
                if (File.Exists(credentialPath))
                {
                    FirebaseApp.Create(new AppOptions()
                    {
                        Credential = GoogleCredential.FromFile(credentialPath)
                    });
                }

                // Подключение к Realtime Database
                var databaseUrl = "https://seabattle-b40a8-default-rtdb.firebaseio.com/";
                var databaseSecret = "ibfe1pDabdUtRaA6gfdRqDNZut3fgi3N8BrYv417"; 

                // Создаём клиент
                _firebaseClient = new FirebaseClient(databaseUrl);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Firebase error: {ex.Message}");
                _firebaseClient = null;
            }
        }

        public async Task<List<Game>> FindWaitingGamesAsync()
        {
            if (_firebaseClient == null) return new List<Game>();

            try
            {
                // Получаем все игры
                var games = await _firebaseClient
                    .Child("games")
                    .OnceAsync<Game>();

                // Фильтруем игры в ожидании
                var waitingGames = new List<Game>();

                foreach (var game in games)
                {
                    if (game.Object.Status == GameStatus.WaitingForPlayer &&
                        string.IsNullOrEmpty(game.Object.Player2Id))
                    {
                        waitingGames.Add(game.Object);
                        Console.WriteLine($"🔍 Найдена ожидающая игра: {game.Key}");
                    }
                }

                return waitingGames;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка поиска игр: {ex.Message}");
                return new List<Game>();
            }
        }

        public async Task SaveGameAsync(Game game)
        {
            if (_firebaseClient == null) return;

            try
            {
                await _firebaseClient
                    .Child("games")
                    .Child(game.Id)
                    .PutAsync(game);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving game: {ex.Message}");
            }
        }

        public async Task<Game> GetGameAsync(string gameId)
        {
            if (_firebaseClient == null) return null;

            try
            {
                var game = await _firebaseClient
                    .Child("games")
                    .Child(gameId)
                    .OnceSingleAsync<Game>();

                if (game != null)
                {
                    Console.WriteLine($"🔄 Игра загружена из Firebase: {game.Id}");

                    
                    if (game.Player1Board != null)
                    {
                        Console.WriteLine($"   Player1Board до восстановления:");
                        Console.WriteLine($"     Кораблей: {game.Player1Board.Ships?.Count ?? 0}");
                        Console.WriteLine($"     Клеток с HasShip: {game.Player1Board.Cells?.Count(c => c.HasShip) ?? 0}");

                        game.Player1Board.RestoreCellShipReferences();

                        Console.WriteLine($"   Player1Board после восстановления:");
                        Console.WriteLine($"     Клеток с HasShip: {game.Player1Board.Cells?.Count(c => c.HasShip) ?? 0}");
                    }

                    if (game.Player2Board != null)
                    {
                        Console.WriteLine($"   Player2Board до восстановления:");
                        Console.WriteLine($"     Кораблей: {game.Player2Board.Ships?.Count ?? 0}");
                        Console.WriteLine($"     Клеток с HasShip: {game.Player2Board.Cells?.Count(c => c.HasShip) ?? 0}");

                        game.Player2Board.RestoreCellShipReferences();

                        Console.WriteLine($"   Player2Board после восстановления:");
                        Console.WriteLine($"     Клеток с HasShip: {game.Player2Board.Cells?.Count(c => c.HasShip) ?? 0}");
                    }
                }

                return game;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error loading game: {ex.Message}");
                return null;
            }
        }

        public async Task UpdateGameAsync(Game game)
        {
            if (_firebaseClient == null) return;

            try
            {
                Console.WriteLine($"💾 Сохранение игры в Firebase...");
                Console.WriteLine($"   GameId: {game.Id}");
                Console.WriteLine($"   Player1 кораблей: {game.Player1Board?.Ships?.Count ?? 0}");
                Console.WriteLine($"   Player2 кораблей: {game.Player2Board?.Ships?.Count ?? 0}");

                // Проверяем клетки
                var player1ShipCells = game.Player1Board?.Cells?.Count(c => c.HasShip) ?? 0;
                var player2ShipCells = game.Player2Board?.Cells?.Count(c => c.HasShip) ?? 0;
                Console.WriteLine($"   Player1 клеток с кораблями: {player1ShipCells}");
                Console.WriteLine($"   Player2 клеток с кораблями: {player2ShipCells}");

                await _firebaseClient
                    .Child("games")
                    .Child(game.Id)
                    .PutAsync(game);

                Console.WriteLine($"✅ Игра сохранена в Firebase");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка сохранения игры: {ex.Message}");
            }
        }

        public async Task UpdateBoardAsync(string gameId, string playerNumber, Board board)
        {
            if (_firebaseClient == null) return;

            try
            {
                // Обновляем только доску, а не всю игру
                await _firebaseClient
                    .Child("games")
                    .Child(gameId)
                    .Child($"player{playerNumber}Board")
                    .PutAsync(board);

                Console.WriteLine($"✅ Доска игрока {playerNumber} обновлена в Firebase");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка обновления доски: {ex.Message}");
            }
        }

        public async Task SaveChatMessageAsync(string gameId, ChatMessage message)
        {
            if (_firebaseClient == null) return;

            try
            {
                await _firebaseClient
                    .Child("games")
                    .Child(gameId)
                    .Child("chatMessages")
                    .PostAsync(message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving chat message: {ex.Message}");
            }
        }

        public async Task<List<ChatMessage>> GetChatHistoryAsync(string gameId)
        {
            if (_firebaseClient == null) return new List<ChatMessage>();

            try
            {
                var messages = await _firebaseClient
                    .Child("games")
                    .Child(gameId)
                    .Child("chatMessages")
                    .OnceAsync<ChatMessage>();

                return messages.Select(m => m.Object)
                    .OrderBy(m => m.Timestamp)
                    .ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading chat history: {ex.Message}");
                return new List<ChatMessage>();
            }
        }

        public async Task SaveShotAsync(string gameId, string playerId, int x, int y, bool isHit)
        {
            if (_firebaseClient == null) return;

            try
            {
                var shot = new
                {
                    PlayerId = playerId,
                    X = x,
                    Y = y,
                    IsHit = isHit,
                    Timestamp = DateTime.UtcNow
                };

                await _firebaseClient
                    .Child("games")
                    .Child(gameId)
                    .Child("shots")
                    .PostAsync(shot);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving shot: {ex.Message}");
            }
        }

        public async Task<string> CreateTestUser()
        {
            return "test-user-" + Guid.NewGuid();
        }
    }
}