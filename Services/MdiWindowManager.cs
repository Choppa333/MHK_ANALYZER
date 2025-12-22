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
        private List<MdiNTCurveChartWindow> _ntCurveWindows = new List<MdiNTCurveChartWindow>();
        private List<MdiContour2DWindow> _contour2DWindows = new List<MdiContour2DWindow>();
        private List<MdiSurface3DWindow> _surface3DWindows = new List<MdiSurface3DWindow>();
        private int _windowZIndex = 0;
        private int _windowOffset = 0;

        public MdiWindowManager(Canvas canvas)
        {
            _mdiCanvas = canvas;
        }

        public MdiChartWindow CreateChartWindow(string fileName, MemoryOptimizedDataSet dataSet, IEnumerable<string>? initialSeries = null)
        {
            var window = new MdiChartWindow
            {
                Width = 800,
                Height = 600,
                WindowTitle = $"Chart - {fileName}",
            };
            if (initialSeries != null)
            {
                window.SetInitialSeriesSelection(initialSeries);
            }
            window.DataSet = dataSet;
            
            var left = _windowOffset * 30;
            var top = _windowOffset * 30;
            
            if (left + window.Width > _mdiCanvas.ActualWidth - 50)
                left = 20;
            if (top + window.Height > _mdiCanvas.ActualHeight - 50)
                top = 20;
                
            Canvas.SetLeft(window, left);
            Canvas.SetTop(window, top);
            MGK_Analyzer.Services.MdiZOrderService.BringToFront(window);
            
            _mdiCanvas.Children.Add(window);
            _windows.Add(window);
            
            window.WindowClosed += Window_Closed;
            window.WindowActivated += Window_Activated;
            
            _windowOffset = (_windowOffset + 1) % 10;
            
            return window;
        }

        public MdiNTCurveChartWindow CreateNTCurveChartWindow(string fileName, MemoryOptimizedDataSet dataSet, IEnumerable<string>? initialSeries = null)
        {
            var window = new MdiNTCurveChartWindow
            {
                Width = 800,
                Height = 600,
                WindowTitle = $"NT-Curve - {fileName}"
            };

            if (initialSeries != null)
            {
                window.SetInitialSeriesSelection(initialSeries);
            }

            window.DataSet = dataSet;

            var left = _windowOffset * 30;
            var top = _windowOffset * 30;

            if (left + window.Width > _mdiCanvas.ActualWidth - 50)
                left = 20;
            if (top + window.Height > _mdiCanvas.ActualHeight - 50)
                top = 20;

            Canvas.SetLeft(window, left);
            Canvas.SetTop(window, top);
            MdiZOrderService.BringToFront(window);

            _mdiCanvas.Children.Add(window);
            _ntCurveWindows.Add(window);

            window.WindowClosed += NtCurveWindow_Closed;
            window.WindowActivated += NtCurveWindow_Activated;

            _windowOffset = (_windowOffset + 1) % 10;

            return window;
        }

        public void AddWindow(MdiChartWindow window)
        {
            var left = _windowOffset * 30;
            var top = _windowOffset * 30;

            if (left + window.Width > _mdiCanvas.ActualWidth - 50)
                left = 20;
            if (top + window.Height > _mdiCanvas.ActualHeight - 50)
                top = 20;

            Canvas.SetLeft(window, left);
            Canvas.SetTop(window, top);
            MdiZOrderService.BringToFront(window);

            _mdiCanvas.Children.Add(window);
            _windows.Add(window);

            window.WindowClosed += Window_Closed;
            window.WindowActivated += Window_Activated;

            _windowOffset = (_windowOffset + 1) % 10;
        }

        public MdiContour2DWindow CreateContour2DWindow(string windowTitle)
        {
            var window = new MdiContour2DWindow
            {
                Width = 1000,
                Height = 700,
                WindowTitle = windowTitle
            };

            var left = _windowOffset * 30;
            var top = _windowOffset * 30;

            if (left + window.Width > _mdiCanvas.ActualWidth - 50)
                left = 20;
            if (top + window.Height > _mdiCanvas.ActualHeight - 50)
                top = 20;

            Canvas.SetLeft(window, left);
            Canvas.SetTop(window, top);
            MGK_Analyzer.Services.MdiZOrderService.BringToFront(window);

            _mdiCanvas.Children.Add(window);
            _contour2DWindows.Add(window);

            window.WindowClosed += Contour2DWindow_Closed;
            window.WindowActivated += Contour2DWindow_Activated;

            _windowOffset = (_windowOffset + 1) % 10;

            return window;
        }

        public MdiSurface3DWindow CreateSurface3DWindow(string windowTitle)
        {
            var window = new MdiSurface3DWindow
            {
                Width = 1000,
                Height = 700,
                WindowTitle = windowTitle
            };

            var left = _windowOffset * 30;
            var top = _windowOffset * 30;

            if (left + window.Width > _mdiCanvas.ActualWidth - 50)
                left = 20;
            if (top + window.Height > _mdiCanvas.ActualHeight - 50)
                top = 20;

            Canvas.SetLeft(window, left);
            Canvas.SetTop(window, top);
            MGK_Analyzer.Services.MdiZOrderService.BringToFront(window);

            _mdiCanvas.Children.Add(window);
            _surface3DWindows.Add(window);

            _windowOffset = (_windowOffset + 1) % 10;

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
            for (int i = 0; i < _windows.Count; i++)
            {
                Canvas.SetLeft(_windows[i], index * 30);
                Canvas.SetTop(_windows[i], index * 30);
                Canvas.SetZIndex(_windows[i], index);
                index++;
            }

            for (int i = 0; i < _ntCurveWindows.Count; i++)
            {
                Canvas.SetLeft(_ntCurveWindows[i], index * 30);
                Canvas.SetTop(_ntCurveWindows[i], index * 30);
                Canvas.SetZIndex(_ntCurveWindows[i], index);
                index++;
            }

            for (int i = 0; i < _contour2DWindows.Count; i++)
            {
                Canvas.SetLeft(_contour2DWindows[i], index * 30);
                Canvas.SetTop(_contour2DWindows[i], index * 30);
                Canvas.SetZIndex(_contour2DWindows[i], index);
                index++;
            }

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
            allWindows.AddRange(_ntCurveWindows);
            allWindows.AddRange(_contour2DWindows);
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
            foreach (var window in _windows)
                window.Height = 30;
            foreach (var window in _ntCurveWindows)
                window.Height = 30;
            foreach (var window in _contour2DWindows)
                window.Height = 30;
            foreach (var window in _surface3DWindows)
                window.Height = 30;
        }

        public void CloseAll()
        {
            var windowsCopy = _windows.ToList();
            foreach (var window in windowsCopy)
                window.Close_Click(null, null);

            var ntWindowsCopy = _ntCurveWindows.ToList();
            foreach (var window in ntWindowsCopy)
                window.Close_Click(null, null);

            var contourCopy = _contour2DWindows.ToList();
            foreach (var window in contourCopy)
                window.Close_Click(null, null);

            var surfaceCopy = _surface3DWindows.ToList();
            foreach (var window in surfaceCopy)
                _mdiCanvas.Children.Remove(window);
            _surface3DWindows.Clear();
        }

        public int WindowCount => _windows.Count + _ntCurveWindows.Count + _contour2DWindows.Count + _surface3DWindows.Count;

        private void NtCurveWindow_Closed(object sender, EventArgs e)
        {
            var window = (MdiNTCurveChartWindow)sender;
            _mdiCanvas.Children.Remove(window);
            _ntCurveWindows.Remove(window);

            window.WindowClosed -= NtCurveWindow_Closed;
            window.WindowActivated -= NtCurveWindow_Activated;
        }

        private void NtCurveWindow_Activated(object sender, EventArgs e)
        {
            var window = (MdiNTCurveChartWindow)sender;
            MdiZOrderService.BringToFront(window);
        }
    }
}