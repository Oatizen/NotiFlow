using System.Windows;
using System.Windows.Media;

namespace NotiFlow.Rendering
{
    /// <summary>
    /// 最底层的视觉元素托盘。
    /// 不参与 WPF 的常规冒泡事件系统和复杂的三段式渲染计算（Measure/Arrange/Render）。
    /// 作为专门兜库容纳海量轻量级 DrawingVisual 的底层图层面板使用，这是突破 WPF GPU 负载瓶颈的关键钥匙。
    /// </summary>
    public class BarrageEngineHost : FrameworkElement
    {
        private readonly VisualCollection _visuals;

        public BarrageEngineHost()
        {
            _visuals = new VisualCollection(this);
        }

        public void AddVisual(Visual visual)
        {
            _visuals.Add(visual);
        }

        public void RemoveVisual(Visual visual)
        {
            _visuals.Remove(visual);
        }

        public void Clear()
        {
            _visuals.Clear();
        }

        // --- 告诉 WPF 引擎如何接入这个底层的纯正图层集合 ---

        protected override int VisualChildrenCount => _visuals.Count;

        protected override Visual GetVisualChild(int index)
        {
            if (index < 0 || index >= _visuals.Count)
                throw new System.ArgumentOutOfRangeException();
            return _visuals[index];
        }
    }
}
