    using System;
    using System.Globalization;
    using System.Windows;
    using System.Windows.Data;

    namespace BattleShip.Client
    {
        public class BoolToVisibilityConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                return (value is bool boolValue && boolValue) ? Visibility.Visible : Visibility.Collapsed;
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                if (value is Visibility visibility)
                {
                    // Например, для конвертера BoolToVisibility:
                    return visibility == Visibility.Visible;
                }
    
                return DependencyProperty.UnsetValue; // или false
            }
        }
    }