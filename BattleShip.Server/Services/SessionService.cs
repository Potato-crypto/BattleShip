using BattleShip.Core.Models;
using Firebase.Database;
using Firebase.Database.Query;
using System.Collections.Concurrent;

namespace BattleShip.Server.Services
{
    public class SessionService
    {
        private readonly FirebaseClient _firebaseClient;
        private readonly ILogger<SessionService> _logger;

        // Кэш сессий в памяти для быстрого доступа
        private readonly ConcurrentDictionary<string, PlayerSession> _sessionsCache
            = new ConcurrentDictionary<string, PlayerSession>();

        public SessionService(FirebaseClient firebaseClient, ILogger<SessionService> logger)
        {
            _firebaseClient = firebaseClient;
            _logger = logger;
        }

        public async Task<PlayerSession> CreateSessionAsync(string playerName)
        {
            var session = new PlayerSession
            {
                SessionId = Guid.NewGuid().ToString(),
                PlayerId = $"player-{Guid.NewGuid():N}",
                PlayerName = playerName,
                CreatedAt = DateTime.UtcNow,
                LastActivity = DateTime.UtcNow,
                IsInLobby = false
            };

            // Сохраняем в Firebase
            await _firebaseClient
                .Child("sessions")
                .Child(session.SessionId)
                .PutAsync(session);

            // Сохраняем в кэш
            _sessionsCache[session.SessionId] = session;

            _logger.LogInformation($"✅ Создана сессия: {session.SessionId} для игрока {playerName}");

            return session;
        }

        public async Task<PlayerSession> GetSessionAsync(string sessionId)
        {
            // Проверяем кэш
            if (_sessionsCache.TryGetValue(sessionId, out var cachedSession))
            {
                // Проверяем актуальность (не старше 1 минуты)
                if (DateTime.UtcNow - cachedSession.LastActivity < TimeSpan.FromMinutes(1))
                {
                    return cachedSession;
                }
            }

            // Загружаем из Firebase
            try
            {
                var session = await _firebaseClient
                    .Child("sessions")
                    .Child(sessionId)
                    .OnceSingleAsync<PlayerSession>();

                if (session != null)
                {
                    _sessionsCache[sessionId] = session;
                    return session;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Ошибка загрузки сессии {sessionId}");
            }

            return null;
        }

        public async Task<bool> ValidateSessionAsync(string sessionId, string playerId)
        {
            var session = await GetSessionAsync(sessionId);
            if (session == null)
            {
                _logger.LogWarning($"⚠️ Сессия не найдена: {sessionId}");
                return false;
            }

            if (session.PlayerId != playerId)
            {
                _logger.LogWarning($"⚠️ Несоответствие PlayerId в сессии: {sessionId}");
                return false;
            }

            // Обновляем время активности
            session.LastActivity = DateTime.UtcNow;
            await UpdateSessionAsync(session);

            return true;
        }

        public async Task UpdateSessionAsync(PlayerSession session)
        {
            try
            {
                // Обновляем в Firebase
                await _firebaseClient
                    .Child("sessions")
                    .Child(session.SessionId)
                    .PutAsync(session);

                // Обновляем кэш
                _sessionsCache[session.SessionId] = session;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Ошибка обновления сессии {session.SessionId}");
            }
        }

        public async Task DeleteSessionAsync(string sessionId)
        {
            try
            {
                await _firebaseClient
                    .Child("sessions")
                    .Child(sessionId)
                    .DeleteAsync();

                _sessionsCache.TryRemove(sessionId, out _);
                _logger.LogInformation($"🗑️ Сессия удалена: {sessionId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Ошибка удаления сессии {sessionId}");
            }
        }

        public async Task<List<PlayerSession>> GetActiveSessionsAsync(int minutesThreshold = 5)
        {
            try
            {
                var allSessions = await _firebaseClient
                    .Child("sessions")
                    .OnceAsync<PlayerSession>();

                var threshold = DateTime.UtcNow.AddMinutes(-minutesThreshold);

                return allSessions
                    .Where(s => s.Object.LastActivity > threshold)
                    .Select(s => s.Object)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Ошибка получения активных сессий");
                return new List<PlayerSession>();
            }
        }

        public async Task<PlayerSession> GetSessionByPlayerIdAsync(string playerId)
        {
            try
            {
                var sessions = await _firebaseClient
                    .Child("sessions")
                    .OnceAsync<PlayerSession>();

                return sessions
                    .Select(s => s.Object)
                    .FirstOrDefault(s => s.PlayerId == playerId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Ошибка поиска сессии по PlayerId {playerId}");
                return null;
            }
        }
    }
}