using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace ShowControl.ControlPanel.Editor
{
    /// <summary>One capture device as the picker sees it.</summary>
    public readonly record struct AudioDeviceChoice(int Index, string Name, bool IsActive);

    /// <summary>
    /// The audio side of the picker, reduced to what the view-model needs — the platform
    /// half (PortAudio, in the MAUI app) lives behind it, the same split as
    /// <see cref="IEditorDialogs"/>, so this project stays plain net9.0 and testable.
    /// </summary>
    public interface IAudioDeviceSelector
    {
        IReadOnlyList<AudioDeviceChoice> ListInputDevices();

        /// <summary>Switches capture. False means the device would not open (the selector
        /// reverts or runs without audio); never throws.</summary>
        bool SelectInputDevice(int index);
    }

    /// <summary>
    /// The capture-device picker: a list of <see cref="OptionRow"/>s the page draws itself
    /// (the Picker-free rule — MAUI's picker is a thumb-wheel with a broken selection
    /// binding on Catalyst). Rows show the active device rather than holding the choice;
    /// choosing one asks the selector and then re-marks from what actually happened, so a
    /// failed switch never shows a device that is not really capturing.
    /// </summary>
    public sealed partial class AudioDeviceListViewModel : EditorViewModelBase
    {
        private readonly IAudioDeviceSelector? _selector;

        [ObservableProperty] private string _statusText = "";

        public AudioDeviceListViewModel(IAudioDeviceSelector? selector, ILogger<AudioDeviceListViewModel>? logger)
            : base(logger)
        {
            _selector = selector;
            Refresh();
        }

        public ObservableCollection<OptionRow> DeviceRows { get; } = new();

        /// <summary>Rebuilds the rows from the selector. PortAudio fixes its device list at
        /// startup, so this mostly re-marks the active row — but it keeps the list honest
        /// after a failed switch.</summary>
        [RelayCommand]
        public void Refresh() => Guard("Refresh audio devices", () =>
        {
            DeviceRows.Clear();
            if (_selector is null)
            {
                StatusText = "No audio device selector wired";
                return;
            }
            var devices = _selector.ListInputDevices();
            foreach (var device in devices)
            {
                int index = device.Index;
                DeviceRows.Add(new OptionRow(device.Name, () => Guard("Select audio device", () => Choose(index)))
                {
                    IsSelected = device.IsActive,
                });
            }
            StatusText = devices.Count == 0 ? "No capture devices (or capture disabled)" : "";
        });

        private void Choose(int index)
        {
            if (_selector is null)
            {
                return;
            }
            bool switched = _selector.SelectInputDevice(index);
            StatusText = switched ? "" : "Device would not open; kept the previous one";
            // Re-mark from reality, not from the click: rows and the selector's list share
            // an ordering because Refresh built one from the other.
            var devices = _selector.ListInputDevices();
            for (int i = 0; i < DeviceRows.Count && i < devices.Count; i++)
            {
                DeviceRows[i].IsSelected = devices[i].IsActive;
            }
        }
    }
}
