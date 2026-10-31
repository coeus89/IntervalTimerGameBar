using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace IntervalTimerOverlay
{
    /// <summary>
    /// A <see cref="SystemBackdrop"/> that paints nothing, so whatever is behind the window shows
    /// through wherever the XAML content is itself transparent. Windows App SDK ships Mica and
    /// Acrylic backdrops but no fully transparent one.
    /// </summary>
    /// <remarks>
    /// A transparent composition brush alone still shows black: the HWND's redirection surface
    /// is opaque. Enabling DWM blur-behind (with an off-screen region, so nothing is actually
    /// blurred) makes DWM honour per-pixel alpha, and painting the client area with a black GDI
    /// brush - alpha 0 - on WM_ERASEBKGND keeps that surface clear.
    /// </remarks>
    internal sealed class TransparentBackdrop : SystemBackdrop
    {
        private const uint WM_ERASEBKGND = 0x0014;
        private const uint WM_DWMCOMPOSITIONCHANGED = 0x031E;
        private const uint DWM_BB_ENABLE = 0x1;
        private const uint DWM_BB_BLURREGION = 0x2;
        private static readonly UIntPtr SubclassId = new(0x17E8);

        // Held (never released) for the process lifetime - the queue dies with its controller.
        private static IntPtr _dispatcherQueueController;

        private readonly SubclassProc _subclassProc;
        private Windows.UI.Composition.Compositor? _compositor;
        private Windows.UI.Composition.CompositionColorBrush? _brush;
        private IntPtr _hwnd;
        private IntPtr _clearBrush;

        public TransparentBackdrop()
        {
            _subclassProc = WndProc;
        }

        protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
        {
            base.OnTargetConnected(connectedTarget, xamlRoot);

            _hwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(xamlRoot.ContentIslandEnvironment.AppWindowId);
            _clearBrush = CreateSolidBrush(0);
            SetWindowSubclass(_hwnd, _subclassProc, SubclassId, UIntPtr.Zero);
            ConfigureDwm(_hwnd, enable: true);

            // The system compositor needs a Windows.System.DispatcherQueue on this thread, which
            // the WinUI 3 (Microsoft.UI.Dispatching) queue doesn't guarantee.
            EnsureSystemDispatcherQueue();
            _compositor ??= new Windows.UI.Composition.Compositor();
            _brush = _compositor.CreateColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
            connectedTarget.SystemBackdrop = _brush;

            var hdc = GetDC(_hwnd);
            ClearBackground(hdc);
            ReleaseDC(_hwnd, hdc);
        }

        protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
        {
            disconnectedTarget.SystemBackdrop = null;
            _brush?.Dispose();
            _brush = null;

            if (_hwnd != IntPtr.Zero)
            {
                RemoveWindowSubclass(_hwnd, _subclassProc, SubclassId);
                ConfigureDwm(_hwnd, enable: false);
                _hwnd = IntPtr.Zero;
            }
            if (_clearBrush != IntPtr.Zero)
            {
                DeleteObject(_clearBrush);
                _clearBrush = IntPtr.Zero;
            }

            base.OnTargetDisconnected(disconnectedTarget);
        }

        private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr refData)
        {
            switch (msg)
            {
                case WM_ERASEBKGND:
                    if (ClearBackground(wParam)) return new IntPtr(1);
                    break;
                case WM_DWMCOMPOSITIONCHANGED:
                    ConfigureDwm(hwnd, enable: true);
                    break;
            }
            return DefSubclassProc(hwnd, msg, wParam, lParam);
        }

        private bool ClearBackground(IntPtr hdc)
        {
            if (hdc == IntPtr.Zero || _clearBrush == IntPtr.Zero) return false;
            if (!GetClientRect(_hwnd, out var rect)) return false;
            FillRect(hdc, ref rect, _clearBrush);
            return true;
        }

        private static void ConfigureDwm(IntPtr hwnd, bool enable)
        {
            var margins = new Margins();
            DwmExtendFrameIntoClientArea(hwnd, ref margins);

            var region = enable ? CreateRectRgn(-2, -2, -1, -1) : IntPtr.Zero;
            var blur = new DwmBlurBehind
            {
                dwFlags = enable ? DWM_BB_ENABLE | DWM_BB_BLURREGION : DWM_BB_ENABLE,
                fEnable = enable,
                hRgnBlur = region,
            };
            DwmEnableBlurBehindWindow(hwnd, ref blur);
            if (region != IntPtr.Zero) DeleteObject(region);
        }

        private static void EnsureSystemDispatcherQueue()
        {
            if (Windows.System.DispatcherQueue.GetForCurrentThread() != null) return;
            if (_dispatcherQueueController != IntPtr.Zero) return;

            var options = new DispatcherQueueOptions
            {
                dwSize = Marshal.SizeOf<DispatcherQueueOptions>(),
                threadType = 2,    // DQTYPE_THREAD_CURRENT
                apartmentType = 2, // DQTAT_COM_STA
            };
            CreateDispatcherQueueController(options, out var controller);
            _dispatcherQueueController = controller;
        }

        private delegate IntPtr SubclassProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr refData);

        [StructLayout(LayoutKind.Sequential)]
        private struct DispatcherQueueOptions
        {
            public int dwSize;
            public int threadType;
            public int apartmentType;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Margins
        {
            public int Left;
            public int Right;
            public int Top;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DwmBlurBehind
        {
            public uint dwFlags;
            [MarshalAs(UnmanagedType.Bool)] public bool fEnable;
            public IntPtr hRgnBlur;
            [MarshalAs(UnmanagedType.Bool)] public bool fTransitionOnMaximized;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("CoreMessaging.dll")]
        private static extern int CreateDispatcherQueueController(
            DispatcherQueueOptions options, out IntPtr dispatcherQueueController);

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

        [DllImport("dwmapi.dll")]
        private static extern int DwmEnableBlurBehindWindow(IntPtr hwnd, ref DwmBlurBehind blurBehind);

        [DllImport("comctl32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc proc, UIntPtr id, UIntPtr refData);

        [DllImport("comctl32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RemoveWindowSubclass(IntPtr hwnd, SubclassProc proc, UIntPtr id);

        [DllImport("comctl32.dll")]
        private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(IntPtr hwnd, out Rect rect);

        [DllImport("user32.dll")]
        private static extern int FillRect(IntPtr hdc, ref Rect rect, IntPtr brush);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateSolidBrush(uint color);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr handle);
    }
}
