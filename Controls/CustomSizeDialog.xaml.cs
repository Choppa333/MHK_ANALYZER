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
            
            // �ؽ�Ʈ�ڽ� ����
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
                MessageBox.Show("�ʺ�� 300 �̻��� ���ڿ��� �մϴ�.", "�Է� ����", 
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                WidthTextBox.Focus();
                WidthTextBox.SelectAll();
                return false;
            }

            if (!double.TryParse(HeightTextBox.Text, out double height) || height < 200)
            {
                MessageBox.Show("���̴� 200 �̻��� ���ڿ��� �մϴ�.", "�Է� ����", 
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                HeightTextBox.Focus();
                HeightTextBox.SelectAll();
                return false;
            }

            if (width > 2560 || height > 1440)
            {
                var result = MessageBox.Show("�ſ� ū ũ���Դϴ�. ��� �����Ͻðڽ��ϱ�?", "ũ�� Ȯ��", 
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