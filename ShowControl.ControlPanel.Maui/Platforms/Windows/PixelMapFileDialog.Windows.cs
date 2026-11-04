using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage.Pickers;

namespace ShowControl.ControlPanel.Maui
{
    public sealed partial class PixelMapFileDialog
    {
        /// <summary>
        /// The picker has to be initialized against a window handle. It takes the active window
        /// rather than the main one: initializing with the main window's handle from a secondary
        /// window is the usual reason a WinUI picker never appears.
        /// </summary>
        /// <summary>MAUI's picker is fine here; the Catalyst problem is Catalyst's.</summary>
        private partial async Task<string?> PickOpenPlatformAsync()
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Open pixel map",
                FileTypes = JsonFileType
            });
            return result?.FullPath;
        }

        private partial async Task<string?> SaveAsPlatformAsync(string suggestedFileName, string contents)
        {
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedFileName)
            };
            picker.FileTypeChoices.Add("Pixel map", new System.Collections.Generic.List<string> { ".json" });

            nint handle = GetActiveWindowHandle();
            if (handle == 0)
            {
                return null;
            }
            WinRT.Interop.InitializeWithWindow.Initialize(picker, handle);

            var file = await picker.PickSaveFileAsync();
            if (file == null)
            {
                return null;
            }

            await Windows.Storage.FileIO.WriteTextAsync(file, contents);
            return file.Path;
        }

        private static nint GetActiveWindowHandle()
        {
            var windows = Application.Current?.Windows;
            if (windows == null)
            {
                return 0;
            }

            // Prefer the window the user is actually in; fall back to the first one that has a
            // platform view yet.
            foreach (var window in windows)
            {
                if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window platformWindow &&
                    platformWindow.Visible)
                {
                    return WinRT.Interop.WindowNative.GetWindowHandle(platformWindow);
                }
            }

            foreach (var window in windows)
            {
                if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window platformWindow)
                {
                    return WinRT.Interop.WindowNative.GetWindowHandle(platformWindow);
                }
            }

            return 0;
        }
    }
}
