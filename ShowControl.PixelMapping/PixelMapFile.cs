using System;
using System.IO;

namespace ShowControl.PixelMapping
{
    /// <summary>File I/O for pixel maps, written so a crash mid-save cannot destroy the previous file.</summary>
    public static class PixelMapFile
    {
        /// <summary>
        /// Writes through a temporary file and moves it into place. The move is atomic on a single
        /// volume for both APFS and NTFS; <c>File.Replace</c> is unreliable under the Mac sandbox.
        /// </summary>
        public static void WriteAtomic(string path, string contents)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is required.", nameof(path));

            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            string temporary = path + ".tmp";
            File.WriteAllText(temporary, contents);
            File.Move(temporary, path, overwrite: true);
        }

        public static bool TryRead(string? path, out string contents)
        {
            contents = string.Empty;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return false;
            }

            contents = File.ReadAllText(path);
            return true;
        }

        public static void Delete(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
