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

        /// <summary>
        /// Writes the file in place — open, truncate, write — with no sibling temporary file. For
        /// destinations outside the sandbox container: the Mac Catalyst sandbox grants access to
        /// the file the user picked, never to its directory, so <see cref="WriteAtomic"/>'s
        /// <c>.tmp</c>-and-move is denied there. The safety trade is deliberate — the working copy
        /// inside the container is the authoritative recovery source and keeps its atomic write,
        /// so a save torn here loses an export, not the map.
        /// </summary>
        public static void WriteInPlace(string path, string contents)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is required.", nameof(path));

            File.WriteAllText(path, contents);
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
