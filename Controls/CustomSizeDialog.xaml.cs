using System;
using System.Windows;

namespace MGK_Analyzer.Controls
{
    public partial class CustomSizeDialog : Window
    {
        public double SelectedWidth { get; private set; }
        public double SelectedHeight { get; private set; }

        public CustomSizeDialog(double currentWidth, double currentHeight)
        {
            InitializeComponent();
            
            CurrentWidthText.Text = currentWidth.ToString("F0");
            CurrentHeightText.Text = currentHeight.ToString("F0");
            
            WidthTextBox.Text = currentWidth.ToString("F0");
            HeightTextBox.Text = currentHeight.ToString("F0");
            
            SelectedWidth = currentWidth;
            SelectedHeight = currentHeight;
            
            // 텍스트박스 선택
            WidthTextBox.Focus();
            WidthTextBox.SelectAll();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (ValidateInput())
            {
                SelectedWidth = double.Parse(WidthTextBox.Text);
                SelectedHeight = double.Parse(HeightTextBox.Text);
                
                DialogResult = true;
                Close();
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private bool ValidateInput()
        {
            if (!double.TryParse(WidthTextBox.Text, out double width) || width < 300)
            {
                MessageBox.Show("너비는 300 이상의 숫자여야 합니다.", "입력 오류", 
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                WidthTextBox.Focus();
                WidthTextBox.SelectAll();
                return false;
            }

            if (!double.TryParse(HeightTextBox.Text, out double height) || height < 200)
            {
                MessageBox.Show("높이는 200 이상의 숫자여야 합니다.", "입력 오류", 
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                HeightTextBox.Focus();
                HeightTextBox.SelectAll();
                return false;
            }

            if (width > 2560 || height > 1440)
            {
                var result = MessageBox.Show("매우 큰 크기입니다. 계속 진행하시겠습니까?", "크기 확인", 
                                           MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.No)
                {
                    return false;
                }
            }

            return true;
        }

        private void SetSmall_Click(object sender, RoutedEventArgs e)
        {
            WidthTextBox.Text = "400";
            HeightTextBox.Text = "300";
        }

        private void SetMedium_Click(object sender, RoutedEventArgs e)
        {
            WidthTextBox.Text = "800";
            HeightTextBox.Text = "600";
        }

        private void SetLarge_Click(object sender, RoutedEventArgs e)
        {
            WidthTextBox.Text = "1200";
            HeightTextBox.Text = "800";
        }

        private void SetHD_Click(object sender, RoutedEventArgs e)
        {
            WidthTextBox.Text = "1920";
            HeightTextBox.Text = "1080";
        }
    }
}