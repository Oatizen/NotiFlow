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
        private BarrageItem? _previewItem;
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
            CompositionTarget.Rendering += CompositionTarget_Rendering;
            
            WeakReferenceMessenger.Default.Register<BarragePreviewMessage>(this, (recipient, message) =>
            {
                SpawnPreviewBarrage();
            });

            // 初始生成一条
            if (PreviewBorder.ActualWidth > 0)
            {
                SpawnPreviewBarrage();
            }
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            CompositionTarget.Rendering -= CompositionTarget_Rendering;
            WeakReferenceMessenger.Default.Unregister<BarragePreviewMessage>(this);
            
            if (_previewItem != null)
            {
                LocalPreviewHost.RemoveVisual(_previewItem);
                _previewItem = null;
            }
        }

        private void SpawnPreviewBarrage()
        {
            if (_previewItem != null)
            {
                LocalPreviewHost.RemoveVisual(_previewItem);
            }

            _previewItem = new BarrageItem();
            LocalPreviewHost.AddVisual(_previewItem);

            var msg = new NotificationMessage
            {
                AppName = "图标 应用名称",
                Title = "",
                Body = "这是一条测试弹幕......"
            };

            // 测试弹幕颜色写死白色，真实中它是个随机数组
            Brush textBrush = _whiteBrush;

            _previewItem.BuildVisual(msg, textBrush, BarrageSettings.FontSize, BarrageSettings.FontFamily, BarrageSettings.FontStyle, BarrageSettings.FontWeight);

            // 从 Border 最右边缘出发
            double targetWidth = PreviewBorder.ActualWidth > 0 ? PreviewBorder.ActualWidth : 800;
            _previewItem.CurrentX = targetWidth;
            
            // 垂直居中测算 (Border 固定高 200)
            double contentHeight = Math.Max(BarrageSettings.FontSize, BarrageSettings.FontSize * 1.25) + 12;
            _previewItem.CurrentY = Math.Max(0, (200 - contentHeight) / 2.0);
            
            _previewItem.Offset = new Vector(_previewItem.CurrentX, _previewItem.CurrentY);

            double speed = BarrageSettings.ScrollSpeedCharsPerSec * BarrageSettings.FontSize;
            if (speed < 10) speed = 10;
            _previewItem.SpeedPixelsPerSec = speed;
        }

        private void CompositionTarget_Rendering(object? sender, EventArgs e)
        {
            if (_previewItem == null) 
            {
                // 如果实际宽度刚加载出来，可以利用回调补充第一条！
                if (PreviewBorder.ActualWidth > 0) SpawnPreviewBarrage();
                return;
            }
            
            var renderingArgs = (RenderingEventArgs)e;
            if (_lastRenderTime == renderingArgs.RenderingTime) return;

            double dt = (_lastRenderTime == TimeSpan.Zero) ? 0 : (renderingArgs.RenderingTime - _lastRenderTime).TotalSeconds;
            _lastRenderTime = renderingArgs.RenderingTime;

            if (dt == 0) return;

            _previewItem.CurrentX -= _previewItem.SpeedPixelsPerSec * dt;
            _previewItem.Offset = new Vector(_previewItem.CurrentX, _previewItem.CurrentY);

            // 出界循环核心：飘出了局部画布的最左边时，立刻将其瞬移回最右边，形成永动
            if (_previewItem.CurrentX < -_previewItem.PhysicalWidth)
            {
                _previewItem.CurrentX = PreviewBorder.ActualWidth;
            }
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

        /// <summary>
        /// 保留对齐相关算法，该逻辑纯属 View 视图视觉调整，不应进入 ViewModel
        /// </summary>
        private void Page_SizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
        {
            if (SettingsGrid == null || LeftSettingsCard == null || RightSettingsCard == null) return;

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
            }
        }
    }
}
