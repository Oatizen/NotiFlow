using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NotiFlow.Models;
using NotiFlow.Helpers;
using CommunityToolkit.Mvvm.Input;

namespace NotiFlow.Views.Controls
{
    public partial class ModernColorPicker : UserControl, INotifyPropertyChanged
    {
        public static readonly DependencyProperty SelectedColorProperty = DependencyProperty.Register(
            nameof(SelectedColor), typeof(Color), typeof(ModernColorPicker),
            new FrameworkPropertyMetadata(Colors.Red, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedColorChanged));

        public Color SelectedColor
        {
            get => (Color)GetValue(SelectedColorProperty);
            set => SetValue(SelectedColorProperty, value);
        }

        public static readonly DependencyProperty PresetColorsProperty = DependencyProperty.Register(
            nameof(PresetColors), typeof(ObservableCollection<ColorPaletteItem>), typeof(ModernColorPicker), new PropertyMetadata(null));

        public ObservableCollection<ColorPaletteItem> PresetColors
        {
            get => (ObservableCollection<ColorPaletteItem>)GetValue(PresetColorsProperty);
            set => SetValue(PresetColorsProperty, value);
        }

        public static readonly DependencyProperty SelectedColorHexProperty = DependencyProperty.Register(
            nameof(SelectedColorHex), typeof(string), typeof(ModernColorPicker), new FrameworkPropertyMetadata("#FFFF0000", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedColorHexChanged));

        public string SelectedColorHex
        {
            get => (string)GetValue(SelectedColorHexProperty);
            set => SetValue(SelectedColorHexProperty, value);
        }

        private bool _isUpdatingInternally = false;
        private bool _isInitialized = false;
        private double _h, _s, _v;

        public event PropertyChangedEventHandler PropertyChanged;

        public ModernColorPicker()
        {
            InitializeComponent();
            SelectPresetCommand = new RelayCommand<ColorPaletteItem>(OnSelectPreset);
            _h = 0;
            _s = 1;
            _v = 1;
            _isInitialized = true;
            UpdateThumbPosition();
            UpdateGradients();
            UpdatePreview();
        }

        public ICommand SelectPresetCommand { get; }

        private void OnSelectPreset(ColorPaletteItem item)
        {
            if (item?.Brush is SolidColorBrush solidColorBrush)
            {
                SelectedColor = solidColorBrush.Color;
            }
        }

        private static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var picker = (ModernColorPicker)d;
            if (picker._isUpdatingInternally) return;

            var c = (Color)e.NewValue;
            ColorHelper.RgbToHsv(c, out picker._h, out picker._s, out picker._v);
            
            picker._isUpdatingInternally = true;
            picker.SelectedColorHex = $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
            picker.HueSlider.Value = picker._h;
            picker.ValueSlider.Value = picker._v;
            picker.AlphaSlider.Value = c.A;
            picker.UpdateThumbPosition();
            picker.UpdateGradients();
            picker.UpdatePreview();
            picker.UpdateRgbSliders(c);
            picker._isUpdatingInternally = false;
        }

        private static void OnSelectedColorHexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var picker = (ModernColorPicker)d;
            if (picker._isUpdatingInternally) return;

            if (e.NewValue is string hex)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(hex)) return;
                    var c = (Color)ColorConverter.ConvertFromString(hex);
                    picker._isUpdatingInternally = true;
                    picker.SelectedColor = c;
                    ColorHelper.RgbToHsv(c, out picker._h, out picker._s, out picker._v);
                    picker.HueSlider.Value = picker._h;
                    picker.ValueSlider.Value = picker._v;
                    picker.AlphaSlider.Value = c.A;
                    picker.UpdateThumbPosition();
                    picker.UpdateGradients();
                    picker.UpdatePreview();
                    picker.UpdateRgbSliders(c);
                    picker._isUpdatingInternally = false;
                }
                catch { }
            }
        }

        private void Tab_Click(object sender, RoutedEventArgs e)
        {
            if (SwatchesView == null) return;
            SwatchesView.Visibility = TabSwatches.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            SpectrumView.Visibility = TabSpectrum.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            SlidersView.Visibility = TabSliders.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateGradients()
        {
            if (!_isInitialized) return;
            ValueSliderTopColor.Color = ColorHelper.HsvToRgb(_h, _s, 1);
            AlphaSliderTopColor.Color = ColorHelper.HsvToRgb(_h, _s, _v);
        }

        private void UpdatePreview()
        {
            if (!_isInitialized) return;
            var c = ColorHelper.HsvToRgb(_h, _s, _v, (byte)AlphaSlider.Value);
            PreviewColorBrush.Color = c;
            
            if (!_isUpdatingInternally)
            {
                _isUpdatingInternally = true;
                SelectedColor = c;
                SelectedColorHex = $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
                UpdateRgbSliders(c);
                _isUpdatingInternally = false;
            }
        }

        private void UpdateRgbSliders(Color c)
        {
            if (!_isInitialized) return;
            RSlider.Value = c.R;
            GSlider.Value = c.G;
            BSlider.Value = c.B;
            ASlider.Value = c.A;
            HexTextBox.Text = $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
        }

        private void RgbSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingInternally) return;
            _isUpdatingInternally = true;

            byte a = (byte)ASlider.Value;
            byte r = (byte)RSlider.Value;
            byte g = (byte)GSlider.Value;
            byte b = (byte)BSlider.Value;
            var c = Color.FromArgb(a, r, g, b);
            
            ColorHelper.RgbToHsv(c, out _h, out _s, out _v);
            
            HueSlider.Value = _h;
            ValueSlider.Value = _v;
            AlphaSlider.Value = a;
            
            UpdateThumbPosition();
            UpdateGradients();
            SelectedColor = c;
            SelectedColorHex = $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
            PreviewColorBrush.Color = c;
            HexTextBox.Text = $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
            
            _isUpdatingInternally = false;
        }

        private void HexTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingInternally) return;
            
            var text = HexTextBox.Text.Trim();
            if (text.StartsWith("#")) text = text.Substring(1);
            
            if (text.Length == 6 || text.Length == 8)
            {
                try
                {
                    byte a = 255;
                    byte r = 0, g = 0, b = 0;
                    if (text.Length == 8)
                    {
                        a = Convert.ToByte(text.Substring(0, 2), 16);
                        r = Convert.ToByte(text.Substring(2, 2), 16);
                        g = Convert.ToByte(text.Substring(4, 2), 16);
                        b = Convert.ToByte(text.Substring(6, 2), 16);
                    }
                    else
                    {
                        r = Convert.ToByte(text.Substring(0, 2), 16);
                        g = Convert.ToByte(text.Substring(2, 2), 16);
                        b = Convert.ToByte(text.Substring(4, 2), 16);
                    }
                    
                    var c = Color.FromArgb(a, r, g, b);
                    _isUpdatingInternally = true;
                    
                    ColorHelper.RgbToHsv(c, out _h, out _s, out _v);
                    HueSlider.Value = _h;
                    ValueSlider.Value = _v;
                    AlphaSlider.Value = a;
                    
                    RSlider.Value = r;
                    GSlider.Value = g;
                    BSlider.Value = b;
                    ASlider.Value = a;

                    UpdateThumbPosition();
                    UpdateGradients();
                    SelectedColor = c;
                    SelectedColorHex = $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
                    PreviewColorBrush.Color = c;
                    
                    _isUpdatingInternally = false;
                }
                catch { }
            }
        }

        private void HueSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingInternally) return;
            _h = e.NewValue;
            UpdateThumbPosition();
            UpdateGradients();
            UpdatePreview();
        }

        private void SpectrumSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingInternally) return;
            if (sender == ValueSlider) _v = e.NewValue;
            UpdateThumbPosition();
            UpdateGradients();
            UpdatePreview();
        }

        private bool _isDraggingColorArea = false;

        private void ColorArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDraggingColorArea = true;
            ColorAreaGrid.CaptureMouse();
            UpdateColorArea(e.GetPosition(ColorAreaGrid));
        }

        private void ColorAreaGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_isInitialized)
                UpdateThumbPosition();
        }

        private void ColorArea_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingColorArea)
            {
                UpdateColorArea(e.GetPosition(ColorAreaGrid));
            }
        }

        private void ColorArea_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDraggingColorArea = false;
            ColorAreaGrid.ReleaseMouseCapture();
        }

        private void UpdateColorArea(Point p)
        {
            double w = ColorAreaGrid.ActualWidth;
            double h = ColorAreaGrid.ActualHeight;
            if (w == 0 || h == 0) return;

            double x = Math.Max(0, Math.Min(p.X, w));
            double y = Math.Max(0, Math.Min(p.Y, h));

            _h = (x / w) * 360.0;
            _s = 1.0 - (y / h);

            ColorThumbTransform.X = x - 8;
            ColorThumbTransform.Y = y - 8;

            UpdateGradients();
            UpdatePreview();
        }

        private void UpdateThumbPosition()
        {
            if (!_isInitialized || ColorAreaGrid.ActualWidth == 0 || ColorAreaGrid.ActualHeight == 0) return;

            double x = (_h / 360.0) * ColorAreaGrid.ActualWidth;
            double y = (1.0 - _s) * ColorAreaGrid.ActualHeight;

            ColorThumbTransform.X = x - 8;
            ColorThumbTransform.Y = y - 8;
        }

        // ================= 吸色器逻辑 (原生 GetPixel) =================

        [DllImport("user32.dll")]
        static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

        [DllImport("gdi32.dll")]
        static extern uint GetPixel(IntPtr hdc, int nXPos, int nYPos);

        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        private DispatcherTimer _pickerTimer;
        private bool _isPickingColor = false;
        private Window _overlayWindow;

        private void EyedropperButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isPickingColor) return;
            _isPickingColor = true;

            _overlayWindow = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
                Topmost = true,
                ShowInTaskbar = false,
                Cursor = Cursors.Cross
            };

            double minX = SystemParameters.VirtualScreenLeft;
            double minY = SystemParameters.VirtualScreenTop;
            double width = SystemParameters.VirtualScreenWidth;
            double height = SystemParameters.VirtualScreenHeight;

            _overlayWindow.Left = minX;
            _overlayWindow.Top = minY;
            _overlayWindow.Width = width;
            _overlayWindow.Height = height;

            _overlayWindow.PreviewMouseLeftButtonDown += (s, ev) => StopPicking();
            _overlayWindow.PreviewKeyDown += (s, ev) =>
            {
                if (ev.Key == Key.Escape) StopPicking(true);
            };

            _overlayWindow.Show();

            _pickerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _pickerTimer.Tick += PickerTimer_Tick;
            _pickerTimer.Start();
        }

        private void PickerTimer_Tick(object sender, EventArgs e)
        {
            if (GetCursorPos(out POINT p))
            {
                IntPtr hdc = GetDC(IntPtr.Zero);
                uint pixel = GetPixel(hdc, p.X, p.Y);
                ReleaseDC(IntPtr.Zero, hdc);

                byte r = (byte)(pixel & 0x000000FF);
                byte g = (byte)((pixel & 0x0000FF00) >> 8);
                byte b = (byte)((pixel & 0x00FF0000) >> 16);

                var color = Color.FromRgb(r, g, b);
                PreviewColorBrush.Color = color;
            }
        }

        private void StopPicking(bool cancel = false)
        {
            if (!_isPickingColor) return;
            _isPickingColor = false;
            
            _pickerTimer?.Stop();
            _pickerTimer = null;
            
            if (_overlayWindow != null)
            {
                _overlayWindow.Close();
                _overlayWindow = null;
            }

            if (!cancel)
            {
                SelectedColor = PreviewColorBrush.Color;
            }
            else
            {
                PreviewColorBrush.Color = SelectedColor;
            }
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
