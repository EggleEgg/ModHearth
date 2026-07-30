using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace ModHearth.UI
{
    public interface IWindowDragTracker : IDisposable
    {
        bool IsDragging { get; }

        event EventHandler? DragStarted;
        event EventHandler? DragFinished;
    }

    public static class WindowDragTracker
    {
        public static IWindowDragTracker Create(Window window)
        {
            if (OperatingSystem.IsWindows())
            {
                return new WindowsWindowDragTracker(window);
            }
            else if (OperatingSystem.IsMacOS())
            {
                return new MacWindowDragTracker(window);
            }
            else
            {
                return new FallbackWindowDragTracker(window);
            }
        }
    }

    public sealed class WindowsWindowDragTracker : IWindowDragTracker
    {
        private readonly Window _window;
        private IntPtr _hwnd;
        private WndProcDelegate? _wndProc;
        private bool _isDragging;

        public bool IsDragging => _isDragging;

        public event EventHandler? DragStarted;
        public event EventHandler? DragFinished;

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, IntPtr subclassId, IntPtr refData);

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern bool SetWindowSubclass(IntPtr hWnd, WndProcDelegate callback, IntPtr subclassId, IntPtr refData);

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern bool RemoveWindowSubclass(IntPtr hWnd, WndProcDelegate callback, IntPtr subclassId);

        private const uint WM_ENTERSIZEMOVE = 0x0231;
        private const uint WM_EXITSIZEMOVE = 0x0232;
        private const uint WM_DESTROY = 0x0002;
        private static readonly IntPtr SubclassId = new IntPtr(1042);

        public WindowsWindowDragTracker(Window window)
        {
            _window = window;
            if (window.IsVisible)
            {
                Initialize();
            }
            else
            {
                window.Opened += OnWindowOpened;
            }
        }

        private void OnWindowOpened(object? sender, EventArgs e)
        {
            _window.Opened -= OnWindowOpened;
            Initialize();
        }

        private void Initialize()
        {
            _hwnd = _window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (_hwnd != IntPtr.Zero)
            {
                _wndProc = new WndProcDelegate(SubclassWndProc);
                SetWindowSubclass(_hwnd, _wndProc, SubclassId, IntPtr.Zero);
            }
        }

        private IntPtr SubclassWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, IntPtr subclassId, IntPtr refData)
        {
            switch (msg)
            {
                case WM_ENTERSIZEMOVE:
                    _isDragging = true;
                    DragStarted?.Invoke(this, EventArgs.Empty);
                    break;

                case WM_EXITSIZEMOVE:
                    _isDragging = false;
                    DragFinished?.Invoke(this, EventArgs.Empty);
                    break;

                case WM_DESTROY:
                    Cleanup();
                    break;
            }

            return DefSubclassProc(hWnd, msg, wParam, lParam);
        }

        private void Cleanup()
        {
            if (_hwnd != IntPtr.Zero && _wndProc != null)
            {
                RemoveWindowSubclass(_hwnd, _wndProc, SubclassId);
                _wndProc = null;
                _hwnd = IntPtr.Zero;
            }
        }

        public void Dispose()
        {
            _window.Opened -= OnWindowOpened;
            Cleanup();
        }
    }

    public sealed class MacWindowDragTracker : IWindowDragTracker
    {
        private readonly Window _window;
        private IntPtr _nsWindow;
        private IntPtr _observerInstance;
        private bool _isDragging;
        private Delegate? _willMoveDelegate;
        private Delegate? _didMoveDelegate;

        public bool IsDragging => _isDragging;

        public event EventHandler? DragStarted;
        public event EventHandler? DragFinished;

        [DllImport("/usr/lib/libobjc.A.dylib")]
        private static extern IntPtr objc_getClass(string className);

        [DllImport("/usr/lib/libobjc.A.dylib")]
        private static extern IntPtr sel_registerName(string selectorName);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg1);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2, IntPtr arg3, IntPtr arg4);

        [DllImport("/usr/lib/libobjc.A.dylib")]
        private static extern IntPtr objc_allocateClassPair(IntPtr superclass, string className, int extraBytes);

        [DllImport("/usr/lib/libobjc.A.dylib")]
        private static extern void objc_registerClassPair(IntPtr cls);

        [DllImport("/usr/lib/libobjc.A.dylib")]
        private static extern bool class_addMethod(IntPtr cls, IntPtr name, Delegate imp, string types);

        private delegate void NotificationCallbackDelegate(IntPtr self, IntPtr _cmd, IntPtr notification);

        public MacWindowDragTracker(Window window)
        {
            _window = window;
            if (window.IsVisible)
            {
                Initialize();
            }
            else
            {
                window.Opened += OnWindowOpened;
            }
        }

        private void OnWindowOpened(object? sender, EventArgs e)
        {
            _window.Opened -= OnWindowOpened;
            Initialize();
        }

        private void Initialize()
        {
            _nsWindow = _window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (_nsWindow == IntPtr.Zero) return;

            try
            {
                IntPtr nsObjectClass = objc_getClass("NSObject");
                string observerClassName = "ModHearthDragObserver_" + Guid.NewGuid().ToString("N");
                IntPtr observerClass = objc_allocateClassPair(nsObjectClass, observerClassName, 0);

                if (observerClass != IntPtr.Zero)
                {
                    _willMoveDelegate = new NotificationCallbackDelegate(OnWillMoveNotification);
                    _didMoveDelegate = new NotificationCallbackDelegate(OnDidMoveNotification);

                    class_addMethod(observerClass, sel_registerName("windowWillMove:"), _willMoveDelegate, "v@:@");
                    class_addMethod(observerClass, sel_registerName("windowDidMove:"), _didMoveDelegate, "v@:@");

                    objc_registerClassPair(observerClass);

                    IntPtr allocSel = sel_registerName("alloc");
                    IntPtr initSel = sel_registerName("init");
                    IntPtr allocated = objc_msgSend(observerClass, allocSel);
                    _observerInstance = objc_msgSend(allocated, initSel);

                    if (_observerInstance != IntPtr.Zero)
                    {
                        IntPtr notificationCenterClass = objc_getClass("NSNotificationCenter");
                        IntPtr defaultCenterSel = sel_registerName("defaultCenter");
                        IntPtr defaultCenter = objc_msgSend(notificationCenterClass, defaultCenterSel);

                        IntPtr addObserverSel = sel_registerName("addObserver:selector:name:object:");

                        IntPtr nsStringClass = objc_getClass("NSString");
                        IntPtr stringWithUtf8Sel = sel_registerName("stringWithUTF8String:");

                        IntPtr willMoveUtf8 = Marshal.StringToHGlobalAnsi("NSWindowWillMoveNotification");
                        IntPtr willMoveName = objc_msgSend(nsStringClass, stringWithUtf8Sel, willMoveUtf8);
                        objc_msgSend(defaultCenter, addObserverSel, _observerInstance, sel_registerName("windowWillMove:"), willMoveName, _nsWindow);
                        Marshal.FreeHGlobal(willMoveUtf8);

                        IntPtr didMoveUtf8 = Marshal.StringToHGlobalAnsi("NSWindowDidMoveNotification");
                        IntPtr didMoveName = objc_msgSend(nsStringClass, stringWithUtf8Sel, didMoveUtf8);
                        objc_msgSend(defaultCenter, addObserverSel, _observerInstance, sel_registerName("windowDidMove:"), didMoveName, _nsWindow);
                        Marshal.FreeHGlobal(didMoveUtf8);
                    }
                }
            }
            catch
            {
                // Fallback / ignore native exceptions
            }
        }

        private void OnWillMoveNotification(IntPtr self, IntPtr _cmd, IntPtr notification)
        {
            if (!_isDragging)
            {
                _isDragging = true;
                DragStarted?.Invoke(this, EventArgs.Empty);
            }
        }

        private void OnDidMoveNotification(IntPtr self, IntPtr _cmd, IntPtr notification)
        {
            if (_isDragging)
            {
                _isDragging = false;
                DragFinished?.Invoke(this, EventArgs.Empty);
            }
        }

        public void Dispose()
        {
            _window.Opened -= OnWindowOpened;
            if (_observerInstance != IntPtr.Zero)
            {
                try
                {
                    IntPtr notificationCenterClass = objc_getClass("NSNotificationCenter");
                    IntPtr defaultCenterSel = sel_registerName("defaultCenter");
                    IntPtr defaultCenter = objc_msgSend(notificationCenterClass, defaultCenterSel);

                    IntPtr removeObserverSel = sel_registerName("removeObserver:");
                    objc_msgSend(defaultCenter, removeObserverSel, _observerInstance);
                }
                catch
                {
                    // Ignore
                }
                _observerInstance = IntPtr.Zero;
            }
        }
    }

    public sealed class FallbackWindowDragTracker : IWindowDragTracker
    {
        private readonly Window _window;
        private DateTime _lastMoveTime;
        private bool _movementCheckScheduled;
        private bool _isDragging;

        public bool IsDragging => _isDragging;

        public event EventHandler? DragStarted;
        public event EventHandler? DragFinished;

        public FallbackWindowDragTracker(Window window)
        {
            _window = window;
            _window.PositionChanged += OnPositionChanged;
        }

        private void OnPositionChanged(object? sender, PixelPointEventArgs e)
        {
            if (!_isDragging)
            {
                _isDragging = true;
                DragStarted?.Invoke(this, EventArgs.Empty);
            }

            _lastMoveTime = DateTime.UtcNow;
            if (!_movementCheckScheduled)
            {
                _movementCheckScheduled = true;
                CheckForMovementStop();
            }
        }

        private async void CheckForMovementStop()
        {
            while (true)
            {
                await Task.Delay(40);

                if (_window == null || !_window.IsVisible)
                {
                    _movementCheckScheduled = false;
                    _isDragging = false;
                    return;
                }

                //Using a timer is not a perfect solution, but no .Net library exists that exposes native window drag state on Linux
                if (DateTime.UtcNow - _lastMoveTime > TimeSpan.FromMilliseconds(800))
                    break;
            }

            _movementCheckScheduled = false;
            _isDragging = false;
            DragFinished?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            if (_window != null)
            {
                _window.PositionChanged -= OnPositionChanged;
            }
        }
    }
}
