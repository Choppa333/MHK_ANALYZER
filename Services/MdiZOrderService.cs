using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace MGK_Analyzer.Services
{
    public static class MdiZOrderService
    {
        private static int _currentZ;

        public static int CurrentZ => _currentZ;

        public static void BringToFront(UIElement element)
        {
            if (element == null) return;
            var newZ = Interlocked.Increment(ref _currentZ);
            Canvas.SetZIndex(element, newZ);
        }
    }
}
