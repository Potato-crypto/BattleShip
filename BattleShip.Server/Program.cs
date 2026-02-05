using BattleShip.Server.Hubs;
using BattleShip.Server.Services;
using BattleShip.Server.Config;
using Microsoft.Extensions.Logging;
using Firebase.Database;

namespace BattleShip.Server
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Конфигурация логгирования
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.AddDebug();
            builder.Logging.SetMinimumLevel(LogLevel.Debug);

            // Добавляем сервисы
            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            // Добавляем SignalR с правильными настройками
            builder.Services.AddSignalR(options =>
            {
                options.EnableDetailedErrors = true;
                options.MaximumReceiveMessageSize = 102400; // 100KB
                options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
                options.KeepAliveInterval = TimeSpan.FromSeconds(15);
            });

            // Регистрация ChatHub с логгером
            builder.Services.AddSingleton<ChatHub>();

            //  РЕГИСТРАЦИЯ FirebaseClient 
            builder.Services.AddSingleton<FirebaseClient>(provider =>
            {
                var databaseUrl = FirebaseConfig.DatabaseUrl;
                var logger = provider.GetRequiredService<ILogger<Program>>();

                logger.LogInformation($"🌐 Создаем FirebaseClient для: {databaseUrl}");

                try
                {
                    var client = new FirebaseClient(databaseUrl, new FirebaseOptions
                    {
                        AuthTokenAsyncFactory = () => Task.FromResult<string>(null)
                    });

                    logger.LogInformation("✅ FirebaseClient создан успешно");
                    return client;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "❌ Ошибка создания FirebaseClient");
                    throw;
                }
            });

            // РЕГИСТРАЦИЯ СЕРВИСОВ 
            builder.Services.AddSingleton<FirebaseService>();
            builder.Services.AddSingleton<SessionService>();
            builder.Services.AddScoped<MatchmakingService>();
            builder.Services.AddScoped<GameService>();
            //builder.Services.AddHostedService<HeartbeatService>();

            // Добавляем CORS для клиента WPF (ОБНОВЛЕНО)
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowAll", builder =>
                {
                    builder.WithOrigins(
                            "http://localhost:5214",    // Ваш сервер
                            "https://localhost:5214",   // HTTPS вариант
                            "http://localhost",         // WPF может использовать
                            "https://localhost",        // HTTPS для WPF
                            "http://127.0.0.1:5000",   // Для тестов
                            "https://127.0.0.1:5001")  // Для тестов
                           .AllowAnyMethod()
                           .AllowAnyHeader()
                           .AllowCredentials()
                           .SetIsOriginAllowed(_ => true); // Разрешаем любые origin для разработки
                });
            });

            var app = builder.Build();

            // Конфигурация pipeline
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();

            // 🔥 ВАЖНО: Сначала UseCors
            app.UseCors("AllowAll");

            // 🔥 ДОБАВЛЯЕМ: UseRouting перед UseAuthorization
            app.UseRouting();

            app.UseAuthorization();

            // 🔥 ИЗМЕНЯЕМ: Используем UseEndpoints для правильной маршрутизации
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();

                // 🔥 ВАЖНО: Сначала MapHub для ChatHub, потом для GameHub
                endpoints.MapHub<ChatHub>("/chatHub");
                endpoints.MapHub<GameHub>("/gameHub");

                // Альтернативные пути для SignalR (для совместимости)
                endpoints.MapHub<ChatHub>("/hubs/chat");
                endpoints.MapHub<GameHub>("/hubs/game");
            });

            // Эндпоинт для очистки базы (только для разработки)
            if (app.Environment.IsDevelopment())
            {
                app.MapGet("/admin/clear-db", async (FirebaseService firebase) =>
                {
                    await firebase.ClearDatabaseAsync();
                    return Results.Ok(new { message = "База очищена" });
                });

                app.MapGet("/admin/stats", async (FirebaseService firebase) =>
                {
                    var stats = await firebase.GetDatabaseStats();
                    return Results.Json(stats);
                });
            }

            // Очистка старых игр при запуске
            using (var scope = app.Services.CreateScope())
            {
                var firebaseService = scope.ServiceProvider.GetRequiredService<FirebaseService>();

                // Запускаем очистку в фоне
                _ = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(10)); // Ждем запуск

                    // Очищаем старые игры каждые 30 минут
                    while (true)
                    {
                        try
                        {
                            await firebaseService.CleanupOldGames(1); // Удаляем игры старше 1 часа
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"❌ Ошибка очистки: {ex.Message}");
                        }

                        await Task.Delay(TimeSpan.FromMinutes(30));
                    }
                });
            }

            Console.WriteLine("🚀 BattleShip Server запущен!");
            Console.WriteLine($"📊 База данных: {FirebaseConfig.DatabaseUrl}");
            Console.WriteLine("📡 Swagger доступен по: /swagger");
            Console.WriteLine("💬 SignalR hubs:");
            Console.WriteLine("   - /chatHub");
            Console.WriteLine("   - /gameHub");
            Console.WriteLine("   - /hubs/chat (альтернатива)");
            Console.WriteLine("   - /hubs/game (альтернатива)");
            Console.WriteLine($"🌐 URL приложения: {app.Urls.FirstOrDefault()}");

            app.Run();
        }
    }
}