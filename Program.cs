// See https://aka.ms/new-console-template for more information
using System.Diagnostics;
using System.Globalization;

class Program
{
    // Estensioni video che il programma può convertire
    static readonly string[] VideoExtensions =
    {
        ".mp4",
        ".mkv",
        ".mov",
        ".avi",
        ".webm",
        ".m4v",
        ".mts",
        ".m2ts",
        ".ts",
        ".flv"
    };

    static void Main()
    {
        ShowTitle();

        string? folder = AskForFolder();

        if (folder == null)
            return;

        List<string> videos = FindVideos(folder);

        if (videos.Count == 0)
        {
            Console.WriteLine("Non ho trovato nessun video.");
            return;
        }

        ShowVideoInfo(videos);

        if (!AskForConfirmation("Continuare? [Y/n]: "))
        {
            Console.WriteLine("Conversione annullata.");
            return;
        }

        string tempFolder = Path.Combine(folder, ".dnxhr_temp");

        Directory.CreateDirectory(tempFolder);

        ConversionResult result = ConvertVideos(videos, tempFolder);

        if (!result.Success)
        {
            ShowFailedConversions(result.FailedFiles);

            Directory.Delete(tempFolder, true);

            return;
        }

        Console.WriteLine("══════════════════════════════════════════");
        Console.WriteLine();
        Console.WriteLine(
            $"Tutti i {videos.Count} video sono stati convertiti correttamente."
        );

        Console.WriteLine();

        if (!AskForConfirmation("Sostituire gli originali con i nuovi file? [y/N]: "))
        {
            Console.WriteLine();
            Console.WriteLine("Originali mantenuti.");
            Console.WriteLine($"I nuovi file sono in: {tempFolder}");

            return;
        }

        Console.WriteLine();
        Console.WriteLine("Sostituzione degli originali...");

        if (ReplaceOriginals(videos, result.ConvertedFiles))
        {
            Directory.Delete(tempFolder, true);

            Console.WriteLine();
            Console.WriteLine("✓ Operazione completata.");
            Console.WriteLine("Gli originali sono stati sostituiti.");
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine(
                "⚠ Si è verificato un errore durante la sostituzione."
            );

            Console.WriteLine(
                $"I file temporanei sono ancora disponibili in: {tempFolder}"
            );
        }
    }

    // ============================================================
    // INTERFACCIA
    // ============================================================

    static void ShowTitle()
    {
        Console.WriteLine("╔══════════════════════════════════════════╗");
        Console.WriteLine("║             V I D F O R G E              ║");
        Console.WriteLine("╚══════════════════════════════════════════╝");
        Console.WriteLine();
    }

    static string? AskForFolder()
    {
        Console.Write("Cartella da convertire: ");

        string? folder = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(folder))
        {
            Console.WriteLine("Nessuna cartella inserita.");
            return null;
        }

        folder = folder.Trim('"');

        if (!Directory.Exists(folder))
        {
            Console.WriteLine("La cartella non esiste.");
            return null;
        }

        return folder;
    }

    static void ShowVideoInfo(List<string> videos)
    {
        Console.WriteLine();
        Console.WriteLine($"Video trovati: {videos.Count}");

        long totalSize = GetTotalSize(videos);

        Console.WriteLine(
            $"Spazio occupato dagli originali: {FormatBytes(totalSize)}"
        );

        double estimatedSize = EstimateDnxhrSize(videos);

        Console.WriteLine(
            $"Spazio stimato per DNxHR SQ:     {FormatBytes((long)estimatedSize)}"
        );

        Console.WriteLine();
    }

    static bool AskForConfirmation(string message)
    {
        Console.Write(message);

        string? answer = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(answer))
            return true;

        return answer.Trim().ToLower() == "y";
    }

    static void ShowFailedConversions(List<string> failedFiles)
    {
        Console.WriteLine();
        Console.WriteLine("Alcune conversioni sono fallite.");
        Console.WriteLine();

        foreach (string file in failedFiles)
        {
            Console.WriteLine($"  ✗ {Path.GetFileName(file)}");
        }

        Console.WriteLine();
        Console.WriteLine("Gli originali NON verranno sostituiti.");
    }

    // ============================================================
    // RICERCA FILE
    // ============================================================

    static List<string> FindVideos(string folder)
    {
        List<string> videos = new();

        string[] files = Directory.GetFiles(
            folder,
            "*",
            SearchOption.TopDirectoryOnly
        );

        foreach (string file in files)
        {
            string extension = Path.GetExtension(file).ToLowerInvariant();

            if (VideoExtensions.Contains(extension))
            {
                videos.Add(file);
            }
        }

        return videos;
    }

    // ============================================================
    // INFORMAZIONI FILE
    // ============================================================

    static long GetTotalSize(List<string> files)
    {
        long total = 0;

        foreach (string file in files)
        {
            FileInfo info = new(file);
            total += info.Length;
        }

        return total;
    }

    static string FormatBytes(long bytes)
    {
        const double KB = 1024;
        const double MB = KB * 1024;
        const double GB = MB * 1024;

        if (bytes >= GB)
            return $"{bytes / GB:F2} GB";

        if (bytes >= MB)
            return $"{bytes / MB:F2} MB";

        if (bytes >= KB)
            return $"{bytes / KB:F2} KB";

        return $"{bytes} B";
    }

    // ============================================================
    // STIMA DIMENSIONE DNXHR
    // ============================================================

    static double EstimateDnxhrSize(List<string> videos)
    {
        double total = 0;

        foreach (string video in videos)
        {
            double duration = GetVideoDuration(video);
            int width = GetVideoWidth(video);

            double bitrate = GetDnxhrBitrate(width);

            total += duration * bitrate / 8;
        }

        // Piccolo margine di sicurezza
        return total * 1.01;
    }

    static double GetDnxhrBitrate(int width)
    {
        if (width >= 3000)
            return 440_000_000;

        if (width >= 1900)
            return 175_000_000;

        if (width >= 1200)
            return 115_000_000;

        return 45_000_000;
    }

    // ============================================================
    // FFPROBE
    // ============================================================

    static double GetVideoDuration(string file)
    {
        string arguments =
            $"-v error " +
            $"-show_entries format=duration " +
            $"-of default=noprint_wrappers=1:nokey=1 " +
            $"\"{file}\"";

        string output = RunProcess("ffprobe", arguments);

        if (double.TryParse(
            output.Trim(),
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out double duration))
        {
            return duration;
        }

        return 0;
    }

    static int GetVideoWidth(string file)
    {
        string arguments =
            $"-v error " +
            $"-select_streams v:0 " +
            $"-show_entries stream=width " +
            $"-of csv=p=0 " +
            $"\"{file}\"";

        string output = RunProcess("ffprobe", arguments);

        if (int.TryParse(output.Trim(), out int width))
        {
            return width;
        }

        return 1920;
    }

    // ============================================================
    // CONVERSIONE
    // ============================================================

    static ConversionResult ConvertVideos(
        List<string> videos,
        string tempFolder)
    {
        List<string> convertedFiles = new();
        List<string> failedFiles = new();

        Console.WriteLine();
        Console.WriteLine("Inizio conversione...");
        Console.WriteLine();

        foreach (string video in videos)
        {
            string fileName = Path.GetFileNameWithoutExtension(video);

            string output = Path.Combine(
                tempFolder,
                fileName + ".mov"
            );

            Console.WriteLine($"→ {Path.GetFileName(video)}");

            bool success = ConvertVideo(video, output);

            if (success)
            {
                convertedFiles.Add(output);
                Console.WriteLine("  ✓ Completato");
            }
            else
            {
                failedFiles.Add(video);
                Console.WriteLine("  ✗ ERRORE");
            }

            Console.WriteLine();
        }

        return new ConversionResult(
            convertedFiles,
            failedFiles
        );
    }

    static bool ConvertVideo(string input, string output)
    {
        string arguments =
            $"-hide_banner -stats " +
            $"-i \"{input}\" " +
            $"-map 0:v:0 " +
            $"-map 0:a? " +
            $"-c:v dnxhd " +
            $"-profile:v dnxhr_sq " +
            $"-c:a pcm_s16le " +
            $"-map_metadata 0 " +
            $"\"{output}\"";

        int exitCode = RunProcessWithExitCode(
            "ffmpeg",
            arguments
        );

        return exitCode == 0 && File.Exists(output);
    }

    // ============================================================
    // SOSTITUZIONE ORIGINALI
    // ============================================================

    static bool ReplaceOriginals(
        List<string> originals,
        List<string> converted)
    {
        try
        {
            for (int i = 0; i < originals.Count; i++)
            {
                string original = originals[i];
                string newFile = converted[i];

                string directory = Path.GetDirectoryName(original)!;

                string fileName =
                    Path.GetFileNameWithoutExtension(original);

                string finalFile = Path.Combine(
                    directory,
                    fileName + ".mov"
                );

                File.Move(
                    newFile,
                    finalFile,
                    true
                );

                File.Delete(original);
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Errore: {ex.Message}");
            return false;
        }
    }

    // ============================================================
    // PROCESSI ESTERNI
    // ============================================================

    static string RunProcess(
        string program,
        string arguments)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = program,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using Process process = new();

        process.StartInfo = startInfo;
        process.Start();

        string output =
            process.StandardOutput.ReadToEnd();

        process.WaitForExit();

        return output;
    }

    static int RunProcessWithExitCode(
        string program,
        string arguments)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = program,
            Arguments = arguments,
            UseShellExecute = false
        };

        using Process process = new();

        process.StartInfo = startInfo;
        process.Start();

        process.WaitForExit();

        return process.ExitCode;
    }
}


// ================================================================
// RISULTATO DELLA CONVERSIONE
// ================================================================

class ConversionResult
{
    public List<string> ConvertedFiles { get; }
    public List<string> FailedFiles { get; }

    public bool Success => FailedFiles.Count == 0;

    public ConversionResult(
        List<string> convertedFiles,
        List<string> failedFiles)
    {
        ConvertedFiles = convertedFiles;
        FailedFiles = failedFiles;
    }
}
