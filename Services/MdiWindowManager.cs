using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MGK_Analyzer.Controls;
using MGK_Analyzer.Models;
using System.Diagnostics;

namespace MGK_Analyzer.Services
{
    public class MdiWindowManager
    {
        private Canvas _mdiCanvas;
        private List<MdiChartWindow> _windows = new List<MdiChartWindow>();
        private List<MdiContour2DWindow> _contour2DWindows = new List<MdiContour2DWindow>();
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
            MGK_Analyzer.Services.MdiZOrderService.BringToFront(window);
            
            _mdiCanvas.Children.Add(window);
            _windows.Add(window);
            
            // 이벤트 핸들러 연결
            window.WindowClosed += Window_Closed;
            window.WindowActivated += Window_Activated;
            
            _windowOffset = (_windowOffset + 1) % 10; // 최대 10개까지 계단식
            
            return window;
        }

        public MdiContour2DWindow CreateContour2DWindow(string windowTitle)
        {
            var window = new MdiContour2DWindow
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
            MGK_Analyzer.Services.MdiZOrderService.BringToFront(window);

            _mdiCanvas.Children.Add(window);
            _contour2DWindows.Add(window);

            // 이벤트 핸들러 연결
            window.WindowClosed += Contour2DWindow_Closed;
            window.WindowActivated += Contour2DWindow_Activated;

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
            MGK_Analyzer.Services.MdiZOrderService.BringToFront(window);
        }

        private void Contour2DWindow_Closed(object sender, EventArgs e)
        {
            var window = (MdiContour2DWindow)sender;
            _mdiCanvas.Children.Remove(window);
            _contour2DWindows.Remove(window);

            window.WindowClosed -= Contour2DWindow_Closed;
            window.WindowActivated -= Contour2DWindow_Activated;
        }

        private void Contour2DWindow_Activated(object sender, EventArgs e)
        {
            var window = (MdiContour2DWindow)sender;
            MGK_Analyzer.Services.MdiZOrderService.BringToFront(window);
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

            // Contour 2D 윈도우 계단식 배치
            for (int i = 0; i < _contour2DWindows.Count; i++)
            {
                Canvas.SetLeft(_contour2DWindows[i], index * 30);
                Canvas.SetTop(_contour2DWindows[i], index * 30);
                Canvas.SetZIndex(_contour2DWindows[i], index);
                index++;
            }
        }

        public void TileWindows()
        {
            var allWindows = new List<FrameworkElement>();
            allWindows.AddRange(_windows);
            allWindows.AddRange(_contour2DWindows);
            
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
            // Contour 2D 윈도우 최소화
            foreach (var window in _contour2DWindows)
            {
                window.Height = 30; // 동일 동작
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
            // Contour 2D 윈도우 닫기
            var contourCopy = _contour2DWindows.ToList();
            foreach (var window in contourCopy)
            {
                window.Close_Click(null, null);
            }
        }

        public int WindowCount => _windows.Count + _contour2DWindows.Count;
    }
}