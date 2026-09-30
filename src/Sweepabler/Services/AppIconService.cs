using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace ProperAppUpdater.Services;

public static class AppIconService
{
    private const string FallbackIconSource = "pack://application:,,,/Assets/supurucu-polished.png";
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiLargeIcon = 0x000000000;

    public static string ResolveIconSource(string displayIcon)
    {
        try
        {
            var iconPath = AppPathResolver.ExtractExecutablePath(displayIcon);
            if (string.IsNullOrWhiteSpace(iconPath) || !File.Exists(iconPath))
            {
                return FallbackIconSource;
            }

            AppPaths.EnsureCreated();
            var cachePath = BuildCachePath(iconPath);
            SafePath.RequireInside(AppPaths.IconCacheRoot, cachePath);
            if (File.Exists(cachePath))
            {
                return cachePath;
            }

            if (!TryWriteShellIcon(iconPath, cachePath))
            {
                return FallbackIconSource;
            }

            return cachePath;
        }
        catch
        {
            return FallbackIconSource;
        }
    }

    private static string BuildCachePath(string iconPath)
    {
        var fileInfo = new FileInfo(iconPath);
        var fingerprint = $"{iconPath}|{fileInfo.Length}|{fileInfo.LastWriteTimeUtc.Ticks}";
        var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(fingerprint)));
        return Path.Combine(AppPaths.IconCacheRoot, $"{hash[..20]}.png");
    }

    private static bool TryWriteShellIcon(string iconPath, string cachePath)
    {
        var info = new ShellFileInfo();
        var result = SHGetFileInfo(
            iconPath,
            0,
            ref info,
            (uint)Marshal.SizeOf<ShellFileInfo>(),
            ShgfiIcon | ShgfiLargeIcon);

        if (result == IntPtr.Zero || info.IconHandle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(
                info.IconHandle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(32, 32));
            source.Freeze();

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));

            using var stream = File.Create(cachePath);
            encoder.Save(stream);
            return true;
        }
        finally
        {
            DestroyIcon(info.IconHandle);
        }
    }

    [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string path,
        uint fileAttributes,
        ref ShellFileInfo fileInfo,
        uint fileInfoSize,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr iconHandle);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public IntPtr IconHandle;
        public int IconIndex;
        public uint Attributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string TypeName;
    }
}
