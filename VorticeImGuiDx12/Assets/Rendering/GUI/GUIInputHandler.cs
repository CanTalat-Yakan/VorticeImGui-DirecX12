﻿using System;

using ImGuiNET;

using Engine.Interoperation;

namespace Engine.GUI
{
    public class GUIInputHandler
    {
        public static GUIInputHandler Instance { get; private set; }

        public IntPtr hwnd;
        ImGuiMouseCursor lastCursor;

        public GUIInputHandler()
        {
            InitKeyMap();

            Instance = this;
        }

        void InitKeyMap()
        {
            var io = ImGui.GetIO();
        }

        public void Update()
        {
            UpdateKeyModifiers();
            UpdateMousePosition();

            var mouseCursor = ImGui.GetIO().MouseDrawCursor ? ImGuiMouseCursor.None : ImGui.GetMouseCursor();
            if (mouseCursor != lastCursor)
            {
                lastCursor = mouseCursor;
                UpdateMouseCursor();
            }
        }

        void UpdateKeyModifiers()
        {
            var io = ImGui.GetIO();
            io.KeyCtrl = (User32.GetKeyState(VK.CONTROL) & 0x8000) != 0;
            io.KeyShift = (User32.GetKeyState(VK.SHIFT) & 0x8000) != 0;
            io.KeyAlt = (User32.GetKeyState(VK.MENU) & 0x8000) != 0;
            io.KeySuper = false;
        }

        public bool UpdateMouseCursor()
        {
            var io = ImGui.GetIO();
            if ((io.ConfigFlags & ImGuiConfigFlags.NoMouseCursorChange) != 0)
                return false;

            var requestedcursor = ImGui.GetMouseCursor();
            if (requestedcursor == ImGuiMouseCursor.None || io.MouseDrawCursor)
                User32.SetCursor(IntPtr.Zero);
            else
            {
                var cursor = SystemCursor.IDC_ARROW;
                switch (requestedcursor)
                {
                    case ImGuiMouseCursor.Arrow: cursor = SystemCursor.IDC_ARROW; break;
                    case ImGuiMouseCursor.TextInput: cursor = SystemCursor.IDC_IBEAM; break;
                    case ImGuiMouseCursor.ResizeAll: cursor = SystemCursor.IDC_SIZEALL; break;
                    case ImGuiMouseCursor.ResizeEW: cursor = SystemCursor.IDC_SIZEWE; break;
                    case ImGuiMouseCursor.ResizeNS: cursor = SystemCursor.IDC_SIZENS; break;
                    case ImGuiMouseCursor.ResizeNESW: cursor = SystemCursor.IDC_SIZENESW; break;
                    case ImGuiMouseCursor.ResizeNWSE: cursor = SystemCursor.IDC_SIZENWSE; break;
                    case ImGuiMouseCursor.Hand: cursor = SystemCursor.IDC_HAND; break;
                    case ImGuiMouseCursor.NotAllowed: cursor = SystemCursor.IDC_NO; break;
                }
                User32.SetCursor(User32.LoadCursor(IntPtr.Zero, cursor));
            }

            return true;
        }

        void UpdateMousePosition()
        {
            var io = ImGui.GetIO();

            if (io.WantSetMousePos)
            {
                var pos = new POINT((int)io.MousePos.X, (int)io.MousePos.Y);
                User32.ClientToScreen(hwnd, ref pos);
                User32.SetCursorPos(pos.X, pos.Y);
            }

            //io.MousePos = new System.Numerics.Vector2(-FLT_MAX, -FLT_MAX);

            var foregroundWindow = User32.GetForegroundWindow();
            if (foregroundWindow == hwnd || User32.IsChild(foregroundWindow, hwnd))
            {
                POINT pos;
                if (User32.GetCursorPos(out pos) && User32.ScreenToClient(hwnd, ref pos))
                    io.MousePos = new System.Numerics.Vector2(pos.X, pos.Y);
            }
        }

        public bool ProcessMessage(WindowMessage msg, UIntPtr wParam, IntPtr lParam)
        {
            if (ImGui.GetCurrentContext() == IntPtr.Zero)
                return false;

            var io = ImGui.GetIO();
            switch (msg)
            {
                case WindowMessage.LButtonDown:
                case WindowMessage.LButtonDoubleClick:
                case WindowMessage.RButtonDown:
                case WindowMessage.RButtonDoubleClick:
                case WindowMessage.MButtonDown:
                case WindowMessage.MButtonDoubleClick:
                case WindowMessage.XButtonDown:
                case WindowMessage.XButtonDoubleClick:
                    {
                        int button = 0;
                        if (msg == WindowMessage.LButtonDown || msg == WindowMessage.LButtonDoubleClick) { button = 0; }
                        if (msg == WindowMessage.RButtonDown || msg == WindowMessage.RButtonDoubleClick) { button = 1; }
                        if (msg == WindowMessage.MButtonDown || msg == WindowMessage.MButtonDoubleClick) { button = 2; }
                        if (msg == WindowMessage.XButtonDown || msg == WindowMessage.XButtonDoubleClick) { button = (GET_XBUTTON_WPARAM(wParam) == 1) ? 3 : 4; }
                        if (!ImGui.IsAnyMouseDown() && User32.GetCapture() == IntPtr.Zero)
                            User32.SetCapture(hwnd);
                        io.MouseDown[button] = true;
                        return false;
                    }
                case WindowMessage.LButtonUp:
                case WindowMessage.RButtonUp:
                case WindowMessage.MButtonUp:
                case WindowMessage.XButtonUp:
                    {
                        int button = 0;
                        if (msg == WindowMessage.LButtonUp) { button = 0; }
                        if (msg == WindowMessage.RButtonUp) { button = 1; }
                        if (msg == WindowMessage.MButtonUp) { button = 2; }
                        if (msg == WindowMessage.XButtonUp) { button = (GET_XBUTTON_WPARAM(wParam) == 1) ? 3 : 4; }
                        io.MouseDown[button] = false;
                        if (!ImGui.IsAnyMouseDown() && User32.GetCapture() == hwnd)
                            User32.ReleaseCapture();
                        return false;
                    }
                case WindowMessage.MouseWheel:
                    io.MouseWheel += GET_WHEEL_DELTA_WPARAM(wParam) / WHEEL_DELTA;
                    return false;
                case WindowMessage.MouseHWheel:
                    io.MouseWheelH += GET_WHEEL_DELTA_WPARAM(wParam) / WHEEL_DELTA;
                    return false;
                case WindowMessage.KeyDown:
                case WindowMessage.SysKeyDown:
                    return false;
                case WindowMessage.KeyUp:
                case WindowMessage.SysKeyUp:
                    return false;
                case WindowMessage.Char:
                    io.AddInputCharacter((uint)wParam);
                    return false;
                case WindowMessage.SetCursor:
                    if (Utils.Loword((int)(long)lParam) == 1 && UpdateMouseCursor())
                        return true;
                    return false;
            }
            return false;
        }

        static int WHEEL_DELTA = 120;
        static int GET_WHEEL_DELTA_WPARAM(IntPtr wParam) => Utils.Hiword((int)(long)wParam);
        static int GET_XBUTTON_WPARAM(IntPtr wParam) => Utils.Hiword((int)(long)wParam);
        static int GET_WHEEL_DELTA_WPARAM(UIntPtr wParam) => Utils.Hiword((int)(long)wParam);
        static int GET_XBUTTON_WPARAM(UIntPtr wParam) => Utils.Hiword((int)(long)wParam);
    }
}
