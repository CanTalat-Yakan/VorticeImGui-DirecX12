﻿global using System;
global using System.Numerics;

global using ImGuiNET;

global using Engine.Buffer;
global using Engine.Data;
global using Engine.DataTypes;
global using Engine.Framework;
global using Engine.Graphics;
global using Engine.GUI;
global using Engine.Helper;
global using Engine.Utilities;

namespace Engine;

public sealed class Kernel
{
    public static Kernel Instance { get; private set; }

    public event Action OnRender;
    public event Action OnInitialize;
    public event Action OnGUI;
    public event Action OnDispose;

    public CommonContext Context;
    public Config Config;

    public GUIRenderer GUIRenderer;
    public GUIInputHandler GUIInputHandler;
    public IntPtr GUIContext;

    public Kernel(Config config)
    {
        Config = config;

        Context = new CommonContext(this);
    }

    public void Initialize(IntPtr hwnd, Vortice.Mathematics.SizeI size, bool win32Window)
    {
        // Set the singleton instance of the class, if it hasn't been already.
        Instance ??= this;

        Context.GraphicsDevice.Initialize(size, win32Window);
        Context.UploadBuffer.Initialize(Context.GraphicsDevice, 67108864); // 64 MB.
        Context.GraphicsContext.Initialize(Context.GraphicsDevice);

        if (Config.GUI)
        {
            GUIRenderer = new();
            GUIRenderer.Context = Context;

            GUIRenderer.LoadDefaultResource();
            GUIRenderer.Initialize();

            GUIInputHandler = new(hwnd);
        }
    }

    public void Frame()
    {
        if (!Context.IsRendering)
            return;

        OnInitialize?.Invoke();
        OnInitialize = null;

        Context.GraphicsDevice.Begin();
        Context.GraphicsContext.BeginCommand();

        Context.GPUUploadData(Context.GraphicsContext);

        Context.GraphicsContext.SetDescriptorHeapDefault();
        Context.GraphicsContext.ScreenBeginRender();
        Context.GraphicsContext.SetRenderTargetScreen();
        Context.GraphicsContext.ClearRenderTargetScreen();

        OnRender?.Invoke();

        if (Config.GUI)
            RenderGUI();

        Context.GraphicsContext.ScreenEndRender();
        Context.GraphicsContext.EndCommand();
        Context.GraphicsContext.Execute();

        Context.GraphicsDevice.Present((int)Config.VSync);
    }

    public void RenderGUI()
    {
        GUIRenderer.Update(GUIContext);
        GUIInputHandler.Update();

        OnGUI?.Invoke();

        GUIRenderer.Render();
    }

    public void Dispose()
    {
        Context?.Dispose();

        OnDispose?.Invoke();
    }
}