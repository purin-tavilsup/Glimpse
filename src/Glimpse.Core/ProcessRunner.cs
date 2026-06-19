using System.Diagnostics;

namespace Glimpse.Core;

public sealed record ProcessResult(int ExitCode, string StdErr);

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> args);
}

/// <summary>Runs a render tool as a child process, capturing stderr.</summary>
public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> args)
    {
        var startInfo = new ProcessStartInfo(executable) { RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo)
            ?? throw new GlimpseRenderToolException(executable, $"Failed to start '{executable}'.");

        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(process.ExitCode, stderr);
    }
}
