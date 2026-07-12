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

            if (SGradStart != null) SGradStart.Color = ColorHelper.HsvToRgb(_h, 0, _v);
            if (SGradEnd != null) SGradEnd.Color = ColorHelper.HsvToRgb(_h, 1, _v);
            if (VGradEnd != null) VGradEnd.Color = ColorHelper.HsvToRgb(_h, _s, 1);
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
            
            if (RTextBox != null) RTextBox.Text = c.R.ToString();
            if (GTextBox != null) GTextBox.Text = c.G.ToString();
            if (BTextBox != null) BTextBox.Text = c.B.ToString();
            if (ATextBox != null) ATextBox.Text = c.A.ToString();
            
            if (HsvHSlider != null) HsvHSlider.Value = _h;
            if (HsvSSlider != null) HsvSSlider.Value = _s;
            if (HsvVSlider != null) HsvVSlider.Value = _v;

            if (HTextBox != null) HTextBox.Text = Math.Round(_h).ToString();
            if (STextBox != null) STextBox.Text = Math.Round(_s * 100).ToString();
            if (VTextBox != null) VTextBox.Text = Math.Round(_v * 100).ToString();

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
            
            if (RTextBox != null) RTextBox.Text = r.ToString();
            if (GTextBox != null) GTextBox.Text = g.ToString();
            if (BTextBox != null) BTextBox.Text = b.ToString();
            if (ATextBox != null) ATextBox.Text = a.ToString();
            
            if (HsvHSlider != null) HsvHSlider.Value = _h;
            if (HsvSSlider != null) HsvSSlider.Value = _s;
            if (HsvVSlider != null) HsvVSlider.Value = _v;

            if (HTextBox != null) HTextBox.Text = Math.Round(_h).ToString();
            if (STextBox != null) STextBox.Text = Math.Round(_s * 100).ToString();
            if (VTextBox != null) VTextBox.Text = Math.Round(_v * 100).ToString();
            
            UpdateThumbPosition();
            UpdateGradients();
            SelectedColor = c;
            SelectedColorHex = $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
            PreviewColorBrush.Color = c;
            HexTextBox.Text = $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
            
            _isUpdatingInternally = false;
        }

        private void HsvSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingInternally) return;
            _isUpdatingInternally = true;

            _h = HsvHSlider.Value;
            _s = HsvSSlider.Value;
            _v = HsvVSlider.Value;
            byte a = (byte)ASlider.Value;
            
            var c = ColorHelper.HsvToRgb(_h, _s, _v, a);
            
            HueSlider.Value = _h;
            ValueSlider.Value = _v;
            AlphaSlider.Value = a;
            
            RSlider.Value = c.R;
            GSlider.Value = c.G;
            BSlider.Value = c.B;
            
            if (RTextBox != null) RTextBox.Text = c.R.ToString();
            if (GTextBox != null) GTextBox.Text = c.G.ToString();
            if (BTextBox != null) BTextBox.Text = c.B.ToString();
            
            if (HTextBox != null) HTextBox.Text = Math.Round(_h).ToString();
            if (STextBox != null) STextBox.Text = Math.Round(_s * 100).ToString();
            if (VTextBox != null) VTextBox.Text = Math.Round(_v * 100).ToString();

            UpdateThumbPosition();
            UpdateGradients();
            SelectedColor = c;
            SelectedColorHex = $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
            PreviewColorBrush.Color = c;
            HexTextBox.Text = $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
            
            _isUpdatingInternally = false;
        }

        private void RgbTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingInternally || RSlider == null || GSlider == null || BSlider == null) return;
            
            if (byte.TryParse(RTextBox.Text, out byte r) && 
                byte.TryParse(GTextBox.Text, out byte g) && 
                byte.TryParse(BTextBox.Text, out byte b))
            {
                _isUpdatingInternally = true;
                RSlider.Value = r;
                GSlider.Value = g;
                BSlider.Value = b;
                
                byte a = (byte)ASlider.Value;
                var c = Color.FromArgb(a, r, g, b);
                ColorHelper.RgbToHsv(c, out _h, out _s, out _v);
                
                HueSlider.Value = _h;
                ValueSlider.Value = _v;
                
                if (HsvHSlider != null) HsvHSlider.Value = _h;
                if (HsvSSlider != null) HsvSSlider.Value = _s;
                if (HsvVSlider != null) HsvVSlider.Value = _v;

                if (HTextBox != null) HTextBox.Text = Math.Round(_h).ToString();
                if (STextBox != null) STextBox.Text = Math.Round(_s * 100).ToString();
                if (VTextBox != null) VTextBox.Text = Math.Round(_v * 100).ToString();
                
                UpdateThumbPosition();
                UpdateGradients();
                SelectedColor = c;
                SelectedColorHex = $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
                PreviewColorBrush.Color = c;
                HexTextBox.Text = $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
                
                _isUpdatingInternally = false;
            }
        }

        private void HsvTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingInternally || HsvHSlider == null || HsvSSlider == null || HsvVSlider == null) return;
            
            if (double.TryParse(HTextBox.Text, out double h) && 
                double.TryParse(STextBox.Text, out double s) && 
                double.TryParse(VTextBox.Text, out double v))
            {
                _isUpdatingInternally = true;
                _h = Math.Max(0, Math.Min(360, h));
                _s = Math.Max(0, Math.Min(100, s)) / 100.0;
                _v = Math.Max(0, Math.Min(100, v)) / 100.0;
                
                HsvHSlider.Value = _h;
                HsvSSlider.Value = _s;
                HsvVSlider.Value = _v;
                
                byte a = (byte)ASlider.Value;
                var c = ColorHelper.HsvToRgb(_h, _s, _v, a);
                
                HueSlider.Value = _h;
                ValueSlider.Value = _v;
                
                RSlider.Value = c.R;
                GSlider.Value = c.G;
                BSlider.Value = c.B;
                
                if (RTextBox != null) RTextBox.Text = c.R.ToString();
                if (GTextBox != null) GTextBox.Text = c.G.ToString();
                if (BTextBox != null) BTextBox.Text = c.B.ToString();

                UpdateThumbPosition();
                UpdateGradients();
                SelectedColor = c;
                SelectedColorHex = $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
                PreviewColorBrush.Color = c;
                HexTextBox.Text = $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
                
                _isUpdatingInternally = false;
            }
        }

        private void ATextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingInternally || ASlider == null) return;
            
            if (byte.TryParse(ATextBox.Text, out byte a))
            {
                _isUpdatingInternally = true;
                ASlider.Value = a;
                AlphaSlider.Value = a;
                
                var c = Color.FromArgb(a, (byte)RSlider.Value, (byte)GSlider.Value, (byte)BSlider.Value);
                SelectedColor = c;
                SelectedColorHex = $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
                PreviewColorBrush.Color = c;
                HexTextBox.Text = $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
                
                _isUpdatingInternally = false;
            }
        }

        private void TabButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender == RgbTabButton)
            {
                RgbTabButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
                HsvTabButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Transparent;

                if (RgbSlidersPanel != null) RgbSlidersPanel.Visibility = Visibility.Visible;
                if (HsvSlidersPanel != null) HsvSlidersPanel.Visibility = Visibility.Collapsed;
            }
            else if (sender == HsvTabButton)
            {
                HsvTabButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
                RgbTabButton.Appearance = Wpf.Ui.Controls.ControlAppearance.Transparent;

                if (RgbSlidersPanel != null) RgbSlidersPanel.Visibility = Visibility.Collapsed;
                if (HsvSlidersPanel != null) HsvSlidersPanel.Visibility = Visibility.Visible;
            }
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
