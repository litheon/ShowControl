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
                new AudioDeviceChoice(0, "Built-in Microphone", IsActive: false),
                new AudioDeviceChoice(3, "Scarlett 2i2 USB", IsActive: true),
                new AudioDeviceChoice(5, "Loopback", IsActive: false),
            };

            public bool NextSelectSucceeds { get; set; } = true;
            public int? LastSelected { get; private set; }

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
