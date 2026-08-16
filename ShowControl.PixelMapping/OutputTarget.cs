using System.Text.Json.Serialization;

namespace ShowControl.PixelMapping
{
    public enum OutputInterfaceType
    {
        Ddp = 0,
        ArtNet
    }

    /// <summary>
    /// Where a fixture's channels are sent. Fixtures sharing a <see cref="Key"/> are grouped into
    /// a single output device whose channel count spans all of them.
    /// </summary>
    public sealed class OutputTarget
    {
        public const int DefaultDdpPort = 4048;

        public OutputInterfaceType Interface { get; set; } = OutputInterfaceType.Ddp;

        public string IpAddress { get; set; } = string.Empty;

        public int Port { get; set; } = DefaultDdpPort;

        /// <summary>ArtNet universe. Unused for DDP.</summary>
        public byte Universe { get; set; }

        /// <summary>
        /// Zero-based channel offset within this target. Explicit rather than derived from
        /// declaration order, so enabling or reordering a fixture cannot shift anyone else's data.
        /// </summary>
        public int StartChannel { get; set; }

        [JsonIgnore]
        public OutputTargetKey Key => new OutputTargetKey(Interface, IpAddress, Port, Universe);

        public OutputTarget Clone() => new OutputTarget
        {
            Interface = Interface,
            IpAddress = IpAddress,
            Port = Port,
            Universe = Universe,
            StartChannel = StartChannel
        };
    }

    /// <summary>Identity of one physical output endpoint; excludes StartChannel by design.</summary>
    public readonly record struct OutputTargetKey(
        OutputInterfaceType Interface,
        string IpAddress,
        int Port,
        byte Universe)
    {
        public override string ToString() => Interface == OutputInterfaceType.ArtNet
            ? $"ArtNet {IpAddress} universe {Universe}"
            : $"DDP {IpAddress}:{Port}";
    }
}
