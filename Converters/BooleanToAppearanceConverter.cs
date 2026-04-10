using System;
using System.Globalization;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace NotiFlow.Converters
{
    public class BooleanToAppearanceConverter : IValueConverter
    {
        public ControlAppearance TrueAppearance { get; set; } = ControlAppearance.Primary;
        public ControlAppearance FalseAppearance { get; set; } = ControlAppearance.Transparent;
        public bool Invert { get; set; } = false;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                bool finalVal = Invert ? !boolValue : boolValue;
                return finalVal ? TrueAppearance : FalseAppearance;
            }
            return FalseAppearance;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
