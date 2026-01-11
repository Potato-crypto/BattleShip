using FirebaseAdmin;
using Firebase.Database;
using Google.Apis.Auth.OAuth2;
using BattleShip.Core.Models;
using Firebase.Database.Query;

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
                return await _firebaseClient
                    .Child("games")
                    .Child(gameId)
                    .OnceSingleAsync<Game>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading game: {ex.Message}");
                return null;
            }
        }

        public async Task UpdateGameAsync(Game game)
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
                Console.WriteLine($"Error updating game: {ex.Message}");
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