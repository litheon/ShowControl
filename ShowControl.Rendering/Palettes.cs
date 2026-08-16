using System;
using System.Drawing;

namespace ShowControl.Rendering
{
    /// <summary>
    /// The color palette library. Palettes are referenced by name from
    /// RenderService.Initialize (via "using static"), including the
    /// commented-out per-venue animation lines, so keep the names stable.
    /// </summary>
    public static class Palettes
    {
        public static readonly Color[] roygbiv = new Color[6]
        {
                Color.FromArgb(0xFF, 0xFF, 0x00, 0x00),
                Color.FromArgb(0xFF, 0xFF, 0xA5, 0x00),
                Color.FromArgb(0xFF, 0xFF, 0xFF, 0x00),
                Color.FromArgb(0xFF, 0x00, 0xFF, 0x00),
                Color.FromArgb(0xFF, 0x00, 0x00, 0xFF),
                Color.FromArgb(0xFF, 0xFF, 0x00, 0xFF)
                //Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)
        };

        public static readonly Color[] warmColdRoygviv = new Color[]
        {
            Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF),
            Color.FromArgb(0xFE, Color.DarkRed),
            Color.FromArgb(0xFD, Color.OrangeRed),

            Color.FromArgb(0xE6, 0xFF, 0xA5, 0x00),
            Color.FromArgb(0xF3, Color.DarkOrange),

            Color.FromArgb(0xC7, 0xFF, 0xFF, 0x00),
            Color.FromArgb(0xF9, Color.Gold),
            Color.FromArgb(0xB6, Color.DarkGoldenrod),

            Color.FromArgb(0xB8, Color.Green),
            Color.FromArgb(0xF8, Color.SeaGreen),

            Color.FromArgb(0xA1, 0x00, 0x00, 0xFF),

            Color.FromArgb(0x95, 0xFF, 0x00, 0xFF),
            Color.FromArgb(0xD8, Color.Purple),


            Color.FromArgb(0xF9, Color.Red),
            Color.FromArgb(0x26, Color.DarkRed),
            Color.FromArgb(0xA9, Color.OrangeRed),
            Color.FromArgb(0x86, Color.Gold),
            Color.FromArgb(0x92, Color.Red),
            Color.FromArgb(0xCD, Color.DarkRed),
            Color.FromArgb(0x20, Color.OrangeRed),
            Color.FromArgb(0xE0, Color.Gold),
            Color.FromArgb(0x50, Color.Red),
            Color.FromArgb(0x10, Color.DarkRed),
            Color.FromArgb(0xC8, Color.OrangeRed),
            Color.FromArgb(0x03, Color.Gold),
            Color.FromArgb(0x82, Color.Red),

            Color.FromArgb(0xC2, Color.DarkRed),
            Color.FromArgb(0x11, Color.OrangeRed),
            Color.FromArgb(0xDB, Color.Gold),
            Color.FromArgb(0x70, Color.Red),
            Color.FromArgb(0x83, Color.DarkRed),
            Color.FromArgb(0x2A, Color.OrangeRed),
            Color.FromArgb(0xD5, Color.Gold),



            Color.FromArgb(0x70, Color.OrangeRed),
            Color.FromArgb(0x83, Color.Orange),
            Color.FromArgb(0x2A, Color.DarkOrange),
            Color.FromArgb(0xD5, Color.OrangeRed),
            Color.FromArgb(0xEF, Color.Orange),
            Color.FromArgb(0x64, Color.DarkOrange),
            Color.FromArgb(0x2F, Color.OrangeRed),
            Color.FromArgb(0x75, Color.Orange),
            Color.FromArgb(0xC7, Color.DarkOrange),
            Color.FromArgb(0xD2, Color.OrangeRed),
            Color.FromArgb(0x9E, Color.Orange),
            Color.FromArgb(0x34, Color.DarkOrange),
            Color.FromArgb(0x9E, Color.OrangeRed),
            Color.FromArgb(0x34, Color.Orange),
            Color.FromArgb(0xBD, Color.DarkOrange),

            Color.FromArgb(0x1A, Color.Gold),
            Color.FromArgb(0xE4, Color.Goldenrod),
            Color.FromArgb(0x3D, Color.DarkGoldenrod),
            Color.FromArgb(0xCE, Color.LightGoldenrodYellow),
            Color.FromArgb(0xA8, Color.PaleGoldenrod),
            Color.FromArgb(0x6B, Color.Gold),
            Color.FromArgb(0x8D, Color.Goldenrod),
            Color.FromArgb(0xDD, Color.DarkGoldenrod),
            Color.FromArgb(0xB6, Color.LightGoldenrodYellow),
            Color.FromArgb(0x7B, Color.PaleGoldenrod),
            Color.FromArgb(0xD3, Color.DarkGoldenrod),
            Color.FromArgb(0x36, Color.LightGoldenrodYellow),
            Color.FromArgb(0x12, Color.PaleGoldenrod),
            Color.FromArgb(0xDC, Color.LightGoldenrodYellow),
            Color.FromArgb(0x6B, Color.PaleGoldenrod),

            //Color.Blue,
            //Color.CornflowerBlue,
            //Color.LightBlue,
            //Color.DarkBlue,
            //Color.DeepSkyBlue,
            //Color.DarkSlateBlue,
            //Color.RoyalBlue,
            //Color.DeepSkyBlue,
            //Color.DarkSlateBlue,
            //Color.RoyalBlue,
            //Color.Blue,

            Color.FromArgb(0x23, Color.BlueViolet),
            Color.FromArgb(0x05, Color.Purple),
            Color.FromArgb(0x63, Color.DarkViolet),
            Color.FromArgb(0xB4, Color.BlueViolet),
            Color.FromArgb(0x17, Color.Purple),
            Color.FromArgb(0xCD, Color.DarkViolet),
            Color.FromArgb(0x1D, Color.MediumVioletRed),
            Color.FromArgb(0x71, Color.PaleVioletRed),
            Color.FromArgb(0x3D, Color.MediumVioletRed),
            Color.FromArgb(0x3D, Color.PaleVioletRed),
            Color.FromArgb(0x4D, Color.Purple),
            Color.FromArgb(0xF2, Color.DarkViolet),
            Color.FromArgb(0xD5, Color.MediumVioletRed),

            Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)
        };

        public static readonly Color[] frequentWarmColdRoygviv = new Color[]
        {
            Color.FromArgb(0xFF, Color.Red),
            Color.FromArgb(0XFD, Color.Orange),

            Color.FromArgb(0xC7, Color.Yellow),
            Color.FromArgb(0xF9, Color.Gold),
            Color.FromArgb(0xB6, Color.DarkGoldenrod),

            Color.FromArgb(0xB8, Color.Green),
            Color.FromArgb(0xF8, Color.SeaGreen),

            Color.LightBlue,
            Color.FromArgb(0xA1, 0x00, 0x00, 0xFF),

            Color.FromArgb(0x95, 0xFF, 0x00, 0xFF),
            Color.FromArgb(0xD8, Color.Purple),

            Color.FromArgb(0xC0, 0xB4, 0xFF, 0x00), // lime
            Color.FromArgb(0xE0, 0xFF, 0x14, 0x93), // hot pink
            Color.FromArgb(0xB0, 0x4B, 0x00, 0x82), // indigo
            Color.FromArgb(0xD0, 0xFF, 0xB4, 0x78), // peach
            Color.FromArgb(0xD8, 0xFF, 0x46, 0x64), // sunset rose
        };

        public static readonly Color[] warm = new Color[]
        {
 
            Color.FromArgb(0xFF, Color.OrangeRed),
            Color.FromArgb(0xFF, Color.DarkOrange),

            Color.FromArgb(0xFF, Color.Gold),
            Color.FromArgb(0xFF, Color.DarkGoldenrod),


            Color.FromArgb(0x79, Color.Gold),
            Color.FromArgb(0x4F, Color.Red),
            Color.FromArgb(0xE1, Color.OrangeRed),
            Color.FromArgb(0xDD, Color.Gold),
            Color.FromArgb(0x35, Color.OrangeRed),
            Color.FromArgb(0x75, Color.Gold),
            Color.FromArgb(0x57, Color.PaleGoldenrod),
            Color.FromArgb(0x49, Color.DarkGoldenrod),
            Color.FromArgb(0xE3, Color.LightGoldenrodYellow),
            Color.FromArgb(0xE4, Color.OrangeRed),
            Color.FromArgb(0x4C, Color.Gold),
            Color.FromArgb(0xDC, Color.OrangeRed),
            Color.FromArgb(0x2F, Color.Gold),



            Color.FromArgb(0x79, Color.OrangeRed),
            Color.FromArgb(0x4F, Color.Orange),
            Color.FromArgb(0xE1, Color.DarkOrange),
            Color.FromArgb(0xDD, Color.OrangeRed),
            Color.FromArgb(0x35, Color.Orange),
            Color.FromArgb(0x75, Color.DarkOrange),
            Color.FromArgb(0x57, Color.OrangeRed),
            Color.FromArgb(0x49, Color.Orange),
            Color.FromArgb(0xE3, Color.DarkOrange),
            Color.FromArgb(0xE4, Color.OrangeRed),
            Color.FromArgb(0x4C, Color.Goldenrod),
            Color.FromArgb(0xDC, Color.DarkGoldenrod),
            Color.FromArgb(0x2F, Color.LightGoldenrodYellow),
            Color.FromArgb(0xAA, Color.Orange),
            Color.FromArgb(0x65, Color.DarkOrange),
            Color.FromArgb(0x8E, Color.OrangeRed),
            Color.FromArgb(0xA1, Color.Orange),
            Color.FromArgb(0xA8, Color.PaleGoldenrod),
            Color.FromArgb(0x12, Color.LightGoldenrodYellow),
            Color.FromArgb(0xD3, Color.PaleGoldenrod),
            Color.FromArgb(0x71, Color.DarkOrange),

            Color.FromArgb(0xf9, Color.Gold),

            Color.FromArgb(0x8F, Color.PaleGoldenrod),
            Color.FromArgb(0x25, Color.Gold),
            Color.FromArgb(0x40, Color.Goldenrod),
            Color.FromArgb(0x11, Color.DarkGoldenrod),
            Color.FromArgb(0x62, Color.LightGoldenrodYellow),

        };

        public static readonly Color[] warmLucidas = new Color[]
        {

            Color.FromArgb(0xFF, Color.OrangeRed),
            Color.FromArgb(0xFF, Color.DarkOrange),
            Color.FromArgb(0xFF, Color.Gold),
            Color.FromArgb(0xFF, Color.DarkGoldenrod),
            Color.FromArgb(0x79, Color.Gold),
            Color.FromArgb(0x4F, Color.Red),
            Color.FromArgb(0xE1, Color.OrangeRed),
            Color.FromArgb(0xDD, Color.Gold),

            Color.FromArgb(0xFF, Color.OrangeRed),
            Color.FromArgb(0xFF, Color.DarkOrange),
            Color.FromArgb(0xFF, Color.Gold),
            Color.FromArgb(0xFF, Color.DarkGoldenrod),
            Color.FromArgb(0x79, Color.Gold),
            Color.FromArgb(0x4F, Color.Red),
            Color.FromArgb(0xE1, Color.OrangeRed),
            Color.FromArgb(0xDD, Color.Gold),

            Color.FromArgb(0xFF, Color.OrangeRed),
            Color.FromArgb(0xFF, Color.DarkOrange),
            Color.FromArgb(0xFF, Color.Gold),
            Color.FromArgb(0xFF, Color.DarkGoldenrod),
            Color.FromArgb(0x79, Color.Gold),
            Color.FromArgb(0x4F, Color.Red),
            Color.FromArgb(0xE1, Color.OrangeRed),
            Color.FromArgb(0xDD, Color.Gold),

            Color.FromArgb(0xFF, Color.OrangeRed),
            Color.FromArgb(0xFF, Color.DarkOrange),
            Color.FromArgb(0xFF, Color.Gold),
            Color.FromArgb(0xFF, Color.DarkGoldenrod),
            Color.FromArgb(0x79, Color.Gold),
            Color.FromArgb(0x4F, Color.Red),
            Color.FromArgb(0xE1, Color.OrangeRed),
            Color.FromArgb(0xDD, Color.Gold),

            Color.FromArgb(0x35, Color.OrangeRed),
            Color.FromArgb(0x75, Color.Gold),
            Color.FromArgb(0x57, Color.PaleGoldenrod),
            Color.FromArgb(0x49, Color.DarkGoldenrod),
            Color.FromArgb(0xE4, Color.OrangeRed),
            Color.FromArgb(0x4C, Color.Gold),
            Color.FromArgb(0xDC, Color.OrangeRed),
            Color.FromArgb(0x2F, Color.Gold),

            Color.FromArgb(0x35, Color.OrangeRed),
            Color.FromArgb(0x75, Color.Gold),
            Color.FromArgb(0x57, Color.PaleGoldenrod),
            Color.FromArgb(0x49, Color.DarkGoldenrod),
            Color.FromArgb(0xE4, Color.OrangeRed),
            Color.FromArgb(0x4C, Color.Gold),
            Color.FromArgb(0xDC, Color.OrangeRed),

            Color.FromArgb(0x35, Color.OrangeRed),
            Color.FromArgb(0x75, Color.Gold),
            Color.FromArgb(0x57, Color.PaleGoldenrod),
            Color.FromArgb(0x49, Color.DarkGoldenrod),
            Color.FromArgb(0xE4, Color.OrangeRed),
            Color.FromArgb(0x4C, Color.Gold),
            Color.FromArgb(0xDC, Color.OrangeRed),

            Color.FromArgb(0x35, Color.OrangeRed),
            Color.FromArgb(0x75, Color.Gold),
            Color.FromArgb(0x57, Color.PaleGoldenrod),
            Color.FromArgb(0x49, Color.DarkGoldenrod),
            Color.FromArgb(0xE4, Color.OrangeRed),
            Color.FromArgb(0x4C, Color.Gold),
            Color.FromArgb(0xDC, Color.OrangeRed),

            Color.FromArgb(0x79, Color.OrangeRed),
            Color.FromArgb(0x4F, Color.Orange),
            Color.FromArgb(0xE1, Color.DarkOrange),
            Color.FromArgb(0xDD, Color.OrangeRed),
            Color.FromArgb(0x35, Color.Orange),
            Color.FromArgb(0x75, Color.DarkOrange),
            Color.FromArgb(0x57, Color.OrangeRed),
            Color.FromArgb(0x49, Color.Orange),

            Color.FromArgb(0x79, Color.OrangeRed),
            Color.FromArgb(0x4F, Color.Orange),
            Color.FromArgb(0xE1, Color.DarkOrange),
            Color.FromArgb(0xDD, Color.OrangeRed),
            Color.FromArgb(0x35, Color.Orange),
            Color.FromArgb(0x75, Color.DarkOrange),
            Color.FromArgb(0x57, Color.OrangeRed),
            Color.FromArgb(0x49, Color.Orange),

            Color.FromArgb(0x79, Color.OrangeRed),
            Color.FromArgb(0x4F, Color.Orange),
            Color.FromArgb(0xE1, Color.DarkOrange),
            Color.FromArgb(0xDD, Color.OrangeRed),
            Color.FromArgb(0x35, Color.Orange),
            Color.FromArgb(0x75, Color.DarkOrange),
            Color.FromArgb(0x57, Color.OrangeRed),
            Color.FromArgb(0x49, Color.Orange),

            Color.FromArgb(0x79, Color.OrangeRed),
            Color.FromArgb(0x4F, Color.Orange),
            Color.FromArgb(0xE1, Color.DarkOrange),
            Color.FromArgb(0xDD, Color.OrangeRed),
            Color.FromArgb(0x35, Color.Orange),
            Color.FromArgb(0x75, Color.DarkOrange),
            Color.FromArgb(0x57, Color.OrangeRed),
            Color.FromArgb(0x49, Color.Orange),

            Color.FromArgb(0xE3, Color.DarkOrange),
            Color.FromArgb(0xE4, Color.OrangeRed),
            Color.FromArgb(0x4C, Color.Goldenrod),
            Color.FromArgb(0xDC, Color.DarkGoldenrod),
            Color.FromArgb(0x2F, Color.LightGoldenrodYellow),
            Color.FromArgb(0xAA, Color.Orange),
            Color.FromArgb(0x65, Color.DarkOrange),
            Color.FromArgb(0x8E, Color.OrangeRed),

            Color.FromArgb(0xE3, Color.DarkOrange),
            Color.FromArgb(0xE4, Color.OrangeRed),
            Color.FromArgb(0x4C, Color.Goldenrod),
            Color.FromArgb(0xDC, Color.DarkGoldenrod),
            Color.FromArgb(0x2F, Color.LightGoldenrodYellow),
            Color.FromArgb(0xAA, Color.Orange),
            Color.FromArgb(0x65, Color.DarkOrange),
            Color.FromArgb(0x8E, Color.OrangeRed),

            Color.FromArgb(0xE3, Color.DarkOrange),
            Color.FromArgb(0xE4, Color.OrangeRed),
            Color.FromArgb(0x4C, Color.Goldenrod),
            Color.FromArgb(0xDC, Color.DarkGoldenrod),
            Color.FromArgb(0x2F, Color.LightGoldenrodYellow),
            Color.FromArgb(0xAA, Color.Orange),
            Color.FromArgb(0x65, Color.DarkOrange),
            Color.FromArgb(0x8E, Color.OrangeRed),

            Color.FromArgb(0xE3, Color.DarkOrange),
            Color.FromArgb(0xE4, Color.OrangeRed),
            Color.FromArgb(0x4C, Color.Goldenrod),
            Color.FromArgb(0xDC, Color.DarkGoldenrod),
            Color.FromArgb(0x2F, Color.LightGoldenrodYellow),
            Color.FromArgb(0xAA, Color.Orange),
            Color.FromArgb(0x65, Color.DarkOrange),
            Color.FromArgb(0x8E, Color.OrangeRed),

            Color.FromArgb(0xA1, Color.Orange),
            Color.FromArgb(0xA8, Color.PaleGoldenrod),
            Color.FromArgb(0x12, Color.LightGoldenrodYellow),
            Color.FromArgb(0xD3, Color.PaleGoldenrod),
            Color.FromArgb(0x71, Color.DarkOrange),
            Color.FromArgb(0xf9, Color.Gold),
            Color.FromArgb(0x8F, Color.PaleGoldenrod),
            Color.FromArgb(0x25, Color.Gold),

            Color.FromArgb(0xA1, Color.Orange),
            Color.FromArgb(0xA8, Color.PaleGoldenrod),
            Color.FromArgb(0x12, Color.LightGoldenrodYellow),
            Color.FromArgb(0xD3, Color.PaleGoldenrod),
            Color.FromArgb(0x71, Color.DarkOrange),
            Color.FromArgb(0xf9, Color.Gold),
            Color.FromArgb(0x8F, Color.PaleGoldenrod),
            Color.FromArgb(0x25, Color.Gold),

            Color.FromArgb(0xA1, Color.Orange),
            Color.FromArgb(0xA8, Color.PaleGoldenrod),
            Color.FromArgb(0x12, Color.LightGoldenrodYellow),
            Color.FromArgb(0xD3, Color.PaleGoldenrod),
            Color.FromArgb(0x71, Color.DarkOrange),
            Color.FromArgb(0xf9, Color.Gold),
            Color.FromArgb(0x8F, Color.PaleGoldenrod),
            Color.FromArgb(0x25, Color.Gold),

            Color.FromArgb(0xA1, Color.Orange),
            Color.FromArgb(0xA8, Color.PaleGoldenrod),
            Color.FromArgb(0x12, Color.LightGoldenrodYellow),
            Color.FromArgb(0xD3, Color.PaleGoldenrod),
            Color.FromArgb(0x71, Color.DarkOrange),
            Color.FromArgb(0xf9, Color.Gold),
            Color.FromArgb(0x8F, Color.PaleGoldenrod),
            Color.FromArgb(0x25, Color.Gold),

            Color.FromArgb(0xA1, Color.Orange),
            Color.FromArgb(0xA8, Color.PaleGoldenrod),
            Color.FromArgb(0x12, Color.LightGoldenrodYellow),
            Color.FromArgb(0xD3, Color.PaleGoldenrod),
            Color.FromArgb(0x71, Color.DarkOrange),
            Color.FromArgb(0xf9, Color.Gold),
            Color.FromArgb(0x8F, Color.PaleGoldenrod),
            Color.FromArgb(0x25, Color.Gold),
        };

        public static readonly Color[] space = new Color[]
        {
            Color.FromArgb(Convert.ToInt32("0x15E91893", 16)), // Redish-Pink
            Color.FromArgb(Convert.ToInt32("0x1DA52668", 16)), // Red-Purple
            Color.FromArgb(Convert.ToInt32("0xDB5D2099", 16)), // Purple
            Color.FromArgb(Convert.ToInt32("0xFEFA8692", 16)), // Peach
            Color.FromArgb(Convert.ToInt32("0xC7FF4DE9", 16)), // Pink
            Color.FromArgb(Convert.ToInt32("0x7828C8E1", 16)), // Blue
            Color.FromArgb(Convert.ToInt32("0x7BF5EC00", 16)), // Yellow

            Color.FromArgb(Convert.ToInt32("0x71E91893", 16)), // Redish-Pink
            Color.FromArgb(Convert.ToInt32("0x36EF32BF", 16)), // Redish-Pink
            Color.FromArgb(Convert.ToInt32("0xEB28C8E1", 16)), // Blue
            Color.FromArgb(Convert.ToInt32("0xA8FA8692", 16)), // Peach
            Color.FromArgb(Convert.ToInt32("0x971BC8E2", 16)), // Blue
            Color.FromArgb(Convert.ToInt32("0xCEC16D75", 16)), // Peach
            Color.FromArgb(Convert.ToInt32("0x97910E5A", 16)), // Redish-Pink
            Color.FromArgb(Convert.ToInt32("0xFEFF4DE9", 16)), // Pink
            Color.FromArgb(Convert.ToInt32("0xA6F5EC00", 16)), // Yellow
            Color.FromArgb(Convert.ToInt32("0x7EA52668", 16)), // Red-Purple
            Color.FromArgb(Convert.ToInt32("0xBC9C236B", 16)), // Red-Purple
            Color.FromArgb(Convert.ToInt32("0x3AFA8692", 16)), // Peach
            Color.FromArgb(Convert.ToInt32("0x048D175B", 16)), // Red-Purple
            Color.FromArgb(Convert.ToInt32("0x57C16D75", 16)), // Peach
            Color.FromArgb(Convert.ToInt32("0xE2CE2968", 16)), // Red-Purple
            Color.FromArgb(Convert.ToInt32("0x6E5D2099", 16)), // Purple
            Color.FromArgb(Convert.ToInt32("0xDE00E9FD", 16)), // Blue
            Color.FromArgb(Convert.ToInt32("0x96641FA1", 16)), // Purple
            Color.FromArgb(Convert.ToInt32("0x5D7033BC", 16)), // Purple
            Color.FromArgb(Convert.ToInt32("0x7400E9FD", 16)), // Blue
            Color.FromArgb(Convert.ToInt32("0xC7A52668", 16)), // Red-Purple
            Color.FromArgb(Convert.ToInt32("0x98FA8692", 16)), // Peach
            Color.FromArgb(Convert.ToInt32("0xC5C16D75", 16)), // Peach
            Color.FromArgb(Convert.ToInt32("0x6B9C236B", 16)), // Red-Purple
            Color.FromArgb(Convert.ToInt32("0x808D175B", 16)), // Red-Purple
            Color.FromArgb(Convert.ToInt32("0x7000E9FD", 16)), // Blue
            Color.FromArgb(Convert.ToInt32("0x2BCE2968", 16)), // Red-Purple
            Color.FromArgb(Convert.ToInt32("0xCAF5EC00", 16)), // Yellow
            Color.FromArgb(Convert.ToInt32("0xFAF7417A", 16)), // Red-Purple

            Color.FromArgb(Convert.ToInt32("0xE300E9FD", 16)), // Blue
            Color.FromArgb(Convert.ToInt32("0x2200E9FD", 16)), // Blue

            Color.FromArgb(Convert.ToInt32("0x46E91893", 16)), // Redish-Pink
            Color.FromArgb(Convert.ToInt32("0xC4EF32BF", 16)), // Redish-Pink
            Color.FromArgb(Convert.ToInt32("0xA628C8E1", 16)), // Blue
            Color.FromArgb(Convert.ToInt32("0x2AFA8692", 16)), // Peach
            Color.FromArgb(Convert.ToInt32("0x151BC8E2", 16)), // Blue
            Color.FromArgb(Convert.ToInt32("0xBBC16D75", 16)), // Peach
            Color.FromArgb(Convert.ToInt32("0x7E910E5A", 16)), // Redish-Pink
            Color.FromArgb(Convert.ToInt32("0x2DFF4DE9", 16)), // Pink
            Color.FromArgb(Convert.ToInt32("0xA1F5EC00", 16)), // Yellow
            Color.FromArgb(Convert.ToInt32("0x8CA52668", 16)), // Red-Purple
            Color.FromArgb(Convert.ToInt32("0x739C236B", 16)), // Red-Purple
            Color.FromArgb(Convert.ToInt32("0x26FA8692", 16)), // Peach
            Color.FromArgb(Convert.ToInt32("0x908D175B", 16)), // Red-Purple
            Color.FromArgb(Convert.ToInt32("0x9DC16D75", 16)), // Peach
            Color.FromArgb(Convert.ToInt32("0x3DCE2968", 16)), // Red-Purple
            Color.FromArgb(Convert.ToInt32("0x3B5D2099", 16)), // Purple
            Color.FromArgb(Convert.ToInt32("0x5300E9FD", 16)), // Blue
            Color.FromArgb(Convert.ToInt32("0x2A641FA1", 16)), // Purple
            Color.FromArgb(Convert.ToInt32("0xFD7033BC", 16)), // Purple
            Color.FromArgb(Convert.ToInt32("0x7200E9FD", 16)), // Blue
            Color.FromArgb(Convert.ToInt32("0xA7A52668", 16)), // Red-Purple
            Color.FromArgb(Convert.ToInt32("0xECFA8692", 16)), // Peach
            Color.FromArgb(Convert.ToInt32("0x4BC16D75", 16)), // Peach
            Color.FromArgb(Convert.ToInt32("0x4F9C236B", 16)), // Red-Purple
            Color.FromArgb(Convert.ToInt32("0xEC8D175B", 16)), // Red-Purple
            Color.FromArgb(Convert.ToInt32("0x5800E9FD", 16)), // Blue
            Color.FromArgb(Convert.ToInt32("0xE9CE2968", 16)), // Red-Purple
            Color.FromArgb(Convert.ToInt32("0x9BF5EC00", 16)), // Yellow
            Color.FromArgb(Convert.ToInt32("0xAEF7417A", 16)), // Red-Purple

            Color.FromArgb(Convert.ToInt32("0xCDE91893", 16)), // Redish-Pink
            Color.FromArgb(Convert.ToInt32("0x9CEF32BF", 16)),
            Color.FromArgb(Convert.ToInt32("0xB8910E5A", 16)),
            Color.FromArgb(Convert.ToInt32("0x88F13085", 16)),
            Color.FromArgb(Convert.ToInt32("0x35FC2975", 16)),
            Color.FromArgb(Convert.ToInt32("0x71D21283", 16)),

            Color.FromArgb(Convert.ToInt32("0xFEF5EC00", 16)), // Yellow

            Color.FromArgb(Convert.ToInt32("0xFFE91893", 16)), // Redish-Pink
            Color.FromArgb(Convert.ToInt32("0xFFEF32BF", 16)),
            Color.FromArgb(Convert.ToInt32("0xFF910E5A", 16)),
            Color.FromArgb(Convert.ToInt32("0xFFF13085", 16)),
            Color.FromArgb(Convert.ToInt32("0xFFFC2975", 16)),
            Color.FromArgb(Convert.ToInt32("0xFFD21283", 16)),

            Color.FromArgb(Convert.ToInt32("0xFFF5EC00", 16)), // Yellow

            Color.FromArgb(Convert.ToInt32("0x9CE91893", 16)), // Redish-Pink
            Color.FromArgb(Convert.ToInt32("0xB8EF32BF", 16)),
            Color.FromArgb(Convert.ToInt32("0x88910E5A", 16)),
            Color.FromArgb(Convert.ToInt32("0x35F13085", 16)),
            Color.FromArgb(Convert.ToInt32("0x71FC2975", 16)),
            Color.FromArgb(Convert.ToInt32("0xFED21283", 16)),

            Color.FromArgb(Convert.ToInt32("0xBC28C8E1", 16)), // Blue
            Color.FromArgb(Convert.ToInt32("0x291BC8E2", 16)),
            Color.FromArgb(Convert.ToInt32("0xD002C3E4", 16)),
            Color.FromArgb(Convert.ToInt32("0x1705CBE5", 16)),
            Color.FromArgb(Convert.ToInt32("0x6850FDFC", 16)),
            Color.FromArgb(Convert.ToInt32("0xB802BCDF", 16)),

            Color.FromArgb(Convert.ToInt32("0x25A52668", 16)), // Red-Purple
            Color.FromArgb(Convert.ToInt32("0x549C236B", 16)),
            Color.FromArgb(Convert.ToInt32("0xE48D175B", 16)),
            Color.FromArgb(Convert.ToInt32("0x17CE2968", 16)),
            Color.FromArgb(Convert.ToInt32("0x1AF7417A", 16)),
            Color.FromArgb(Convert.ToInt32("0x5300E9FD", 16)), // Blue
            Color.FromArgb(Convert.ToInt32("0x90A52668", 16)), // Red-Purple
            Color.FromArgb(Convert.ToInt32("0xB59C236B", 16)),
            Color.FromArgb(Convert.ToInt32("0xF28D175B", 16)),
            Color.FromArgb(Convert.ToInt32("0x36CE2968", 16)),
            Color.FromArgb(Convert.ToInt32("0x93F7417A", 16)),
            Color.FromArgb(Convert.ToInt32("0xA0A52668", 16)), // Red-Purple
            Color.FromArgb(Convert.ToInt32("0x939C236B", 16)),
            Color.FromArgb(Convert.ToInt32("0xA08D175B", 16)),
            Color.FromArgb(Convert.ToInt32("0xFDCE2968", 16)),
            Color.FromArgb(Convert.ToInt32("0xB5F7417A", 16)),
            Color.FromArgb(Convert.ToInt32("0x6F00E9FD", 16)), // Blue
            Color.FromArgb(Convert.ToInt32("0x59A52668", 16)), // Red-Purple
            Color.FromArgb(Convert.ToInt32("0x5F9C236B", 16)),
            Color.FromArgb(Convert.ToInt32("0x3A8D175B", 16)),
            Color.FromArgb(Convert.ToInt32("0x2ACE2968", 16)),
            Color.FromArgb(Convert.ToInt32("0x68F7417A", 16)),

            Color.FromArgb(Convert.ToInt32("0x495D2099", 16)), // Purple
            Color.FromArgb(Convert.ToInt32("0x21641FA1", 16)),
            Color.FromArgb(Convert.ToInt32("0xD17033BC", 16)),
            Color.FromArgb(Convert.ToInt32("0x63621D9D", 16)),
            Color.FromArgb(Convert.ToInt32("0xB57332BC", 16)),
            Color.FromArgb(Convert.ToInt32("0x6F6D2FBE", 16)),
            Color.FromArgb(Convert.ToInt32("0x59F5EC00", 16)), // Yellow
            Color.FromArgb(Convert.ToInt32("0x5F5D2099", 16)), // Purple
            Color.FromArgb(Convert.ToInt32("0x3A641FA1", 16)),
            Color.FromArgb(Convert.ToInt32("0x2A7033BC", 16)),
            Color.FromArgb(Convert.ToInt32("0x68621D9D", 16)),
            Color.FromArgb(Convert.ToInt32("0x497332BC", 16)),
            Color.FromArgb(Convert.ToInt32("0x216D2FBE", 16)),
            Color.FromArgb(Convert.ToInt32("0xD15D2099", 16)), // Purple
            Color.FromArgb(Convert.ToInt32("0x63641FA1", 16)),
            Color.FromArgb(Convert.ToInt32("0x477033BC", 16)),
            Color.FromArgb(Convert.ToInt32("0x4D621D9D", 16)),
            Color.FromArgb(Convert.ToInt32("0xE97332BC", 16)),
            Color.FromArgb(Convert.ToInt32("0xA56D2FBE", 16)),
            Color.FromArgb(Convert.ToInt32("0x94F5EC00", 16)), // Yellow
            Color.FromArgb(Convert.ToInt32("0xC15D2099", 16)), // Purple
            Color.FromArgb(Convert.ToInt32("0x6D641FA1", 16)),
            Color.FromArgb(Convert.ToInt32("0x8F7033BC", 16)),
            Color.FromArgb(Convert.ToInt32("0x4F621D9D", 16)),
            Color.FromArgb(Convert.ToInt32("0xDE7332BC", 16)),
            Color.FromArgb(Convert.ToInt32("0x216D2FBE", 16)),

            Color.FromArgb(Convert.ToInt32("0x50FA8692", 16)), // Peach
            Color.FromArgb(Convert.ToInt32("0xB1C16D75", 16)),
            Color.FromArgb(Convert.ToInt32("0xDEDF7C87", 16)),
            Color.FromArgb(Convert.ToInt32("0xD9E98F99", 16)),
            Color.FromArgb(Convert.ToInt32("0xC1E87580", 16)),
            Color.FromArgb(Convert.ToInt32("0x6DE9537F", 16)),

            Color.FromArgb(Convert.ToInt32("0x8FFF4DE9", 16)), // Pink

            Color.FromArgb(Convert.ToInt32("0x8FFA8692", 16)), // Peach
            Color.FromArgb(Convert.ToInt32("0x4FC16D75", 16)),
            Color.FromArgb(Convert.ToInt32("0xDEDF7C87", 16)),
            Color.FromArgb(Convert.ToInt32("0x21E98F99", 16)),
            Color.FromArgb(Convert.ToInt32("0x50E87580", 16)),
            Color.FromArgb(Convert.ToInt32("0xB1E9537F", 16)),

            Color.FromArgb(Convert.ToInt32("0xDEFF4DE9", 16)), // Pink

            Color.FromArgb(Convert.ToInt32("0xDEFA8692", 16)), // Peach
            Color.FromArgb(Convert.ToInt32("0xD9C16D75", 16)),
            Color.FromArgb(Convert.ToInt32("0xAEDF7C87", 16)),
            Color.FromArgb(Convert.ToInt32("0x71E98F99", 16)),
            Color.FromArgb(Convert.ToInt32("0xD4E87580", 16)),
            Color.FromArgb(Convert.ToInt32("0x81E9537F", 16)),

            Color.FromArgb(Convert.ToInt32("0xFFFA8692", 16)), // Peach
            Color.FromArgb(Convert.ToInt32("0xFFC16D75", 16)),
            Color.FromArgb(Convert.ToInt32("0xFFDF7C87", 16)),
            Color.FromArgb(Convert.ToInt32("0xFFE98F99", 16)),
            Color.FromArgb(Convert.ToInt32("0xFFE87580", 16)),
            Color.FromArgb(Convert.ToInt32("0xFFE9537F", 16)),

            Color.FromArgb(Convert.ToInt32("0x7528C8E1", 16)), // Blue
            Color.FromArgb(Convert.ToInt32("0xDA1BC8E2", 16)),
            Color.FromArgb(Convert.ToInt32("0x2702C3E4", 16)),
            Color.FromArgb(Convert.ToInt32("0x8C05CBE5", 16)),
            Color.FromArgb(Convert.ToInt32("0xCD50FDFC", 16)),
            Color.FromArgb(Convert.ToInt32("0x7D02BCDF", 16)),

            Color.FromArgb(Convert.ToInt32("0x64FF4DE9", 16)), // Pink
            Color.FromArgb(Convert.ToInt32("0x7DF5EC00", 16)), // Yellow

            Color.FromArgb(Convert.ToInt32("0x6428C8E1", 16)), // Blue
            Color.FromArgb(Convert.ToInt32("0x821BC8E2", 16)),
            Color.FromArgb(Convert.ToInt32("0x2B02C3E4", 16)),
            Color.FromArgb(Convert.ToInt32("0xF005CBE5", 16)),
            Color.FromArgb(Convert.ToInt32("0x3050FDFC", 16)),
            Color.FromArgb(Convert.ToInt32("0x3A02BCDF", 16)),

            Color.FromArgb(Convert.ToInt32("0x2DFF4DE9", 16)), // Pink
            Color.FromArgb(Convert.ToInt32("0x8CF5EC00", 16)), // Yellow

            Color.FromArgb(Convert.ToInt32("0x8328C8E1", 16)), // Blue
            Color.FromArgb(Convert.ToInt32("0x591BC8E2", 16)),
            Color.FromArgb(Convert.ToInt32("0xB302C3E4", 16)),
            Color.FromArgb(Convert.ToInt32("0x5505CBE5", 16)),
            Color.FromArgb(Convert.ToInt32("0x5450FDFC", 16)),
            Color.FromArgb(Convert.ToInt32("0x8402BCDF", 16)),

            Color.FromArgb(Convert.ToInt32("0xFFE91893", 16)), // Redish-Pink
            Color.FromArgb(Convert.ToInt32("0xFFEF32BF", 16)), // Redish-Pink
            Color.FromArgb(Convert.ToInt32("0xFF28C8E1", 16)), // Blue
            Color.FromArgb(Convert.ToInt32("0xFFFA8692", 16)), // Peach
            Color.FromArgb(Convert.ToInt32("0xFF1BC8E2", 16)), // Blue
            Color.FromArgb(Convert.ToInt32("0xFFC16D75", 16)), // Peach
            Color.FromArgb(Convert.ToInt32("0xFF910E5A", 16)), // Redish-Pink
            Color.FromArgb(Convert.ToInt32("0xFFFF4DE9", 16)), // Pink
            Color.FromArgb(Convert.ToInt32("0xFFF5EC00", 16)), // Yellow
            Color.FromArgb(Convert.ToInt32("0xFFA52668", 16)), // Red-Purple
            Color.FromArgb(Convert.ToInt32("0xFF9C236B", 16)), // Red-Purple
            Color.FromArgb(Convert.ToInt32("0xFFFA8692", 16)), // Peach
            Color.FromArgb(Convert.ToInt32("0xFF8D175B", 16)), // Red-Purple
            Color.FromArgb(Convert.ToInt32("0xFFC16D75", 16)), // Peach
            Color.FromArgb(Convert.ToInt32("0xFFCE2968", 16)), // Red-Purple
            Color.FromArgb(Convert.ToInt32("0xFF5D2099", 16)), // Purple
            Color.FromArgb(Convert.ToInt32("0xFF00E9FD", 16)), // Blue
            Color.FromArgb(Convert.ToInt32("0xFF641FA1", 16)), // Purple
            Color.FromArgb(Convert.ToInt32("0xFF7033BC", 16)), // Purple
            Color.FromArgb(Convert.ToInt32("0xFF00E9FD", 16)), // Blue
            Color.FromArgb(Convert.ToInt32("0xFFA52668", 16)), // Red-Purple
            Color.FromArgb(Convert.ToInt32("0xFFFA8692", 16)), // Peach
            Color.FromArgb(Convert.ToInt32("0xFFC16D75", 16)), // Peach
            Color.FromArgb(Convert.ToInt32("0xFF9C236B", 16)), // Red-Purple
            Color.FromArgb(Convert.ToInt32("0xFF8D175B", 16)), // Red-Purple
            Color.FromArgb(Convert.ToInt32("0xFF00E9FD", 16)), // Blue
            Color.FromArgb(Convert.ToInt32("0xFFCE2968", 16)), // Red-Purple
            Color.FromArgb(Convert.ToInt32("0xFFF5EC00", 16)), // Yellow
            Color.FromArgb(Convert.ToInt32("0xFFF7417A", 16)), // Red-Purple

            Color.FromArgb(Convert.ToInt32("0xFF00E9FD", 16)), // Blue
            Color.FromArgb(Convert.ToInt32("0xFF00E9FD", 16)), // Blue

            Color.FromArgb(Convert.ToInt32("0x64E91893", 16)), // Redish-Pink
            Color.FromArgb(Convert.ToInt32("0x82EF32BF", 16)), // Redish-Pink
            Color.FromArgb(Convert.ToInt32("0x2B28C8E1", 16)), // Blue
            Color.FromArgb(Convert.ToInt32("0xF0FA8692", 16)), // Peach
            Color.FromArgb(Convert.ToInt32("0x301BC8E2", 16)), // Blue
            Color.FromArgb(Convert.ToInt32("0x3AC16D75", 16)), // Peach
            Color.FromArgb(Convert.ToInt32("0x2D910E5A", 16)), // Redish-Pink
            Color.FromArgb(Convert.ToInt32("0x8CFF4DE9", 16)), // Pink
            Color.FromArgb(Convert.ToInt32("0x83F5EC00", 16)), // Yellow
            Color.FromArgb(Convert.ToInt32("0x59A52668", 16)), // Red-Purple
            Color.FromArgb(Convert.ToInt32("0xB39C236B", 16)), // Red-Purple
            Color.FromArgb(Convert.ToInt32("0x55FA8692", 16)), // Peach
            Color.FromArgb(Convert.ToInt32("0x548D175B", 16)), // Red-Purple
            Color.FromArgb(Convert.ToInt32("0x84C16D75", 16)), // Peach
            Color.FromArgb(Convert.ToInt32("0x7FCE2968", 16)), // Red-Purple
            Color.FromArgb(Convert.ToInt32("0x0A5D2099", 16)), // Purple
            Color.FromArgb(Convert.ToInt32("0x1100E9FD", 16)), // Blue
            Color.FromArgb(Convert.ToInt32("0xC6641FA1", 16)), // Purple
            Color.FromArgb(Convert.ToInt32("0x477033BC", 16)), // Purple
            Color.FromArgb(Convert.ToInt32("0x1800E9FD", 16)), // Blue
            Color.FromArgb(Convert.ToInt32("0x30A52668", 16)), // Red-Purple
            Color.FromArgb(Convert.ToInt32("0xA9FA8692", 16)), // Peach
            Color.FromArgb(Convert.ToInt32("0x4DC16D75", 16)), // Peach
            Color.FromArgb(Convert.ToInt32("0xB59C236B", 16)), // Red-Purple
            Color.FromArgb(Convert.ToInt32("0x0C8D175B", 16)), // Red-Purple
            Color.FromArgb(Convert.ToInt32("0x0800E9FD", 16)), // Blue
            Color.FromArgb(Convert.ToInt32("0x33CE2968", 16)), // Red-Purple
            Color.FromArgb(Convert.ToInt32("0x41F5EC00", 16)), // Yellow
            Color.FromArgb(Convert.ToInt32("0x6EF7417A", 16)), // Red-Purple
        };

        public static readonly Color[] reds = new Color[]
        {
            Color.FromArgb(0xF9, 0xFF, 0x00, 0x00),
            Color.FromArgb(0xc7, Color.DarkRed),

            Color.FromArgb(0xD9, 0xFF, 0x18, 0x00),
            Color.FromArgb(0xEF, 0xE0, 0x00, 0x00),

            Color.FromArgb(0xB6, 0xFF, 0x16, 0x00),

            Color.FromArgb(0xF9, 0xFF, 0x00, 0x00),
            Color.FromArgb(0xc7, Color.DarkRed),

            Color.FromArgb(0xD9, 0xFF, 0x18, 0x00),
            Color.FromArgb(0xEF, 0xE0, 0x00, 0x00),

            Color.FromArgb(0xB6, 0xFF, 0x16, 0x00),

            Color.FromArgb(0xF9, 0xFF, 0x00, 0x00),
            Color.FromArgb(0xc7, Color.DarkRed),

            Color.FromArgb(0xD9, 0xFF, 0x18, 0x00),
            Color.FromArgb(0xEF, 0xE0, 0x00, 0x00),

            Color.FromArgb(0xB6, 0xFF, 0x16, 0x00),
        };

        public static readonly Color[] greens = new Color[]
        {
            Color.FromArgb(0xF9, 0x00, 0xFF, 0x00),
            Color.FromArgb(0xc7, Color.ForestGreen),

            Color.FromArgb(0xD9, Color.LawnGreen),
            Color.FromArgb(0xEF, Color.PaleGreen),

            Color.FromArgb(0xB6, Color.SeaGreen),

            Color.FromArgb(0xF9, 0x00, 0xFF, 0x00),
            Color.FromArgb(0xc7, Color.ForestGreen),

            Color.FromArgb(0xD9, Color.LawnGreen),
            Color.FromArgb(0xEF, Color.PaleGreen),

            Color.FromArgb(0xB6, Color.SeaGreen),

            Color.FromArgb(0xF9, 0x00, 0xFF, 0x00),
            Color.FromArgb(0xc7, Color.ForestGreen),

            Color.FromArgb(0xD9, Color.LawnGreen),
            Color.FromArgb(0xEF, Color.PaleGreen),

            Color.FromArgb(0xB6, Color.SeaGreen),
        };

        public static readonly Color[] redGreenWhite = new Color[]
        {
            Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
            Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(Convert.ToInt32("0x006847", 16)),
            Color.FromArgb(Convert.ToInt32("0x006847", 16)),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(0xFF, Color.White),

            Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
            Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(Convert.ToInt32("0x006847", 16)),
            Color.FromArgb(Convert.ToInt32("0x006847", 16)),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(0xFF, Color.White),

            Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
            Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(Convert.ToInt32("0x006847", 16)),
            Color.FromArgb(Convert.ToInt32("0x006847", 16)),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(0xFF, Color.White),

            Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
            Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(Convert.ToInt32("0x006847", 16)),
            Color.FromArgb(Convert.ToInt32("0x006847", 16)),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(0xFF, Color.White),

            Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
            Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(Convert.ToInt32("0x006847", 16)),
            Color.FromArgb(Convert.ToInt32("0x006847", 16)),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(0xFF, Color.White),

            Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
            Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(Convert.ToInt32("0x006847", 16)),
            Color.FromArgb(Convert.ToInt32("0x006847", 16)),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(0xFF, Color.White),

            Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
            Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(Convert.ToInt32("0x006847", 16)),
            Color.FromArgb(Convert.ToInt32("0x006847", 16)),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(0xFF, Color.White),

            Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
            Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(Convert.ToInt32("0x006847", 16)),
            Color.FromArgb(Convert.ToInt32("0x006847", 16)),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(0xFF, Color.White),

            Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
            Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(Convert.ToInt32("0x006847", 16)),
            Color.FromArgb(Convert.ToInt32("0x006847", 16)),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(0xFF, Color.White),

            Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
            Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(Convert.ToInt32("0x006847", 16)),
            Color.FromArgb(Convert.ToInt32("0x006847", 16)),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(0xFF, Color.White),

            Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
            Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(Convert.ToInt32("0x006847", 16)),
            Color.FromArgb(Convert.ToInt32("0x006847", 16)),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(0xFF, Color.White),

            Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
            Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(Convert.ToInt32("0x006847", 16)),
            Color.FromArgb(Convert.ToInt32("0x006847", 16)),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(0xFF, Color.White),

            Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
            Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(Convert.ToInt32("0x006847", 16)),
            Color.FromArgb(Convert.ToInt32("0x006847", 16)),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(0xFF, Color.White),

            Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
            Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(Convert.ToInt32("0x006847", 16)),
            Color.FromArgb(Convert.ToInt32("0x006847", 16)),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(0xFF, Color.White),

            Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
            Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(Convert.ToInt32("0x006847", 16)),
            Color.FromArgb(Convert.ToInt32("0x006847", 16)),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(0xFF, Color.White),

            Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
            Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(Convert.ToInt32("0x006847", 16)),
            Color.FromArgb(Convert.ToInt32("0x006847", 16)),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(0xFF, Color.White),
        };

        public static readonly Color[] redGreenWhiteAsymmetric = new Color[]
        {
            Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(Convert.ToInt32("0x006847", 16)),
            Color.FromArgb(0xFF, Color.White),

            Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
            Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(Convert.ToInt32("0x006847", 16)),
            Color.FromArgb(0xFF, Color.White),

            Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(Convert.ToInt32("0x006847", 16)),
            Color.FromArgb(0xFF, Color.White),

            Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(Convert.ToInt32("0x006847", 16)),
            Color.FromArgb(0xFF, Color.White),

            Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
            Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
            Color.FromArgb(Convert.ToInt32("0x006847", 16)),
            Color.FromArgb(0xFF, Color.White),

            Color.FromArgb(0xFF, Color.White),
            Color.FromArgb(Convert.ToInt32("0x006847", 16)),
            Color.FromArgb(0xFF, Color.White),
        };

        public static readonly Color[] redGreenWhiteLong = new Color[]
        {
            Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
            Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
            Color.FromArgb(0xD9, 0xFF, 0x18, 0x00),
            Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
            Color.FromArgb(Convert.ToInt32("0xce1127", 16)),

            Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
            Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
            Color.FromArgb(Convert.ToInt32("0xce1127", 16)),
            Color.FromArgb(Convert.ToInt32("0xce1127", 16)),

            Color.FromArgb(Convert.ToInt32("0x006847", 16)),
            Color.FromArgb(Convert.ToInt32("0x006847", 16)),
            Color.FromArgb(0xF9, 0x00, 0xFF, 0x00),
            Color.FromArgb(Convert.ToInt32("0x006847", 16)),
            Color.FromArgb(Convert.ToInt32("0x006847", 16)),

            Color.FromArgb(Convert.ToInt32("0x006847", 16)),
            Color.FromArgb(Convert.ToInt32("0x006847", 16)),
            Color.FromArgb(0xc7, Color.ForestGreen),
            Color.FromArgb(Convert.ToInt32("0x006847", 16)),
            Color.FromArgb(Convert.ToInt32("0x006847", 16)),

            Color.FromArgb(0xF9, 0x00, 0xFF, 0x00),
            Color.FromArgb(0xc7, Color.ForestGreen),

            Color.FromArgb(0xD9, Color.LawnGreen),
            Color.FromArgb(0xEF, Color.PaleGreen),

            Color.FromArgb(0xB6, Color.SeaGreen),

            Color.FromArgb(0xF9, 0x00, 0xFF, 0x00),
            Color.FromArgb(0xc7, Color.ForestGreen),

            Color.FromArgb(0xD9, Color.LawnGreen),
            Color.FromArgb(0xEF, Color.PaleGreen),

            Color.FromArgb(0xB6, Color.SeaGreen),

            Color.FromArgb(0xF9, 0x00, 0xFF, 0x00),
            Color.FromArgb(0xc7, Color.ForestGreen),

            Color.FromArgb(0xD9, Color.LawnGreen),
            Color.FromArgb(0xEF, Color.PaleGreen),

            Color.FromArgb(0xB6, Color.SeaGreen),

            Color.FromArgb(0xF9, 0x00, 0xFF, 0x00),
            Color.FromArgb(0xc7, Color.ForestGreen),

            Color.FromArgb(0xD9, Color.LawnGreen),
            Color.FromArgb(0xEF, Color.PaleGreen),

            Color.FromArgb(0xB6, Color.SeaGreen),

            Color.FromArgb(0xF9, 0x00, 0xFF, 0x00),
            Color.FromArgb(0xc7, Color.ForestGreen),

            Color.FromArgb(0xD9, Color.LawnGreen),
            Color.FromArgb(0xEF, Color.PaleGreen),

            Color.FromArgb(0xB6, Color.SeaGreen),

            Color.FromArgb(0xF9, 0x00, 0xFF, 0x00),
            Color.FromArgb(0xc7, Color.ForestGreen),

            Color.FromArgb(0xD9, Color.LawnGreen),
            Color.FromArgb(0xEF, Color.PaleGreen),

            Color.FromArgb(0xB6, Color.SeaGreen),

            Color.FromArgb(0xF9, 0xFF, 0x00, 0x00),
            Color.FromArgb(0xc7, Color.DarkRed),

            Color.FromArgb(0xD9, 0xFF, 0x18, 0x00),
            Color.FromArgb(0xEF, 0xE0, 0x00, 0x00),

            Color.FromArgb(0xB6, 0xFF, 0x16, 0x00),

            Color.FromArgb(0xF9, 0xFF, 0x00, 0x00),
            Color.FromArgb(0xc7, Color.DarkRed),

            Color.FromArgb(0xD9, 0xFF, 0x18, 0x00),
            Color.FromArgb(0xEF, 0xE0, 0x00, 0x00),

            Color.FromArgb(0xB6, 0xFF, 0x16, 0x00),

            Color.FromArgb(0xF9, 0xFF, 0x00, 0x00),
            Color.FromArgb(0xc7, Color.DarkRed),

            Color.FromArgb(0xD9, 0xFF, 0x18, 0x00),
            Color.FromArgb(0xEF, 0xE0, 0x00, 0x00),

            Color.FromArgb(0xB6, 0xFF, 0x16, 0x00),

            Color.FromArgb(0xF9, 0xFF, 0x00, 0x00),
            Color.FromArgb(0xc7, Color.DarkRed),

            Color.FromArgb(0xD9, 0xFF, 0x18, 0x00),
            Color.FromArgb(0xEF, 0xE0, 0x00, 0x00),

            Color.FromArgb(0xB6, 0xFF, 0x16, 0x00),

            Color.FromArgb(0xF9, 0xFF, 0x00, 0x00),
            Color.FromArgb(0xc7, Color.DarkRed),

            Color.FromArgb(0xD9, 0xFF, 0x18, 0x00),
            Color.FromArgb(0xEF, 0xE0, 0x00, 0x00),

            Color.FromArgb(0xB6, 0xFF, 0x16, 0x00),

            Color.FromArgb(0xF9, 0xFF, 0x00, 0x00),
            Color.FromArgb(0xc7, Color.DarkRed),

            Color.FromArgb(0xD9, 0xFF, 0x18, 0x00),
            Color.FromArgb(0xEF, 0xE0, 0x00, 0x00),

            Color.FromArgb(0xB6, 0xFF, 0x16, 0x00),

            Color.FromArgb(0xF9, 0xFF, 0x00, 0x00),
            Color.FromArgb(0xc7, Color.DarkRed),

            Color.FromArgb(0xD9, 0xFF, 0x18, 0x00),
            Color.FromArgb(0xEF, 0xE0, 0x00, 0x00),

            Color.FromArgb(0xB6, 0xFF, 0x16, 0x00),

            Color.FromArgb(0xF9, 0xFF, 0x00, 0x00),
            Color.FromArgb(0xc7, Color.DarkRed),

            Color.FromArgb(0xD9, 0xFF, 0x18, 0x00),
            Color.FromArgb(0xEF, 0xE0, 0x00, 0x00),

            Color.FromArgb(0xB6, 0xFF, 0x16, 0x00),

            Color.FromArgb(0xF9, 0xFF, 0x00, 0x00),
            Color.FromArgb(0xc7, Color.DarkRed),

            Color.FromArgb(0xD9, 0xFF, 0x18, 0x00),
            Color.FromArgb(0xEF, 0xE0, 0x00, 0x00),

            Color.FromArgb(0xB6, 0xFF, 0x16, 0x00),
        };

        public static readonly Color[] bestButt = new Color[]
        {
            Color.FromArgb(Convert.ToInt32("0x52FFFF00", 16)), // Yellow
            Color.FromArgb(Convert.ToInt32("0x410000FF", 16)), // Blue
            Color.FromArgb(Convert.ToInt32("0x6EFFFF00", 16)), // Yellow
            Color.FromArgb(Convert.ToInt32("0x520000FF", 16)), // Blue
            Color.FromArgb(Convert.ToInt32("0x90FFFF00", 16)), // Yellow
            Color.FromArgb(Convert.ToInt32("0xF40000FF", 16)), // Blue
            Color.FromArgb(Convert.ToInt32("0xC9FFFF00", 16)), // Yellow
            Color.FromArgb(Convert.ToInt32("0x870000FF", 16)), // Blue
            Color.FromArgb(Convert.ToInt32("0x4D0000FF", 16)), // Blue
            Color.FromArgb(Convert.ToInt32("0x920000FF", 16)), // Blue
            Color.FromArgb(Convert.ToInt32("0x730000FF", 16)), // Blue
            Color.FromArgb(Convert.ToInt32("0x2D0000FF", 16)), // Blue
            Color.FromArgb(Convert.ToInt32("0xA0FFFF00", 16)), // Yellow
            Color.FromArgb(Convert.ToInt32("0x7BFFFF00", 16)), // Yellow
            Color.FromArgb(Convert.ToInt32("0x49FFFF00", 16)), // Yellow
            Color.FromArgb(Convert.ToInt32("0xB3FFFF00", 16)), // Yellow
        };

        public static readonly Color[] dragonCyborg1 = new Color[] //1
        {
            Color.FromArgb(Convert.ToInt32("0xF0FE6AFF", 16)), // Purpulish
            Color.FromArgb(Convert.ToInt32("0xC473D7E7", 16)), // Blueish
            Color.FromArgb(Convert.ToInt32("0xEB8D3FD1", 16)), // Purpulish 2
            Color.FromArgb(Convert.ToInt32("0xA0FF00FF", 16)), // Purple AF
            Color.FromArgb(Convert.ToInt32("0xDD4BE8FA", 16)), // Blueish 2
            Color.FromArgb(Convert.ToInt32("0xF2541141", 16)), // Dark Purpleish
            Color.FromArgb(Convert.ToInt32("0xE73287A1", 16)), // Turquoisish

            Color.FromArgb(Convert.ToInt32("0x968D3FD1", 16)), // Purpulish 2
            Color.FromArgb(Convert.ToInt32("0x4173D7E7", 16)), // Blueish
            Color.FromArgb(Convert.ToInt32("0xB33287A1", 16)), // Turquoisish
            Color.FromArgb(Convert.ToInt32("0x8A73D7E7", 16)), // Blueish
            Color.FromArgb(Convert.ToInt32("0xB08D3FD1", 16)), // Purpulish 2
            Color.FromArgb(Convert.ToInt32("0xD6FF00FF", 16)), // Purple AF
            Color.FromArgb(Convert.ToInt32("0xEB8D3FD1", 16)), // Purpulish 2
            Color.FromArgb(Convert.ToInt32("0x943287A1", 16)), // Turquoisish
            Color.FromArgb(Convert.ToInt32("0xF073D7E7", 16)), // Blueish
            Color.FromArgb(Convert.ToInt32("0xC43287A1", 16)), // Turquoisish
            Color.FromArgb(Convert.ToInt32("0xEB8D3FD1", 16)), // Purpulish 2

            Color.FromArgb(Convert.ToInt32("0xA0541141", 16)), // Dark Purpleish
            Color.FromArgb(Convert.ToInt32("0xDD4BE8FA", 16)), // Blueish 2
            Color.FromArgb(Convert.ToInt32("0xF2541141", 16)), // Dark Purpleish
            Color.FromArgb(Convert.ToInt32("0xE74BE8FA", 16)), // Blueish 2
            Color.FromArgb(Convert.ToInt32("0x96541141", 16)), // Dark Purpleish
            Color.FromArgb(Convert.ToInt32("0x41FF00FF", 16)), // Purple AF
            Color.FromArgb(Convert.ToInt32("0xB38D3FD1", 16)), // Purpulish 2
            Color.FromArgb(Convert.ToInt32("0x8A541141", 16)), // Dark Purpleish
            Color.FromArgb(Convert.ToInt32("0xB08D3FD1", 16)), // Purpulish 2
            Color.FromArgb(Convert.ToInt32("0xD6FF00FF", 16)), // Purple AF
            Color.FromArgb(Convert.ToInt32("0xEB4BE8FA", 16)), // Blueish 2
            Color.FromArgb(Convert.ToInt32("0x94541141", 16)), // Dark Purpleish
            Color.FromArgb(Convert.ToInt32("0xDB4BE8FA", 16)), // Blueish 2

            Color.FromArgb(Convert.ToInt32("0x068D3FD1", 16)), // Purpulish 2
            Color.FromArgb(Convert.ToInt32("0x46541141", 16)), // Dark Purpleish
            Color.FromArgb(Convert.ToInt32("0x288D3FD1", 16)), // Purpulish 2
            Color.FromArgb(Convert.ToInt32("0x504BE8FA", 16)), // Blueish 2
            Color.FromArgb(Convert.ToInt32("0xE9541141", 16)), // Dark Purpleish
            Color.FromArgb(Convert.ToInt32("0xDE8D3FD1", 16)), // Purpulish 2
            Color.FromArgb(Convert.ToInt32("0x44541141", 16)), // Dark Purpleish
            Color.FromArgb(Convert.ToInt32("0x468D3FD1", 16)), // Purpulish 2
            Color.FromArgb(Convert.ToInt32("0x544BE8FA", 16)), // Blueish 2
            Color.FromArgb(Convert.ToInt32("0xA9541141", 16)), // Dark Purpleish
            Color.FromArgb(Convert.ToInt32("0x8B8D3FD1", 16)), // Purpulish 2
            Color.FromArgb(Convert.ToInt32("0x98541141", 16)), // Dark Purpleish
            Color.FromArgb(Convert.ToInt32("0x558D3FD1", 16)), // Purpulish 2
            Color.FromArgb(Convert.ToInt32("0xC0541141", 16)), // Dark Purpleish
            Color.FromArgb(Convert.ToInt32("0x548D3FD1", 16)), // Purpulish 2
            Color.FromArgb(Convert.ToInt32("0xA94BE8FA", 16)), // Blueish 2
            Color.FromArgb(Convert.ToInt32("0x8B541141", 16)), // Dark Purpleish
            Color.FromArgb(Convert.ToInt32("0x988D3FD1", 16)), // Purpulish 2
            Color.FromArgb(Convert.ToInt32("0x554BE8FA", 16)), // Blueish 2
            Color.FromArgb(Convert.ToInt32("0xC0541141", 16)), // Dark Purpleish
        };

        public static readonly Color[] dragonCyborg2 = new Color[] //2
        {
            Color.FromArgb(Convert.ToInt32("0xFA31F5C1", 16)), // Blue-green
            Color.FromArgb(Convert.ToInt32("0xBFFF8533", 16)), // Bright orange
            Color.FromArgb(Convert.ToInt32("0x9AFF8C4F", 16)), // Less bright orange
            Color.FromArgb(Convert.ToInt32("0x9853CCFF", 16)), // Dark blue
            Color.FromArgb(Convert.ToInt32("0xE5CC8458", 16)), // Faded orange
            Color.FromArgb(Convert.ToInt32("0xF067FFE3", 16)), // Tealish
            Color.FromArgb(Convert.ToInt32("0xCF31F5C1", 16)), // Blue-green
            Color.FromArgb(Convert.ToInt32("0x6FFF8533", 16)), // Bright orange
            Color.FromArgb(Convert.ToInt32("0xDAFF8C4F", 16)), // Less bright orange
            Color.FromArgb(Convert.ToInt32("0xFA53CCFF", 16)), // Dark blue
            Color.FromArgb(Convert.ToInt32("0xB3CC8458", 16)), // Faded orange
            Color.FromArgb(Convert.ToInt32("0x1167FFE3", 16)), // Tealish


            Color.FromArgb(Convert.ToInt32("0x7831F5C1", 16)), // Blue-green
            Color.FromArgb(Convert.ToInt32("0x9353CCFF", 16)), // Dark blue
            Color.FromArgb(Convert.ToInt32("0x3167FFE3", 16)), // Tealish
            Color.FromArgb(Convert.ToInt32("0x27CC8458", 16)), // Faded orange
            Color.FromArgb(Convert.ToInt32("0x7331F5C1", 16)), // Blue-green
            Color.FromArgb(Convert.ToInt32("0x5A53CCFF", 16)), // Dark blue
            Color.FromArgb(Convert.ToInt32("0x7067FFE3", 16)), // Tealish
            Color.FromArgb(Convert.ToInt32("0x8631F5C1", 16)), // Blue-green
            Color.FromArgb(Convert.ToInt32("0x65CC8458", 16)), // Faded orange
            Color.FromArgb(Convert.ToInt32("0xA253CCFF", 16)), // Dark blue
            Color.FromArgb(Convert.ToInt32("0x5967FFE3", 16)), // Tealish

            Color.FromArgb(Convert.ToInt32("0x4DCC8458", 16)), // Faded orange
            Color.FromArgb(Convert.ToInt32("0x57FF8C4F", 16)), // Less bright orange
            Color.FromArgb(Convert.ToInt32("0x67FF8533", 16)), // Bright orange
            Color.FromArgb(Convert.ToInt32("0xCD31F5C1", 16)), // Blue-green
            Color.FromArgb(Convert.ToInt32("0x45CC8458", 16)), // Faded orange
            Color.FromArgb(Convert.ToInt32("0xEAFF8C4F", 16)), // Less bright orange
            Color.FromArgb(Convert.ToInt32("0xC2FF8533", 16)), // Bright orange
            Color.FromArgb(Convert.ToInt32("0x46CC8458", 16)), // Faded orange
            Color.FromArgb(Convert.ToInt32("0xCE31F5C1", 16)), // Blue-green
            Color.FromArgb(Convert.ToInt32("0xACFF8C4F", 16)), // Less bright orange
            Color.FromArgb(Convert.ToInt32("0xCFFF8533", 16)), // Bright orange
            Color.FromArgb(Convert.ToInt32("0xF1FF8C4F", 16)), // Less bright orange
            Color.FromArgb(Convert.ToInt32("0x28FF8533", 16)), // Bright orange
            Color.FromArgb(Convert.ToInt32("0x74FF8C4F", 16)), // Less bright orange
            Color.FromArgb(Convert.ToInt32("0x4AFF8533", 16)), // Bright orange
        };

        public static readonly Color[] dragonCyborg2Lucidas = new Color[] //2
        {
            Color.FromArgb(Convert.ToInt32("0xFA31F5C1", 16)), // Blue-green
            Color.FromArgb(Convert.ToInt32("0xBFFF8533", 16)), // Bright orange
            Color.FromArgb(Convert.ToInt32("0x9AFF8C4F", 16)), // Less bright orange
            Color.FromArgb(Convert.ToInt32("0x9853CCFF", 16)), // Dark blue
            Color.FromArgb(Convert.ToInt32("0xE5CC8458", 16)), // Faded orange
            Color.FromArgb(Convert.ToInt32("0xF067FFE3", 16)), // Tealish
            Color.FromArgb(Convert.ToInt32("0xCF31F5C1", 16)), // Blue-green
            Color.FromArgb(Convert.ToInt32("0x6FFF8533", 16)), // Bright orange

            Color.FromArgb(Convert.ToInt32("0xFA31F5C1", 16)), // Blue-green
            Color.FromArgb(Convert.ToInt32("0xBFFF8533", 16)), // Bright orange
            Color.FromArgb(Convert.ToInt32("0x9AFF8C4F", 16)), // Less bright orange
            Color.FromArgb(Convert.ToInt32("0x9853CCFF", 16)), // Dark blue
            Color.FromArgb(Convert.ToInt32("0xE5CC8458", 16)), // Faded orange
            Color.FromArgb(Convert.ToInt32("0xF067FFE3", 16)), // Tealish
            Color.FromArgb(Convert.ToInt32("0xCF31F5C1", 16)), // Blue-green
            Color.FromArgb(Convert.ToInt32("0x6FFF8533", 16)), // Bright orange

            Color.FromArgb(Convert.ToInt32("0xFA31F5C1", 16)), // Blue-green
            Color.FromArgb(Convert.ToInt32("0xBFFF8533", 16)), // Bright orange
            Color.FromArgb(Convert.ToInt32("0x9AFF8C4F", 16)), // Less bright orange
            Color.FromArgb(Convert.ToInt32("0x9853CCFF", 16)), // Dark blue
            Color.FromArgb(Convert.ToInt32("0xE5CC8458", 16)), // Faded orange
            Color.FromArgb(Convert.ToInt32("0xF067FFE3", 16)), // Tealish
            Color.FromArgb(Convert.ToInt32("0xCF31F5C1", 16)), // Blue-green
            Color.FromArgb(Convert.ToInt32("0x6FFF8533", 16)), // Bright orange

            Color.FromArgb(Convert.ToInt32("0xFA31F5C1", 16)), // Blue-green
            Color.FromArgb(Convert.ToInt32("0xBFFF8533", 16)), // Bright orange
            Color.FromArgb(Convert.ToInt32("0x9AFF8C4F", 16)), // Less bright orange
            Color.FromArgb(Convert.ToInt32("0x9853CCFF", 16)), // Dark blue
            Color.FromArgb(Convert.ToInt32("0xE5CC8458", 16)), // Faded orange
            Color.FromArgb(Convert.ToInt32("0xF067FFE3", 16)), // Tealish
            Color.FromArgb(Convert.ToInt32("0xCF31F5C1", 16)), // Blue-green
            Color.FromArgb(Convert.ToInt32("0x6FFF8533", 16)), // Bright orange

            Color.FromArgb(Convert.ToInt32("0xDAFF8C4F", 16)), // Less bright orange
            Color.FromArgb(Convert.ToInt32("0xFA53CCFF", 16)), // Dark blue
            Color.FromArgb(Convert.ToInt32("0xB3CC8458", 16)), // Faded orange
            Color.FromArgb(Convert.ToInt32("0x1167FFE3", 16)), // Tealish
            Color.FromArgb(Convert.ToInt32("0x7831F5C1", 16)), // Blue-green
            Color.FromArgb(Convert.ToInt32("0x9353CCFF", 16)), // Dark blue
            Color.FromArgb(Convert.ToInt32("0x3167FFE3", 16)), // Tealish
            Color.FromArgb(Convert.ToInt32("0x27CC8458", 16)), // Faded orange

            Color.FromArgb(Convert.ToInt32("0xDAFF8C4F", 16)), // Less bright orange
            Color.FromArgb(Convert.ToInt32("0xFA53CCFF", 16)), // Dark blue
            Color.FromArgb(Convert.ToInt32("0xB3CC8458", 16)), // Faded orange
            Color.FromArgb(Convert.ToInt32("0x1167FFE3", 16)), // Tealish
            Color.FromArgb(Convert.ToInt32("0x7831F5C1", 16)), // Blue-green
            Color.FromArgb(Convert.ToInt32("0x9353CCFF", 16)), // Dark blue
            Color.FromArgb(Convert.ToInt32("0x3167FFE3", 16)), // Tealish
            Color.FromArgb(Convert.ToInt32("0x27CC8458", 16)), // Faded orange

            Color.FromArgb(Convert.ToInt32("0xDAFF8C4F", 16)), // Less bright orange
            Color.FromArgb(Convert.ToInt32("0xFA53CCFF", 16)), // Dark blue
            Color.FromArgb(Convert.ToInt32("0xB3CC8458", 16)), // Faded orange
            Color.FromArgb(Convert.ToInt32("0x1167FFE3", 16)), // Tealish
            Color.FromArgb(Convert.ToInt32("0x7831F5C1", 16)), // Blue-green
            Color.FromArgb(Convert.ToInt32("0x9353CCFF", 16)), // Dark blue
            Color.FromArgb(Convert.ToInt32("0x3167FFE3", 16)), // Tealish
            Color.FromArgb(Convert.ToInt32("0x27CC8458", 16)), // Faded orange

            Color.FromArgb(Convert.ToInt32("0xDAFF8C4F", 16)), // Less bright orange
            Color.FromArgb(Convert.ToInt32("0xFA53CCFF", 16)), // Dark blue
            Color.FromArgb(Convert.ToInt32("0xB3CC8458", 16)), // Faded orange
            Color.FromArgb(Convert.ToInt32("0x1167FFE3", 16)), // Tealish
            Color.FromArgb(Convert.ToInt32("0x7831F5C1", 16)), // Blue-green
            Color.FromArgb(Convert.ToInt32("0x9353CCFF", 16)), // Dark blue
            Color.FromArgb(Convert.ToInt32("0x3167FFE3", 16)), // Tealish
            Color.FromArgb(Convert.ToInt32("0x27CC8458", 16)), // Faded orange

            Color.FromArgb(Convert.ToInt32("0x7331F5C1", 16)), // Blue-green
            Color.FromArgb(Convert.ToInt32("0x5A53CCFF", 16)), // Dark blue
            Color.FromArgb(Convert.ToInt32("0x7067FFE3", 16)), // Tealish
            Color.FromArgb(Convert.ToInt32("0x8631F5C1", 16)), // Blue-green
            Color.FromArgb(Convert.ToInt32("0x65CC8458", 16)), // Faded orange
            Color.FromArgb(Convert.ToInt32("0xA253CCFF", 16)), // Dark blue
            Color.FromArgb(Convert.ToInt32("0x5967FFE3", 16)), // Tealish
            Color.FromArgb(Convert.ToInt32("0x4DCC8458", 16)), // Faded orange

            Color.FromArgb(Convert.ToInt32("0x7331F5C1", 16)), // Blue-green
            Color.FromArgb(Convert.ToInt32("0x5A53CCFF", 16)), // Dark blue
            Color.FromArgb(Convert.ToInt32("0x7067FFE3", 16)), // Tealish
            Color.FromArgb(Convert.ToInt32("0x8631F5C1", 16)), // Blue-green
            Color.FromArgb(Convert.ToInt32("0x65CC8458", 16)), // Faded orange
            Color.FromArgb(Convert.ToInt32("0xA253CCFF", 16)), // Dark blue
            Color.FromArgb(Convert.ToInt32("0x5967FFE3", 16)), // Tealish
            Color.FromArgb(Convert.ToInt32("0x4DCC8458", 16)), // Faded orange

            Color.FromArgb(Convert.ToInt32("0x7331F5C1", 16)), // Blue-green
            Color.FromArgb(Convert.ToInt32("0x5A53CCFF", 16)), // Dark blue
            Color.FromArgb(Convert.ToInt32("0x7067FFE3", 16)), // Tealish
            Color.FromArgb(Convert.ToInt32("0x8631F5C1", 16)), // Blue-green
            Color.FromArgb(Convert.ToInt32("0x65CC8458", 16)), // Faded orange
            Color.FromArgb(Convert.ToInt32("0xA253CCFF", 16)), // Dark blue
            Color.FromArgb(Convert.ToInt32("0x5967FFE3", 16)), // Tealish
            Color.FromArgb(Convert.ToInt32("0x4DCC8458", 16)), // Faded orange

            Color.FromArgb(Convert.ToInt32("0x7331F5C1", 16)), // Blue-green
            Color.FromArgb(Convert.ToInt32("0x5A53CCFF", 16)), // Dark blue
            Color.FromArgb(Convert.ToInt32("0x7067FFE3", 16)), // Tealish
            Color.FromArgb(Convert.ToInt32("0x8631F5C1", 16)), // Blue-green
            Color.FromArgb(Convert.ToInt32("0x65CC8458", 16)), // Faded orange
            Color.FromArgb(Convert.ToInt32("0xA253CCFF", 16)), // Dark blue
            Color.FromArgb(Convert.ToInt32("0x5967FFE3", 16)), // Tealish
            Color.FromArgb(Convert.ToInt32("0x4DCC8458", 16)), // Faded orange

            Color.FromArgb(Convert.ToInt32("0x57FF8C4F", 16)), // Less bright orange
            Color.FromArgb(Convert.ToInt32("0x67FF8533", 16)), // Bright orange
            Color.FromArgb(Convert.ToInt32("0xCD31F5C1", 16)), // Blue-green
            Color.FromArgb(Convert.ToInt32("0x45CC8458", 16)), // Faded orange
            Color.FromArgb(Convert.ToInt32("0xEAFF8C4F", 16)), // Less bright orange
            Color.FromArgb(Convert.ToInt32("0xC2FF8533", 16)), // Bright orange
            Color.FromArgb(Convert.ToInt32("0x46CC8458", 16)), // Faded orange
            Color.FromArgb(Convert.ToInt32("0xCE31F5C1", 16)), // Blue-green

            Color.FromArgb(Convert.ToInt32("0x57FF8C4F", 16)), // Less bright orange
            Color.FromArgb(Convert.ToInt32("0x67FF8533", 16)), // Bright orange
            Color.FromArgb(Convert.ToInt32("0xCD31F5C1", 16)), // Blue-green
            Color.FromArgb(Convert.ToInt32("0x45CC8458", 16)), // Faded orange
            Color.FromArgb(Convert.ToInt32("0xEAFF8C4F", 16)), // Less bright orange
            Color.FromArgb(Convert.ToInt32("0xC2FF8533", 16)), // Bright orange
            Color.FromArgb(Convert.ToInt32("0x46CC8458", 16)), // Faded orange
            Color.FromArgb(Convert.ToInt32("0xCE31F5C1", 16)), // Blue-green

            Color.FromArgb(Convert.ToInt32("0x57FF8C4F", 16)), // Less bright orange
            Color.FromArgb(Convert.ToInt32("0x67FF8533", 16)), // Bright orange
            Color.FromArgb(Convert.ToInt32("0xCD31F5C1", 16)), // Blue-green
            Color.FromArgb(Convert.ToInt32("0x45CC8458", 16)), // Faded orange
            Color.FromArgb(Convert.ToInt32("0xEAFF8C4F", 16)), // Less bright orange
            Color.FromArgb(Convert.ToInt32("0xC2FF8533", 16)), // Bright orange
            Color.FromArgb(Convert.ToInt32("0x46CC8458", 16)), // Faded orange
            Color.FromArgb(Convert.ToInt32("0xCE31F5C1", 16)), // Blue-green

            Color.FromArgb(Convert.ToInt32("0xACFF8C4F", 16)), // Less bright orange
            Color.FromArgb(Convert.ToInt32("0xCFFF8533", 16)), // Bright orange
            Color.FromArgb(Convert.ToInt32("0xF1FF8C4F", 16)), // Less bright orange
            Color.FromArgb(Convert.ToInt32("0x28FF8533", 16)), // Bright orange
            Color.FromArgb(Convert.ToInt32("0x74FF8C4F", 16)), // Less bright orange
            Color.FromArgb(Convert.ToInt32("0x4AFF8533", 16)), // Bright orange
            Color.FromArgb(Convert.ToInt32("0x74FF8C4F", 16)), // Less bright orange
            Color.FromArgb(Convert.ToInt32("0x4AFF8533", 16)), // Bright orange
        };

        public static readonly Color[] dragonCyborg2Asymmetric = new Color[] //2
        {
            Color.FromArgb(Convert.ToInt32("0xFA31F5C1", 16)), // Blue-green
            Color.FromArgb(Convert.ToInt32("0xBFFF8533", 16)), // Bright orange
            Color.FromArgb(Convert.ToInt32("0x9AFF8C4F", 16)), // Less bright orange
            Color.FromArgb(Convert.ToInt32("0x9853CCFF", 16)), // Dark blue
            Color.FromArgb(Convert.ToInt32("0xE5CC8458", 16)), // Faded orange


            Color.FromArgb(Convert.ToInt32("0xFA31F5C1", 16)), // Blue-green
            Color.FromArgb(Convert.ToInt32("0xBFFF8533", 16)), // Bright orange

            Color.FromArgb(Convert.ToInt32("0xF067FFE3", 16)), // Tealish
            Color.FromArgb(Convert.ToInt32("0xCF31F5C1", 16)), // Blue-green
            Color.FromArgb(Convert.ToInt32("0x6FFF8533", 16)), // Bright orange

            Color.FromArgb(Convert.ToInt32("0xFA31F5C1", 16)), // Blue-green
            Color.FromArgb(Convert.ToInt32("0xBFFF8533", 16)), // Bright orange
            Color.FromArgb(Convert.ToInt32("0x9AFF8C4F", 16)), // Less bright orange

            Color.FromArgb(Convert.ToInt32("0xF067FFE3", 16)), // Tealish
            Color.FromArgb(Convert.ToInt32("0xCF31F5C1", 16)), // Blue-green
            Color.FromArgb(Convert.ToInt32("0x6FFF8533", 16)), // Bright orange

            Color.FromArgb(Convert.ToInt32("0xFA31F5C1", 16)), // Blue-green

            Color.FromArgb(Convert.ToInt32("0xE5CC8458", 16)), // Faded orange
            Color.FromArgb(Convert.ToInt32("0xF067FFE3", 16)), // Tealish
            Color.FromArgb(Convert.ToInt32("0xCF31F5C1", 16)), // Blue-green
            Color.FromArgb(Convert.ToInt32("0x6FFF8533", 16)), // Bright orange

            Color.FromArgb(Convert.ToInt32("0xDAFF8C4F", 16)), // Less bright orange
            Color.FromArgb(Convert.ToInt32("0xFA53CCFF", 16)), // Dark blue
            Color.FromArgb(Convert.ToInt32("0xB3CC8458", 16)), // Faded orange
            Color.FromArgb(Convert.ToInt32("0x1167FFE3", 16)), // Tealish
            Color.FromArgb(Convert.ToInt32("0x7831F5C1", 16)), // Blue-green

            Color.FromArgb(Convert.ToInt32("0x3167FFE3", 16)), // Tealish
            Color.FromArgb(Convert.ToInt32("0x27CC8458", 16)), // Faded orange

            Color.FromArgb(Convert.ToInt32("0xDAFF8C4F", 16)), // Less bright orange
            Color.FromArgb(Convert.ToInt32("0xFA53CCFF", 16)), // Dark blue
            Color.FromArgb(Convert.ToInt32("0xB3CC8458", 16)), // Faded orange
            Color.FromArgb(Convert.ToInt32("0x1167FFE3", 16)), // Tealish

            Color.FromArgb(Convert.ToInt32("0x3167FFE3", 16)), // Tealish
            Color.FromArgb(Convert.ToInt32("0x27CC8458", 16)), // Faded orange

            Color.FromArgb(Convert.ToInt32("0xDAFF8C4F", 16)), // Less bright orange
            Color.FromArgb(Convert.ToInt32("0xFA53CCFF", 16)), // Dark blue
            Color.FromArgb(Convert.ToInt32("0xB3CC8458", 16)), // Faded orange
            Color.FromArgb(Convert.ToInt32("0x1167FFE3", 16)), // Tealish

            Color.FromArgb(Convert.ToInt32("0x3167FFE3", 16)), // Tealish
            Color.FromArgb(Convert.ToInt32("0x27CC8458", 16)), // Faded orange

            Color.FromArgb(Convert.ToInt32("0xDAFF8C4F", 16)), // Less bright orange
            Color.FromArgb(Convert.ToInt32("0xFA53CCFF", 16)), // Dark blue
            Color.FromArgb(Convert.ToInt32("0xB3CC8458", 16)), // Faded orange
            Color.FromArgb(Convert.ToInt32("0x1167FFE3", 16)), // Tealish
            Color.FromArgb(Convert.ToInt32("0x7831F5C1", 16)), // Blue-green

            Color.FromArgb(Convert.ToInt32("0x3167FFE3", 16)), // Tealish
            Color.FromArgb(Convert.ToInt32("0x27CC8458", 16)), // Faded orange

            Color.FromArgb(Convert.ToInt32("0x7331F5C1", 16)), // Blue-green
            Color.FromArgb(Convert.ToInt32("0x5A53CCFF", 16)), // Dark blue
            Color.FromArgb(Convert.ToInt32("0x7067FFE3", 16)), // Tealish
            Color.FromArgb(Convert.ToInt32("0x65CC8458", 16)), // Faded orange
            Color.FromArgb(Convert.ToInt32("0xA253CCFF", 16)), // Dark blue
            Color.FromArgb(Convert.ToInt32("0x5967FFE3", 16)), // Tealish
            Color.FromArgb(Convert.ToInt32("0x4DCC8458", 16)), // Faded orange

            Color.FromArgb(Convert.ToInt32("0x7331F5C1", 16)), // Blue-green
            Color.FromArgb(Convert.ToInt32("0x5A53CCFF", 16)), // Dark blue
            Color.FromArgb(Convert.ToInt32("0x7067FFE3", 16)), // Tealish
            Color.FromArgb(Convert.ToInt32("0x8631F5C1", 16)), // Blue-green
            Color.FromArgb(Convert.ToInt32("0xA253CCFF", 16)), // Dark blue
            Color.FromArgb(Convert.ToInt32("0x5967FFE3", 16)), // Tealish
            Color.FromArgb(Convert.ToInt32("0x4DCC8458", 16)), // Faded orange

            Color.FromArgb(Convert.ToInt32("0x7331F5C1", 16)), // Blue-green
            Color.FromArgb(Convert.ToInt32("0x5A53CCFF", 16)), // Dark blue
            Color.FromArgb(Convert.ToInt32("0x7067FFE3", 16)), // Tealish
            Color.FromArgb(Convert.ToInt32("0xA253CCFF", 16)), // Dark blue
            Color.FromArgb(Convert.ToInt32("0x5967FFE3", 16)), // Tealish
            Color.FromArgb(Convert.ToInt32("0x4DCC8458", 16)), // Faded orange

            Color.FromArgb(Convert.ToInt32("0x5A53CCFF", 16)), // Dark blue
            Color.FromArgb(Convert.ToInt32("0x7067FFE3", 16)), // Tealish
            Color.FromArgb(Convert.ToInt32("0x8631F5C1", 16)), // Blue-green
            Color.FromArgb(Convert.ToInt32("0x65CC8458", 16)), // Faded orange
            Color.FromArgb(Convert.ToInt32("0xA253CCFF", 16)), // Dark blue
            Color.FromArgb(Convert.ToInt32("0x5967FFE3", 16)), // Tealish
            Color.FromArgb(Convert.ToInt32("0x4DCC8458", 16)), // Faded orange

            Color.FromArgb(Convert.ToInt32("0x57FF8C4F", 16)), // Less bright orange
            Color.FromArgb(Convert.ToInt32("0x45CC8458", 16)), // Faded orange
            Color.FromArgb(Convert.ToInt32("0xEAFF8C4F", 16)), // Less bright orange
            Color.FromArgb(Convert.ToInt32("0xC2FF8533", 16)), // Bright orange
            Color.FromArgb(Convert.ToInt32("0x46CC8458", 16)), // Faded orange
            Color.FromArgb(Convert.ToInt32("0xCE31F5C1", 16)), // Blue-green

            Color.FromArgb(Convert.ToInt32("0x57FF8C4F", 16)), // Less bright orange
            Color.FromArgb(Convert.ToInt32("0x67FF8533", 16)), // Bright orange
            Color.FromArgb(Convert.ToInt32("0xCD31F5C1", 16)), // Blue-green
            Color.FromArgb(Convert.ToInt32("0x45CC8458", 16)), // Faded orange
            Color.FromArgb(Convert.ToInt32("0xEAFF8C4F", 16)), // Less bright orange
            Color.FromArgb(Convert.ToInt32("0xCE31F5C1", 16)), // Blue-green

            Color.FromArgb(Convert.ToInt32("0x57FF8C4F", 16)), // Less bright orange
            Color.FromArgb(Convert.ToInt32("0x67FF8533", 16)), // Bright orange
            Color.FromArgb(Convert.ToInt32("0xCD31F5C1", 16)), // Blue-green
            Color.FromArgb(Convert.ToInt32("0x45CC8458", 16)), // Faded orange
            Color.FromArgb(Convert.ToInt32("0xC2FF8533", 16)), // Bright orange
            Color.FromArgb(Convert.ToInt32("0x46CC8458", 16)), // Faded orange
            Color.FromArgb(Convert.ToInt32("0xCE31F5C1", 16)), // Blue-green

            Color.FromArgb(Convert.ToInt32("0xACFF8C4F", 16)), // Less bright orange
            Color.FromArgb(Convert.ToInt32("0xCFFF8533", 16)), // Bright orange
            Color.FromArgb(Convert.ToInt32("0xF1FF8C4F", 16)), // Less bright orange
            Color.FromArgb(Convert.ToInt32("0x28FF8533", 16)), // Bright orange
            Color.FromArgb(Convert.ToInt32("0x74FF8C4F", 16)), // Less bright orange
            Color.FromArgb(Convert.ToInt32("0x4AFF8533", 16)), // Bright orange
            Color.FromArgb(Convert.ToInt32("0x74FF8C4F", 16)), // Less bright orange
            Color.FromArgb(Convert.ToInt32("0x4AFF8533", 16)), // Bright orange
        };

        public static readonly Color[] dragonCyborg2LucidasLong = new Color[] //2
        {
            Color.FromArgb(Convert.ToInt32("0xBFFF8533", 16)), // Bright orange
            Color.FromArgb(Convert.ToInt32("0x9AFF8C4F", 16)), // Less bright orange
            Color.FromArgb(Convert.ToInt32("0x9853CCFF", 16)), // Dark blue
            Color.FromArgb(Convert.ToInt32("0xE5CC8458", 16)), // Faded orange
            Color.FromArgb(Convert.ToInt32("0xBFFF8533", 16)), // Bright orange
            Color.FromArgb(Convert.ToInt32("0x9AFF8C4F", 16)), // Less bright orange
            Color.FromArgb(Convert.ToInt32("0x9853CCFF", 16)), // Dark blue
            Color.FromArgb(Convert.ToInt32("0xE5CC8458", 16)), // Faded orange
            Color.FromArgb(Convert.ToInt32("0xBFFF8533", 16)), // Bright orange
            Color.FromArgb(Convert.ToInt32("0x9AFF8C4F", 16)), // Less bright orange
            Color.FromArgb(Convert.ToInt32("0x9853CCFF", 16)), // Dark blue
            Color.FromArgb(Convert.ToInt32("0xE5CC8458", 16)), // Faded orange
            Color.FromArgb(Convert.ToInt32("0xBFFF8533", 16)), // Bright orange
            Color.FromArgb(Convert.ToInt32("0x9AFF8C4F", 16)), // Less bright orange
            Color.FromArgb(Convert.ToInt32("0x9853CCFF", 16)), // Dark blue
            Color.FromArgb(Convert.ToInt32("0xE5CC8458", 16)), // Faded orange

            Color.FromArgb(Convert.ToInt32("0x4DCC8458", 16)), // Faded orange
            Color.FromArgb(Convert.ToInt32("0x57FF8C4F", 16)), // Less bright orange
            Color.FromArgb(Convert.ToInt32("0x67FF8533", 16)), // Bright orange
            Color.FromArgb(Convert.ToInt32("0xCD31F5C1", 16)), // Blue-green
            Color.FromArgb(Convert.ToInt32("0x45CC8458", 16)), // Faded orange
            Color.FromArgb(Convert.ToInt32("0xEAFF8C4F", 16)), // Less bright orange
            Color.FromArgb(Convert.ToInt32("0xC2FF8533", 16)), // Bright orange
            Color.FromArgb(Convert.ToInt32("0x46CC8458", 16)), // Faded orange
            Color.FromArgb(Convert.ToInt32("0xCE31F5C1", 16)), // Blue-green
            Color.FromArgb(Convert.ToInt32("0xACFF8C4F", 16)), // Less bright orange
            Color.FromArgb(Convert.ToInt32("0xCFFF8533", 16)), // Bright orange
            Color.FromArgb(Convert.ToInt32("0xF1FF8C4F", 16)), // Less bright orange
            Color.FromArgb(Convert.ToInt32("0x28FF8533", 16)), // Bright orange
            Color.FromArgb(Convert.ToInt32("0x74FF8C4F", 16)), // Less bright orange
            Color.FromArgb(Convert.ToInt32("0x4AFF8533", 16)), // Bright orange
        };

        public static readonly Color[] dragonCyborg3 = new Color[] //3
        {
            Color.FromArgb(Convert.ToInt32("0xB0E57FEF", 16)), // Pinkish
            Color.FromArgb(Convert.ToInt32("0xB9D62FDE", 16)), // Pinkish 2
            Color.FromArgb(Convert.ToInt32("0xCFEB0DEC", 16)), // Pinkish 3
            Color.FromArgb(Convert.ToInt32("0xC3E05D46", 16)), // Orangeish
            Color.FromArgb(Convert.ToInt32("0x97A63056", 16)), // Red-pink
            Color.FromArgb(Convert.ToInt32("0x87FF9480", 16)), // Peach
            Color.FromArgb(Convert.ToInt32("0x71EA84D8", 16)), // Light pink
            Color.FromArgb(Convert.ToInt32("0x22FFA1D5", 16)), // Peach-pink
            Color.FromArgb(Convert.ToInt32("0x54BB264C", 16)), // Pink-red

            Color.FromArgb(Convert.ToInt32("0xC5E57FEF", 16)), // Pinkish
            Color.FromArgb(Convert.ToInt32("0x1AD62FDE", 16)), // Pinkish 2
            Color.FromArgb(Convert.ToInt32("0xC5BB264C", 16)), // Pink-red
            Color.FromArgb(Convert.ToInt32("0xAAEB0DEC", 16)), // Pinkish 3
            Color.FromArgb(Convert.ToInt32("0x9BEA84D8", 16)), // Light pink
            Color.FromArgb(Convert.ToInt32("0xD2BB264C", 16)), // Pink-red
            Color.FromArgb(Convert.ToInt32("0xBBFFA1D5", 16)), // Peach-pink
            Color.FromArgb(Convert.ToInt32("0xC9E57FEF", 16)), // Pinkish
            Color.FromArgb(Convert.ToInt32("0x0ED62FDE", 16)), // Pinkish 2
            Color.FromArgb(Convert.ToInt32("0xECBB264C", 16)), // Pink-red
            Color.FromArgb(Convert.ToInt32("0x42EB0DEC", 16)), // Pinkish 3
            Color.FromArgb(Convert.ToInt32("0x6EEA84D8", 16)), // Light pink
            Color.FromArgb(Convert.ToInt32("0xEFBB264C", 16)), // Pink-red
            Color.FromArgb(Convert.ToInt32("0x32FFA1D5", 16)), // Peach-pink

            Color.FromArgb(Convert.ToInt32("0xBBBB264C", 16)), // Pink-red
            Color.FromArgb(Convert.ToInt32("0x83FFA1D5", 16)), // Peach-pink
            Color.FromArgb(Convert.ToInt32("0x6ABB264C", 16)), // Pink-red
            Color.FromArgb(Convert.ToInt32("0x8EE05D46", 16)), // Orangeish
            Color.FromArgb(Convert.ToInt32("0x13A63056", 16)), // Red-pink
            Color.FromArgb(Convert.ToInt32("0xB9FFA1D5", 16)), // Peach-pink
            Color.FromArgb(Convert.ToInt32("0x1FBB264C", 16)), // Pink-red
            Color.FromArgb(Convert.ToInt32("0x12E05D46", 16)), // Orangeish
            Color.FromArgb(Convert.ToInt32("0xF7A63056", 16)), // Red-pink
            Color.FromArgb(Convert.ToInt32("0x87A63056", 16)), // Red-pink
            Color.FromArgb(Convert.ToInt32("0x68FFA1D5", 16)), // Peach-pink

        };

        public static readonly Color[] dragonCyborg3Lucidas = new Color[] //3
        {
            Color.FromArgb(Convert.ToInt32("0xB0E57FEF", 16)), // Pinkish
            Color.FromArgb(Convert.ToInt32("0xB9D62FDE", 16)), // Pinkish 2
            Color.FromArgb(Convert.ToInt32("0xCFEB0DEC", 16)), // Pinkish 3
            Color.FromArgb(Convert.ToInt32("0xC3E05D46", 16)), // Orangeish
            Color.FromArgb(Convert.ToInt32("0x97A63056", 16)), // Red-pink
            Color.FromArgb(Convert.ToInt32("0x87FF9480", 16)), // Peach
            Color.FromArgb(Convert.ToInt32("0x71EA84D8", 16)), // Light pink
            Color.FromArgb(Convert.ToInt32("0x22FFA1D5", 16)), // Peach-pink

            Color.FromArgb(Convert.ToInt32("0xB0E57FEF", 16)), // Pinkish
            Color.FromArgb(Convert.ToInt32("0xB9D62FDE", 16)), // Pinkish 2
            Color.FromArgb(Convert.ToInt32("0xCFEB0DEC", 16)), // Pinkish 3
            Color.FromArgb(Convert.ToInt32("0xC3E05D46", 16)), // Orangeish
            Color.FromArgb(Convert.ToInt32("0x97A63056", 16)), // Red-pink
            Color.FromArgb(Convert.ToInt32("0x87FF9480", 16)), // Peach
            Color.FromArgb(Convert.ToInt32("0x71EA84D8", 16)), // Light pink
            Color.FromArgb(Convert.ToInt32("0x22FFA1D5", 16)), // Peach-pink

            Color.FromArgb(Convert.ToInt32("0xB0E57FEF", 16)), // Pinkish
            Color.FromArgb(Convert.ToInt32("0xB9D62FDE", 16)), // Pinkish 2
            Color.FromArgb(Convert.ToInt32("0xCFEB0DEC", 16)), // Pinkish 3
            Color.FromArgb(Convert.ToInt32("0xC3E05D46", 16)), // Orangeish
            Color.FromArgb(Convert.ToInt32("0x97A63056", 16)), // Red-pink
            Color.FromArgb(Convert.ToInt32("0x87FF9480", 16)), // Peach
            Color.FromArgb(Convert.ToInt32("0x71EA84D8", 16)), // Light pink
            Color.FromArgb(Convert.ToInt32("0x22FFA1D5", 16)), // Peach-pink

            Color.FromArgb(Convert.ToInt32("0xB0E57FEF", 16)), // Pinkish
            Color.FromArgb(Convert.ToInt32("0xB9D62FDE", 16)), // Pinkish 2
            Color.FromArgb(Convert.ToInt32("0xCFEB0DEC", 16)), // Pinkish 3
            Color.FromArgb(Convert.ToInt32("0xC3E05D46", 16)), // Orangeish
            Color.FromArgb(Convert.ToInt32("0x97A63056", 16)), // Red-pink
            Color.FromArgb(Convert.ToInt32("0x87FF9480", 16)), // Peach
            Color.FromArgb(Convert.ToInt32("0x71EA84D8", 16)), // Light pink
            Color.FromArgb(Convert.ToInt32("0x22FFA1D5", 16)), // Peach-pink

            Color.FromArgb(Convert.ToInt32("0xB0E57FEF", 16)), // Pinkish
            Color.FromArgb(Convert.ToInt32("0xB9D62FDE", 16)), // Pinkish 2
            Color.FromArgb(Convert.ToInt32("0xCFEB0DEC", 16)), // Pinkish 3
            Color.FromArgb(Convert.ToInt32("0xC3E05D46", 16)), // Orangeish
            Color.FromArgb(Convert.ToInt32("0x97A63056", 16)), // Red-pink
            Color.FromArgb(Convert.ToInt32("0x87FF9480", 16)), // Peach
            Color.FromArgb(Convert.ToInt32("0x71EA84D8", 16)), // Light pink
            Color.FromArgb(Convert.ToInt32("0x22FFA1D5", 16)), // Peach-pink

            Color.FromArgb(Convert.ToInt32("0xB0E57FEF", 16)), // Pinkish
            Color.FromArgb(Convert.ToInt32("0xB9D62FDE", 16)), // Pinkish 2
            Color.FromArgb(Convert.ToInt32("0xCFEB0DEC", 16)), // Pinkish 3
            Color.FromArgb(Convert.ToInt32("0xC3E05D46", 16)), // Orangeish
            Color.FromArgb(Convert.ToInt32("0x97A63056", 16)), // Red-pink
            Color.FromArgb(Convert.ToInt32("0x87FF9480", 16)), // Peach
            Color.FromArgb(Convert.ToInt32("0x71EA84D8", 16)), // Light pink
            Color.FromArgb(Convert.ToInt32("0x22FFA1D5", 16)), // Peach-pink

            Color.FromArgb(Convert.ToInt32("0xB0E57FEF", 16)), // Pinkish
            Color.FromArgb(Convert.ToInt32("0xB9D62FDE", 16)), // Pinkish 2
            Color.FromArgb(Convert.ToInt32("0xCFEB0DEC", 16)), // Pinkish 3
            Color.FromArgb(Convert.ToInt32("0xC3E05D46", 16)), // Orangeish
            Color.FromArgb(Convert.ToInt32("0x97A63056", 16)), // Red-pink
            Color.FromArgb(Convert.ToInt32("0x87FF9480", 16)), // Peach
            Color.FromArgb(Convert.ToInt32("0x71EA84D8", 16)), // Light pink
            Color.FromArgb(Convert.ToInt32("0x22FFA1D5", 16)), // Peach-pink

            Color.FromArgb(Convert.ToInt32("0xB0E57FEF", 16)), // Pinkish
            Color.FromArgb(Convert.ToInt32("0xB9D62FDE", 16)), // Pinkish 2
            Color.FromArgb(Convert.ToInt32("0xCFEB0DEC", 16)), // Pinkish 3
            Color.FromArgb(Convert.ToInt32("0xC3E05D46", 16)), // Orangeish
            Color.FromArgb(Convert.ToInt32("0x97A63056", 16)), // Red-pink
            Color.FromArgb(Convert.ToInt32("0x87FF9480", 16)), // Peach
            Color.FromArgb(Convert.ToInt32("0x71EA84D8", 16)), // Light pink
            Color.FromArgb(Convert.ToInt32("0x22FFA1D5", 16)), // Peach-pink

            Color.FromArgb(Convert.ToInt32("0x54BB264C", 16)), // Pink-red
            Color.FromArgb(Convert.ToInt32("0xC5E57FEF", 16)), // Pinkish
            Color.FromArgb(Convert.ToInt32("0x1AD62FDE", 16)), // Pinkish 2
            Color.FromArgb(Convert.ToInt32("0xC5BB264C", 16)), // Pink-red
            Color.FromArgb(Convert.ToInt32("0xAAEB0DEC", 16)), // Pinkish 3
            Color.FromArgb(Convert.ToInt32("0x9BEA84D8", 16)), // Light pink
            Color.FromArgb(Convert.ToInt32("0xD2BB264C", 16)), // Pink-red
            Color.FromArgb(Convert.ToInt32("0xBBFFA1D5", 16)), // Peach-pink

            Color.FromArgb(Convert.ToInt32("0x54BB264C", 16)), // Pink-red
            Color.FromArgb(Convert.ToInt32("0xC5E57FEF", 16)), // Pinkish
            Color.FromArgb(Convert.ToInt32("0x1AD62FDE", 16)), // Pinkish 2
            Color.FromArgb(Convert.ToInt32("0xC5BB264C", 16)), // Pink-red
            Color.FromArgb(Convert.ToInt32("0xAAEB0DEC", 16)), // Pinkish 3
            Color.FromArgb(Convert.ToInt32("0x9BEA84D8", 16)), // Light pink
            Color.FromArgb(Convert.ToInt32("0xD2BB264C", 16)), // Pink-red
            Color.FromArgb(Convert.ToInt32("0xBBFFA1D5", 16)), // Peach-pink

            Color.FromArgb(Convert.ToInt32("0x54BB264C", 16)), // Pink-red
            Color.FromArgb(Convert.ToInt32("0xC5E57FEF", 16)), // Pinkish
            Color.FromArgb(Convert.ToInt32("0x1AD62FDE", 16)), // Pinkish 2
            Color.FromArgb(Convert.ToInt32("0xC5BB264C", 16)), // Pink-red
            Color.FromArgb(Convert.ToInt32("0xAAEB0DEC", 16)), // Pinkish 3
            Color.FromArgb(Convert.ToInt32("0x9BEA84D8", 16)), // Light pink
            Color.FromArgb(Convert.ToInt32("0xD2BB264C", 16)), // Pink-red
            Color.FromArgb(Convert.ToInt32("0xBBFFA1D5", 16)), // Peach-pink

            Color.FromArgb(Convert.ToInt32("0x54BB264C", 16)), // Pink-red
            Color.FromArgb(Convert.ToInt32("0xC5E57FEF", 16)), // Pinkish
            Color.FromArgb(Convert.ToInt32("0x1AD62FDE", 16)), // Pinkish 2
            Color.FromArgb(Convert.ToInt32("0xC5BB264C", 16)), // Pink-red
            Color.FromArgb(Convert.ToInt32("0xAAEB0DEC", 16)), // Pinkish 3
            Color.FromArgb(Convert.ToInt32("0x9BEA84D8", 16)), // Light pink
            Color.FromArgb(Convert.ToInt32("0xD2BB264C", 16)), // Pink-red
            Color.FromArgb(Convert.ToInt32("0xBBFFA1D5", 16)), // Peach-pink

            Color.FromArgb(Convert.ToInt32("0xBBBB264C", 16)), // Pink-red
            Color.FromArgb(Convert.ToInt32("0x83FFA1D5", 16)), // Peach-pink
            Color.FromArgb(Convert.ToInt32("0x6ABB264C", 16)), // Pink-red
            Color.FromArgb(Convert.ToInt32("0x8EE05D46", 16)), // Orangeish
            Color.FromArgb(Convert.ToInt32("0x13A63056", 16)), // Red-pink
            Color.FromArgb(Convert.ToInt32("0xB9FFA1D5", 16)), // Peach-pink
            Color.FromArgb(Convert.ToInt32("0x1FBB264C", 16)), // Pink-red
            Color.FromArgb(Convert.ToInt32("0x12E05D46", 16)), // Orangeish

            Color.FromArgb(Convert.ToInt32("0xBBBB264C", 16)), // Pink-red
            Color.FromArgb(Convert.ToInt32("0x83FFA1D5", 16)), // Peach-pink
            Color.FromArgb(Convert.ToInt32("0x6ABB264C", 16)), // Pink-red
            Color.FromArgb(Convert.ToInt32("0x8EE05D46", 16)), // Orangeish
            Color.FromArgb(Convert.ToInt32("0x13A63056", 16)), // Red-pink
            Color.FromArgb(Convert.ToInt32("0xB9FFA1D5", 16)), // Peach-pink
            Color.FromArgb(Convert.ToInt32("0x1FBB264C", 16)), // Pink-red
            Color.FromArgb(Convert.ToInt32("0x12E05D46", 16)), // Orangeish

            Color.FromArgb(Convert.ToInt32("0xBBBB264C", 16)), // Pink-red
            Color.FromArgb(Convert.ToInt32("0x83FFA1D5", 16)), // Peach-pink
            Color.FromArgb(Convert.ToInt32("0x6ABB264C", 16)), // Pink-red
            Color.FromArgb(Convert.ToInt32("0x8EE05D46", 16)), // Orangeish
            Color.FromArgb(Convert.ToInt32("0x13A63056", 16)), // Red-pink
            Color.FromArgb(Convert.ToInt32("0xB9FFA1D5", 16)), // Peach-pink
            Color.FromArgb(Convert.ToInt32("0x1FBB264C", 16)), // Pink-red
            Color.FromArgb(Convert.ToInt32("0x12E05D46", 16)), // Orangeish

            Color.FromArgb(Convert.ToInt32("0xBBBB264C", 16)), // Pink-red
            Color.FromArgb(Convert.ToInt32("0x83FFA1D5", 16)), // Peach-pink
            Color.FromArgb(Convert.ToInt32("0x6ABB264C", 16)), // Pink-red
            Color.FromArgb(Convert.ToInt32("0x8EE05D46", 16)), // Orangeish
            Color.FromArgb(Convert.ToInt32("0x13A63056", 16)), // Red-pink
            Color.FromArgb(Convert.ToInt32("0xB9FFA1D5", 16)), // Peach-pink
            Color.FromArgb(Convert.ToInt32("0x1FBB264C", 16)), // Pink-red
            Color.FromArgb(Convert.ToInt32("0x12E05D46", 16)), // Orangeish
        };

        public static readonly Color[] nochesCincoDeMayo1 = new Color[] //3
        {
            Color.FromArgb(Convert.ToInt32("0xffc95b", 16)), // Yellowish
            Color.FromArgb(Convert.ToInt32("0xd71919", 16)), // Reddish
            Color.FromArgb(Convert.ToInt32("0xf98f21", 16)), // Orangeish
            Color.FromArgb(Convert.ToInt32("0x25bcc0", 16)), // Blueish
            Color.FromArgb(Convert.ToInt32("0x234c91", 16)), // Blue
            Color.FromArgb(Convert.ToInt32("0x87FF9480", 16)), // Peach
            Color.FromArgb(Convert.ToInt32("0xffc95b", 16)), // Yellowish
            Color.FromArgb(Convert.ToInt32("0xd71919", 16)), // Reddish
        };

        public static readonly Color[] nochesCincoDeMayo1Asymmetric = new Color[] //3
        {
            Color.FromArgb(Convert.ToInt32("0xffc95b", 16)), // Yellowish
            Color.FromArgb(Convert.ToInt32("0xd71919", 16)), // Reddish
            Color.FromArgb(Convert.ToInt32("0x25bcc0", 16)), // Blueish
            Color.FromArgb(Convert.ToInt32("0x234c91", 16)), // Blue
            Color.FromArgb(Convert.ToInt32("0x87FF9480", 16)), // Peach
            Color.FromArgb(Convert.ToInt32("0xffc95b", 16)), // Yellowish
            Color.FromArgb(Convert.ToInt32("0xd71919", 16)), // Reddish

            Color.FromArgb(Convert.ToInt32("0xffc95b", 16)), // Yellowish
            Color.FromArgb(Convert.ToInt32("0xd71919", 16)), // Reddish
            Color.FromArgb(Convert.ToInt32("0x25bcc0", 16)), // Blueish
            Color.FromArgb(Convert.ToInt32("0x234c91", 16)), // Blue
            Color.FromArgb(Convert.ToInt32("0x87FF9480", 16)), // Peach
            Color.FromArgb(Convert.ToInt32("0xffc95b", 16)), // Yellowish
            Color.FromArgb(Convert.ToInt32("0xd71919", 16)), // Reddish

            Color.FromArgb(Convert.ToInt32("0xffc95b", 16)), // Yellowish
            Color.FromArgb(Convert.ToInt32("0xd71919", 16)), // Reddish
            Color.FromArgb(Convert.ToInt32("0x25bcc0", 16)), // Blueish
            Color.FromArgb(Convert.ToInt32("0x87FF9480", 16)), // Peach
            Color.FromArgb(Convert.ToInt32("0xffc95b", 16)), // Yellowish
            Color.FromArgb(Convert.ToInt32("0xd71919", 16)), // Reddish

            Color.FromArgb(Convert.ToInt32("0xffc95b", 16)), // Yellowish
            Color.FromArgb(Convert.ToInt32("0xd71919", 16)), // Reddish
            Color.FromArgb(Convert.ToInt32("0xf98f21", 16)), // Orangeish
            Color.FromArgb(Convert.ToInt32("0x234c91", 16)), // Blue
            Color.FromArgb(Convert.ToInt32("0x87FF9480", 16)), // Peach
            Color.FromArgb(Convert.ToInt32("0xffc95b", 16)), // Yellowish
            Color.FromArgb(Convert.ToInt32("0xd71919", 16)), // Reddish
        };

        public static readonly Color[] nochesCincoDeMayo1Long = new Color[] //3
        {
            Color.FromArgb(Convert.ToInt32("0xffc95b", 16)), // Yellowish
            Color.FromArgb(Convert.ToInt32("0xf98f21", 16)), // Orangeish
            Color.FromArgb(Convert.ToInt32("0xffc95b", 16)), // Yellowish
            Color.FromArgb(Convert.ToInt32("0x21F5E13B", 16)), // Orange-yellowish
            Color.FromArgb(Convert.ToInt32("0x8FED6259", 16)), // Redish
            Color.FromArgb(Convert.ToInt32("0x7AE83D46", 16)), // Redish 2
            Color.FromArgb(Convert.ToInt32("0x9DFFA44F", 16)), // Redish-orange
            Color.FromArgb(Convert.ToInt32("0x9BEA84D8", 16)), // Light pink
            Color.FromArgb(Convert.ToInt32("0x83AC77BD", 16)), // Washed purple
            Color.FromArgb(Convert.ToInt32("0x6AEF2416", 16)), // Lenseflare redish
            Color.FromArgb(Convert.ToInt32("0xd71919", 16)), // Reddish
            Color.FromArgb(Convert.ToInt32("0x87FF9480", 16)), // Peach
            Color.FromArgb(Convert.ToInt32("0xffc95b", 16)), // Yellowish
            Color.FromArgb(Convert.ToInt32("0xd71919", 16)), // Reddish
            Color.FromArgb(Convert.ToInt32("0xFFF8B532", 16)), // Golden orange
        };

        public static readonly Color[] dragonCyborg5 = new Color[] //5
        {
            Color.FromArgb(Convert.ToInt32("0xAEFF50E9", 16)), // Bright pinkish
            Color.FromArgb(Convert.ToInt32("0x22B634D4", 16)), // Dark purpleish
            Color.FromArgb(Convert.ToInt32("0x9E6300AC", 16)), // Dark purpleish 2
            Color.FromArgb(Convert.ToInt32("0x6ED10163", 16)), // Deep pinkish
            Color.FromArgb(Convert.ToInt32("0xAFFF295A", 16)), // Pink orangeish
            Color.FromArgb(Convert.ToInt32("0x96FF4A6B", 16)), // Lighter redish
            Color.FromArgb(Convert.ToInt32("0xDEE90041", 16)), // Deeper redish
            Color.FromArgb(Convert.ToInt32("0x0332179C", 16)), // Blueish purple
            Color.FromArgb(Convert.ToInt32("0x09532A88", 16)), // Dark purpleish
            Color.FromArgb(Convert.ToInt32("0x127D0ED9", 16)), // Ethereal purple
        };

        public static readonly Color[] dragonCyborg6 = new Color[] //6
        {
            Color.FromArgb(Convert.ToInt32("0xFFFD8342", 16)), // Orangeish
            Color.FromArgb(Convert.ToInt32("0xFFEF2416", 16)), // Lenseflare redish
            Color.FromArgb(Convert.ToInt32("0xFF26C5E6", 16)), // Forest green-blue
            Color.FromArgb(Convert.ToInt32("0xFFF8B532", 16)), // Golden orange
            Color.FromArgb(Convert.ToInt32("0xFF3E9F5A", 16)), // Greenish
            Color.FromArgb(Convert.ToInt32("0xFF0F90D2", 16)), // Blueish
            Color.FromArgb(Convert.ToInt32("0xFFD840A5", 16)), // Deep pinkish
            Color.FromArgb(Convert.ToInt32("0xFFD44E89", 16)), // Different pinkish
            Color.FromArgb(Convert.ToInt32("0xFFF57457", 16)), // Peach orangeish
            Color.FromArgb(Convert.ToInt32("0xFFAC77BD", 16)), // Washed purple

            Color.FromArgb(Convert.ToInt32("0x7EEF2416", 16)), // Lenseflare redish
            Color.FromArgb(Convert.ToInt32("0x39F8B532", 16)), // Golden orange
            Color.FromArgb(Convert.ToInt32("0x3ED840A5", 16)), // Deep pinkish
            Color.FromArgb(Convert.ToInt32("0xC1AC77BD", 16)), // Washed purple
            Color.FromArgb(Convert.ToInt32("0x7FF57457", 16)), // Peach orangeish
            Color.FromArgb(Convert.ToInt32("0x0CEF2416", 16)), // Lenseflare redish
            Color.FromArgb(Convert.ToInt32("0x3DF8B532", 16)), // Golden orange
            Color.FromArgb(Convert.ToInt32("0x6DAC77BD", 16)), // Washed purple
            Color.FromArgb(Convert.ToInt32("0xC0D840A5", 16)), // Deep pinkish
            Color.FromArgb(Convert.ToInt32("0xB9F57457", 16)), // Peach orangeish

            Color.FromArgb(Convert.ToInt32("0xC2D840A5", 16)), // Deep pinkish
            Color.FromArgb(Convert.ToInt32("0xC5AC77BD", 16)), // Washed purple
            Color.FromArgb(Convert.ToInt32("0x81EF2416", 16)), // Lenseflare redish
            Color.FromArgb(Convert.ToInt32("0x0426C5E6", 16)), // Forest green-blue
            Color.FromArgb(Convert.ToInt32("0xA2D840A5", 16)), // Deep pinkish
            Color.FromArgb(Convert.ToInt32("0x5AAC77BD", 16)), // Washed purple
            Color.FromArgb(Convert.ToInt32("0xC5EF2416", 16)), // Lenseflare redish
            Color.FromArgb(Convert.ToInt32("0xACD840A5", 16)), // Deep pinkish
            Color.FromArgb(Convert.ToInt32("0x6226C5E6", 16)), // Forest green-blue
            Color.FromArgb(Convert.ToInt32("0x83AC77BD", 16)), // Washed purple
            Color.FromArgb(Convert.ToInt32("0x6AEF2416", 16)), // Lenseflare redish
        };

        public static readonly Color[] dragonCyborg8 = new Color[] //9
        {
            Color.FromArgb(Convert.ToInt32("0x46ED6259", 16)), // Redish
            Color.FromArgb(Convert.ToInt32("0x2CE83D46", 16)), // Redish 2
            Color.FromArgb(Convert.ToInt32("0x7AFFA44F", 16)), // Redish-orange
            Color.FromArgb(Convert.ToInt32("0x21F5E13B", 16)), // Orange-yellowish

            Color.FromArgb(Convert.ToInt32("0x6C4CEDE0", 16)), // turquoiseish
            Color.FromArgb(Convert.ToInt32("0x3B3887B8", 16)), // Dark turquoiseish
            Color.FromArgb(Convert.ToInt32("0x1D3787D6", 16)), // Dark blueish
            Color.FromArgb(Convert.ToInt32("0x2D9E2CDB", 16)), // Purpleish
            Color.FromArgb(Convert.ToInt32("0xA4BC2BD4", 16)), // Deep pinkish
            Color.FromArgb(Convert.ToInt32("0x1CD427B8", 16)), // Pink

            Color.FromArgb(Convert.ToInt32("0xBFD41C64", 16)), // Pink-red
            Color.FromArgb(Convert.ToInt32("0x8FED6259", 16)), // Redish
            Color.FromArgb(Convert.ToInt32("0x7AE83D46", 16)), // Redish 2
            Color.FromArgb(Convert.ToInt32("0xE83887B8", 16)), // Dark turquoiseish
            Color.FromArgb(Convert.ToInt32("0x9DFFA44F", 16)), // Redish-orange
            Color.FromArgb(Convert.ToInt32("0xD6F5E13B", 16)), // Orange-yellowish
            Color.FromArgb(Convert.ToInt32("0x69D41C64", 16)), // Pink-red
            Color.FromArgb(Convert.ToInt32("0x79ED6259", 16)), // Redish
            Color.FromArgb(Convert.ToInt32("0x503787D6", 16)), // Dark blueish
            Color.FromArgb(Convert.ToInt32("0xEB9E2CDB", 16)), // Purpleish
            Color.FromArgb(Convert.ToInt32("0x124CEDE0", 16)), // turquoiseish
            Color.FromArgb(Convert.ToInt32("0x48E83D46", 16)), // Redish 2
            Color.FromArgb(Convert.ToInt32("0xC9FFA44F", 16)), // Redish-orange
            Color.FromArgb(Convert.ToInt32("0xDFF5E13B", 16)), // Orange-yellowish
        };

        public static readonly Color[] dragonCyborg9 = new Color[] //9
        {
            Color.FromArgb(Convert.ToInt32("0x1FE00206", 16)), // Redish
            Color.FromArgb(Convert.ToInt32("0x31F97200", 16)), // Red-orange
            Color.FromArgb(Convert.ToInt32("0xA8FCC209", 16)), // Orangeish
            Color.FromArgb(Convert.ToInt32("0x26F2DD41", 16)), // Yellowish

            Color.FromArgb(Convert.ToInt32("0xD458D969", 16)), // Green
            Color.FromArgb(Convert.ToInt32("0x4B03D901", 16)), // Brighter green
            Color.FromArgb(Convert.ToInt32("0x9E07C4D9", 16)), // Background blue
            Color.FromArgb(Convert.ToInt32("0x950060BD", 16)), // Darker background blue
            Color.FromArgb(Convert.ToInt32("0x8B9178CF", 16)), // Faded purple
            Color.FromArgb(Convert.ToInt32("0x72A951D6", 16)), // Richer purple
            Color.FromArgb(Convert.ToInt32("0x26EC87FF", 16)), // Purple-pink

            Color.FromArgb(Convert.ToInt32("0x26D41C64", 16)), // Pink-red
            Color.FromArgb(Convert.ToInt32("0xECE41BC3", 16)), // Red-pink

            Color.FromArgb(Convert.ToInt32("0x08E00206", 16)), // Redish
            Color.FromArgb(Convert.ToInt32("0x98F97200", 16)), // Red-orange
            Color.FromArgb(Convert.ToInt32("0x16FCC209", 16)), // Orangeish
            Color.FromArgb(Convert.ToInt32("0x20E00206", 16)), // Redish
            Color.FromArgb(Convert.ToInt32("0x42F97200", 16)), // Red-orange
            Color.FromArgb(Convert.ToInt32("0x2DFCC209", 16)), // Orangeish
            Color.FromArgb(Convert.ToInt32("0x7AD41C64", 16)), // Pink-red
            Color.FromArgb(Convert.ToInt32("0xA1E41BC3", 16)), // Red-pink
            Color.FromArgb(Convert.ToInt32("0x9CFCC209", 16)), // Orangeish
            Color.FromArgb(Convert.ToInt32("0x49F2DD41", 16)), // Yellowish

            Color.FromArgb(Convert.ToInt32("0x090060BD", 16)), // Darker background blue
            Color.FromArgb(Convert.ToInt32("0xCB9178CF", 16)), // Faded purple
            Color.FromArgb(Convert.ToInt32("0x03A951D6", 16)), // Richer purple
            Color.FromArgb(Convert.ToInt32("0xF7F97200", 16)), // Red-orange
            Color.FromArgb(Convert.ToInt32("0x30EC87FF", 16)), // Purple-pink
            Color.FromArgb(Convert.ToInt32("0xAE0060BD", 16)), // Darker background blue
            Color.FromArgb(Convert.ToInt32("0x519178CF", 16)), // Faded purple
            Color.FromArgb(Convert.ToInt32("0x93D41C64", 16)), // Pink-red
            Color.FromArgb(Convert.ToInt32("0x72A951D6", 16)), // Richer purple
            Color.FromArgb(Convert.ToInt32("0xEDEC87FF", 16)), // Purple-pink
        };

        public static readonly Color[] frequentDragonCyborg9 = new Color[] //9
        {
            Color.FromArgb(Convert.ToInt32("0x1FE00206", 16)), // Redish
            Color.FromArgb(0xFF, Color.Orange), // Orange
            Color.FromArgb(Convert.ToInt32("0xA8FCC209", 16)), // Orangeish

            Color.FromArgb(Convert.ToInt32("0x4B03D901", 16)), // Brighter green
            Color.FromArgb(Convert.ToInt32("0x9E07C4D9", 16)), // Background blue
            Color.FromArgb(Convert.ToInt32("0x8B9178CF", 16)), // Faded purple
            Color.FromArgb(Convert.ToInt32("0x72E823D6", 16)), // Richer purple
            Color.FromArgb(Convert.ToInt32("0x26FF23FF", 16)), // Purple-pink
        };

        public static readonly Color[] plc = new Color[]
        {
            Color.FromArgb(Convert.ToInt32("0xA8FCC209", 16)), // Orangeish
            Color.FromArgb(Convert.ToInt32("0x72E823D6", 16)), // Richer purple
            Color.FromArgb(Convert.ToInt32("0x26FF23FF", 16)), // Purple-pink
            Color.FromArgb(Convert.ToInt32("0xD458D969", 16)), // Green
            Color.FromArgb(0XFD, Color.Orange),
            Color.FromArgb(0xD8, Color.Purple),
            Color.FromArgb(0xA1, 0x00, 0x00, 0xFF),
        };

        public static readonly Color[] dragChurch = new Color[]
        {
            // Color.FromArgb(Convert.ToInt32("0x00AEEEEE", 16)), // Baby Pink
            // Color.FromArgb(Convert.ToInt32("0x00F4C2C2", 16)), // Baby Pink
            // Color.FromArgb(Convert.ToInt32("0x00FFF5BA", 16)), // Lavender
            // Color.FromArgb(Convert.ToInt32("0x00C1E1C1", 16)), // Mint Green
            // Color.FromArgb(Convert.ToInt32("0x00CDE7F0", 16)), // Baby Blue
            // Color.FromArgb(Convert.ToInt32("0x00FFF5BA", 16)), // Pale Yellow

            // Color.FromArgb(Convert.ToInt32("0x00FF9EC7", 16)), // Baby Pink
            // Color.FromArgb(Convert.ToInt32("0x00FFE066", 16)), // Baby Pink
            // Color.FromArgb(Convert.ToInt32("0x00C79BFF", 16)), // Lavender
            // Color.FromArgb(Convert.ToInt32("0x007EE4B8", 16)), // Mint Green
            // Color.FromArgb(Convert.ToInt32("0x00CDE7F0", 16)), // Baby Blue
            // Color.FromArgb(Convert.ToInt32("0x006FE7E7", 16)), // Pale Yellow

            Color.FromArgb(Convert.ToInt32("0x00FF7FB8", 16)), // Pink (more vivid, less white)
            Color.FromArgb(Convert.ToInt32("0x00FFD633", 16)), // Yellow (richer but still safe)
            Color.FromArgb(Convert.ToInt32("0x00B574FF", 16)), // Lavender (deeper purple)
            Color.FromArgb(Convert.ToInt32("0x005FE0A6", 16)), // Mint (more color, less milky)
            Color.FromArgb(Convert.ToInt32("0x009EDAF0", 16)), // Baby Blue (cleaner cyan-blue)
            Color.FromArgb(Convert.ToInt32("0x004FDCDC", 16)), // Robin Egg (more saturated cyan)

            Color.FromArgb(Convert.ToInt32("0x00FF66A8", 16)), // Pink (punchy but still soft)
            Color.FromArgb(Convert.ToInt32("0x00FFCC1A", 16)), // Yellow (approaching gold, still clean)
            Color.FromArgb(Convert.ToInt32("0x009F5BFF", 16)), // Lavender (vivid lilac)
            Color.FromArgb(Convert.ToInt32("0x004DD89C", 16)), // Mint (stronger green tint)
            Color.FromArgb(Convert.ToInt32("0x007FD0F0", 16)), // Baby Blue (more defined blue)
            Color.FromArgb(Convert.ToInt32("0x003AD6D6", 16)), // Robin Egg (crisper cyan)
        };
    }
}
