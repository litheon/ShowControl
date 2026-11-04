using System;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ShowControl.ControlPanel.Editor
{
    /// <summary>
    /// The live knob readout over the preview: which shader is running and what the rotary encoders
    /// have it set to.
    /// <para>
    /// The view polls the graphics host — that host is a MAUI-referencing project this one cannot
    /// see — and hands the values here as plain numbers. What each knob is called depends on the
    /// shader, and that mapping is the only thing worth getting wrong, so it lives here rather than
    /// in a code-behind that nothing can test.
    /// </para>
    /// </summary>
    public sealed partial class ShaderParametersViewModel : ObservableObject
    {
        // Mirrors ShowControl.Graphics.Shaders. Duplicated rather than referenced: Graphics is a
        // MAUI project and this one is deliberately plain net9.0 so the tests can reach it.
        private const string Kaleidoscope = "kaleidoscope";
        private const string Clown = "clown";
        private const string Fluid = "fluid";
        private const string AudioLite = "audiolite";

        private static readonly string[] DefaultLabels = { "Param1", "Param2", "Param3", "Param4" };
        private static readonly string[] KaleidoscopeLabels = { "Spiral Twist", "Time Offset", "Ring Density", "Flow Freq" };
        private static readonly string[] ClownLabels = { "Blades", "Spin Offset", "Hue Offset", "Dot Radius" };
        private static readonly string[] AudioLiteLabels = { "Flash Mode", "Param2", "Param3", "Param4" };
        private static readonly string[] FluidLabels = { "Decay", "Drive", "Depth", "Damp" };

        /// <summary>
        /// What the label shows. Assigned through <see cref="ObservableObject.SetProperty{T}"/>, so
        /// a poll that finds nothing moved does not notify and the label is not re-laid out.
        /// </summary>
        [ObservableProperty]
        private string _text = "Shader Params";

        /// <summary>Rebuilds the readout from the host's current values.</summary>
        public void Update(
            string? shader,
            float distortionStrength,
            float param1,
            float param2,
            float param3,
            float param4)
        {
            var labels = LabelsFor(shader);
            Text = string.Format(
                CultureInfo.InvariantCulture,
                "Shader: {0}\nDistortion: {1:F3}\n{2}: {3:F3}\n{4}: {5:F3}\n{6}: {7:F3}\n{8}: {9:F3}",
                shader,
                distortionStrength,
                labels[0], param1,
                labels[1], param2,
                labels[2], param3,
                labels[3], param4);
        }

        internal static string[] LabelsFor(string? shader) => shader switch
        {
            Kaleidoscope => KaleidoscopeLabels,
            Clown => ClownLabels,
            AudioLite => AudioLiteLabels,
            Fluid => FluidLabels,
            _ => DefaultLabels
        };
    }
}
