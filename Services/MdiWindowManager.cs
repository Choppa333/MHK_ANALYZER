using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MGK_Analyzer.Controls;
using MGK_Analyzer.Models;

namespace MGK_Analyzer.Services
{
    public class MdiWindowManager
    {
        private Canvas _mdiCanvas;
        private List<MdiChartWindow> _windows = new List<MdiChartWindow>();
        private int _windowZIndex = 0;
        private int _windowOffset = 0;

        public MdiWindowManager(Canvas canvas)
        {
            _mdiCanvas = canvas;
        }

        public MdiChartWindow CreateChartWindow(string fileName, MemoryOptimizedDataSet dataSet)
        {
            var window = new MdiChartWindow
            {
                Width = 800,
                Height = 600,
                WindowTitle = $"Chart - {fileName}",
                DataSet = dataSet
            };
            
            // �� ������ ��ġ ���� (��ܽ�)
            var left = _windowOffset * 30;
            var top = _windowOffset * 30;
            
            // Canvas ��� üũ
            if (left + window.Width > _mdiCanvas.ActualWidth - 50)
                left = 20;
            if (top + window.Height > _mdiCanvas.ActualHeight - 50)
                top = 20;
                
            Canvas.SetLeft(window, left);
            Canvas.SetTop(window, top);
            Canvas.SetZIndex(window, ++_windowZIndex);
            
            _mdiCanvas.Children.Add(window);
            _windows.Add(window);
            
            // �̺�Ʈ �ڵ鷯 ����
            window.WindowClosed += Window_Closed;
            window.WindowActivated += Window_Activated;
            
            _windowOffset = (_windowOffset + 1) % 10; // �ִ� 10������ ��ܽ�
            
            return window;
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            var window = (MdiChartWindow)sender;
            _mdiCanvas.Children.Remove(window);
            _windows.Remove(window);
            
            window.WindowClosed -= Window_Closed;
            window.WindowActivated -= Window_Activated;
        }

        private void Window_Activated(object sender, EventArgs e)
        {
            var window = (MdiChartWindow)sender;
            Canvas.SetZIndex(window, ++_windowZIndex);
        }

        public void CascadeWindows()
        {
            for (int i = 0; i < _windows.Count; i++)
            {
                Canvas.SetLeft(_windows[i], i * 30);
                Canvas.SetTop(_windows[i], i * 30);
                Canvas.SetZIndex(_windows[i], i);
            }
        }

        public void TileWindows()
        {
            if (_windows.Count == 0) return;
            
            int cols = (int)Math.Ceiling(Math.Sqrt(_windows.Count));
            int rows = (int)Math.Ceiling((double)_windows.Count / cols);
            
            double windowWidth = (_mdiCanvas.ActualWidth - 20) / cols;
            double windowHeight = (_mdiCanvas.ActualHeight - 20) / rows;
            
            for (int i = 0; i < _windows.Count; i++)
            {
                int row = i / cols;
                int col = i % cols;
                
                Canvas.SetLeft(_windows[i], col * windowWidth + 10);
                Canvas.SetTop(_windows[i], row * windowHeight + 10);
                _windows[i].Width = windowWidth - 10;
                _windows[i].Height = windowHeight - 10;
            }
        }

        public void MinimizeAll()
        {
            foreach (var window in _windows)
            {
                window.Height = 30; // Ÿ��Ʋ�ٸ� ���̵���
            }
        }

        public void CloseAll()
        {
            var windowsCopy = _windows.ToList();
            foreach (var window in windowsCopy)
            {
                window.Close_Click(null, null);
            }
        }

        public int WindowCount => _windows.Count;
    }
}