using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace NotiFlow
{
    public partial class SettingsWindow : FluentWindow
    {
        public SettingsWindow()
        {
            InitializeComponent();
            
            // 手动设定加载时要显示的首页（也可以在使用 MVVM 和导航服务时通过路由来配置）
            Loaded += (s, e) => RootNavigation.Navigate(typeof(Views.Pages.CustomPage));
        }

        protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
        {
            base.OnPreviewMouseWheel(e);

            if (e.Handled) return;

            // 1. 使用 OriginalSource 获取触发滚轮事件的真实底层元素。
            //    不要使用 VisualTreeHelper.HitTest(this)，因为它无法穿透到具有独立视觉树的 Popup (例如 ComboBox 的下拉弹窗) 中，
            //    导致下拉框中的滚轮事件被错误地路由到了背后的主页面上。
            if (e.OriginalSource is DependencyObject originalSource)
            {
                // 2. 从命中元素向上遍历，找到第一个「真正能滚动」的 ScrollViewer
                ScrollViewer sv = FindScrollableParent(originalSource);

                // 3. 强行驱动滚动
                if (sv != null)
                {
                    double scrollAmount = e.Delta / 2.0;
                    sv.ScrollToVerticalOffset(sv.VerticalOffset - scrollAmount);
                    e.Handled = true;
                }
            }
        }

        /// <summary>
        /// 递归向上查找第一个 ScrollableHeight > 0 的 ScrollViewer。
        /// 这是解决 NavigationView 嵌套 ScrollViewer 问题的核心：
        /// 页面内部的 ScrollViewer (ScrollableHeight=0) 会被跳过，
        /// 直到找到 NavigationView 自带的那个真正能滚动的 ScrollViewer。
        /// </summary>
        private static ScrollViewer? FindScrollableParent(DependencyObject child)
        {
            DependencyObject current = child;
            while (current != null)
            {
                if (current is ScrollViewer sv && sv.ScrollableHeight > 0)
                {
                    return sv;
                }
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }
    }
}
