using System.Threading.Tasks;
using ShowControl.ControlPanel.Editor;

namespace ShowControl.ControlPanel.Maui
{
    /// <summary>
    /// The platform half of <see cref="IPixelMapFileDialog"/>.
    /// <para>
    /// MAUI ships <c>FilePicker</c> for opening but has no save dialog, so Save As is implemented
    /// per platform. Hand-rolled rather than pulling in CommunityToolkit.Maui for one dialog: the
    /// project already carries platform-specific code for its native surfaces.
    /// </para>
    /// </summary>
    public sealed partial class PixelMapFileDialog : IPixelMapFileDialog
    {
        private static readonly FilePickerFileType JsonFileType = new FilePickerFileType(
            new System.Collections.Generic.Dictionary<DevicePlatform, System.Collections.Generic.IEnumerable<string>>
            {
                [DevicePlatform.MacCatalyst] = new[] { "public.json" },
                [DevicePlatform.WinUI] = new[] { ".json" }
            });

        /// <summary>
        /// Open is per platform as well, on Mac Catalyst at least. MAUI's <c>FilePicker</c> resolves
        /// the picked URL inside an <c>NSFileCoordinator</c> accessor on a background operation
        /// queue, and when the bookmark it makes there fails — a file in the app's own container is
        /// enough — the <c>NSErrorException</c> escapes a native block on a non-UI thread and takes
        /// the process down (crash report 2026-10-17 00:56). The platform picker's delegate runs on
        /// the main thread and makes no bookmark, so it is used directly, as Save As already is.
        /// </summary>
        public Task<string?> PickOpenPathAsync() => PickOpenPlatformAsync();

        public Task<string?> SaveAsAsync(string suggestedFileName, string contents) =>
            SaveAsPlatformAsync(suggestedFileName, contents);

        private partial Task<string?> PickOpenPlatformAsync();

        private partial Task<string?> SaveAsPlatformAsync(string suggestedFileName, string contents);
    }
}
