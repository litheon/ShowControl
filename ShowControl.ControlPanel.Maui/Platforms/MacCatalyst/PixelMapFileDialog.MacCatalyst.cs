using System;
using System.IO;
using System.Threading.Tasks;
using Foundation;
using UIKit;

namespace ShowControl.ControlPanel.Maui
{
    public sealed partial class PixelMapFileDialog
    {
        // The security scope of the file last opened, held for as long as the session may save
        // back to it. Catalyst grants access to a picked file only between Start/Stop, and the
        // editor's Save writes to the same path later, from a different call — so the scope is
        // kept open until another file is picked. One file at a time is what the editor has.
        private static NSUrl _openedScope;

        /// <summary>
        /// Presents the document picker in open mode and returns the path of the file in place
        /// (not a copy — Save has to write back to it). See <see cref="PickOpenPathAsync"/> for
        /// why this is not MAUI's <c>FilePicker</c>.
        /// </summary>
        private partial async Task<string?> PickOpenPlatformAsync()
        {
            var completion = new TaskCompletionSource<NSUrl>();
            var picker = new UIDocumentPickerViewController(new[] { UniformTypeIdentifiers.UTTypes.Json }, asCopy: false);
            picker.DidPickDocumentAtUrls += (_, e) =>
                completion.TrySetResult(e.Urls != null && e.Urls.Length > 0 ? e.Urls[0] : null);
            picker.WasCancelled += (_, _) => completion.TrySetResult(null);

            var presenter = TopViewController();
            if (presenter == null)
            {
                return null;
            }

            presenter.PresentViewController(picker, animated: true, completionHandler: null);
            NSUrl chosen = await completion.Task;
            if (chosen == null)
            {
                return null;
            }

            _openedScope?.StopAccessingSecurityScopedResource();
            _openedScope = chosen;
            // False for a file the sandbox already reaches (the app's own container); the read
            // still works there, so it is not an error.
            chosen.StartAccessingSecurityScopedResource();
            return chosen.Path;
        }

        /// <summary>
        /// Writes a temporary file inside the container and hands it to the document picker in
        /// export mode.
        /// <para>
        /// <c>asCopy: true</c> matters — the non-copy form <em>moves</em> the file out of the
        /// container. The existing <c>files.user-selected.read-write</c> entitlement covers this,
        /// so there is no plist change.
        /// </para>
        /// </summary>
        private partial async Task<string?> SaveAsPlatformAsync(string suggestedFileName, string contents)
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
