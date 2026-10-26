using Launcher.Models;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace Launcher.Services;

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
            if (!File.Exists(item.ExecutablePath) && !Directory.Exists(item.ExecutablePath) && !LooksLikeShellCommand(item.ExecutablePath))
            {
                ShowWarning($"Không tìm thấy shortcut/file của {item.Name}:\n{item.ExecutablePath}");
                return Task.CompletedTask;
            }

            var target = ResolveShortcut(item.ExecutablePath);
            
            // Check if target file exists, if not, try to launch the shortcut path itself as a fallback
            var targetExists = File.Exists(target.FileName) || Directory.Exists(target.FileName);
            string launchPath = targetExists ? target.FileName : item.ExecutablePath;
            string launchArgs = targetExists ? target.Arguments : string.Empty;
            string launchDir = targetExists
                ? target.WorkingDirectory
                : (Directory.Exists(item.ExecutablePath) ? item.ExecutablePath : (Path.GetDirectoryName(item.ExecutablePath) ?? string.Empty));

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
            if (string.IsNullOrWhiteSpace(item.ExecutablePath) || (!File.Exists(item.ExecutablePath) && !Directory.Exists(item.ExecutablePath)))
            {
                ShowWarning($"Không tìm thấy shortcut/file:\n{item.ExecutablePath}");
                return Task.CompletedTask;
            }

            var target = ResolveShortcut(item.ExecutablePath);
            var selectPath = (File.Exists(target.FileName) || Directory.Exists(target.FileName)) ? target.FileName : item.ExecutablePath;
            if (Directory.Exists(selectPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = selectPath,
                    UseShellExecute = true
                });
                return Task.CompletedTask;
            }

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

    public Task<string> GetIconPathAsync(string filePath)
    {
        return Task.Run(() =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath))
                {
                    return AppPaths.DefaultIconPath;
                }

                AppPaths.EnsureDataDirectories();
                var iconDir = AppPaths.IconDirectory;

                var normalizedKey = Path.GetFullPath(filePath).ToLowerInvariant();
                var lastWrite = File.Exists(filePath) ? File.GetLastWriteTimeUtc(filePath).Ticks : 0;
                var hashInput = $"{normalizedKey}_{lastWrite}";
                var hashBytes = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(hashInput));
                var hashString = Convert.ToHexString(hashBytes).ToLowerInvariant();
                var safeName = string.Join("_", Path.GetFileNameWithoutExtension(filePath).Split(Path.GetInvalidFileNameChars()));
                if (safeName.Length > 24) safeName = safeName[..24];
                var iconPath = Path.Combine(iconDir, $"{safeName}_{hashString}.png");

                // Reuse existing cached icon if already extracted
                if (File.Exists(iconPath) && new FileInfo(iconPath).Length > 0)
                {
                    return iconPath;
                }

                // 1. Try IShellItemImageFactory on ORIGINAL path
                if (TryExtractModern(filePath, iconPath)) return iconPath;

                // 2. Try IShellItemImageFactory on RESOLVED target
                var target = ResolveShortcut(filePath);
                if (File.Exists(target.FileName) && TryExtractModern(target.FileName, iconPath)) return iconPath;

                // 3. Fallback to high-quality Shell Image List on both
                var legacyResult = GetLegacyIconPath(filePath, iconPath);
                if (legacyResult != AppPaths.DefaultIconPath) return legacyResult;

                if (File.Exists(target.FileName))
                {
                    return GetLegacyIconPath(target.FileName, iconPath);
                }
            }
            catch { }

            return AppPaths.DefaultIconPath;
        });
    }

    private static bool TryExtractModern(string path, string outputPath)
    {
        IShellItemImageFactory? factory = null;
        try
        {
            Guid guid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");
            SHCreateItemFromParsingName(path, IntPtr.Zero, guid, out factory);
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
        finally
        {
            if (factory != null)
            {
                try { Marshal.ReleaseComObject(factory); } catch { }
            }
        }
        return false;
    }

    private static string GetLegacyIconPath(string filePath, string iconPath)
    {
        IntPtr hIcon = IntPtr.Zero;
        IntPtr hSmallIcon = IntPtr.Zero;
        IImageList? iml = null;
        try
        {
            var shinfo = new SHFILEINFO();
            var res = SHGetFileInfo(filePath, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), SHGFI_SYSICONINDEX);
            if (res != IntPtr.Zero)
            {
                Guid iidImageList = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950");
                if (SHGetImageList(SHIL_JUMBO, ref iidImageList, out iml) == 0 && iml != null)
                {
                    iml.GetIcon(shinfo.iIcon, ILD_TRANSPARENT, ref hIcon);
                }
            }

            if (hIcon == IntPtr.Zero)
            {
                ExtractIconEx(filePath, 0, out hIcon, out hSmallIcon, 1);
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
            if (hSmallIcon != IntPtr.Zero) DestroyIcon(hSmallIcon);
            if (iml != null)
            {
                try { Marshal.ReleaseComObject(iml); } catch { }
            }
        }
        return AppPaths.DefaultIconPath;
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
                    : (Path.GetDirectoryName(fileName) ?? string.Empty)
            });

            return process is not null ||
                   fileName.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ||
                   Directory.Exists(fileName) ||
                   LooksLikeShellCommand(fileName);
        }
        catch
        {
            return false;
        }
    }

    private static bool LooksLikeShellCommand(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Scheme)) return true;
        return !path.Contains(Path.DirectorySeparatorChar) &&
               !path.Contains(Path.AltDirectorySeparatorChar) &&
               path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
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
                object? shortcut = null;
                try
                {
                    shortcut = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, [path]);
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
                    if (shortcut != null)
                    {
                        try { Marshal.FinalReleaseComObject(shortcut); } catch { }
                    }
                }
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

    private static void ShowInfo(string message) => UserDialog.ShowInfo(message);
    private static void ShowWarning(string message) => UserDialog.ShowWarning(message);
    private static void ShowError(string message) => UserDialog.ShowError(message);

    private sealed record LaunchTarget(string FileName, string Arguments, string WorkingDirectory);
}
