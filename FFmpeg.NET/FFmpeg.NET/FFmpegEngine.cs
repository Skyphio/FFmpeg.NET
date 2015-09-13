using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading;

namespace FFmpeg.NET
{
    /// <summary>
    /// 
    /// </summary>
    public class FFmpegEngine : IDisposable
    {
        /// <summary>
        /// The directory containing the FFmpeg application.
        /// </summary>
        public string FFmpegDirectoryPath
        {
            get { return Path.GetDirectoryName(FFmpegFilePath); }
        }

        /// <summary>
        /// The path to the FFmpeg application.
        /// </summary>
        public string FFmpegFilePath { get; private set; }

        /// <summary>
        /// The <see cref="Mutex"/> class that handles synchronizing requests for the FFmpeg application.
        /// </summary>
        public Mutex FFmpegMutex { get; private set; }

        /// <summary>
        /// The <see cref="Process"/> class that manages running the FFmpeg application.
        /// </summary>
        public Process FFmpegProcess { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="FFmpegEngine"/> class and decompressess FFmpeg into a temporary folder for use.
        /// </summary>
        public FFmpegEngine()
        {
            FFmpegFilePath = Path.Combine(Path.GetTempPath(), "FFmpeg.NET\\ffmpeg.exe");
            FFmpegMutex = new Mutex(false, "FFmpeg.NET");

            if (!Directory.Exists(FFmpegDirectoryPath))
                Directory.CreateDirectory(FFmpegDirectoryPath);

            if (!File.Exists(FFmpegFilePath))
                DecompressExecutable();

            if (Document.IsLocked(FFmpegFilePath))
                try
                {
                    FFmpegMutex.WaitOne();
                    KillProcesses();
                }
                finally
                {
                    FFmpegMutex.ReleaseMutex();
                }
        }

        /// <summary>
        /// Converts a video by running FFmpeg with a serialized command.
        /// </summary>
        /// <param name="inputPath">The path to the file to convert.</param>
        /// <param name="outputPath">The path to the file to be created. Note: the output extension determines the output format.</param>
        /// <param name="parameters">Additional parameters for greater control over the conversion.</param>
        public void Convert(string inputPath, string outputPath, string parameters = "")
        {
            if (string.IsNullOrWhiteSpace(inputPath))
                throw new ArgumentNullException("inputPath");

            if (!File.Exists(inputPath))
                throw new FileNotFoundException("The input file could not be found!");

            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentNullException("outputPath");

            try
            {
                FFmpegMutex.WaitOne();
                var processStartInfo = new ProcessStartInfo
                {
                    Arguments = string.Format("-i \"{0}\" {1} \"{2}\"", inputPath, parameters, outputPath),
                    CreateNoWindow = true,
                    FileName = FFmpegFilePath,
                    RedirectStandardError = true,
                    RedirectStandardInput = false,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (FFmpegProcess = Process.Start(processStartInfo))
                    FFmpegProcess.WaitForExit();
            }
            finally
            {
                FFmpegMutex.ReleaseMutex();
            }
        }

        /// <summary>
        /// Releases all resources used by the current instance of the <see cref="FFmpegEngine"/> class.
        /// </summary>
        public void Dispose()
        {
            FFmpegMutex.Dispose();
            FFmpegProcess.Dispose();
            Directory.Delete(FFmpegDirectoryPath, true);
        }

        private void DecompressExecutable()
        {
            using (var resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FFmpeg.NET.Resources.ffmpeg.exe.gz"))
            {
                using (var compressedFileStream = new FileStream(FFmpegFilePath + ".gz", FileMode.Create))
                {
                    resourceStream.CopyTo(compressedFileStream);
                }
            }

            var compressedFileInfo = new FileInfo(FFmpegFilePath + ".gz");

            using (FileStream originalFileStream = compressedFileInfo.OpenRead())
            {
                string currentFileName = compressedFileInfo.FullName;
                string newFileName = currentFileName.Remove(currentFileName.Length - compressedFileInfo.Extension.Length);

                using (FileStream decompressedFileStream = File.Create(newFileName))
                {
                    using (GZipStream decompressionStream = new GZipStream(originalFileStream, CompressionMode.Decompress))
                    {
                        decompressionStream.CopyTo(decompressedFileStream);
                    }
                }
            }
        }

        private void KillProcesses()
        {
            var processes = Process.GetProcessesByName("ffmepg.exe");
            if (processes.Length <= 0)
                return;

            foreach (var process in processes)
                process.Kill();
        }
    }

    internal static class Document
    {
        internal static bool IsLocked(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentNullException("filePath");

            var fileInfo = new FileInfo(filePath);
            var fileStream = Stream.Null;

            try
            {
                fileStream = fileInfo.Open(FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            catch (IOException)
            {
                return true;
            }
            finally
            {
                if (fileStream != Stream.Null)
                    fileStream.Dispose();
            }
            return false;
        }
    }
}
