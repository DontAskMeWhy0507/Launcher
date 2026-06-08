using RevoltLauncher.Models;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace RevoltLauncher.Services;

public sealed class LauncherService : ILauncherService
{
    public Task LaunchAsync(LauncherItem item)
    {
        if (string.IsNullOrWhiteSpace(item.ExecutablePath))
        {
            ShowInfo($"Chưa cấu hình đường dẫn chạy cho {item.Name}.");
            return Task.CompletedTask;
        }

        try
        {
            if (!File.Exists(item.ExecutablePath))
            {
                ShowWarning($"Không tìm thấy shortcut/file của {item.Name}:\n{item.ExecutablePath}");
                return Task.CompletedTask;
            }

            var target = ResolveShortcut(item.ExecutablePath);
            
            // Check if target file exists, if not, try to launch the shortcut path itself as a fallback
            string launchPath = File.Exists(target.FileName) ? target.FileName : item.ExecutablePath;
            string launchArgs = File.Exists(target.FileName) ? target.Arguments : string.Empty;
            string launchDir = File.Exists(target.FileName) ? target.WorkingDirectory : (Path.GetDirectoryName(item.ExecutablePath) ?? string.Empty);

            var started = TryStart(launchPath, launchArgs, launchDir);

            if (!started)
            {
                ShowWarning($"Đã gọi mở {item.Name}, nhưng Windows không trả về tiến trình.\n\nPath: {launchPath}");
            }
        }
        catch (Exception ex)
        {
            ShowError($"Không mở được {item.Name}:\n{ex.Message}\n\nPath:\n{item.ExecutablePath}");
        }

        return Task.CompletedTask;
    }

    public Task OpenFileLocationAsync(LauncherItem item)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(item.ExecutablePath) || !File.Exists(item.ExecutablePath))
            {
                ShowWarning($"Không tìm thấy shortcut/file:\n{item.ExecutablePath}");
                return Task.CompletedTask;
            }

            var target = ResolveShortcut(item.ExecutablePath);
            var selectPath = File.Exists(target.FileName) ? target.FileName : item.ExecutablePath;
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{selectPath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ShowError($"Không mở được file location:\n{ex.Message}");
        }

        return Task.CompletedTask;
    }

    public async Task<string> GetIconPathAsync(string filePath)
    {
        try
        {
            var iconDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons");
            if (!Directory.Exists(iconDir)) Directory.CreateDirectory(iconDir);

            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var uniqueId = DateTime.Now.Ticks;
            var iconPath = Path.Combine(iconDir, $"{fileName}_{uniqueId}.png");

            // 1. Try IShellItemImageFactory on ORIGINAL path
            if (await TryExtractModernAsync(filePath, iconPath)) return iconPath;

            // 2. Try IShellItemImageFactory on RESOLVED target
            var target = ResolveShortcut(filePath);
            if (File.Exists(target.FileName) && await TryExtractModernAsync(target.FileName, iconPath)) return iconPath;

            // 3. Fallback to high-quality Shell Image List on both
            var legacyResult = await GetLegacyIconPathAsync(filePath, iconPath);
            if (legacyResult != "Assets/Icons/apps.png") return legacyResult;

            if (File.Exists(target.FileName))
            {
                return await GetLegacyIconPathAsync(target.FileName, iconPath);
            }
        }
        catch { }

        return "Assets/Icons/apps.png";
    }

    private async Task<bool> TryExtractModernAsync(string path, string outputPath)
    {
        try
        {
            Guid guid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");
            SHCreateItemFromParsingName(path, IntPtr.Zero, guid, out IShellItemImageFactory factory);
            if (factory != null)
            {
                IntPtr hBitmap;
                // SIIGBF_BIGGERSIZEOK | SIIGBF_ICONONLY | SIIGBF_SCALEUP
                int hr = factory.GetImage(new SIZE(256, 256), (SIIGBF)0x104, out hBitmap);
                if (hr == 0 && hBitmap != IntPtr.Zero)
                {
                    try
                    {
                        using var bitmap = System.Drawing.Bitmap.FromHbitmap(hBitmap);
                        bitmap.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
                        return true;
                    }
                    finally
                    {
                        DeleteObject(hBitmap);
                    }
                }
            }
        }
        catch { }
        return false;
    }

    private async Task<string> GetLegacyIconPathAsync(string filePath, string iconPath)
    {
        IntPtr hIcon = IntPtr.Zero;
        try
        {
            var shinfo = new SHFILEINFO();
            var res = SHGetFileInfo(filePath, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), SHGFI_SYSICONINDEX);
            if (res != IntPtr.Zero)
            {
                Guid iidImageList = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950");
                if (SHGetImageList(SHIL_JUMBO, ref iidImageList, out IImageList iml) == 0 && iml != null)
                {
                    iml.GetIcon(shinfo.iIcon, ILD_TRANSPARENT, ref hIcon);
                }
            }

            if (hIcon == IntPtr.Zero)
            {
                ExtractIconEx(filePath, 0, out hIcon, out _, 1);
            }

            if (hIcon != IntPtr.Zero)
            {
                using var bitmap = System.Drawing.Bitmap.FromHicon(hIcon);
                bitmap.Save(iconPath, System.Drawing.Imaging.ImageFormat.Png);
                return iconPath;
            }
        }
        catch { }
        finally
        {
            if (hIcon != IntPtr.Zero) DestroyIcon(hIcon);
        }
        return "Assets/Icons/apps.png";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
        public SIZE(int cx, int cy) { this.cx = cx; this.cy = cy; }
    }

    private enum SIIGBF
    {
        SIIGBF_RESIZETOFIT = 0x00,
        SIIGBF_BIGGERSIZEOK = 0x01,
        SIIGBF_MEMORYONLY = 0x02,
        SIIGBF_ICONONLY = 0x04,
        SIIGBF_THUMBNAILONLY = 0x08,
        SIIGBF_INCACHEONLY = 0x10,
        SIIGBF_CROPTOSQUARE = 0x20,
        SIIGBF_WIDETHUMBNAILS = 0x40,
        SIIGBF_ICONBACKGROUND = 0x80,
        SIIGBF_SCALEUP = 0x100
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage([In, MarshalAs(UnmanagedType.Struct)] SIZE size, [In] SIIGBF flags, [Out] out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        [In, MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        [In] IntPtr pbc,
        [In, MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [Out, MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    private const uint SHGFI_SYSICONINDEX = 0x4000;
    private const int SHIL_JUMBO = 0x4;
    private const int ILD_TRANSPARENT = 0x1;

    [ComImport]
    [Guid("46EB5926-582E-4017-9FDF-E8998DAA0950")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageList
    {
        [PreserveSig] int GetIcon(int i, int flags, ref IntPtr picon);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("shell32.dll", EntryPoint = "#727")]
    private static extern int SHGetImageList(int iImageList, ref Guid riid, out IImageList ppv);

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern uint ExtractIconEx(string szExeFileName, int nIconIndex, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIcons);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static bool TryStart(string fileName, string arguments, string workingDirectory)
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = true,
                WorkingDirectory = Directory.Exists(workingDirectory)
                    ? workingDirectory
                    : Path.GetDirectoryName(fileName)
            });

            return process is not null || fileName.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static LaunchTarget ResolveShortcut(string path)
    {
        if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return new LaunchTarget(path, string.Empty, Path.GetDirectoryName(path) ?? string.Empty);
        }

        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            var shell = shellType is null ? null : Activator.CreateInstance(shellType);
            if (shellType is null || shell is null)
            {
                return new LaunchTarget(path, string.Empty, Path.GetDirectoryName(path) ?? string.Empty);
            }

            try
            {
                var shortcut = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, [path]);
                if (shortcut is null)
                {
                    return new LaunchTarget(path, string.Empty, Path.GetDirectoryName(path) ?? string.Empty);
                }

                var shortcutType = shortcut.GetType();
                return new LaunchTarget(
                    shortcutType.InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, shortcut, null)?.ToString() ?? path,
                    shortcutType.InvokeMember("Arguments", System.Reflection.BindingFlags.GetProperty, null, shortcut, null)?.ToString() ?? string.Empty,
                    shortcutType.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.GetProperty, null, shortcut, null)?.ToString() ?? string.Empty);
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
        catch
        {
            return new LaunchTarget(path, string.Empty, Path.GetDirectoryName(path) ?? string.Empty);
        }
    }

    private static void ShowInfo(string message) => MessageBox.Show(message, "Launcher", MessageBoxButton.OK, MessageBoxImage.Information);
    private static void ShowWarning(string message) => MessageBox.Show(message, "Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
    private static void ShowError(string message) => MessageBox.Show(message, "Launcher", MessageBoxButton.OK, MessageBoxImage.Error);

    private sealed record LaunchTarget(string FileName, string Arguments, string WorkingDirectory);
}
