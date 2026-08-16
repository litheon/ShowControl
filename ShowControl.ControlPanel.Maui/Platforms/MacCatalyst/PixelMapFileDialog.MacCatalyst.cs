using System;
using System.IO;
using System.Threading.Tasks;
using Foundation;
using UIKit;

namespace ShowControl.ControlPanel.Maui
{
    public sealed partial class PixelMapFileDialog
    {
        /// <summary>
        /// Writes a temporary file inside the container and hands it to the document picker in
        /// export mode.
        /// <para>
        /// <c>asCopy: true</c> matters — the non-copy form <em>moves</em> the file out of the
        /// container. The existing <c>files.user-selected.read-write</c> entitlement covers this,
        /// so there is no plist change.
        /// </para>
        /// </summary>
        private partial async Task<string> SaveAsPlatformAsync(string suggestedFileName, string contents)
        {
            string staged = Path.Combine(FileSystem.CacheDirectory, suggestedFileName);
            File.WriteAllText(staged, contents);

            var completion = new TaskCompletionSource<string>();
            var url = NSUrl.FromFilename(staged);

            var picker = new UIDocumentPickerViewController(new[] { url }, asCopy: true);
            picker.DidPickDocumentAtUrls += (_, e) =>
                completion.TrySetResult(e.Urls != null && e.Urls.Length > 0 ? e.Urls[0].Path : null);
            picker.WasCancelled += (_, _) => completion.TrySetResult(null);

            var presenter = TopViewController();
            if (presenter == null)
            {
                return null;
            }

            presenter.PresentViewController(picker, animated: true, completionHandler: null);
            string chosen = await completion.Task;

            try
            {
                File.Delete(staged);
            }
            catch (IOException)
            {
                // A stale file in the cache directory is not worth surfacing.
            }

            return chosen;
        }

        /// <summary>
        /// The frontmost controller of the window this page is in. Presenting from the wrong scene
        /// is the usual reason a picker never appears when the app has more than one window.
        /// </summary>
        private static UIViewController TopViewController()
        {
            UIWindow window = null;
            foreach (var scene in UIApplication.SharedApplication.ConnectedScenes)
            {
                if (scene is UIWindowScene windowScene && scene.ActivationState == UISceneActivationState.ForegroundActive)
                {
                    foreach (var candidate in windowScene.Windows)
                    {
                        if (candidate.IsKeyWindow)
                        {
                            window = candidate;
                            break;
                        }
                    }
                }
                if (window != null)
                {
                    break;
                }
            }

            var controller = window?.RootViewController;
            while (controller?.PresentedViewController != null)
            {
                controller = controller.PresentedViewController;
            }
            return controller;
        }
    }
}
