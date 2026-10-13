using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace GDMENUCardManager.Core
{
    public static class Redump2CdiConverter
    {
        private const string ToolName = "redump2cdi";
        private const string WindowsToolName = "redump2cdi.exe";
        private const string SuccessMarker = "Enjoy!";

        /// <summary>
        /// False for GD-ROM cues and for anything that is not a cue at all.
        /// </summary>
        public static bool IsRedumpCdRomCue(string cuePath)
        {
            if (string.IsNullOrEmpty(cuePath) || !File.Exists(cuePath))
                return false;

            if (!cuePath.EndsWith(".cue", StringComparison.OrdinalIgnoreCase))
                return false;

            try
            {
                var content = File.ReadAllText(cuePath);
                // GD-ROM cues carry a HIGH-DENSITY AREA comment.
                if (content.Contains("HIGH-DENSITY AREA", StringComparison.OrdinalIgnoreCase))
                    return false;

                // Must have at least one FILE and TRACK command to be valid
                return content.Contains("FILE ", StringComparison.OrdinalIgnoreCase) &&
                       content.Contains("TRACK ", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static bool IsRedumpGdRomCue(string cuePath)
        {
            if (string.IsNullOrEmpty(cuePath) || !File.Exists(cuePath))
                return false;

            if (!cuePath.EndsWith(".cue", StringComparison.OrdinalIgnoreCase))
                return false;

            try
            {
                var content = File.ReadAllText(cuePath);
                return content.Contains("HIGH-DENSITY AREA", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Returns the path whether or not the file exists. See IsToolAvailable.
        /// </summary>
        public static string GetToolPath()
        {
            var toolsDir = Path.Combine(AppContext.BaseDirectory, "tools");

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return Path.Combine(toolsDir, WindowsToolName);
            }
            else
            {
                return Path.Combine(toolsDir, ToolName);
            }
        }

        public static bool IsToolAvailable()
        {
            var toolPath = GetToolPath();
            return File.Exists(toolPath);
        }

        /// <summary>
        /// Runs the tool synchronously and gives up after five minutes.
        /// </summary>
        public static (bool success, string message) ConvertToCdi(string cuePath, string cdiOutputPath)
        {
            var toolPath = GetToolPath();

            if (!File.Exists(toolPath))
            {
                return (false, $"redump2cdi tool not found at: {toolPath}");
            }

            if (!File.Exists(cuePath))
            {
                return (false, $"Input CUE file not found: {cuePath}");
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = toolPath,
                    Arguments = $"--cue \"{cuePath}\" --cdi \"{cdiOutputPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                //if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                //{
                //    EnsureExecutable(toolPath);
                //}

                using var process = new Process { StartInfo = startInfo };
                process.Start();

                // Read stdout and stderr in parallel to avoid deadlock
                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();

                bool exited = process.WaitForExit(300000);
                if (!exited)
                {
                    try { process.Kill(); } catch { }
                    return (false, "Conversion timed out after 5 minutes");
                }

                var stdout = stdoutTask.GetAwaiter().GetResult();
                var stderr = stderrTask.GetAwaiter().GetResult();

                var combinedOutput = stdout + stderr;

                if (combinedOutput.Contains(SuccessMarker))
                {
                    // Verify the output file was created
                    if (File.Exists(cdiOutputPath))
                    {
                        return (true, "Conversion successful");
                    }
                    else
                    {
                        return (false, "Conversion appeared successful but output file not found");
                    }
                }
                else
                {
                    return (false, $"Conversion failed: {combinedOutput}");
                }
            }
            catch (Exception ex)
            {
                return (false, $"Error running redump2cdi: {ex.Message}");
            }
        }

        //private static void EnsureExecutable(string filePath)
        //{
        //    try
        //    {
        //        var chmodInfo = new ProcessStartInfo
        //        {
        //            FileName = "chmod",
        //            Arguments = $"+x \"{filePath}\"",
        //            UseShellExecute = false,
        //            CreateNoWindow = true,
        //            RedirectStandardOutput = true,
        //            RedirectStandardError = true
        //        };

        //        using var chmod = Process.Start(chmodInfo);
        //        chmod?.WaitForExit();
        //    }
        //    catch
        //    {
        //        // Ignore chmod errors. The file might already be executable.
        //    }
        //}

        public static string GetCdiOutputName(string cuePath)
        {
            var baseName = Path.GetFileNameWithoutExtension(cuePath);
            return baseName + ".cdi";
        }
    }
}
