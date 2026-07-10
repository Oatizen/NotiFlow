using System;
using System.Runtime.InteropServices;
using Microsoft.Graphics.Canvas;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.UI.Composition;
using WinRT;

namespace NotiFlow.Rendering
{
    /// <summary>
    /// Windows.UI.Composition 与 Win2D 的手动桥接工具。
    /// 替代 CanvasComposition 静态类（仅支持 Microsoft.UI.Composition），
    /// 使 OS 级 Windows.UI.Composition 可与 Win2D 协作。
    ///
    /// 核心策略：从零创建 D3D11 设备，用同一个 DXGI 设备同时构建
    /// CompositionGraphicsDevice（合成纹理工厂）和 CanvasDevice（Win2D 绘图设备），
    /// 确保两者共享同一 GPU 上下文。
    /// </summary>
    internal static class CompositionHelper
    {
        private static readonly Guid IID_IDXGISurface =
            new("cafcb56c-6ac3-4889-bf47-9e23bbd260ec");
        private static readonly Guid IID_IDXGIDevice =
            new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
        private static readonly Guid IID_ICompositorInterop =
            new("25297D5C-3AD4-4C9C-B5CF-E36A38512330");

        /// <summary>
        /// 创建共享同一 D3D11/DXGI 设备的 CompositionGraphicsDevice 和 CanvasDevice。
        /// 路径：D3D11CreateDevice → IDXGIDevice → ICompositorInterop.CreateGraphicsDevice
        ///       + CreateDirect3D11DeviceFromDXGIDevice → CanvasDevice.CreateFromDirect3D11Device
        /// </summary>
        public static (CompositionGraphicsDevice graphicsDevice, CanvasDevice canvasDevice)
            CreateSharedDevices(Compositor compositor)
        {
            // 1. 创建 D3D11 硬件设备（BGRA 支持是 D2D/Win2D 的必要条件）
            int hr = NativeMethods.D3D11CreateDevice(
                IntPtr.Zero,    // pAdapter: 默认适配器
                1,              // D3D_DRIVER_TYPE_HARDWARE
                IntPtr.Zero,    // Software: 非软件渲染
                0x20,           // D3D11_CREATE_DEVICE_BGRA_SUPPORT
                IntPtr.Zero,    // pFeatureLevels: 使用默认
                0,              // FeatureLevels
                7,              // D3D11_SDK_VERSION
                out IntPtr d3d11Device,
                out _,          // pFeatureLevel (不需要)
                out IntPtr d3d11Context);
            Marshal.ThrowExceptionForHR(hr);
            Marshal.Release(d3d11Context); // 不需要 immediate context

            try
            {
                // 2. 从 D3D11 设备获取 IDXGIDevice
                Guid iidDxgi = IID_IDXGIDevice;
                Marshal.ThrowExceptionForHR(
                    Marshal.QueryInterface(d3d11Device, ref iidDxgi, out IntPtr dxgiDevice));

                try
                {
                    // 3. 通过 ICompositorInterop 创建 CompositionGraphicsDevice
                    var graphicsDevice = CreateGraphicsDeviceFromDxgi(compositor, dxgiDevice);

                    // 4. 用同一个 DXGI 设备创建 CanvasDevice（共享 GPU 上下文）
                    NativeMethods.CreateDirect3D11DeviceFromDXGIDevice(
                        dxgiDevice, out IntPtr d3dInspectable);
                    var d3dDevice = MarshalInterface<IDirect3DDevice>.FromAbi(d3dInspectable);
                    Marshal.Release(d3dInspectable);
                    var canvasDevice = CanvasDevice.CreateFromDirect3D11Device(d3dDevice);

                    return (graphicsDevice, canvasDevice);
                }
                finally { Marshal.Release(dxgiDevice); }
            }
            finally { Marshal.Release(d3d11Device); }
        }

        /// <summary>
        /// 通过 ICompositorInterop 从 DXGI 设备创建 CompositionGraphicsDevice。
        /// 使用原生 vtable 调用绕过 .NET COM RCW 的 QI 兼容性问题。
        /// </summary>
        private static CompositionGraphicsDevice CreateGraphicsDeviceFromDxgi(
            Compositor compositor, IntPtr dxgiDevice)
        {
            IntPtr compositorPtr = ((IWinRTObject)compositor).NativeObject.ThisPtr;
            Guid iidInterop = IID_ICompositorInterop;
            Marshal.ThrowExceptionForHR(
                Marshal.QueryInterface(compositorPtr, ref iidInterop, out IntPtr interopPtr));

            try
            {
                // ICompositorInterop vtable:
                // [0] QueryInterface  [1] AddRef  [2] Release
                // [3] CreateCompositionSurfaceForHandle
                // [4] CreateCompositionSurfaceForSwapChain
                // [5] CreateGraphicsDevice
                IntPtr vtable = Marshal.ReadIntPtr(interopPtr);
                IntPtr fnPtr = Marshal.ReadIntPtr(vtable, 5 * IntPtr.Size);
                var createGraphicsDevice =
                    Marshal.GetDelegateForFunctionPointer<CreateGraphicsDeviceDelegate>(fnPtr);

                int hr = createGraphicsDevice(interopPtr, dxgiDevice, out IntPtr resultPtr);
                Marshal.ThrowExceptionForHR(hr);

                try
                {
                    return MarshalInterface<CompositionGraphicsDevice>.FromAbi(resultPtr);
                }
                finally { Marshal.Release(resultPtr); }
            }
            finally { Marshal.Release(interopPtr); }
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateGraphicsDeviceDelegate(
            IntPtr @this, IntPtr renderingDevice, out IntPtr result);

        /// <summary>
        /// 在 CompositionDrawingSurface 上创建 Win2D 绘图会话。
        /// 返回的包装器在 Dispose 时自动提交绘制并释放资源。
        /// </summary>
        public static SurfaceDrawingSession CreateDrawingSession(
            CompositionDrawingSurface surface, CanvasDevice canvasDevice)
        {
            // 1. QI surface → ICompositionDrawingSurfaceInterop（通过 vtable 调用）
            IntPtr surfacePtr = ((IWinRTObject)surface).NativeObject.ThisPtr;
            Guid iidInterop = new("FD04E6E3-FE0C-4C3C-AB19-A07601A576EE");
            Marshal.ThrowExceptionForHR(
                Marshal.QueryInterface(surfacePtr, ref iidInterop, out IntPtr interopPtr));

            try
            {
                // ICompositionDrawingSurfaceInterop vtable:
                // [0-2] IUnknown  [3] BeginDraw  [4] EndDraw  ...
                IntPtr vtable = Marshal.ReadIntPtr(interopPtr);
                IntPtr beginDrawFn = Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size);
                IntPtr endDrawFn = Marshal.ReadIntPtr(vtable, 4 * IntPtr.Size);

                // 2. BeginDraw — 获取底层 IDXGISurface（IntPtr.Zero = 更新整个表面）
                var beginDraw = Marshal.GetDelegateForFunctionPointer<BeginDrawDelegate>(beginDrawFn);
                Guid iidSurface = IID_IDXGISurface;
                int hr = beginDraw(interopPtr, IntPtr.Zero, ref iidSurface,
                    out IntPtr dxgiSurfacePtr, out NativeMethods.POINT offset);
                Marshal.ThrowExceptionForHR(hr);

                // 保存 endDraw 供 Dispose 调用
                var endDraw = Marshal.GetDelegateForFunctionPointer<EndDrawDelegate>(endDrawFn);
                // AddRef interopPtr 因为 SurfaceDrawingSession 需要持有它
                Marshal.AddRef(interopPtr);

                // 3. IDXGISurface → WinRT IDirect3DSurface
                NativeMethods.CreateDirect3D11SurfaceFromDXGISurface(
                    dxgiSurfacePtr, out IntPtr d3dInspectable);
                Marshal.Release(dxgiSurfacePtr);

                var d3dSurface = MarshalInterface<IDirect3DSurface>.FromAbi(d3dInspectable);
                Marshal.Release(d3dInspectable);

                // 4. 用 Win2D 包装为可绘制的 CanvasRenderTarget
                var renderTarget = CanvasRenderTarget.CreateFromDirect3D11Surface(
                    canvasDevice, d3dSurface);
                var session = renderTarget.CreateDrawingSession();

                return new SurfaceDrawingSession(
                    session, renderTarget, d3dSurface, interopPtr, endDraw, offset);
            }
            finally { Marshal.Release(interopPtr); }
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        internal delegate int BeginDrawDelegate(
            IntPtr @this, IntPtr updateRect, ref Guid iid,
            out IntPtr updateObject, out NativeMethods.POINT updateOffset);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        internal delegate int EndDrawDelegate(IntPtr @this);
    }

    /// <summary>
    /// 包装 Win2D CanvasDrawingSession，在 Dispose 时自动调用 EndDraw 提交绘制。
    /// </summary>
    internal sealed class SurfaceDrawingSession : IDisposable
    {
        public CanvasDrawingSession Session { get; }
        public NativeMethods.POINT Offset { get; }

        private readonly CanvasRenderTarget _renderTarget;
        private readonly IDirect3DSurface _d3dSurface;
        private readonly IntPtr _interopPtr;
        private readonly CompositionHelper.EndDrawDelegate _endDraw;
        private bool _disposed;

        internal SurfaceDrawingSession(
            CanvasDrawingSession session,
            CanvasRenderTarget renderTarget,
            IDirect3DSurface d3dSurface,
            IntPtr interopPtr,
            CompositionHelper.EndDrawDelegate endDraw,
            NativeMethods.POINT offset)
        {
            Session = session;
            _renderTarget = renderTarget;
            _d3dSurface = d3dSurface;
            _interopPtr = interopPtr;
            _endDraw = endDraw;
            Offset = offset;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // 顺序关键：先刷新 D2D 绘制命令，再提交合成表面
            Session?.Dispose();
            _renderTarget?.Dispose();
            _d3dSurface?.Dispose();
            _endDraw(_interopPtr);
            Marshal.Release(_interopPtr);
        }
    }
}
