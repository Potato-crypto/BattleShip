using System;
using System.Linq;
using System.Windows;

namespace BattleShip.Client.Services
{
    public enum AlertType
    {
        Info,
        Warning,
        Error,
        Success
    }

    public static class AlertService
    {
        // ДОБАВЛЕНО: Флаг для предотвращения повторных алертов об отключении
        private static bool _serverDisconnectAlertShown = false;

        public static void ShowAlert(string title, string message, AlertType type = AlertType.Info)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var activeWindow = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);

                var alertWindow = new AlertWindow(title, message, type);
                alertWindow.Owner = activeWindow;
                alertWindow.ShowDialog();
            });
        }

        public static void ShowNetworkAlert(ErrorMessage error)
        {
            switch (error.Code)
            {
                case "NOT_YOUR_TURN":
                    ShowAlert("Ход противника",
                        "Сейчас ходит ваш противник. Дождитесь своей очереди.",
                        AlertType.Warning);
                    break;

                case "ALREADY_SHOT":
                    ShowAlert("Клетка уже обстреляна",
                        "Вы уже стреляли в эту клетку. Пожалуйста, выберите другую цель.",
                        AlertType.Warning);
                    break;

                case "SERVER_DISCONNECTED":
                    // ДОБАВЛЕНО: Показываем алерт только один раз
                    if (!_serverDisconnectAlertShown)
                    {
                        _serverDisconnectAlertShown = true;
                        ShowAlert("Сервер недоступен",
                            "Соединение с сервером потеряно. Игра будет завершена.",
                            AlertType.Error);
                    }
                    break;

                default:
                    ShowAlert("Ошибка", error.Message, AlertType.Error);
                    break;
            }
        }

        // ДОБАВЛЕНО: Метод для сброса флага (вызывать при новом подключении)
        public static void ResetServerDisconnectFlag()
        {
            _serverDisconnectAlertShown = false;
        }
    }
}