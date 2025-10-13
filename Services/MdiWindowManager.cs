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
        private List<Mdi3DSurfaceWindow> _surface3DWindows = new List<Mdi3DSurfaceWindow>();
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
            
            // 새 윈도우 위치 설정 (계단식)
            var left = _windowOffset * 30;
            var top = _windowOffset * 30;
            
            // Canvas 경계 체크
            if (left + window.Width > _mdiCanvas.ActualWidth - 50)
                left = 20;
            if (top + window.Height > _mdiCanvas.ActualHeight - 50)
                top = 20;
                
            Canvas.SetLeft(window, left);
            Canvas.SetTop(window, top);
            Canvas.SetZIndex(window, ++_windowZIndex);
            
            _mdiCanvas.Children.Add(window);
            _windows.Add(window);
            
            // 이벤트 핸들러 연결
            window.WindowClosed += Window_Closed;
            window.WindowActivated += Window_Activated;
            
            _windowOffset = (_windowOffset + 1) % 10; // 최대 10개까지 계단식
            
            return window;
        }

        public Mdi3DSurfaceWindow Create3DSurfaceWindow(string windowTitle)
        {
            var window = new Mdi3DSurfaceWindow
            {
                Width = 1000,
                Height = 700,
                WindowTitle = windowTitle
            };
            
            // 새 윈도우 위치 설정 (계단식)
            var left = _windowOffset * 30;
            var top = _windowOffset * 30;
            
            // Canvas 경계 체크
            if (left + window.Width > _mdiCanvas.ActualWidth - 50)
                left = 20;
            if (top + window.Height > _mdiCanvas.ActualHeight - 50)
                top = 20;
                
            Canvas.SetLeft(window, left);
            Canvas.SetTop(window, top);
            Canvas.SetZIndex(window, ++_windowZIndex);
            
            _mdiCanvas.Children.Add(window);
            _surface3DWindows.Add(window);
            
            // 이벤트 핸들러 연결
            window.WindowClosed += Surface3DWindow_Closed;
            window.WindowMinimized += Surface3DWindow_Minimized;
            window.WindowMaximized += Surface3DWindow_Maximized;
            
            _windowOffset = (_windowOffset + 1) % 10; // 최대 10개까지 계단식
            
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

        private void Surface3DWindow_Closed(object sender, EventArgs e)
        {
            var window = (Mdi3DSurfaceWindow)sender;
            _mdiCanvas.Children.Remove(window);
            _surface3DWindows.Remove(window);
            
            window.WindowClosed -= Surface3DWindow_Closed;
            window.WindowMinimized -= Surface3DWindow_Minimized;
            window.WindowMaximized -= Surface3DWindow_Maximized;
        }

        private void Surface3DWindow_Minimized(object sender, EventArgs e)
        {
            var window = (Mdi3DSurfaceWindow)sender;
            // 최소화 로직 추가 가능
        }

        private void Surface3DWindow_Maximized(object sender, EventArgs e)
        {
            var window = (Mdi3DSurfaceWindow)sender;
            Canvas.SetZIndex(window, ++_windowZIndex);
        }

        public void CascadeWindows()
        {
            int index = 0;
            
            // 2D 차트 윈도우 계단식 배치
            for (int i = 0; i < _windows.Count; i++)
            {
                Canvas.SetLeft(_windows[i], index * 30);
                Canvas.SetTop(_windows[i], index * 30);
                Canvas.SetZIndex(_windows[i], index);
                index++;
            }
            
            // 3D Surface 윈도우 계단식 배치
            for (int i = 0; i < _surface3DWindows.Count; i++)
            {
                Canvas.SetLeft(_surface3DWindows[i], index * 30);
                Canvas.SetTop(_surface3DWindows[i], index * 30);
                Canvas.SetZIndex(_surface3DWindows[i], index);
                index++;
            }
        }

        public void TileWindows()
        {
            var allWindows = new List<FrameworkElement>();
            allWindows.AddRange(_windows);
            allWindows.AddRange(_surface3DWindows);
            
            if (allWindows.Count == 0) return;
            
            int cols = (int)Math.Ceiling(Math.Sqrt(allWindows.Count));
            int rows = (int)Math.Ceiling((double)allWindows.Count / cols);
            
            double windowWidth = (_mdiCanvas.ActualWidth - 20) / cols;
            double windowHeight = (_mdiCanvas.ActualHeight - 20) / rows;
            
            for (int i = 0; i < allWindows.Count; i++)
            {
                int row = i / cols;
                int col = i % cols;
                
                Canvas.SetLeft(allWindows[i], col * windowWidth + 10);
                Canvas.SetTop(allWindows[i], row * windowHeight + 10);
                allWindows[i].Width = windowWidth - 10;
                allWindows[i].Height = windowHeight - 10;
            }
        }

        public void MinimizeAll()
        {
            // 2D 차트 윈도우 최소화
            foreach (var window in _windows)
            {
                window.Height = 30; // 타이틀바만 보이도록
            }
            
            // 3D Surface 윈도우 최소화
            foreach (var window in _surface3DWindows)
            {
                window.Visibility = Visibility.Collapsed;
            }
        }

        public void CloseAll()
        {
            // 2D 차트 윈도우 닫기
            var windowsCopy = _windows.ToList();
            foreach (var window in windowsCopy)
            {
                window.Close_Click(null, null);
            }
            
            // 3D Surface 윈도우 닫기
            var surface3DWindowsCopy = _surface3DWindows.ToList();
            foreach (var window in surface3DWindowsCopy)
            {
                window.Close_Click(null, null);
            }
        }

        public int WindowCount => _windows.Count + _surface3DWindows.Count;
    }
}