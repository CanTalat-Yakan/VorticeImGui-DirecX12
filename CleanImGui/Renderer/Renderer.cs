﻿using SharpGen.Runtime;
using Vortice.Direct3D12.Debug;
using Vortice.Direct3D12;
using Vortice.Direct3D;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Engine;

public sealed partial class Renderer
{
    public static Renderer Instance { get; private set; }

    public bool IsRendering => Data.BufferRenderTargetView?.NativePointer is not 0;

    public SizeI Size { get; private set; }

    public ID3D12Device2 Device { get; private set; }
    public RenderData Data = new();

    private Win32Window _win32Window;

    public Renderer(Win32Window win32Window)
    {
        // Initializes the singleton instance of the class, if it hasn't been already.
        Instance ??= this;

        // Store the instance of Win32Window.
        if (win32Window is not null)
            _win32Window = win32Window;
        else
            throw new NullReferenceException(
                """
                An invalid or null Win32Window instance was passed to the Renderer. 
                Please ensure that you provide a valid Win32Window object to the Renderer.
                """);

        // Set the size.
        Size = new(
            _win32Window.Width,
            _win32Window.Height);

        var result = Initialize(true);
        if (result.Failure)
            throw new Exception(result.Description);
    }

    private Result Initialize(bool forHwnd = false)
    {
        CreateDevice(out var result);
        if (result.Failure)
            return result;

        CreateCommandAllocator();
        CreateCommandList();

        CreateGraphicsQueue();
        CreateFrameFence();

        SetupSwapChain(forHwnd);

        GetSwapChainBuffersAndCreateRenderTargetViews();
        CreateOutputTextureAndRenderTargetView();
        CreateDepthStencilView();

        CreateBlendDescription();
        CreateRasterizerDescription();

        return Result.Ok;
    }

    public void Dispose()
    {
        WaitIdle();

        // Dispose all DirectX resources that were created.
        Device?.Dispose();
    }
}

public sealed partial class Renderer
{
    public void Present() =>
        // Present the final render to the screen.
        Data.SwapChain.Present(0, PresentFlags.DoNotWait);

    public void WaitIdle()
    {
        // Signal the frame fence, incrementing the frame count.
        Data.GraphicsQueue.Signal(Data.FrameFence, ++Data.FrameCount);

        // Set an event to be signaled when the frame fence reaches the specified frame count.
        Data.FrameFence.SetEventOnCompletion(Data.FrameCount, Data.FrameFenceEvent);
        // Wait for the event to be signaled, indicating the completion of the specified frame.
        Data.FrameFenceEvent.WaitOne();
    }

    public void Execute()
    {
        Data.CommandList.Close();
        Data.GraphicsQueue.ExecuteCommandList(Data.CommandList);

        Data.CommandAllocator.Reset();
        Data.CommandList.Reset(Data.CommandAllocator, null);
    }

    public void Resolve()
    {
        // Transition the output buffer to CopyDest state.
        Data.CommandList.ResourceBarrierTransition(Data.OutputRenderTargetTexture, ResourceStates.Present, ResourceStates.CopyDest);

        // Resolve the output render target texture to the back buffer.
        Data.CommandList.ResolveSubresource(
            Data.BufferRenderTargetTextures[Data.SwapChain.CurrentBackBufferIndex], 0,
            Data.OutputRenderTargetTexture, 0,
            RenderData.RenderTargetFormat);

        // Transition back buffer to Present state.
        Data.CommandList.ResourceBarrierTransition(Data.OutputRenderTargetTexture, ResourceStates.CopyDest, ResourceStates.Present);
    }

    public void Draw(int indexCount, IndexBufferView indexBufferViews, int instanceCount, params VertexBufferView[] vertexBufferViews)
    {
        Data.SetupInputAssembler(indexBufferViews, vertexBufferViews);

        Data.CommandList.DrawIndexedInstanced(indexCount, instanceCount, 0, 0, 0);
    }

    public void EndRenderPass() =>
        Data.CommandList.EndRenderPass();

    public void BeginRenderPass()
    {
        Data.CommandList.BeginEvent("Render Pass");

        var renderPassDescription = new RenderPassRenderTargetDescription(Data.BufferRenderTargetView.GetCPUDescriptorHandleForHeapStart(),
            new RenderPassBeginningAccess(new ClearValue(RenderData.RenderTargetFormat, Colors.DarkGray)),
            new RenderPassEndingAccess(RenderPassEndingAccessType.Preserve));

        var depthStencil = new RenderPassDepthStencilDescription(
            Data.DepthStencilView.GetCPUDescriptorHandleForHeapStart(),
            new RenderPassBeginningAccess(new ClearValue(RenderData.DepthStencilFormat, 1.0f, 0)),
            new RenderPassEndingAccess(RenderPassEndingAccessType.Discard));

        Data.CommandList.BeginRenderPass(renderPassDescription, depthStencil);
    }

    public void EndFrame()
    {
        // Transition back buffer to CopySource state.
        Data.CommandList.ResourceBarrierTransition(Data.OutputRenderTargetTexture, ResourceStates.RenderTarget, ResourceStates.CopySource);
        Data.CommandList.EndEvent();
    }

    public void BeginFrame()
    {
        Data.CommandList.BeginEvent("Frame");

        // Transition MSAA render target texture to RenderTarget state.
        Data.CommandList.ResourceBarrierTransition(Data.OutputRenderTargetTexture, ResourceStates.CopySource, ResourceStates.RenderTarget);

        // Clear render target view and depth stencil view.
        Data.CommandList.ClearRenderTargetView(Data.OutputRenderTargetView.GetCPUDescriptorHandleForHeapStart(), Colors.DarkGray);
        Data.CommandList.ClearDepthStencilView(Data.DepthStencilView.GetCPUDescriptorHandleForHeapStart(), ClearFlags.Depth | ClearFlags.Stencil, 1.0f, 0);

        // Set render target and depth stencil view for the device context.
        Data.CommandList.OMSetRenderTargets(Data.OutputRenderTargetView.GetCPUDescriptorHandleForHeapStart(), Data.DepthStencilView.GetCPUDescriptorHandleForHeapStart());
    }

    public bool CheckDeviceRemoved()
    {
        if (Device.DeviceRemovedReason.Failure)
        {
            Console.WriteLine(
                $"""
                Device RemovedReason: {Device.DeviceRemovedReason}
                {Device.DeviceRemovedReason.Description}
                """);

            return true;
        }

        return false;
    }
}

public sealed partial class Renderer
{
    private void CreateDevice(out Result result)
    {
        if (D3D12.D3D12GetDebugInterface(out ID3D12Debug1 debug).Success)
        {
            debug!.EnableDebugLayer();
            debug!.SetEnableGPUBasedValidation(true);
            debug!.Dispose();
        }

        FeatureLevel[] s_featureLevels = [
            FeatureLevel.Level_12_2,
            FeatureLevel.Level_12_1,
            FeatureLevel.Level_12_0,
            FeatureLevel.Level_11_1,
            FeatureLevel.Level_11_0];

        using (IDXGIFactory4 DXGIFactory = DXGI.CreateDXGIFactory2<IDXGIFactory4>(true))
        {
            ID3D12Device2 d3d12Device = default;
            IDXGIAdapter1 adapter = default;

            for (int i = 0; DXGIFactory.EnumAdapters1(i, out adapter).Success; i++)
                // Don't select the Basic Render Driver adapter.
                if ((adapter.Description1.Flags & AdapterFlags.Software) is not AdapterFlags.None)
                    adapter.Dispose();
                else break;

            for (int i = 0; i < s_featureLevels.Length; i++)
                // Create the D3D12 Device with the current adapter and the highest possible Feature level.
                if (D3D12.D3D12CreateDevice(adapter, s_featureLevels[i], out d3d12Device).Success)
                {
                    adapter.Dispose();
                    break;
                }

            if (d3d12Device is null)
            {
                result = Result.Fail;
                return;
            }
            else
            {
                // Assign the device to a variable.
                Device = d3d12Device!;
                Device.Name = "Device";
            }
        }

        result = Result.Ok;
    }

    private void CreateCommandAllocator()
    {
        Data.CommandAllocator = Device.CreateCommandAllocator(CommandListType.Direct);
        Data.CommandAllocator.Name = "CommandAllocator";
    }

    private void CreateCommandList()
    {
        Data.CommandList = Device.CreateCommandList<ID3D12GraphicsCommandList4>(
            CommandListType.Direct,
            Data.CommandAllocator,
            null);
        Data.CommandList.Name = "CommandList";

        CreateRootSignature(out var rootSignature);
        Data.CommandList.SetGraphicsRootSignature(rootSignature);
    }

    private void CreateRootSignature(out ID3D12RootSignature rootSignature)
    {
        // Create a root signature
        RootSignatureFlags rootSignatureFlags =
              RootSignatureFlags.AllowInputAssemblerInputLayout
            | RootSignatureFlags.ConstantBufferViewShaderResourceViewUnorderedAccessViewHeapDirectlyIndexed;

        RootSignatureDescription1 rootSignatureDescription = new(rootSignatureFlags);

        RootParameter1 constantbuffer = new(
            new RootDescriptorTable1(new DescriptorRange1(
                DescriptorRangeType.ConstantBufferView, 1, 0, 0, 0)),
            ShaderVisibility.All);

        // Define the root parameters
        RootParameter1[] rootParameters = new[] { constantbuffer };
        rootSignatureDescription.Parameters = rootParameters;

        rootSignature = Device.CreateRootSignature(rootSignatureDescription);
        rootSignature.Name = "Root Signature";
    }

    private void CreateGraphicsQueue()
    {
        // Create Command queue.
        Data.GraphicsQueue = Device.CreateCommandQueue(CommandListType.Direct);
        Data.GraphicsQueue.Name = "Graphics Queue";
    }

    private void CreateFrameFence()
    {
        // Create synchronization objects.
        Data.FrameFence = Device.CreateFence(0);
        Data.FrameFenceEvent = new System.Threading.AutoResetEvent(false);
    }

    private void SetupSwapChain(bool forHwnd)
    {
        // Initialize the SwapChainDescription structure.
        SwapChainDescription1 swapChainDescription = new()
        {
            BufferCount = RenderData.RenderLatency,
            Width = Size.Width,
            Height = Size.Height,
            Format = RenderData.RenderTargetFormat,
            BufferUsage = Usage.RenderTargetOutput,
            SwapEffect = SwapEffect.FlipSequential,
            SampleDescription = SampleDescription.Default
        };

        // Create the IDXGIFactory4.
        using IDXGIFactory4 DXGIFactory = DXGI.CreateDXGIFactory2<IDXGIFactory4>(true);
        // Creates a swap chain using the swap chain description.
        using IDXGISwapChain1 swapChain1 = forHwnd
            ? DXGIFactory.CreateSwapChainForHwnd(Data.GraphicsQueue, _win32Window.Handle, swapChainDescription)
            : DXGIFactory.CreateSwapChainForComposition(Data.GraphicsQueue, swapChainDescription);

        Data.SwapChain = swapChain1.QueryInterface<IDXGISwapChain3>();
    }

    private void GetSwapChainBuffersAndCreateRenderTargetViews()
    {
        Data.BufferRenderTargetTextures = new ID3D12Resource[RenderData.RenderLatency];
        Data.BufferRenderTargetView = Device.CreateDescriptorHeap(new()
        {
            DescriptorCount = RenderData.RenderLatency,
            Type = DescriptorHeapType.RenderTargetView
        });
        for (int i = 0; i < RenderData.RenderLatency; i++)
        {
            // Get the buffers of the swap chain as a texture.
            Data.BufferRenderTargetTextures[i] = Data.SwapChain.GetBuffer<ID3D12Resource>(i);
            Data.BufferRenderTargetTextures[i].Name = $"SwapChain Buffer {i}";
            // Create a render target view for the back buffer render target texture.
            Device.CreateRenderTargetView(Data.BufferRenderTargetTextures[i], null, Data.BufferRenderTargetView.GetCPUDescriptorHandleForHeapStart());
        }

        var size = Device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
        Data.BufferRenderTargetView.GetCPUDescriptorHandleForHeapStart().Offset(size, RenderData.RenderLatency);
    }

    private void CreateOutputTextureAndRenderTargetView()
    {
        ResourceDescription MSAATextureDescription = ResourceDescription.Texture2D(
            RenderData.RenderTargetFormat,
            (uint)Size.Width,
            (uint)Size.Height,
            arraySize: 1,
            mipLevels: 1,
            sampleCount: 1,
            sampleQuality: 0,
            flags: ResourceFlags.AllowRenderTarget); // BindFlags.ShaderResource

        // Create the multi sample texture based on the description.
        Data.OutputRenderTargetTexture = Device.CreateCommittedResource(
            HeapType.Default,
            MSAATextureDescription,
            ResourceStates.AllShaderResource);
        Data.OutputRenderTargetTexture.Name = "MSAA Output Render Target Texture";

        // Create a render target view description for the multi sampling.
        RenderTargetViewDescription MSAARenderTargetViewDescription = new()
        {
            Format = RenderData.RenderTargetFormat,
            ViewDimension = RenderTargetViewDimension.Texture2DMultisampled
        };

        Data.OutputRenderTargetView = Device.CreateDescriptorHeap(new()
        {
            DescriptorCount = 1,
            Type = DescriptorHeapType.RenderTargetView
        });
        // Create a render target view for the MSAA render target texture.
        Device.CreateRenderTargetView(Data.OutputRenderTargetTexture, MSAARenderTargetViewDescription, Data.OutputRenderTargetView.GetCPUDescriptorHandleForHeapStart());

        var size = Device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
        Data.OutputRenderTargetView.GetCPUDescriptorHandleForHeapStart().Offset(size);
    }

    private void CreateDepthStencilView()
    {
        ResourceDescription depthStencilTextureDescription = ResourceDescription.Texture2D(
            RenderData.DepthStencilFormat,
            (uint)Size.Width,
            (uint)Size.Height,
            arraySize: 1,
            mipLevels: 1,
            sampleCount: 1,
            sampleQuality: 0,
            flags: ResourceFlags.AllowDepthStencil);

        // Create the depth stencil texture based on the description.
        Data.DepthStencilTexture = Device.CreateCommittedResource(
            HeapType.Default,
            depthStencilTextureDescription,
            ResourceStates.DepthWrite,
            new(RenderData.DepthStencilFormat, 1.0f, 0));
        Data.DepthStencilTexture.Name = "DepthStencil Texture";

        // Create a depth stencil view description for the multi sampling.
        DepthStencilViewDescription depthStencilViewDescription = new()
        {
            Format = RenderData.DepthStencilFormat,
            ViewDimension = DepthStencilViewDimension.Texture2DMultisampled
        };

        Data.DepthStencilView = Device.CreateDescriptorHeap(new()
        {
            DescriptorCount = 1,
            Type = DescriptorHeapType.DepthStencilView
        });
        // Create a depth stencil view for the depth stencil texture.
        Device.CreateDepthStencilView(Data.DepthStencilTexture, depthStencilViewDescription, Data.DepthStencilView.GetCPUDescriptorHandleForHeapStart());

        var size = Device.GetDescriptorHandleIncrementSize(DescriptorHeapType.DepthStencilView);
        Data.DepthStencilView.GetCPUDescriptorHandleForHeapStart().Offset(size);
    }

    private void CreateBlendDescription()
    {
        // Set up the blend state description.
        Data.BlendDescription = new()
        {
            AlphaToCoverageEnable = true
        };

        // Render target blend description setup.
        RenderTargetBlendDescription renderTargetBlendDescription = new()
        {
            BlendEnable = true, // Enable blend.
            SourceBlend = Blend.SourceAlpha,
            DestinationBlend = Blend.InverseSourceAlpha,
            BlendOperation = BlendOperation.Add,
            SourceBlendAlpha = Blend.One,
            DestinationBlendAlpha = Blend.Zero,
            BlendOperationAlpha = BlendOperation.Add,
            RenderTargetWriteMask = ColorWriteEnable.All
        };
        // Assign the render target blend description to the blend state description.
        Data.BlendDescription.RenderTarget[0] = renderTargetBlendDescription;
    }

    private void CreateRasterizerDescription()
    {
        // Create a rasterizer description
        // using solid fill mode, multi sampled and counter clockwise.
        Data.RasterizerDescription = new()
        {
            FillMode = FillMode.Solid,
            CullMode = CullMode.None,
            AntialiasedLineEnable = true,
            MultisampleEnable = true,
            FrontCounterClockwise = true
        };
    }
}