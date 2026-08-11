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
        private double _offsetX = 0;
        private double _offsetY = 0;
        
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
            
            _canvasHeight = Math.Max(BarrageSettings.FontSize, BarrageSettings.FontSize * 1.25) + 12; // roughly matches padV*2
            _maxCanvasWidth = BarrageSettings.MaxTextLength * BarrageSettings.FontSize * 0.8; 
            if (_maxCanvasWidth < 200) _maxCanvasWidth = 200;
            
            ScaleText.Text = $"{(_imageScale * 100):F0}%";
            
            _isUpdatingFromCode = true;
            OffsetXBox.Text = _offsetX.ToString("F0");
            OffsetYBox.Text = _offsetY.ToString("F0");
            ScaleSlider.Value = _imageScale * 100;
            KeepBaseColorCheckBox.IsChecked = BarrageSettings.BackgroundImageKeepBaseColor;
            UpdateCanvasBackground();
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
