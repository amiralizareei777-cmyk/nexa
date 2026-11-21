using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Nexa.Windows.Screenshot;

/// <summary>
/// Captures screenshots on Windows.
/// </summary>
public class ScreenshotService
{
    private readonly string _screenshotFolder;

    public ScreenshotService()
    {
        var picturesFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (string.IsNullOrWhiteSpace(picturesFolder))
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            picturesFolder = !string.IsNullOrWhiteSpace(userProfile)
                ? Path.Combine(userProfile, "Pictures")
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Screenshots");
        }

        _screenshotFolder = Path.Combine(picturesFolder, "Nexa Screenshots");
        try
        {
            Directory.CreateDirectory(_screenshotFolder);
        }
        catch
        {
            _screenshotFolder = Path.Combine(Path.GetTempPath(), "Nexa Screenshots");
            try
            {
                Directory.CreateDirectory(_screenshotFolder);
            }
            catch
            {
                // Fallback to local app base
                _screenshotFolder = AppDomain.CurrentDomain.BaseDirectory;
            }
        }
    }

    public Task<string?> CaptureFullScreenAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                var screenWidth = (int)SystemParameters.VirtualScreenWidth;
                var screenHeight = (int)SystemParameters.VirtualScreenHeight;
                var screenLeft = (int)SystemParameters.VirtualScreenLeft;
                var screenTop = (int)SystemParameters.VirtualScreenTop;

                using var bitmap = new Bitmap(screenWidth, screenHeight, PixelFormat.Format32bppArgb);
                using var graphics = Graphics.FromImage(bitmap);
                graphics.CopyFromScreen(screenLeft, screenTop, 0, 0, new System.Drawing.Size(screenWidth, screenHeight));

                var fileName = $"Screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                var filePath = Path.Combine(_screenshotFolder, fileName);
                bitmap.Save(filePath, ImageFormat.Png);

                return filePath;
            }
            catch
            {
                return null;
            }
        });
    }

    public Task<string?> CaptureActiveWindowAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                var hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return null;

                GetWindowRect(hwnd, out var rect);
                var width = rect.Right - rect.Left;
                var height = rect.Bottom - rect.Top;

                if (width <= 0 || height <= 0) return null;

                using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                using var graphics = Graphics.FromImage(bitmap);
                graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, new System.Drawing.Size(width, height));

                var fileName = $"Screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                var filePath = Path.Combine(_screenshotFolder, fileName);
                bitmap.Save(filePath, ImageFormat.Png);

                return filePath;
            }
            catch
            {
                return null;
            }
        });
    }

    public Task<BitmapSource?> GetScreenshotAsBitmapSourceAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                var screenWidth = (int)SystemParameters.VirtualScreenWidth;
                var screenHeight = (int)SystemParameters.VirtualScreenHeight;
                var screenLeft = (int)SystemParameters.VirtualScreenLeft;
                var screenTop = (int)SystemParameters.VirtualScreenTop;

                using var bitmap = new Bitmap(screenWidth, screenHeight, PixelFormat.Format32bppArgb);
                using var graphics = Graphics.FromImage(bitmap);
                graphics.CopyFromScreen(screenLeft, screenTop, 0, 0, new System.Drawing.Size(screenWidth, screenHeight));

                var bitmapData = bitmap.LockBits(
                    new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format32bppArgb
                );

                var bitmapSource = BitmapSource.Create(
                    bitmapData.Width, bitmapData.Height,
                    bitmap.HorizontalResolution, bitmap.VerticalResolution,
                    System.Windows.Media.PixelFormats.Bgra32,
                    null,
                    bitmapData.Scan0,
                    bitmapData.Stride * bitmapData.Height,
                    bitmapData.Stride
                );

                bitmap.UnlockBits(bitmapData);
                return bitmapSource;
            }
            catch
            {
                return null;
            }
        });
    }

    public string ScreenshotFolder => _screenshotFolder;

    #region Native Interop

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    #endregion
}