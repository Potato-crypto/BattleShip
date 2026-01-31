using BattleShip.Server.Hubs;
using BattleShip.Server.Services;
using BattleShip.Server.Config;
using Microsoft.Extensions.Logging;

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
            builder.Services.AddSignalR();

            // Регистрируем наши сервисы с правильным порядком:
            builder.Services.AddSingleton<FirebaseService>();
            builder.Services.AddScoped<GameService>();

            // Добавляем CORS для клиента WPF
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowAll", builder =>
                {
                    builder.AllowAnyOrigin()
                           .AllowAnyMethod()
                           .AllowAnyHeader();
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
            app.UseCors("AllowAll");
            app.UseAuthorization();

            app.MapControllers();
            app.MapHub<ChatHub>("/chatHub");
            app.MapHub<GameHub>("/gameHub");

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
            Console.WriteLine("💬 SignalR hubs: /chatHub, /gameHub");

            app.Run();
        }
    }
}