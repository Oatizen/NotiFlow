using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using NotiFlow.Models;
using NotiFlow.Views.Windows;

namespace NotiFlow.Views.Pages
{
    public partial class ScopePage : Page
    {
        public ScopePage()
        {
            InitializeComponent();
            Loaded += ScopePage_Loaded;
            Unloaded += ScopePage_Unloaded;
        }

        private void ScopePage_Loaded(object sender, RoutedEventArgs e)
        {
            // 在页面完全加载后才触发数据初始化，避免在 XAML 解析阶段执行 P/Invoke 导致崩溃
            if (DataContext is ScopeViewModel vm)
            {
                vm.Initialize();
                // 确保只订阅一次
                vm.PropertyChanged -= Vm_PropertyChanged;
                vm.PropertyChanged += Vm_PropertyChanged;
            }
        }

        private void ScopePage_Unloaded(object sender, RoutedEventArgs e)
        {
            // 在页面卸载移出视觉树时，通知 ViewModel 彻底注销并停用实时轮询定时器，防范任何后台空转与内存泄漏
            if (DataContext is ScopeViewModel vm)
            {
                vm.Deinitialize();
                vm.PropertyChanged -= Vm_PropertyChanged;
            }
        }

        private void Vm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ScopeViewModel.IsSourceTabActive))
            {
                PlayTabTransitionAnimation();
            }
            else if (e.PropertyName == nameof(ScopeViewModel.CurrentMode))
            {
                if (DataContext is ScopeViewModel vm && 
                   (vm.CurrentMode == "Blacklist" || vm.CurrentMode == "Whitelist"))
                {
                    PlayListTransitionAnimation();
                }
            }
        }

        private void PlayTabTransitionAnimation()
        {
            var storyboard = new Storyboard();
            
            var opacityAnim = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            
            var translateAnim = new DoubleAnimation
            {
                From = 20.0,
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            
            Storyboard.SetTarget(opacityAnim, TabContentPanel);
            Storyboard.SetTargetProperty(opacityAnim, new PropertyPath(OpacityProperty));
            
            Storyboard.SetTarget(translateAnim, TabContentPanel);
            Storyboard.SetTargetProperty(translateAnim, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));
            
            storyboard.Children.Add(opacityAnim);
            storyboard.Children.Add(translateAnim);
            
            storyboard.Begin();
        }

        private void PlayListTransitionAnimation()
        {
            var storyboard = new Storyboard();
            
            var opacityAnim = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            
            var translateAnim = new DoubleAnimation
            {
                From = 20.0,
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            
            Storyboard.SetTarget(opacityAnim, ListContentPanel);
            Storyboard.SetTargetProperty(opacityAnim, new PropertyPath(OpacityProperty));
            
            Storyboard.SetTarget(translateAnim, ListContentPanel);
            Storyboard.SetTargetProperty(translateAnim, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));
            
            storyboard.Children.Add(opacityAnim);
            storyboard.Children.Add(translateAnim);
            
            storyboard.Begin();
        }

        private void Fab_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new InputDialogWindow(Window.GetWindow(this));
            dialog.ShowDialog();

            if (dialog.IsConfirmed && DataContext is ScopeViewModel vm)
            {
                vm.AddManualRule(dialog.Identifier, dialog.DisplayName);
            }
        }

        private void TopHelpButton_Click(object sender, RoutedEventArgs e)
        {
            TopHelpFlyout.IsOpen = true;
        }

        private void BottomHelpButton_Click(object sender, RoutedEventArgs e)
        {
            BottomHelpFlyout.IsOpen = true;
        }
    }
}
