using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;

namespace BattleShip.Server.Services
{
    public class FirebaseService
    {
        public FirebaseService(IConfiguration configuration)
        {
            try
            {
                var credentialPath = "firebase-credentials.json";

                if (File.Exists(credentialPath))
                {
                    FirebaseApp.Create(new AppOptions()
                    {
                        Credential = GoogleCredential.FromFile(credentialPath)
                    });
                    Console.WriteLine("✅ Firebase initialized successfully!");
                }
                else
                {
                    Console.WriteLine("⚠️  Warning: Firebase credentials not found.");
                    Console.WriteLine("   Looking for: " + Path.GetFullPath(credentialPath));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Firebase error: {ex.Message}");
            }
        }

        public async Task<string?> CreateTestUser()
        {
            try
            {
                return "test-user-" + Guid.NewGuid();
            }
            catch
            {
                return null;
            }
        }
    }
}