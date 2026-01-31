using BattleShip.Server.Services;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/test")]
public class TestController : ControllerBase
{
    private readonly FirebaseService _firebaseService;
    private readonly ILogger<TestController> _logger;

    public TestController(FirebaseService firebaseService, ILogger<TestController> logger)
    {
        _firebaseService = firebaseService;
        _logger = logger;
    }

    [HttpGet("simple-firebase")]
    public async Task<IActionResult> SimpleFirebaseTest()
    {
        try
        {
            _logger.LogInformation("🧪 Начинаем простой тест Firebase...");

            // 1. Пробуем просто записать что-то
            var testData = new
            {
                test = "Hello from BattleShip",
                timestamp = DateTime.UtcNow,
                server = "Test API"
            };

            // Используем прямой HTTP запрос к Firebase
            var httpClient = new HttpClient();
            var url = "https://seabattle-new-default-rtdb.asia-southeast1.firebasedatabase.app/test/simple_test.json";

            var response = await httpClient.PutAsJsonAsync(url, testData);

            _logger.LogInformation($"📤 Отправлен запрос к Firebase: {url}");
            _logger.LogInformation($"📥 Ответ: {response.StatusCode}");

            var content = await response.Content.ReadAsStringAsync();
            _logger.LogInformation($"📄 Тело ответа: {content}");

            return Ok(new
            {
                success = response.IsSuccessStatusCode,
                statusCode = response.StatusCode,
                message = response.IsSuccessStatusCode ? "Firebase доступен!" : "Ошибка Firebase",
                response = content
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Ошибка в простом тесте Firebase");
            return StatusCode(500, new
            {
                success = false,
                error = ex.Message,
                stackTrace = ex.StackTrace
            });
        }
    }

    [HttpGet("check-database")]
    public async Task<IActionResult> CheckDatabase()
    {
        try
        {
            var httpClient = new HttpClient();
            var url = "https://seabattle-new-default-rtdb.asia-southeast1.firebasedatabase.app/.json";

            var response = await httpClient.GetAsync(url);

            return Ok(new
            {
                success = response.IsSuccessStatusCode,
                statusCode = response.StatusCode,
                message = response.IsSuccessStatusCode ? "База доступна" : "База недоступна",
                content = await response.Content.ReadAsStringAsync()
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}