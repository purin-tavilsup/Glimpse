namespace Glimpse.Drive;

/// <summary>
/// Finds a Chromium browser that is already installed, so pages the agent opens itself need no browser download.
/// Edge comes first because Windows always has it; Chrome covers a Mac without Edge.
/// </summary>
public static class InstalledBrowser
{
    public static IReadOnlyList<string> Candidates()
    {
        if (OperatingSystem.IsWindows())
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return
            [
                Path.Combine(programFilesX86, @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(programFiles, @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(programFiles, @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(localAppData, @"Google\Chrome\Application\chrome.exe"),
            ];
        }

        return
        [
            "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge",
            "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
        ];
    }

    /// <exception cref="InvalidOperationException">None of the candidates exists.</exception>
    public static string Find(IReadOnlyList<string> candidates, Func<string, bool> exists) =>
        candidates.FirstOrDefault(exists)
        ?? throw new InvalidOperationException($"no Edge or Chrome found; looked in: {string.Join(", ", candidates)}");
}
