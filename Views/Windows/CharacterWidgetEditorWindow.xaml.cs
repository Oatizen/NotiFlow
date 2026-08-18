using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using NotiFlow.Models;

namespace NotiFlow.Views.Windows
{
    /// <summary>
    /// 角色伴随挂件可视化排版画板窗口。
    /// </summary>
    public partial class CharacterWidgetEditorWindow : Window
    {
        private string _loadedImagePath = "";
        private ImageAnchor _currentAnchor = ImageAnchor.TopRight;
        private double _imageScale = 1.0;
        private double _offsetX = -15;
        private double _offsetY = -20;
        
        private double _maxBarrageWidth = 600;
        private double _barrageHeight = 52;
        private bool _isUpdatingFromCode = true;
        private BitmapImage? _currentBitmap;

        public CharacterWidgetEditorWindow()
        {
            _isUpdatingFromCode = true;
            InitializeComponent();
            LoadCurrentSettings();
            UpdateAnchorUI();
            UpdateCanvasLayout();
            _isUpdatingFromCode = false;
        }

        private void LoadCurrentSettings()
        {
            _loadedImagePath = BarrageSettings.CharacterWidgetPath;
            if (string.IsNullOrEmpty(_loadedImagePath) || !File.Exists(_loadedImagePath))
            {
                string presetInApp = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "Characters", "Preset1_Pajing.png");
                if (File.Exists(presetInApp))
                {
                    _loadedImagePath = presetInApp;
                }
                else if (File.Exists(@"E:\PhotoShop成品\组件1.png"))
                {
                    _loadedImagePath = @"E:\PhotoShop成品\组件1.png";
                }
            }

            if (File.Exists(_loadedImagePath))
            {
                LoadImageToCanvas(_loadedImagePath);
            }

            _offsetX = BarrageSettings.CharacterWidgetOffsetX;
            _offsetY = BarrageSettings.CharacterWidgetOffsetY;
            _imageScale = BarrageSettings.CharacterWidgetScale > 0 ? BarrageSettings.CharacterWidgetScale : 1.0;

            _barrageHeight = Math.Max(BarrageSettings.FontSize, 36) + 16;
            BarragePreviewBar.Height = _barrageHeight;
            BarragePreviewBar.CornerRadius = BarrageSettings.BackgroundCornerRadius;
            
            _maxBarrageWidth = Math.Max(300, BarrageSettings.MaxTextLength * BarrageSettings.FontSize * 0.55);

            _isUpdatingFromCode = true;
            ScaleText.Text = $"{(_imageScale * 100):F0}%";
            ScaleSlider.Value = _imageScale * 100;
            OffsetXBox.Text = _offsetX.ToString("F0");
            OffsetYBox.Text = _offsetY.ToString("F0");
            _isUpdatingFromCode = false;
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

                _currentBitmap = bitmap;
                _loadedImagePath = path;

                CharacterThumb.ApplyTemplate();
                var imageEl = (Image)CharacterThumb.Template.FindName("CharacterImageElement", CharacterThumb);
                if (imageEl != null)
                {
                    imageEl.Source = bitmap;
                    RenderOptions.SetBitmapScalingMode(imageEl, BitmapScalingMode.HighQuality);
                }

                UpdateCharacterThumbSize();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载角色图片失败: {ex.Message}");
            }
        }

        private void UpdateCharacterThumbSize()
        {
            if (_currentBitmap == null) return;

            double baseHeight = BarrageSettings.FontSize * 2.8;
            double targetH = baseHeight * _imageScale;
            double targetW = targetH * (_currentBitmap.PixelWidth / (double)_currentBitmap.PixelHeight);

            CharacterThumb.Width = targetW;
            CharacterThumb.Height = targetH;

            UpdateImagePosition();
        }

        private void UpdateCanvasLayout()
        {
            double simRatio = LengthSimulationSlider.Value / 100.0;
            double currentBarrageW = _maxBarrageWidth * simRatio;
            BarragePreviewBar.Width = Math.Max(220, currentBarrageW);

            // 弹幕在 900x400 画板居中偏下放置，留出顶部探出空间
            double barrageLeft = (900 - BarragePreviewBar.Width) / 2.0;
            double barrageTop = 200;

            Canvas.SetLeft(BarragePreviewBar, barrageLeft);
            Canvas.SetTop(BarragePreviewBar, barrageTop);

            UpdateImagePosition();
        }

        private void UpdateImagePosition()
        {
            if (_currentBitmap == null) return;

            double barrageLeft = Canvas.GetLeft(BarragePreviewBar);
            double barrageTop = Canvas.GetTop(BarragePreviewBar);
            double barrageW = BarragePreviewBar.Width;
            double barrageH = BarragePreviewBar.Height;

            double charW = CharacterThumb.Width;
            double charH = CharacterThumb.Height;

            double baseAnchorX = barrageLeft;
            double baseAnchorY = barrageTop;

            switch (_currentAnchor)
            {
                case ImageAnchor.TopLeft:
                case ImageAnchor.MiddleLeft:
                case ImageAnchor.BottomLeft:
                    baseAnchorX = barrageLeft;
                    break;
                case ImageAnchor.TopCenter:
                case ImageAnchor.MiddleCenter:
                case ImageAnchor.BottomCenter:
                    baseAnchorX = barrageLeft + (barrageW - charW) / 2.0;
                    break;
                case ImageAnchor.TopRight:
                case ImageAnchor.MiddleRight:
                case ImageAnchor.BottomRight:
                    baseAnchorX = barrageLeft + barrageW - charW * 0.95;
                    break;
            }

            switch (_currentAnchor)
            {
                case ImageAnchor.TopLeft:
                case ImageAnchor.TopCenter:
                case ImageAnchor.TopRight:
                    baseAnchorY = barrageTop - charH;
                    break;
                case ImageAnchor.MiddleLeft:
                case ImageAnchor.MiddleCenter:
                case ImageAnchor.MiddleRight:
                    baseAnchorY = barrageTop + (barrageH - charH) / 2.0;
                    break;
                case ImageAnchor.BottomLeft:
                case ImageAnchor.BottomCenter:
                case ImageAnchor.BottomRight:
                    baseAnchorY = barrageTop + barrageH - charH * 0.30;
                    break;
            }

            double targetLeft = baseAnchorX + _offsetX;
            double targetTop = baseAnchorY + _offsetY;

            Canvas.SetLeft(CharacterThumb, targetLeft);
            Canvas.SetTop(CharacterThumb, targetTop);
        }

        private void CharacterThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            _offsetX += e.HorizontalChange;
            _offsetY += e.VerticalChange;

            _isUpdatingFromCode = true;
            OffsetXBox.Text = _offsetX.ToString("F0");
            OffsetYBox.Text = _offsetY.ToString("F0");
            _isUpdatingFromCode = false;

            UpdateImagePosition();
        }

        private void SelectImage_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Title = "选择角色挂件图片",
                Filter = "图片文件 (*.png;*.jpg;*.jpeg;*.webp)|*.png;*.jpg;*.jpeg;*.webp|所有文件 (*.*)|*.*"
            };

            if (ofd.ShowDialog() == true)
            {
                LoadImageToCanvas(ofd.FileName);
                UpdateImagePosition();
            }
        }

        private void LengthSimulationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (BarragePreviewBar != null)
            {
                UpdateCanvasLayout();
            }
        }

        private void ScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingFromCode) return;
            if (ScaleText == null || CharacterThumb == null) return;

            _imageScale = e.NewValue / 100.0;
            ScaleText.Text = $"{e.NewValue:F0}%";

            UpdateCharacterThumbSize();
        }

        private void OffsetBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingFromCode || OffsetXBox == null || OffsetYBox == null) return;

            if (double.TryParse(OffsetXBox.Text, out double newX)) _offsetX = newX;
            if (double.TryParse(OffsetYBox.Text, out double newY)) _offsetY = newY;

            UpdateImagePosition();
        }

        private void Anchor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton tb)
            {
                if (tb == AnchorTopLeft) _currentAnchor = ImageAnchor.TopLeft;
                else if (tb == AnchorTopCenter) _currentAnchor = ImageAnchor.TopCenter;
                else if (tb == AnchorTopRight) _currentAnchor = ImageAnchor.TopRight;
                else if (tb == AnchorMiddleLeft) _currentAnchor = ImageAnchor.MiddleLeft;
                else if (tb == AnchorMiddleCenter) _currentAnchor = ImageAnchor.MiddleCenter;
                else if (tb == AnchorMiddleRight) _currentAnchor = ImageAnchor.MiddleRight;
                else if (tb == AnchorBottomLeft) _currentAnchor = ImageAnchor.BottomLeft;
                else if (tb == AnchorBottomCenter) _currentAnchor = ImageAnchor.BottomCenter;
                else if (tb == AnchorBottomRight) _currentAnchor = ImageAnchor.BottomRight;

                UpdateAnchorUI();
                UpdateImagePosition();
            }
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

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_loadedImagePath) && File.Exists(_loadedImagePath))
            {
                BarrageSettings.CharacterWidgetPath = _loadedImagePath;
            }

            BarrageSettings.CharacterWidgetScale = _imageScale;
            BarrageSettings.CharacterWidgetOffsetX = _offsetX;
            BarrageSettings.CharacterWidgetOffsetY = _offsetY;
            BarrageSettings.ShowCharacterWidget = true;

            BarrageSettings.ExportConfig();
            this.Close();
        }
    }
}
