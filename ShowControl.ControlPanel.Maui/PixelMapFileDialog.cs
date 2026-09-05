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

        public async Task<string?> PickOpenPathAsync()
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Open pixel map",
                FileTypes = JsonFileType
            });
            return result?.FullPath;
        }

        public Task<string?> SaveAsAsync(string suggestedFileName, string contents) =>
            SaveAsPlatformAsync(suggestedFileName, contents);

        private partial Task<string?> SaveAsPlatformAsync(string suggestedFileName, string contents);
    }
}
