using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;

namespace WinMac.Bar.Win.Shell;

/// <summary>
/// WM-029: the picture of the signed-in Windows account, as premultiplied BGRA side×side, or null. First the public API (Windows.System.User.GetPictureAsync);
/// if it gives nothing for this desktop app, the image file Windows registers for the current user (HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\
/// AccountPicture\Users\&lt;SID&gt;, read only). No administrator, no other user's data, nothing written.
/// </summary>
internal static class AccountPicture
{
    public static async Task<byte[]?> LoadAsync(int side)
    {
        try
        {
            var users = await global::Windows.System.User.FindAllAsync(global::Windows.System.UserType.LocalUser, global::Windows.System.UserAuthenticationStatus.LocallyAuthenticated);
            var me = users.FirstOrDefault() ?? (await global::Windows.System.User.FindAllAsync()).FirstOrDefault();
            if (me is not null && await me.GetPictureAsync(global::Windows.System.UserPictureSize.Size208x208) is { } reference)
            {
                using var stream = await reference.OpenReadAsync();
                if (stream.Size > 0) return await Decode(stream, side);
            }
        }
        catch (Exception) { /* not available to this app: try the registered file */ }

        try
        {
            var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value; if (sid is null) return null;
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\AccountPicture\Users\" + sid);
            foreach (var v in new[] { "Image192", "Image208", "Image96", "Image240", "Image448" })
                if (key?.GetValue(v) is string path && File.Exists(path))
                {
                    var bytes = await File.ReadAllBytesAsync(path);
                    using var ms = new MemoryStream(bytes);
                    return await Decode(ms.AsRandomAccessStream(), side);
                }
        }
        catch (Exception) { }
        return null;
    }

    private static async Task<byte[]?> Decode(global::Windows.Storage.Streams.IRandomAccessStream stream, int side)
    {
        var dec = await BitmapDecoder.CreateAsync(stream);
        // centre square crop, then scale: proportions kept, the painter clips it to a circle
        uint sq = Math.Min(dec.OrientedPixelWidth, dec.OrientedPixelHeight);
        var t = new BitmapTransform
        {
            Bounds = new BitmapBounds { X = (uint)((dec.OrientedPixelWidth - sq) / 2 * side / (double)sq), Y = (uint)((dec.OrientedPixelHeight - sq) / 2 * side / (double)sq), Width = (uint)side, Height = (uint)side },
            ScaledWidth = (uint)Math.Round(dec.OrientedPixelWidth * side / (double)sq), ScaledHeight = (uint)Math.Round(dec.OrientedPixelHeight * side / (double)sq), InterpolationMode = BitmapInterpolationMode.Fant,
        };
        var data = await dec.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, t, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
        var px = data.DetachPixelData();
        return px.Length == side * side * 4 ? px : null;
    }
}
