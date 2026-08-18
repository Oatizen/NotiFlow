using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using NotiFlow.Models;

namespace NotiFlow.Views.Windows
{
    public partial class BackgroundImageEditorWindow : Window
    {
        private string _loadedImagePath = "";
        private ImageAnchor _currentAnchor = ImageAnchor.MiddleLeft;
        private double _imageScale = 1.0;
        private double _opacity = 1.0;
        private double _offsetX = 0;
        private double _offsetY = 0;
        private double _edgeBlur = 0;
        
        private double _maxCanvasWidth = 800;
        private double _canvasHeight = 60;
        private bool _isUpdatingFromCode = false;

        public BackgroundImageEditorWindow()
        {
            InitializeComponent();
            LoadCurrentSettings();
            UpdateCanvasSize();
            UpdateAnchorUI();
            UpdateImagePosition();
        }

        private void LoadCurrentSettings()
        {
            _loadedImagePath = BarrageSettings.BackgroundImagePath;
            if (File.Exists(_loadedImagePath))
            {
                LoadImageToCanvas(_loadedImagePath);
            }
            
            _currentAnchor = BarrageSettings.BackgroundImageAnchor;
            _offsetX = BarrageSettings.BackgroundImageOffsetX;
            _offsetY = BarrageSettings.BackgroundImageOffsetY;
            _imageScale = BarrageSettings.BackgroundImageScale > 0 ? BarrageSettings.BackgroundImageScale : 1.0;
            _opacity = BarrageSettings.BackgroundImageOpacity;
            _edgeBlur = BarrageSettings.BackgroundImageEdgeBlur;
            
            _canvasHeight = Math.Max(BarrageSettings.FontSize, BarrageSettings.FontSize * 1.25) + 12; // roughly matches padV*2
            _maxCanvasWidth = BarrageSettings.MaxTextLength * BarrageSettings.FontSize * 0.8; 
            if (_maxCanvasWidth < 200) _maxCanvasWidth = 200;
            
            ScaleText.Text = $"{(_imageScale * 100):F0}%";
            OpacityText.Text = $"{(_opacity * 100):F0}%";
            EdgeBlurText.Text = $"{_edgeBlur:F0}px";
            
            _isUpdatingFromCode = true;
            OffsetXBox.Text = _offsetX.ToString("F0");
            OffsetYBox.Text = _offsetY.ToString("F0");
            ScaleSlider.Value = _imageScale * 100;
            OpacitySlider.Value = _opacity * 100;
            EdgeBlurSlider.Value = _edgeBlur;
            KeepBaseColorCheckBox.IsChecked = BarrageSettings.BackgroundImageKeepBaseColor;
            if (ImageThumb != null) ImageThumb.Opacity = _opacity;
            UpdateCanvasBackground();
            UpdateImageMask();
            _isUpdatingFromCode = false;
        }

        private void UpdateCanvasSize()
        {
            double simRatio = LengthSimulationSlider.Value / 100.0;
            ArtboardCanvas.Width = _maxCanvasWidth * simRatio;
            ArtboardCanvas.Height = _canvasHeight;
            
            UpdateImagePosition();
        }

        private void LoadImageToCanvas(string path)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                }
                bitmap.Freeze();

                ImageThumb.ApplyTemplate();
                var imageEl = (Image)ImageThumb.Template.FindName("BackgroundImageElement", ImageThumb);
                if (imageEl != null)
                {
                    imageEl.Source = bitmap;
                }
                
                // Set initial thumb size
                ImageThumb.Width = bitmap.PixelWidth * _imageScale;
                ImageThumb.Height = bitmap.PixelHeight * _imageScale;
                
                _loadedImagePath = path;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载图片失败: {ex.Message}");
            }
        }

        private void SelectImage_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp|所有文件|*.*"
            };
            
            if (ofd.ShowDialog() == true)
            {
                _imageScale = 1.0; // reset scale for new image
                ScaleText.Text = "100%";
                LoadImageToCanvas(ofd.FileName);
                UpdateImagePosition();
            }
        }

        private void LengthSimulationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (ArtboardCanvas != null)
            {
                UpdateCanvasSize();
            }
        }

        private void ImageThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            double currentLeft = Canvas.GetLeft(ImageThumb);
            if (double.IsNaN(currentLeft)) currentLeft = 0;
            
            double currentTop = Canvas.GetTop(ImageThumb);
            if (double.IsNaN(currentTop)) currentTop = 0;
            
            double newLeft = currentLeft + e.HorizontalChange;
            double newTop = currentTop + e.VerticalChange;
            
            Canvas.SetLeft(ImageThumb, newLeft);
            Canvas.SetTop(ImageThumb, newTop);
            
            CalculateOffsetsFromPosition();
        }

        private void ScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingFromCode) return;
            if (ScaleText == null || ImageThumb == null) return;
            
            _imageScale = e.NewValue / 100.0;
            ScaleText.Text = $"{e.NewValue:F0}%";
            
            if (ImageThumb.Template != null && ImageThumb.Template.FindName("BackgroundImageElement", ImageThumb) is Image imageEl && imageEl.Source is BitmapSource src)
            {
                ImageThumb.Width = src.PixelWidth * _imageScale;
                ImageThumb.Height = src.PixelHeight * _imageScale;
                UpdateImagePosition();
                CalculateOffsetsFromPosition();
            }
        }

        private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingFromCode) return;
            if (OpacityText == null || ImageThumb == null) return;
            
            _opacity = e.NewValue / 100.0;
            OpacityText.Text = $"{e.NewValue:F0}%";
            ImageThumb.Opacity = _opacity;
        }

        private void KeepBaseColor_Changed(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingFromCode) return;
            if (ArtboardCanvas == null) return;
            
            UpdateCanvasBackground();
        }

        private void UpdateCanvasBackground()
        {
            if (KeepBaseColorCheckBox.IsChecked == true)
            {
                var bgBrush = BarrageSettings.BackgroundColor as System.Windows.Media.SolidColorBrush;
                if (bgBrush != null)
                {
                    System.Windows.Media.Color color = bgBrush.Color;
                    color.A = (byte)(BarrageSettings.BackgroundOpacity * 255);
                    ArtboardCanvas.Background = new System.Windows.Media.SolidColorBrush(color);
                }
            }
            else
            {
                ArtboardCanvas.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0, 0, 0, 0));
            }
        }

        private void CalculateOffsetsFromPosition()
        {
            double left = Canvas.GetLeft(ImageThumb);
            if (double.IsNaN(left)) left = 0;
            double top = Canvas.GetTop(ImageThumb);
            if (double.IsNaN(top)) top = 0;
            
            double canvasW = ArtboardCanvas.Width;
            double canvasH = ArtboardCanvas.Height;
            double imgW = ImageThumb.Width;
            double imgH = ImageThumb.Height;

            switch (_currentAnchor)
            {
                case ImageAnchor.TopLeft:
                case ImageAnchor.MiddleLeft:
                case ImageAnchor.BottomLeft:
                    _offsetX = left;
                    break;
                case ImageAnchor.TopCenter:
                case ImageAnchor.MiddleCenter:
                case ImageAnchor.BottomCenter:
                    _offsetX = left - (canvasW - imgW) / 2.0;
                    break;
                case ImageAnchor.TopRight:
                case ImageAnchor.MiddleRight:
                case ImageAnchor.BottomRight:
                    _offsetX = canvasW - imgW - left;
                    break;
            }

            switch (_currentAnchor)
            {
                case ImageAnchor.TopLeft:
                case ImageAnchor.TopCenter:
                case ImageAnchor.TopRight:
                    _offsetY = top;
                    break;
                case ImageAnchor.MiddleLeft:
                case ImageAnchor.MiddleCenter:
                case ImageAnchor.MiddleRight:
                    _offsetY = top - (canvasH - imgH) / 2.0;
                    break;
                case ImageAnchor.BottomLeft:
                case ImageAnchor.BottomCenter:
                case ImageAnchor.BottomRight:
                    _offsetY = canvasH - imgH - top;
                    break;
            }
            
            _isUpdatingFromCode = true;
            OffsetXBox.Text = _offsetX.ToString("F0");
            OffsetYBox.Text = _offsetY.ToString("F0");
            _isUpdatingFromCode = false;
        }

        private void UpdateImagePosition()
        {
            double canvasW = ArtboardCanvas.Width;
            double canvasH = ArtboardCanvas.Height;
            double imgW = ImageThumb.Width;
            double imgH = ImageThumb.Height;
            if (double.IsNaN(imgW) || imgW == 0) return;

            double left = 0;
            double top = 0;

            switch (_currentAnchor)
            {
                case ImageAnchor.TopLeft:
                case ImageAnchor.MiddleLeft:
                case ImageAnchor.BottomLeft:
                    left = _offsetX;
                    break;
                case ImageAnchor.TopCenter:
                case ImageAnchor.MiddleCenter:
                case ImageAnchor.BottomCenter:
                    left = (canvasW - imgW) / 2.0 + _offsetX;
                    break;
                case ImageAnchor.TopRight:
                case ImageAnchor.MiddleRight:
                case ImageAnchor.BottomRight:
                    left = canvasW - imgW - _offsetX;
                    break;
            }

            switch (_currentAnchor)
            {
                case ImageAnchor.TopLeft:
                case ImageAnchor.TopCenter:
                case ImageAnchor.TopRight:
                    top = _offsetY;
                    break;
                case ImageAnchor.MiddleLeft:
                case ImageAnchor.MiddleCenter:
                case ImageAnchor.MiddleRight:
                    top = (canvasH - imgH) / 2.0 + _offsetY;
                    break;
                case ImageAnchor.BottomLeft:
                case ImageAnchor.BottomCenter:
                case ImageAnchor.BottomRight:
                    top = canvasH - imgH - _offsetY;
                    break;
            }

            Canvas.SetLeft(ImageThumb, left);
            Canvas.SetTop(ImageThumb, top);
        }

        private void Anchor_Click(object sender, RoutedEventArgs e)
        {
            if (sender == AnchorTopLeft) _currentAnchor = ImageAnchor.TopLeft;
            else if (sender == AnchorTopCenter) _currentAnchor = ImageAnchor.TopCenter;
            else if (sender == AnchorTopRight) _currentAnchor = ImageAnchor.TopRight;
            else if (sender == AnchorMiddleLeft) _currentAnchor = ImageAnchor.MiddleLeft;
            else if (sender == AnchorMiddleCenter) _currentAnchor = ImageAnchor.MiddleCenter;
            else if (sender == AnchorMiddleRight) _currentAnchor = ImageAnchor.MiddleRight;
            else if (sender == AnchorBottomLeft) _currentAnchor = ImageAnchor.BottomLeft;
            else if (sender == AnchorBottomCenter) _currentAnchor = ImageAnchor.BottomCenter;
            else if (sender == AnchorBottomRight) _currentAnchor = ImageAnchor.BottomRight;

            UpdateAnchorUI();
            
            // Re-calculate the position based on the NEW anchor so the image jumps to it
            UpdateImagePosition();
            UpdateImageMask();
        }

        private void UpdateAnchorUI()
        {
            AnchorTopLeft.IsChecked = _currentAnchor == ImageAnchor.TopLeft;
            AnchorTopCenter.IsChecked = _currentAnchor == ImageAnchor.TopCenter;
            AnchorTopRight.IsChecked = _currentAnchor == ImageAnchor.TopRight;
            AnchorMiddleLeft.IsChecked = _currentAnchor == ImageAnchor.MiddleLeft;
            AnchorMiddleCenter.IsChecked = _currentAnchor == ImageAnchor.MiddleCenter;
            AnchorMiddleRight.IsChecked = _currentAnchor == ImageAnchor.MiddleRight;
            AnchorBottomLeft.IsChecked = _currentAnchor == ImageAnchor.BottomLeft;
            AnchorBottomCenter.IsChecked = _currentAnchor == ImageAnchor.BottomCenter;
            AnchorBottomRight.IsChecked = _currentAnchor == ImageAnchor.BottomRight;
        }

        private void OffsetBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingFromCode) return;
            if (OffsetXBox == null || OffsetYBox == null || ImageThumb == null || ArtboardCanvas == null) return;
            
            if (double.TryParse(OffsetXBox.Text, out double ox)) _offsetX = ox;
            if (double.TryParse(OffsetYBox.Text, out double oy)) _offsetY = oy;
            
            UpdateImagePosition();
        }

        private void EdgeBlurSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingFromCode) return;
            if (EdgeBlurText == null) return;

            _edgeBlur = e.NewValue;
            EdgeBlurText.Text = $"{_edgeBlur:F0}px";
            UpdateImageMask();
        }

        private void UpdateImageMask()
        {
            if (ImageThumb == null || ImageThumb.Template == null) return;
            var imageEl = ImageThumb.Template.FindName("BackgroundImageElement", ImageThumb) as Image;
            if (imageEl == null) return;

            if (_edgeBlur <= 0)
            {
                imageEl.OpacityMask = null;
                ImageThumb.OpacityMask = null;
                return;
            }

            double w = ImageThumb.Width;
            double h = ImageThumb.Height;
            if (w <= 0 || h <= 0) return;

            // 垂直方向仅做轻微的平滑边缘防锯齿（最大 6px），避免上下画面被吞噬
            double vBlur = Math.Min(6.0, h * 0.15);
            double ry = vBlur > 0 ? Math.Min(0.2, vBlur / h) : 0.0;

            var vStops = new System.Windows.Media.GradientStopCollection
            {
                new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(0, 255, 255, 255), 0.0),
                new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(255, 255, 255, 255), ry),
                new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(255, 255, 255, 255), 1.0 - ry),
                new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(0, 255, 255, 255), 1.0)
            };

            // 水平方向：朝向感知的超长平滑过渡（拉长到数百像素）
            var hStops = new System.Windows.Media.GradientStopCollection();

            bool isRightAnchor = _currentAnchor == ImageAnchor.TopRight || _currentAnchor == ImageAnchor.MiddleRight || _currentAnchor == ImageAnchor.BottomRight;
            bool isLeftAnchor = _currentAnchor == ImageAnchor.TopLeft || _currentAnchor == ImageAnchor.MiddleLeft || _currentAnchor == ImageAnchor.BottomLeft;

            if (isRightAnchor)
            {
                // 图片在右侧，朝向内部的【左边缘】展开长距离平滑过渡；右边缘贴紧弹幕边界保持 100% 不透明
                double rx = Math.Clamp(_edgeBlur / w, 0.01, 1.0);
                hStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(0, 255, 255, 255), 0.0));
                hStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(38, 255, 255, 255), rx * 0.25));
                hStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(128, 255, 255, 255), rx * 0.50));
                hStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(217, 255, 255, 255), rx * 0.75));
                hStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(255, 255, 255, 255), rx));
                hStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(255, 255, 255, 255), 1.0));
            }
            else if (isLeftAnchor)
            {
                // 图片在左侧，朝向内部的【右边缘】展开长距离平滑过渡；左边缘贴紧弹幕边界保持 100% 不透明
                double rx = Math.Clamp(_edgeBlur / w, 0.01, 1.0);
                hStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(255, 255, 255, 255), 0.0));
                hStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(255, 255, 255, 255), 1.0 - rx));
                hStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(217, 255, 255, 255), 1.0 - rx * 0.75));
                hStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(128, 255, 255, 255), 1.0 - rx * 0.50));
                hStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(38, 255, 255, 255), 1.0 - rx * 0.25));
                hStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(0, 255, 255, 255), 1.0));
            }
            else
            {
                // 图片居中，左右双侧对称平滑展开过渡
                double rx = Math.Clamp(_edgeBlur / w, 0.01, 0.5);
                hStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(0, 255, 255, 255), 0.0));
                hStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(38, 255, 255, 255), rx * 0.25));
                hStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(128, 255, 255, 255), rx * 0.50));
                hStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(217, 255, 255, 255), rx * 0.75));
                hStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(255, 255, 255, 255), rx));
                hStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(255, 255, 255, 255), 1.0 - rx));
                hStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(217, 255, 255, 255), 1.0 - rx * 0.75));
                hStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(128, 255, 255, 255), 1.0 - rx * 0.50));
                hStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(38, 255, 255, 255), 1.0 - rx * 0.25));
                hStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(0, 255, 255, 255), 1.0));
            }

            ImageThumb.OpacityMask = new System.Windows.Media.LinearGradientBrush(hStops, new Point(0, 0), new Point(1, 0));
            imageEl.OpacityMask = new System.Windows.Media.LinearGradientBrush(vStops, new Point(0, 0), new Point(0, 1));
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_loadedImagePath) || !File.Exists(_loadedImagePath))
            {
                MessageBox.Show("请先选择图片");
                return;
            }

            try
            {
                // Copy to AppData to avoid locking original or losing it
                string destDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NotiFlow", "Assets");
                Directory.CreateDirectory(destDir);
                
                string ext = Path.GetExtension(_loadedImagePath);
                string newFileName = $"custom_bg_{DateTime.Now.Ticks}{ext}";
                string destPath = Path.Combine(destDir, newFileName);
                
                if (_loadedImagePath != destPath && !destPath.Equals(BarrageSettings.BackgroundImagePath, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(_loadedImagePath, destPath, true);
                }
                
                BarrageSettings.BackgroundImagePath = destPath;
                BarrageSettings.BackgroundImageAnchor = _currentAnchor;
                BarrageSettings.BackgroundImageOffsetX = _offsetX;
                BarrageSettings.BackgroundImageOffsetY = _offsetY;
                BarrageSettings.BackgroundImageScale = _imageScale;
                BarrageSettings.BackgroundImageOpacity = _opacity;
                BarrageSettings.BackgroundImageEdgeBlur = _edgeBlur;
                BarrageSettings.BackgroundImageKeepBaseColor = KeepBaseColorCheckBox.IsChecked ?? true;
                BarrageSettings.ShowBackgroundImage = true;
                
                BarrageSettings.ExportConfig();
                
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存失败: {ex.Message}");
            }
        }
    }
}
