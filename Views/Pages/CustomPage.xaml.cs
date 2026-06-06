using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Messaging;
using NotiFlow.Models;

namespace NotiFlow.Views.Pages
{
    public partial class CustomPage : Page
    {
        private TimeSpan _lastRenderTime = TimeSpan.Zero;
        private readonly SolidColorBrush _whiteBrush;

        public CustomPage()
        {
            InitializeComponent();
            _whiteBrush = new SolidColorBrush(Colors.White);
            _whiteBrush.Freeze();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateWorkButtonState(); // 刚进入页面时先校准一次当前实际状态
            
            WeakReferenceMessenger.Default.Register<BarragePreviewMessage>(this, (recipient, message) =>
            {
                SpawnPreviewBarrage();
            });

            if (PreviewBorder.ActualWidth > 0)
            {
                SpawnPreviewBarrage();
            }
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            WeakReferenceMessenger.Default.Unregister<BarragePreviewMessage>(this);
        }

        private void SpawnPreviewBarrage()
        {
            PreviewCanvas.Children.Clear();

            var textBlock = new TextBlock
            {
                Text = "图标 应用名称：这是一条测试弹幕......",
                Foreground = BarrageSettings.TextColor,
                FontSize = BarrageSettings.FontSize,
                FontFamily = BarrageSettings.FontFamily,
                FontStyle = BarrageSettings.FontStyle,
                FontWeight = BarrageSettings.FontWeight,
                VerticalAlignment = VerticalAlignment.Center
            };

            var bgBrush = BarrageSettings.BackgroundColor as SolidColorBrush ?? new SolidColorBrush(System.Windows.Media.Colors.Black);
            var border = new Border
            {
                Background = BarrageSettings.ShowBackground ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(
                    (byte)(bgBrush.Color.A * BarrageSettings.BackgroundOpacity),
                    bgBrush.Color.R, bgBrush.Color.G, bgBrush.Color.B)) : Brushes.Transparent,
                CornerRadius = BarrageSettings.BackgroundCornerRadius,
                Padding = new Thickness(12, 6, 12, 6),
                Child = textBlock
            };

            Canvas.SetLeft(border, 20);
            Canvas.SetTop(border, Math.Max(0, (200 - BarrageSettings.FontSize * 1.5) / 2.0));
            PreviewCanvas.Children.Add(border);
        }

        private void ToggleWorkButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            // 在内存中取反实际渲染开关，并更新所有与之相连的状态组件
            BarrageSettings.IsWorking = !BarrageSettings.IsWorking;
            UpdateWorkButtonState();

            // 同步刷新托盘图标菜单状态与主窗口可见性
            if (Application.Current is App app)
            {
                app.RefreshTrayState();
            }
        }

        private void UpdateWorkButtonState()
        {
            if (BarrageSettings.IsWorking)
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

        private void ColorPickerButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            ColorPaletteFlyout.Show();
        }

        /// <summary>
        /// 保留对齐相关算法，该逻辑纯属 View 视图视觉调整，不应进入 ViewModel
        /// </summary>
        private void Page_SizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
        {
            if (SettingsGrid == null || LeftSettingsCard == null || RightSettingsCard == null ||
                BottomSettingsGrid == null || AnimationCard == null || OtherCard == null) return;

            if (e.NewSize.Width < 700)
            {
                // Single column layout
                SettingsGrid.ColumnDefinitions.Clear();
                SettingsGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });

                SettingsGrid.RowDefinitions.Clear();
                SettingsGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
                SettingsGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });

                System.Windows.Controls.Grid.SetColumn(LeftSettingsCard, 0);
                System.Windows.Controls.Grid.SetRow(LeftSettingsCard, 0);

                System.Windows.Controls.Grid.SetColumn(RightSettingsCard, 0);
                System.Windows.Controls.Grid.SetRow(RightSettingsCard, 1);
                RightSettingsCard.Margin = new System.Windows.Thickness(0, 24, 0, 0);

                // BottomSettingsGrid Single column layout
                BottomSettingsGrid.ColumnDefinitions.Clear();
                BottomSettingsGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });

                BottomSettingsGrid.RowDefinitions.Clear();
                BottomSettingsGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
                BottomSettingsGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });

                System.Windows.Controls.Grid.SetColumn(AnimationCard, 0);
                System.Windows.Controls.Grid.SetRow(AnimationCard, 0);

                System.Windows.Controls.Grid.SetColumn(OtherCard, 0);
                System.Windows.Controls.Grid.SetRow(OtherCard, 1);
                OtherCard.Margin = new System.Windows.Thickness(0, 24, 0, 0);
            }
            else
            {
                // Two columns layout
                SettingsGrid.ColumnDefinitions.Clear();
                SettingsGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
                SettingsGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(16) });
                SettingsGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });

                SettingsGrid.RowDefinitions.Clear();

                System.Windows.Controls.Grid.SetColumn(LeftSettingsCard, 0);
                System.Windows.Controls.Grid.SetRow(LeftSettingsCard, 0);

                System.Windows.Controls.Grid.SetColumn(RightSettingsCard, 2);
                System.Windows.Controls.Grid.SetRow(RightSettingsCard, 0);
                RightSettingsCard.Margin = new System.Windows.Thickness(0);

                // BottomSettingsGrid Two columns layout
                BottomSettingsGrid.ColumnDefinitions.Clear();
                BottomSettingsGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
                BottomSettingsGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(16) });
                BottomSettingsGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });

                BottomSettingsGrid.RowDefinitions.Clear();

                System.Windows.Controls.Grid.SetColumn(AnimationCard, 0);
                System.Windows.Controls.Grid.SetRow(AnimationCard, 0);

                System.Windows.Controls.Grid.SetColumn(OtherCard, 2);
                System.Windows.Controls.Grid.SetRow(OtherCard, 0);
                OtherCard.Margin = new System.Windows.Thickness(0);
            }
        }
    }
}
