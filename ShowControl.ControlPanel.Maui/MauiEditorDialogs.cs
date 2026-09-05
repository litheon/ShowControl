using System;
using System.Threading.Tasks;
using ShowControl.ControlPanel.Editor;

namespace ShowControl.ControlPanel.Maui
{
    /// <summary>
    /// The platform half of <see cref="IEditorDialogs"/>: the editor's questions asked with MAUI's
    /// alerts, on a page it is given rather than one it goes looking for.
    /// <para>
    /// The wording lives here on purpose. What the view-model needs is the answer, and keeping the
    /// captions on this side is what lets the decisions behind them — confirm before discarding,
    /// keep-or-save recovered work — be driven from a test with no UI in the process.
    /// </para>
    /// </summary>
    public sealed class MauiEditorDialogs : IEditorDialogs
    {
        private const string Keep = "Keep editing";
        private const string SaveAs = "Save As…";
        private const string Discard = "Discard";

        /// <summary>
        /// Resolved per call, not captured: the view-model is built before the preview page exists —
        /// the page is constructed with it — so there is no page to hand this at the time.
        /// </summary>
        private readonly Func<Page?> _page;

        public MauiEditorDialogs(Func<Page?> page)
        {
            _page = page;
        }

        public Task<bool> ConfirmDiscardingUnsavedChangesAsync()
        {
            var page = _page();
            if (page == null)
            {
                return Task.FromResult(false);
            }

            return page.DisplayAlert(
                "Unsaved changes",
                "Opening another map discards the current unsaved edits.",
                "Open anyway",
                "Cancel");
        }

        public async Task<RecoveredWorkChoice> AskAboutRecoveredWorkAsync()
        {
            var page = _page();
            if (page == null)
            {
                return RecoveredWorkChoice.Keep;
            }

            string action = await page.DisplayActionSheet(
                "A pixel map with unsaved changes was recovered from the last session.",
                cancel: null,
                destruction: Discard,
                Keep,
                SaveAs);

            // Dismissing the sheet without choosing keeps the work: losing an edit to a stray click
            // is the one outcome here that cannot be undone.
            return action switch
            {
                Discard => RecoveredWorkChoice.Discard,
                SaveAs => RecoveredWorkChoice.SaveAs,
                _ => RecoveredWorkChoice.Keep
            };
        }
    }
}
