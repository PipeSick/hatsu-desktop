using System.Numerics;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace WinMac.Dock.Win.Rendering;

/// <summary>Dispositivos D3D11 + Direct2D compartidos. Intenta GPU y cae a WARP (software) si no hay: necesario para runners de CI y GPUs problemáticas.</summary>
public sealed class D2DDevice : IDisposable
{
    public ID3D11Device D3D { get; }
    public IDXGIDevice Dxgi { get; }
    public ID2D1Factory1 Factory { get; }
    public ID2D1Device Device { get; }
    public ID2D1DeviceContext Context { get; }
    public bool IsSoftware { get; }
    public string Description { get; }

    public D2DDevice(bool forceSoftware = false)
    {
        ID3D11Device? dev = null; bool soft = false;
        var levels = new[] { Vortice.Direct3D.FeatureLevel.Level_11_1, Vortice.Direct3D.FeatureLevel.Level_11_0, Vortice.Direct3D.FeatureLevel.Level_10_1, Vortice.Direct3D.FeatureLevel.Level_10_0 };
        if (!forceSoftware && D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport, levels, out dev).Success) { }
        else
        {
            D3D11.D3D11CreateDevice(null, DriverType.Warp, DeviceCreationFlags.BgraSupport, levels, out dev).CheckError();
            soft = true;
        }
        D3D = dev!; IsSoftware = soft;
        Dxgi = D3D.QueryInterface<IDXGIDevice>();
        using (var adapter = Dxgi.GetAdapter()) Description = adapter.Description.Description + (soft ? " (WARP/software)" : "");
        Factory = D2D1.D2D1CreateFactory<ID2D1Factory1>();
        Device = Factory.CreateDevice(Dxgi);
        Context = Device.CreateDeviceContext(DeviceContextOptions.None);
    }

    public void SetDpi(double scale) => Context.SetDpi((float)(96 * scale), (float)(96 * scale));

    public void Dispose() { Context.Dispose(); Device.Dispose(); Factory.Dispose(); Dxgi.Dispose(); D3D.Dispose(); }
}
