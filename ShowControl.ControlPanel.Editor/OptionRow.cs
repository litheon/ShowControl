using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ShowControl.ControlPanel.Editor
{
    /// <summary>
    /// One choice in a list the panel draws itself.
    /// <para>
    /// These replaced the two <c>Picker</c>s. MAUI's picker is a <c>UIPickerView</c> — a phone
    /// thumb-wheel in a popover — on Mac Catalyst, which is wrong for a desktop panel, and its
    /// two-way selection binding is the one that hands back a null every time the item list is
    /// assigned. A list of rows the operator clicks has neither problem, works the same on Windows,
    /// and is a plain view-model the tests can drive.
    /// </para>
    /// </summary>
    public sealed partial class OptionRow : ObservableObject
    {
        private readonly Action _choose;

        public OptionRow(string label, Action choose)
        {
            Label = label ?? string.Empty;
            _choose = choose ?? throw new ArgumentNullException(nameof(choose));
        }

        public string Label { get; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Display))]
        private bool _isSelected;

        /// <summary>
        /// The marker and the label in one string, so a row is a single bound label and the list
        /// needs no value converter to show which one is current.
        /// </summary>
        public string Display => (IsSelected ? "● " : "○ ") + Label;

        /// <summary>
        /// Chooses this row. The body it runs is supplied by the owning view-model already wrapped
        /// in <c>Guard</c> — a command binding does not pass through the view's try/catch, and an
        /// exception leaving a UI callback on Mac Catalyst aborts the process.
        /// </summary>
        [RelayCommand]
        private void Choose() => _choose();
    }
}
