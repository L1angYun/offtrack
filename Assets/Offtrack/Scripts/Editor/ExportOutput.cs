using System;
using System.IO;

namespace Offtrack.Editor
{
    public static class ExportOutput
    {
        public static void Clean(string output, BuildRootResolution resolution)
        {
            Run("clean output", output, resolution, () =>
            {
                if (!Directory.Exists(output)) return;
                try { Directory.Delete(output, true); }
                catch (UnauthorizedAccessException) when (Directory.Exists(output))
                {
                    // Mono can remove nested junctions but retain a mount-point cleanup error.
                    // Finish the remaining tree once; persistent IO/permission errors still propagate.
                    Directory.Delete(output, true);
                }
            });
        }

        public static void CreateDirectory(string path, BuildRootResolution resolution)
        {
            Run("create directory", path, resolution, () => Directory.CreateDirectory(path));
        }

        public static void DeleteFile(string path, BuildRootResolution resolution)
        {
            Run("delete file", path, resolution, () =>
            {
                if (File.Exists(path)) File.Delete(path);
            });
        }

        public static void CleanupPartial(string path, BuildRootResolution resolution, bool exportFailed)
        {
            try { DeleteFile(path, resolution); }
            // Cleanup must not replace the exception already leaving the export's try block.
            catch (Exception) when (exportFailed) { }
        }

        public static FileStream CreatePartialArchive(string path, BuildRootResolution resolution)
        {
            FileStream stream = null;
            Run("create partial archive", path, resolution, () =>
            {
                // Unlink an existing hard link before opening a new file, so its other names remain unchanged.
                if (File.Exists(path)) File.Delete(path);
                stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            });
            return stream;
        }

        public static void Move(string source, string destination, BuildRootResolution resolution)
        {
            Run("move archive", source + " -> " + destination, resolution, () => File.Move(source, destination));
        }

        private static void Run(string operation, string path, BuildRootResolution resolution, Action action)
        {
            try { action(); }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                throw resolution.Failure("Cannot " + operation + " at " + path + ": " + exception.Message);
            }
        }
    }
}
