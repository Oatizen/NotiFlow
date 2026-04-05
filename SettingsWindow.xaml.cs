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

            // 1. 获取当前鼠标在窗口内的坐标
            Point mousePoint = e.GetPosition(this);

            // 2. HitTest 向下扫描鼠标悬停的底层元素
            HitTestResult hitResult = VisualTreeHelper.HitTest(this, mousePoint);
            if (hitResult == null || hitResult.VisualHit == null) return;

            // 3. 从命中元素向上遍历，找到第一个「真正能滚动」的 ScrollViewer
            //    关键：跳过 ScrollableHeight == 0 的 ScrollViewer（它们只是空壳）
            ScrollViewer sv = FindScrollableParent(hitResult.VisualHit);

            // 4. 强行驱动滚动
            if (sv != null)
            {
                double scrollAmount = e.Delta / 2.0;
                sv.ScrollToVerticalOffset(sv.VerticalOffset - scrollAmount);
                e.Handled = true;
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
