using System;
using System.Collections.Generic;
using System.Linq;
using ShowControl.ControlPanel.Editor;
using Xunit;

namespace ShowControl.Tests
{
    public class AudioDeviceListViewModelTests
    {
        private sealed class FakeSelector : IAudioDeviceSelector
        {
            public List<AudioDeviceChoice> Devices { get; } = new()
            {
                new AudioDeviceChoice(0, "Built-in Microphone", ChannelCount: 1, IsActive: false),
                new AudioDeviceChoice(3, "Scarlett 2i2 USB", ChannelCount: 2, IsActive: true),
                new AudioDeviceChoice(5, "Loopback", ChannelCount: 8, IsActive: false),
            };

            public bool NextSelectSucceeds { get; set; } = true;
            public int? LastSelected { get; private set; }
            public (int Left, int Right) ActiveChannels { get; private set; } = (0, 1);
            public (int Left, int Right)? LastChannels { get; private set; }

            private int ActiveChannelCount => Devices.FirstOrDefault(d => d.IsActive).ChannelCount;

            public bool SelectInputChannels(int left, int right)
            {
                LastChannels = (left, right);
                int count = ActiveChannelCount;
                if (left < 0 || right < 0 || left >= count || right >= count)
                {
                    return false;
                }
                ActiveChannels = (left, right);
                return true;
            }

            public IReadOnlyList<AudioDeviceChoice> ListInputDevices() => Devices;

            public bool SelectInputDevice(int index)
            {
                LastSelected = index;
                if (!NextSelectSucceeds)
                {
                    return false;
                }
                for (int i = 0; i < Devices.Count; i++)
                {
                    Devices[i] = Devices[i] with { IsActive = Devices[i].Index == index };
                }
                // The service clamps the pair to the new device, as the real one does.
                int count = ActiveChannelCount;
                ActiveChannels = (Math.Min(ActiveChannels.Left, count - 1), Math.Min(ActiveChannels.Right, count - 1));
                return true;
            }
        }

        [Fact]
        public void Construction_BuildsRowsWithTheActiveDeviceMarked()
        {
            var vm = new AudioDeviceListViewModel(new FakeSelector(), logger: null);

            Assert.Equal(3, vm.DeviceRows.Count);
            Assert.Equal(new[] { false, true, false }, vm.DeviceRows.Select(r => r.IsSelected));
            Assert.Contains("Scarlett", vm.DeviceRows[1].Label);
            Assert.Equal("", vm.StatusText);
        }

        [Fact]
        public void DeviceRows_ShowTheChannelCount()
        {
            var vm = new AudioDeviceListViewModel(new FakeSelector(), logger: null);

            Assert.EndsWith("(1 input)", vm.DeviceRows[0].Label);
            Assert.EndsWith("(2 inputs)", vm.DeviceRows[1].Label);
            Assert.EndsWith("(8 inputs)", vm.DeviceRows[2].Label);
        }

        [Fact]
        public void ChannelRows_CoverTheActiveDevice_WithThePairMarked()
        {
            var vm = new AudioDeviceListViewModel(new FakeSelector(), logger: null);

            Assert.Equal(2, vm.ActiveChannelCount);
            Assert.Equal(new[] { "1", "2" }, vm.LeftChannelRows.Select(r => r.Label));
            Assert.Equal(new[] { "1", "2" }, vm.RightChannelRows.Select(r => r.Label));
            Assert.Equal(new[] { true, false }, vm.LeftChannelRows.Select(r => r.IsSelected));
            Assert.Equal(new[] { false, true }, vm.RightChannelRows.Select(r => r.IsSelected));
        }

        [Fact]
        public void ChoosingAChannelRow_KeepsTheOtherSide_AndMovesTheMark()
        {
            var selector = new FakeSelector();
            var vm = new AudioDeviceListViewModel(selector, logger: null);

            vm.LeftChannelRows[1].ChooseCommand.Execute(null);

            Assert.Equal((1, 1), selector.LastChannels);
            Assert.Equal(new[] { false, true }, vm.LeftChannelRows.Select(r => r.IsSelected));
            Assert.Equal(new[] { false, true }, vm.RightChannelRows.Select(r => r.IsSelected));
            Assert.Equal("", vm.StatusText);

            vm.RightChannelRows[0].ChooseCommand.Execute(null);

            Assert.Equal((1, 0), selector.LastChannels);
            Assert.Equal(new[] { false, true }, vm.LeftChannelRows.Select(r => r.IsSelected));
            Assert.Equal(new[] { true, false }, vm.RightChannelRows.Select(r => r.IsSelected));
        }

        [Fact]
        public void SwitchingDevice_RebuildsTheChannelRowsForItsCount()
        {
            var selector = new FakeSelector();
            var vm = new AudioDeviceListViewModel(selector, logger: null);

            vm.DeviceRows[2].ChooseCommand.Execute(null); // Loopback, 8 inputs
            Assert.Equal(8, vm.ActiveChannelCount);
            Assert.Equal(8, vm.LeftChannelRows.Count);
            Assert.Equal(8, vm.RightChannelRows.Count);
            Assert.True(vm.LeftChannelRows[0].IsSelected);
            Assert.True(vm.RightChannelRows[1].IsSelected);

            vm.RightChannelRows[7].ChooseCommand.Execute(null);
            vm.DeviceRows[0].ChooseCommand.Execute(null); // Built-in mic, 1 input

            Assert.Equal(1, vm.ActiveChannelCount);
            Assert.Single(vm.LeftChannelRows);
            Assert.Single(vm.RightChannelRows);
            // The pair was clamped by the selector and the rows say so.
            Assert.True(vm.LeftChannelRows[0].IsSelected);
            Assert.True(vm.RightChannelRows[0].IsSelected);
        }

        [Fact]
        public void FailedDeviceSwitch_LeavesTheChannelRowsOnTheRealDevice()
        {
            var selector = new FakeSelector { NextSelectSucceeds = false };
            var vm = new AudioDeviceListViewModel(selector, logger: null);

            vm.DeviceRows[2].ChooseCommand.Execute(null);

            Assert.Equal(2, vm.ActiveChannelCount);
            Assert.Equal(2, vm.LeftChannelRows.Count);
        }

        [Fact]
        public void NoActiveDevice_HasNoChannelRows()
        {
            var selector = new FakeSelector();
            for (int i = 0; i < selector.Devices.Count; i++)
            {
                selector.Devices[i] = selector.Devices[i] with { IsActive = false };
            }
            var vm = new AudioDeviceListViewModel(selector, logger: null);

            Assert.Equal(0, vm.ActiveChannelCount);
            Assert.Empty(vm.LeftChannelRows);
            Assert.Empty(vm.RightChannelRows);
        }

        [Fact]
        public void ChoosingARow_SelectsByDeviceIndex_AndMovesTheMark()
        {
            var selector = new FakeSelector();
            var vm = new AudioDeviceListViewModel(selector, logger: null);

            vm.DeviceRows[2].ChooseCommand.Execute(null);

            Assert.Equal(5, selector.LastSelected); // the PortAudio index, not the row position
            Assert.Equal(new[] { false, false, true }, vm.DeviceRows.Select(r => r.IsSelected));
        }

        [Fact]
        public void FailedSwitch_ReportsAndKeepsTheRealDeviceMarked()
        {
            var selector = new FakeSelector { NextSelectSucceeds = false };
            var vm = new AudioDeviceListViewModel(selector, logger: null);

            vm.DeviceRows[0].ChooseCommand.Execute(null);

            Assert.Contains("kept the previous", vm.StatusText);
            // The mark stays where capture actually is.
            Assert.Equal(new[] { false, true, false }, vm.DeviceRows.Select(r => r.IsSelected));
        }

        [Fact]
        public void NoSelector_SaysSoInsteadOfThrowing()
        {
            var vm = new AudioDeviceListViewModel(selector: null, logger: null);

            Assert.Empty(vm.DeviceRows);
            Assert.Contains("No audio device selector", vm.StatusText);
        }

        [Fact]
        public void EmptyDeviceList_ReportsIt()
        {
            var selector = new FakeSelector();
            selector.Devices.Clear();
            var vm = new AudioDeviceListViewModel(selector, logger: null);

            Assert.Empty(vm.DeviceRows);
            Assert.Contains("No capture devices", vm.StatusText);
        }
    }
}
