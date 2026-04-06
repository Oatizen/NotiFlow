using System.Windows.Controls;

namespace NotiFlow.Views.Pages
{
    public partial class CustomPage : Page
    {
        public CustomPage()
        {
            InitializeComponent();
        }

        private void ToggleWorkButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (ToggleWorkButton.Content.ToString() == "开启")
            {
                ToggleWorkButton.Content = "工作中";
                ToggleWorkButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
            }
            else
            {
                ToggleWorkButton.Content = "开启";
                ToggleWorkButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
            }
        }

        private void HelpButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            HelpFlyout.Show();
        }

        private void FontWeightNormalBtn_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            FontWeightNormalBtn.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
            FontWeightBoldBtn.Appearance = Wpf.Ui.Controls.ControlAppearance.Transparent;
        }

        private void FontWeightBoldBtn_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            FontWeightNormalBtn.Appearance = Wpf.Ui.Controls.ControlAppearance.Transparent;
            FontWeightBoldBtn.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
        }

        private void FontStyleItalicBtn_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            FontStyleItalicBtn.Appearance = FontStyleItalicBtn.Appearance == Wpf.Ui.Controls.ControlAppearance.Primary
                ? Wpf.Ui.Controls.ControlAppearance.Transparent
                : Wpf.Ui.Controls.ControlAppearance.Primary;
        }

        private void FontStyleUnderlineBtn_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            FontStyleUnderlineBtn.Appearance = FontStyleUnderlineBtn.Appearance == Wpf.Ui.Controls.ControlAppearance.Primary
                ? Wpf.Ui.Controls.ControlAppearance.Transparent
                : Wpf.Ui.Controls.ControlAppearance.Primary;
        }

        private void SpeedSlider_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            if (SpeedValueText != null)
            {
                int speed = (int)e.NewValue;
                string label = "中";
                if (speed < 10) label = "慢";
                else if (speed >= 20) label = "快";
                
                SpeedValueText.Text = $"{label} ({speed} 字/秒)";
                BarrageSettings.ScrollSpeedCharsPerSec = speed;
            }
        }

        private void FontSizeSlider_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            if (FontSizeText != null)
            {
                int size = (int)e.NewValue;
                FontSizeText.Text = $"{size}px";
                BarrageSettings.FontSize = size;
            }
        }

        private void OpacitySlider_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            if (OpacityText != null)
            {
                int opacity = (int)e.NewValue;
                OpacityText.Text = $"{opacity}%";
                BarrageSettings.TextOpacity = opacity / 100.0;
            }
        }

        private void BackgroundOpacitySlider_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            if (BackgroundOpacityText != null)
            {
                int opacity = (int)e.NewValue;
                BackgroundOpacityText.Text = $"{opacity}%";
                BarrageSettings.BackgroundOpacity = opacity / 100.0;
            }
        }
    }
}
