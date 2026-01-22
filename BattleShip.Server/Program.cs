using BattleShip.Server.Hubs;
using BattleShip.Server.Services;

namespace BattleShip.Server
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Добавляем сервисы
            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();
            builder.Services.AddSignalR();

            // Регистрируем наши сервисы:
            builder.Services.AddSingleton<FirebaseService>();
            builder.Services.AddScoped<GameService>();
            builder.Services.AddSignalR();

            var app = builder.Build();

            // Конфигурация pipeline
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();
            app.UseAuthorization();
            app.MapControllers();
            app.MapHub<ChatHub>("/chatHub");
            app.MapHub<GameHub>("/gameHub");

            app.Run();
        }
    }
}