using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace ShowControl.ControlPanel.Editor
{
    /// <summary>One capture device as the picker sees it. <paramref name="ChannelCount"/> is
    /// how many inputs it has, which is both shown on the row and the range of the channel
    /// pickers while it is active.</summary>
    public readonly record struct AudioDeviceChoice(int Index, string Name, int ChannelCount, bool IsActive);

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

        /// <summary>The 0-based input pair on the active device that is averaged to mono.</summary>
        (int Left, int Right) ActiveChannels { get; }

        /// <summary>Chooses the pair. False when an index is outside the active device's
        /// inputs or nothing is capturing; never throws.</summary>
        bool SelectInputChannels(int left, int right);
    }

    /// <summary>
    /// The capture-device picker: a list of <see cref="OptionRow"/>s the page draws itself
    /// (the Picker-free rule — MAUI's picker is a thumb-wheel with a broken selection
    /// binding on Catalyst). Rows show the active device rather than holding the choice;
    /// choosing one asks the selector and then re-marks from what actually happened, so a
    /// failed switch never shows a device that is not really capturing.
    /// <para>
    /// Below it, two more row lists pick the left and right input on the active device —
    /// one row per channel the device reports, labelled 1-based the way the hardware is —
    /// and they are rebuilt whenever the active device changes, since its channel count
    /// does. Choosing one asks the selector for the pair and, as with devices, re-marks
    /// from what the selector reports rather than from the click.
    /// </para>
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

        public ObservableCollection<OptionRow> LeftChannelRows { get; } = new();

        public ObservableCollection<OptionRow> RightChannelRows { get; } = new();

        /// <summary>How many inputs the active device has, shown as the channel pickers'
        /// caption; zero when nothing is capturing.</summary>
        [ObservableProperty] private int _activeChannelCount;

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
                DeviceRows.Add(new OptionRow(DeviceLabel(device), () => Guard("Select audio device", () => Choose(index)))
                {
                    IsSelected = device.IsActive,
                });
            }
            StatusText = devices.Count == 0 ? "No capture devices (or capture disabled)" : "";
            RebuildChannelRows(devices);
        });

        private static string DeviceLabel(AudioDeviceChoice device) =>
            $"{device.Name} ({device.ChannelCount} {(device.ChannelCount == 1 ? "input" : "inputs")})";

        /// <summary>One row per input of the active device, for each side. Rebuilt rather
        /// than re-marked because the count is the active device's, and that just changed
        /// or may have.</summary>
        private void RebuildChannelRows(IReadOnlyList<AudioDeviceChoice> devices)
        {
            LeftChannelRows.Clear();
            RightChannelRows.Clear();
            int channels = 0;
            foreach (var device in devices)
            {
                if (device.IsActive)
                {
                    channels = device.ChannelCount;
                    break;
                }
            }
            ActiveChannelCount = channels;
            if (_selector is null || channels == 0)
            {
                return;
            }
            var (left, right) = _selector.ActiveChannels;
            for (int channel = 0; channel < channels; channel++)
            {
                int index = channel;
                string label = (channel + 1).ToString();
                LeftChannelRows.Add(new OptionRow(label, () => Guard("Select left channel", () => ChooseChannels(index, _selector.ActiveChannels.Right)))
                {
                    IsSelected = channel == left,
                });
                RightChannelRows.Add(new OptionRow(label, () => Guard("Select right channel", () => ChooseChannels(_selector.ActiveChannels.Left, index)))
                {
                    IsSelected = channel == right,
                });
            }
        }

        private void ChooseChannels(int left, int right)
        {
            if (_selector is null)
            {
                return;
            }
            bool accepted = _selector.SelectInputChannels(left, right);
            StatusText = accepted ? "" : "Channel change failed; kept the previous pair";
            // Re-mark from reality: rows are positional over 0..channels-1.
            var (actualLeft, actualRight) = _selector.ActiveChannels;
            for (int i = 0; i < LeftChannelRows.Count; i++)
            {
                LeftChannelRows[i].IsSelected = i == actualLeft;
            }
            for (int i = 0; i < RightChannelRows.Count; i++)
            {
                RightChannelRows[i].IsSelected = i == actualRight;
            }
        }

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
            // The channel pickers are the active device's; a different device has a
            // different count (and the service clamps the pair to it).
            RebuildChannelRows(devices);
        }
    }
}
