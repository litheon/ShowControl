namespace ShowControl.PixelMapping
{
    /// <summary>
    /// A run of pixel indices within one fixture. Used for dead tails, which occur as runs
    /// rather than as scattered individuals.
    /// </summary>
    public readonly record struct PixelRange(int Start, int Count)
    {
        public int EndExclusive => Start + Count;

        public bool Contains(int pixelIndex) => pixelIndex >= Start && pixelIndex < Start + Count;
    }
}
