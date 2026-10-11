namespace Fadem.Shell;

/// <summary>One line of the session's notice list: a message, how many times it was delivered,
/// and when it first was.</summary>
internal sealed record NoticeEntry(string Message, int Count, DateTime FirstDelivered);

/// <summary>
/// This session's notices as delivered through <c>IEngineSink.Notice</c>, and the shell's own
/// (build issue 09). A repeat merges into the first entry with that message and counts; it does
/// not move. No WinForms and no engine state: it is a record of messages already shown. Nothing
/// is written to disk.
/// </summary>
internal sealed class NoticeLog
{
    private readonly List<NoticeEntry> _entries = new();
    private readonly Dictionary<string, int> _indexOf = new();
    private int _latest = -1;

    /// <summary>In the order each message was first delivered.</summary>
    public IReadOnlyList<NoticeEntry> Entries => _entries;

    /// <summary>The entry of the message delivered last, wherever it sits; null before any.</summary>
    public NoticeEntry? Latest => _latest < 0 ? null : _entries[_latest];

    /// <summary>Where <see cref="Latest"/> sits in <see cref="Entries"/>; -1 before any.</summary>
    public int LatestIndex => _latest;

    public void Add(string message)
    {
        if (_indexOf.TryGetValue(message, out var index))
        {
            _entries[index] = _entries[index] with { Count = _entries[index].Count + 1 };
        }
        else
        {
            index = _entries.Count;
            _indexOf[message] = index;
            _entries.Add(new NoticeEntry(message, 1, DateTime.Now));
        }
        _latest = index;
    }
}
