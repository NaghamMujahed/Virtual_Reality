using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;

internal static class WindowsTouchpadGestures
{
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    private const int WindowProcedureIndex = -4;
    private const uint MessageGesture = 0x0119;
    private const uint GestureIdAll = 0;
    private const uint GestureIdZoom = 3;
    private const uint GestureIdPan = 4;
    private const uint GestureFlagBegin = 0x00000001;
    private const uint GestureFlagEnd = 0x00000004;
    private const uint GestureConfigAll = 0x00000001;

    private static readonly object Sync = new object();

    private static WindowProcedure _windowProcedure;
    private static IntPtr _windowHandle;
    private static IntPtr _previousWindowProcedure;
    private static int _clientCount;

    private static ulong _previousZoomDistance;
    private static PointShort _previousPanPoint;
    private static bool _zoomInProgress;
    private static bool _panInProgress;
    private static float _pendingZoom;
    private static Vector2 _pendingPanPixels;
#endif

    public static void Acquire()
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        _clientCount++;
        EnsureHooked();
#endif
    }

    public static void Release()
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        _clientCount = Mathf.Max(0, _clientCount - 1);
        if (_clientCount == 0)
            RemoveHook();
#endif
    }

    public static bool TryConsume(out float zoom, out Vector2 panPixels)
    {
        zoom = 0f;
        panPixels = Vector2.zero;

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        EnsureHooked();
        lock (Sync)
        {
            zoom = _pendingZoom;
            panPixels = _pendingPanPixels;
            _pendingZoom = 0f;
            _pendingPanPixels = Vector2.zero;
        }
#endif

        return Mathf.Abs(zoom) > 0.00001f || panPixels.sqrMagnitude > 0.0001f;
    }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    private static void EnsureHooked()
    {
        if (_windowHandle != IntPtr.Zero || _clientCount <= 0)
            return;

        IntPtr candidate = GetActiveWindow();
        if (candidate == IntPtr.Zero)
            candidate = GetForegroundWindow();
        if (candidate == IntPtr.Zero)
            return;

        GetWindowThreadProcessId(candidate, out uint processId);
        if (processId != (uint)Process.GetCurrentProcess().Id)
            return;

        _windowProcedure = ProcessWindowMessage;
        IntPtr procedurePointer = Marshal.GetFunctionPointerForDelegate(_windowProcedure);
        IntPtr previous = SetWindowProcedure(candidate, procedurePointer);
        if (previous == IntPtr.Zero)
        {
            _windowProcedure = null;
            return;
        }

        _windowHandle = candidate;
        _previousWindowProcedure = previous;

        GestureConfig[] config =
        {
            new GestureConfig
            {
                id = GestureIdAll,
                want = GestureConfigAll,
                block = 0
            }
        };
        bool configured = SetGestureConfig(
            _windowHandle,
            0,
            (uint)config.Length,
            config,
            (uint)Marshal.SizeOf(typeof(GestureConfig)));

        if (!configured)
        {
            UnityEngine.Debug.LogWarning(
                "InteractiveCamera: Windows did not accept the explicit touchpad gesture configuration; using the window defaults.");
        }

        UnityEngine.Debug.Log(
            "InteractiveCamera: Native Windows touchpad pinch and pan input is active.");
    }

    private static void RemoveHook()
    {
        if (_windowHandle != IntPtr.Zero && _previousWindowProcedure != IntPtr.Zero)
        {
            SetWindowProcedure(_windowHandle, _previousWindowProcedure);
        }

        _windowHandle = IntPtr.Zero;
        _previousWindowProcedure = IntPtr.Zero;
        _windowProcedure = null;
        _zoomInProgress = false;
        _panInProgress = false;
    }

    private static IntPtr ProcessWindowMessage(
        IntPtr window,
        uint message,
        IntPtr wordParameter,
        IntPtr longParameter)
    {
        if (message == MessageGesture && TryReadGesture(longParameter))
        {
            CloseGestureInfoHandle(longParameter);
            return IntPtr.Zero;
        }

        return CallWindowProc(
            _previousWindowProcedure,
            window,
            message,
            wordParameter,
            longParameter);
    }

    private static bool TryReadGesture(IntPtr gestureHandle)
    {
        GestureInfo info = new GestureInfo
        {
            size = (uint)Marshal.SizeOf(typeof(GestureInfo))
        };
        if (!GetGestureInfo(gestureHandle, ref info))
            return false;

        switch (info.id)
        {
            case GestureIdZoom:
                ReadZoomGesture(info);
                return true;

            case GestureIdPan:
                ReadPanGesture(info);
                return true;

            default:
                return false;
        }
    }

    private static void ReadZoomGesture(GestureInfo info)
    {
        bool beginning = (info.flags & GestureFlagBegin) != 0;
        bool ending = (info.flags & GestureFlagEnd) != 0;

        lock (Sync)
        {
            if (beginning || !_zoomInProgress)
            {
                _previousZoomDistance = info.arguments;
                _zoomInProgress = true;
            }
            else if (_previousZoomDistance > 0 && info.arguments > 0)
            {
                double ratio = (double)info.arguments / _previousZoomDistance;
                if (ratio > 0.25 && ratio < 4.0)
                    _pendingZoom += (float)Math.Log(ratio);
                _previousZoomDistance = info.arguments;
            }

            if (ending)
                _zoomInProgress = false;
        }
    }

    private static void ReadPanGesture(GestureInfo info)
    {
        bool beginning = (info.flags & GestureFlagBegin) != 0;
        bool ending = (info.flags & GestureFlagEnd) != 0;

        lock (Sync)
        {
            if (beginning || !_panInProgress)
            {
                _previousPanPoint = info.location;
                _panInProgress = true;
            }
            else
            {
                _pendingPanPixels += new Vector2(
                    info.location.x - _previousPanPoint.x,
                    info.location.y - _previousPanPoint.y);
                _previousPanPoint = info.location;
            }

            if (ending)
                _panInProgress = false;
        }
    }

    private static IntPtr SetWindowProcedure(IntPtr window, IntPtr procedure)
    {
        return IntPtr.Size == 8
            ? SetWindowLongPtr64(window, WindowProcedureIndex, procedure)
            : SetWindowLong32(window, WindowProcedureIndex, procedure);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointShort
    {
        public short x;
        public short y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GestureInfo
    {
        public uint size;
        public uint flags;
        public uint id;
        public IntPtr targetWindow;
        public PointShort location;
        public uint instanceId;
        public uint sequenceId;
        public ulong arguments;
        public uint extraArgumentsSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GestureConfig
    {
        public uint id;
        public uint want;
        public uint block;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WindowProcedure(
        IntPtr window,
        uint message,
        IntPtr wordParameter,
        IntPtr longParameter);

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        IntPtr window,
        out uint processId);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(
        IntPtr window,
        int index,
        IntPtr newValue);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern IntPtr SetWindowLong32(
        IntPtr window,
        int index,
        IntPtr newValue);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CallWindowProc(
        IntPtr previousProcedure,
        IntPtr window,
        uint message,
        IntPtr wordParameter,
        IntPtr longParameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGestureInfo(
        IntPtr gestureInfo,
        ref GestureInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseGestureInfoHandle(IntPtr gestureInfo);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetGestureConfig(
        IntPtr window,
        uint reserved,
        uint count,
        [In] GestureConfig[] config,
        uint configSize);
#endif
}
