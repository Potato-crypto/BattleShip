// FirebaseConfig.cs
namespace BattleShip.Server.Config
{
    public static class FirebaseConfig
    {
        public static string DatabaseUrl => "https://seabattle-new-default-rtdb.asia-southeast1.firebasedatabase.app/";
    
        public static bool ClearDatabaseOnStartup = false;
    }
}