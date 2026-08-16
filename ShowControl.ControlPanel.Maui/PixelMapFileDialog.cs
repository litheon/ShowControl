using System.Threading.Tasks;

namespace ShowControl.ControlPanel.Maui
{
    /// <summary>
    /// Open and Save As dialogs for pixel map files.
    /// <para>
    /// MAUI ships <c>FilePicker</c> for opening but has no save dialog, so Save As is implemented
    /// per platform. Hand-rolled rather than pulling in CommunityToolkit.Maui for one dialog: the
    /// project already carries platform-specific code for its native surfaces.
    /// </para>
    /// </summary>
    public interface IPixelMapFileDialog
    {
        /// <summary>Returns the chosen path, or null if the user cancelled.</summary>
        Task<string> PickOpenPathAsync();

        /// <summary>
        /// Exports <paramref name="contents"/> to a location the user chooses. Returns the path
        /// written, or null if cancelled.
        /// </summary>
        Task<string> SaveAsAsync(string suggestedFileName, string contents);
    }

    public sealed partial class PixelMapFileDialog : IPixelMapFileDialog
    {
        private static readonly FilePickerFileType JsonFileType = new FilePickerFileType(
            new System.Collections.Generic.Dictionary<DevicePlatform, System.Collections.Generic.IEnumerable<string>>
            {
                [DevicePlatform.MacCatalyst] = new[] { "public.json" },
                [DevicePlatform.WinUI] = new[] { ".json" }
            });

        public async Task<string> PickOpenPathAsync()
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Open pixel map",
                FileTypes = JsonFileType
            });
            return result?.FullPath;
        }

        public Task<string> SaveAsAsync(string suggestedFileName, string contents) =>
            SaveAsPlatformAsync(suggestedFileName, contents);

        private partial Task<string> SaveAsPlatformAsync(string suggestedFileName, string contents);
    }
}
