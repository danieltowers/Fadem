namespace Faderio.Core.Persistence;

/// <summary>
/// Which library a launch opens (library issue 02, D12): the folder named on the command line,
/// else the one remembered from the last launch while it still exists, else the user is asked.
/// </summary>
/// <param name="Folder">The folder to open; null when the user must be asked for one.</param>
public sealed record LibraryLaunch(string? Folder)
{
    public bool MustAsk => Folder is null;

    /// <param name="argument">The <c>--library=</c> value, or null when there was none.</param>
    /// <param name="remembered">The last library that opened, or null when none has.</param>
    /// <param name="exists">Whether a folder is there: <see cref="Directory.Exists"/>, bar tests.</param>
    public static LibraryLaunch Resolve(string? argument, string? remembered, Func<string, bool> exists)
    {
        if (!string.IsNullOrWhiteSpace(argument))
        {
            return new(argument); // opened even if it is gone: that is a notice, not a prompt
        }
        if (!string.IsNullOrWhiteSpace(remembered) && exists(remembered))
        {
            return new(remembered);
        }
        return new((string?)null);
    }
}
