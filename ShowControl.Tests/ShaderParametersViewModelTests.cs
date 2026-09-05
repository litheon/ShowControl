using System.Collections.Generic;
using System.ComponentModel;
using ShowControl.ControlPanel.Editor;
using Xunit;

namespace ShowControl.Tests
{
    /// <summary>
    /// The knob readout over the preview. It used to be built in the page's code-behind, where the
    /// per-shader label mapping was the one thing worth getting wrong and the one thing nothing
    /// could check.
    /// </summary>
    public class ShaderParametersViewModelTests
    {
        [Fact]
        public void EachShaderNamesItsOwnKnobs()
        {
            var vm = new ShaderParametersViewModel();

            vm.Update("kaleidoscope", 0.5f, 1f, 2f, 3f, 4f);
            Assert.Contains("Spiral Twist", vm.Text);
            Assert.Contains("Flow Freq", vm.Text);

            vm.Update("clown", 0.5f, 1f, 2f, 3f, 4f);
            Assert.Contains("Blades", vm.Text);
            Assert.Contains("Dot Radius", vm.Text);
            Assert.DoesNotContain("Spiral Twist", vm.Text);
        }

        /// <summary>A shader nobody has named knobs for still shows its numbers.</summary>
        [Fact]
        public void AnUnknownShaderFallsBackToNumberedParameters()
        {
            var vm = new ShaderParametersViewModel();

            vm.Update("geometry", 0.25f, 1f, 2f, 3f, 4f);

            Assert.Contains("Shader: geometry", vm.Text);
            Assert.Contains("Param1: 1.000", vm.Text);
            Assert.Contains("Param4: 4.000", vm.Text);
            Assert.Contains("Distortion: 0.250", vm.Text);
        }

        /// <summary>
        /// The page polls this four times a second whether or not a knob has moved. Notifying only
        /// on a real change is what keeps a still panel from being re-laid out for nothing.
        /// </summary>
        [Fact]
        public void APollThatFindsNothingMoved_NotifiesNobody()
        {
            var vm = new ShaderParametersViewModel();
            var notified = new List<string?>();
            ((INotifyPropertyChanged)vm).PropertyChanged += (s, e) => notified.Add(e.PropertyName);

            vm.Update("clown", 0.5f, 1f, 2f, 3f, 4f);
            vm.Update("clown", 0.5f, 1f, 2f, 3f, 4f);
            vm.Update("clown", 0.5f, 1f, 2f, 3f, 4f);

            Assert.Equal(new[] { nameof(ShaderParametersViewModel.Text) }, notified);
        }
    }
}
