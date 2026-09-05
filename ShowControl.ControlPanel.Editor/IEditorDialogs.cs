using System.Threading.Tasks;

namespace ShowControl.ControlPanel.Editor
{
    /// <summary>
    /// Open and Save As dialogs for pixel map files.
    /// <para>
    /// The interface lives here rather than with its MAUI implementation so the view-model can own
    /// the file commands: choosing a path is presentation, but deciding whether the current work has
    /// to be confirmed away first, what to suggest as a name, and what to do with the answer is
    /// editor logic and belongs where the tests can reach it.
    /// </para>
    /// </summary>
    public interface IPixelMapFileDialog
    {
        /// <summary>Returns the chosen path, or null if the user cancelled.</summary>
        Task<string?> PickOpenPathAsync();

        /// <summary>
        /// Exports <paramref name="contents"/> to a location the user chooses. Returns the path
        /// written, or null if cancelled.
        /// </summary>
        Task<string?> SaveAsAsync(string suggestedFileName, string contents);
    }

    /// <summary>What the operator chose to do with a working copy recovered from the last session.</summary>
    public enum RecoveredWorkChoice
    {
        /// <summary>Carry on editing it. The default, and what an unanswered prompt means.</summary>
        Keep,

        /// <summary>Throw it away and go back to what was last saved.</summary>
        Discard,

        /// <summary>Keep it and put it somewhere permanent straight away.</summary>
        SaveAs
    }

    /// <summary>
    /// The questions the editor needs to ask before it can act. Kept to the decisions themselves —
    /// no titles or button captions reach the view — so the view-model can be driven from a test.
    /// </summary>
    public interface IEditorDialogs
    {
        /// <summary>Asks before an action that discards unsaved work. True means go ahead.</summary>
        Task<bool> ConfirmDiscardingUnsavedChangesAsync();

        /// <summary>Asks what to do with a working copy recovered from a previous session.</summary>
        Task<RecoveredWorkChoice> AskAboutRecoveredWorkAsync();
    }
}
