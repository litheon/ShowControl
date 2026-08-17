using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ShowControl.ControlPanel.Editor
{
    /// <summary>
    /// Base for the editor's view-models.
    /// <para>
    /// The only thing here beyond change notification is <see cref="Guard"/>, and it is not
    /// optional. Every UI callback in this app runs inside a try/catch because on Mac Catalyst a
    /// managed exception escaping one is marshalled into an Objective-C exception and aborts the
    /// process — which has already cost this branch one crash. Binding a button to a command
    /// removes the view's catch from the path, so command bodies have to bring their own.
    /// </para>
    /// </summary>
    public abstract class EditorViewModelBase : ObservableObject
    {
        protected EditorViewModelBase(ILogger? logger)
        {
            Logger = logger ?? NullLogger.Instance;
        }

        protected ILogger Logger { get; }

        /// <summary>Runs a command body so a fault is logged and reported rather than thrown at the UI.</summary>
        protected void Guard(string what, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[Editor] {What} failed.", what);
                OnGuardedFailure(what, ex);
            }
        }

        protected virtual void OnGuardedFailure(string what, Exception exception)
        {
        }
    }
}
