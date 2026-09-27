using System.Diagnostics;

namespace Birchpad.Core.Mac;

/// <summary>Runs a program with arguments, captures its output, and kills it after a timeout.</summary>
public static class ProcessRunner
{
    public static (int ExitCode, string Output, string Error) Run(string file, IEnumerable<string> args, TimeSpan timeout, string? workingDirectory = null)
    {
        var info = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (workingDirectory is not null)
        {
            info.WorkingDirectory = workingDirectory;
        }

        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeout))
        {
            process.Kill(entireProcessTree: true);
            return (-1, string.Empty, $"{Path.GetFileName(file)} timed out after {timeout.TotalSeconds:0} seconds");
        }

        return (process.ExitCode, output.Result, error.Result);
    }
}
