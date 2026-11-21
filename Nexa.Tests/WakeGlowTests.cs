using System.Runtime.InteropServices;
using System.Windows.Interop;
using Nexa.App.Windows;
using Xunit;

namespace Nexa.Tests;

public class WakeGlowTests
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x20;
    private const int WsExNoActivate = 0x08000000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr handle, int index);

    [Fact]
    public async Task WakeGlow_DoesNotInterceptDesktopClicksOrFocus()
    {
        var result = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var glow = new WakeGlowWindow();
                glow.Show();
                var style = GetWindowLong(new WindowInteropHelper(glow).Handle, GwlExStyle);
                glow.Close();
                result.TrySetResult(style);
            }
            catch (Exception ex) { result.TrySetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        var style = await result.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(WsExTransparent, style & WsExTransparent);
        Assert.Equal(WsExNoActivate, style & WsExNoActivate);
    }
}
