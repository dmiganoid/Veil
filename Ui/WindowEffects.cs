using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Veil.Ui;

/// <summary>
/// Native window touches for borderless windows: Windows 11 rounded corners, a dark frame
/// and a working close command for the themed dialog chrome.
/// </summary>
internal static class WindowEffects
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwcpRound = 2;
    private const int DwmwcpRoundSmall = 3;

    public static void ApplyWindowFrame(Window window, bool smallCorners = false)
    {
        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            SetAttribute(handle, DwmwaUseImmersiveDarkMode, 1);
            SetAttribute(handle, DwmwaWindowCornerPreference, smallCorners ? DwmwcpRoundSmall : DwmwcpRound);
            // COLORREF is 0x00BBGGRR; matches BorderStrongColor #34405A.
            SetAttribute(handle, DwmwaBorderColor, 0x005A4034);
        };
    }

    public static void EnableDialogChrome(Window window)
    {
        ApplyWindowFrame(window, smallCorners: true);
        window.CommandBindings.Add(new CommandBinding(
            SystemCommands.CloseWindowCommand,
            (_, _) => SystemCommands.CloseWindow(window)));
    }

    private static void SetAttribute(IntPtr handle, int attribute, int value)
    {
        try
        {
            _ = DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
