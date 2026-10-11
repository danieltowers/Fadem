using Fadem.Core.Queue;

namespace Fadem.Core.Playback;

/// <summary>Engine-issued identity of a section (phase 6 issue 09). A stale id is a
/// <see cref="IEngineSink.Notice"/>, never an exception.</summary>
public readonly record struct SectionId(long Value)
{
    public override string ToString() => $"section {Value}";
}

/// <summary>A section as the shell sees it: its name, its colour, an index into a fixed palette
/// of <see cref="Colours"/>, and whether it is shown collapsed to its header.</summary>
public sealed record SectionView(SectionId Id, string Name, int Colour, bool Collapsed)
{
    /// <summary>How many colours the palette has: a colour is 0 to one less.</summary>
    public const int Colours = 8;

    /// <summary>Whether <c>MoveSection</c> can move it up a section: a section is above it. Never
    /// under shuffle, which shows no sections to move past.</summary>
    public bool CanMoveUp { get; init; }

    /// <summary>Whether <c>MoveSection</c> can move it down a section.</summary>
    public bool CanMoveDown { get; init; }
}

public sealed partial class PlaybackEngine
{
    // ── Sections: named runs of the playlist's rows, labels only (phase 6 issue 09) ──────

    /// <summary>One section of the open playlist. Mutable under the lock; the view sees a
    /// <see cref="SectionView"/> of it.</summary>
    private sealed class Section(SectionId id, string name, int colour, bool collapsed)
    {
        public SectionId Id { get; } = id;
        public string Name { get; set; } = name;
        public int Colour { get; set; } = colour;
        public bool Collapsed { get; set; } = collapsed;

        public SectionView View => new(Id, Name, Colour, Collapsed);
    }

    /// <summary>The section of each row of the open playlist that has one. A row with none sits
    /// above the first section. The sections' rows are consecutive in the playlist's own order
    /// (<see cref="_originalOrder"/>), and a section with no rows left is gone.</summary>
    private readonly Dictionary<RowId, Section> _rowSection = new();

    private long _nextSectionId = 1;

    /// <summary>Starts a section called <paramref name="name"/> at <paramref name="at"/>: it holds
    /// that row and the rows after it, in the playlist's own order, up to where the row's section
    /// ended. A row that already starts a section, a blank name and a row in no playlist are
    /// refused with a notice, as is any section edit while the playlist is still opening.</summary>
    public void StartSection(RowId at, string name)
    {
        ThrowIfDisposed();
        Run(() =>
        {
            ObserveClock();
            if (!SectionsSettled() || SectionName(name) is not { } named || PlaylistIndexOf(at) is not { } index)
            {
                return;
            }
            var old = SectionOf(_originalOrder[index]);
            if (old is not null && (index == 0 || SectionOf(_originalOrder[index - 1]) != old))
            {
                RaiseNotice($"That row already starts the section {old.Name}.");
                return;
            }
            var end = index;
            while (end < _originalOrder.Count && SectionOf(_originalOrder[end]) == old)
            {
                end++;
            }
            var above = index > 0 ? SectionOf(_originalOrder[index - 1]) : null;
            var below = end < _originalOrder.Count ? SectionOf(_originalOrder[end]) : null;
            var section = NewSection(named, NextColour(above, below), collapsed: false);
            for (var i = index; i < end; i++)
            {
                _rowSection[_originalOrder[i].Id] = section;
            }
            SectionsChanged();
        });
    }

    /// <summary>Renames <paramref name="section"/>; a blank name is refused with a notice.</summary>
    public void RenameSection(SectionId section, string name) =>
        EditSection(section, found =>
        {
            if (SectionName(name) is not { } named || named == found.Name)
            {
                return false;
            }
            found.Name = named;
            return true;
        });

    /// <summary>Gives <paramref name="section"/> the palette's colour <paramref name="colour"/>;
    /// one outside the palette is refused with a notice.</summary>
    public void RecolourSection(SectionId section, int colour) =>
        EditSection(section, found =>
        {
            if (colour is < 0 or >= SectionView.Colours)
            {
                RaiseNotice($"There is no section colour {colour}: the palette has {SectionView.Colours}.");
                return false;
            }
            var changed = found.Colour != colour;
            found.Colour = colour;
            return changed;
        });

    /// <summary>Shows <paramref name="section"/> collapsed to its header, or open. The playlist
    /// keeps which, so it opens as it was left.</summary>
    public void CollapseSection(SectionId section, bool collapsed) =>
        EditSection(section, found =>
        {
            var changed = found.Collapsed != collapsed;
            found.Collapsed = collapsed;
            return changed;
        });

    /// <summary>Removes <paramref name="section"/>, not its rows: they join the section above it,
    /// or none when it is the first.</summary>
    public void RemoveSection(SectionId section) =>
        EditSection(section, found =>
        {
            var first = _originalOrder.FindIndex(e => SectionOf(e) == found);
            var above = first > 0 ? SectionOf(_originalOrder[first - 1]) : null;
            foreach (var entry in _originalOrder.Where(e => SectionOf(e) == found).ToList())
            {
                SetSection(entry.Id, above);
            }
            return true;
        });

    /// <summary>Moves <paramref name="section"/> and all its rows <paramref name="places"/> sections
    /// down, or up when negative, as far as the first or last section; the rows above the first
    /// section stay at the top. Rows keep their identity, so what plays is never interrupted, and
    /// the pending boundary follows the new order, as <see cref="Move"/> moves rows. Under shuffle
    /// the playlist's own order changes, which turning shuffle off returns to.</summary>
    public void MoveSection(SectionId section, int places) =>
        EditSection(section, found =>
        {
            var runs = Runs();
            var top = runs.Count > 0 && runs[0].Key is null ? runs[0].Items : [];
            var sections = runs.Where(r => r.Key is not null).ToList();
            var from = sections.FindIndex(r => r.Key == found);
            var to = Math.Max(0, Math.Min(sections.Count - 1, from + places));
            if (to == from)
            {
                return false;
            }
            var moving = sections[from];
            sections.RemoveAt(from);
            sections.Insert(to, moving);
            var order = top.Concat(sections.SelectMany(r => r.Items)).ToList();
            _originalOrder.Clear();
            _originalOrder.AddRange(order);
            if (!_settings.Shuffle)
            {
                // Unshuffled, the visible order is the original order.
                RestoreOriginalOrder();
                RescheduleCurrentBoundary();
            }
            return true;
        });

    /// <summary>Runs <paramref name="change"/> on the section under the lock; it says whether it
    /// changed anything. A section with no rows left is gone, a notice.</summary>
    private void EditSection(SectionId id, Func<Section, bool> change)
    {
        ThrowIfDisposed();
        Run(() =>
        {
            ObserveClock();
            if (SectionsSettled() && FindSection(id) is { } found && change(found))
            {
                SectionsChanged();
            }
        });
    }

    /// <summary>Whether the sections can be edited: not while the playlist is opening, since its
    /// rows still to load go into the sections its file named, and an edit made in between could
    /// split one. If not, a notice says why.</summary>
    private bool SectionsSettled()
    {
        if (_loading is null)
        {
            return true;
        }
        RaiseNotice("The playlist is still opening: its sections can be changed once it has.");
        return false;
    }

    /// <summary>The open playlist's section <paramref name="id"/>, or null with a notice.</summary>
    private Section? FindSection(SectionId id)
    {
        var found = _rowSection.Values.FirstOrDefault(s => s.Id == id);
        if (found is null)
        {
            RaiseNotice($"That section is no longer in the playlist ({id}).");
        }
        return found;
    }

    /// <summary><paramref name="name"/> as a section is called: trimmed, on one line. Null with a
    /// notice when nothing is left.</summary>
    private string? SectionName(string name)
    {
        var named = name.ReplaceLineEndings(" ").Trim();
        if (named.Length == 0)
        {
            RaiseNotice("A section needs a name.");
            return null;
        }
        return named;
    }

    /// <summary>Where <paramref name="row"/> is in the playlist's own order, or null with a notice
    /// when it is gone or is in no playlist (a passing row).</summary>
    private int? PlaylistIndexOf(RowId row)
    {
        var index = _originalOrder.FindIndex(e => e.Id == row);
        if (index >= 0)
        {
            return index;
        }
        RaiseNotice(_queue.IndexOf(row) < 0
            ? $"That row is no longer in the queue ({row})."
            : "That row is in no playlist: it is still sounding from the playlist open before.");
        return null;
    }

    private Section NewSection(string name, int colour, bool collapsed) =>
        new(new SectionId(_nextSectionId++), name, colour, collapsed);

    /// <summary>The palette's next colour after the section above's, or its first; passed over
    /// when the section below has it, so a section stands apart from both it touches.</summary>
    private static int NextColour(Section? above, Section? below)
    {
        var colour = above is null ? 0 : (above.Colour + 1) % SectionView.Colours;
        return colour == below?.Colour ? (colour + 1) % SectionView.Colours : colour;
    }

    /// <summary>A change to the sections: the rows show it and the playlist keeps it.</summary>
    private void SectionsChanged()
    {
        _queueVersion++;
        SaveOpenPlaylist();
        Publish();
    }

    /// <summary>Puts the new row at <paramref name="index"/> in the playlist's own order into the
    /// section of the row after it there, or at the end the row before it: the section it lands in.</summary>
    private void JoinSectionAt(int index)
    {
        var neighbour = index + 1 < _originalOrder.Count ? _originalOrder[index + 1]
            : index > 0 ? _originalOrder[index - 1]
            : null;
        SetSection(_originalOrder[index].Id, neighbour is null ? null : SectionOf(neighbour));
    }

    /// <summary>Puts <paramref name="row"/> in <paramref name="section"/>, or in none.</summary>
    private void SetSection(RowId row, Section? section)
    {
        if (section is null)
        {
            _rowSection.Remove(row);
        }
        else
        {
            _rowSection[row] = section;
        }
    }

    /// <summary>The section each of <paramref name="file"/>'s entries loads into, parallel to them.
    /// A colour the file lacks, or one outside the palette, is chosen as a new section's is.</summary>
    private IReadOnlyList<Section?> SectionsOf(PlaylistFile file)
    {
        var sections = new Section?[file.Entries.Count];
        Section? current = null;
        var next = 0;
        for (var i = 0; i < sections.Length; i++)
        {
            if (next < file.Sections.Count && file.Sections[next].Start == i)
            {
                var header = file.Sections[next++];
                var colour = header.Colour is { } c and >= 0 and < SectionView.Colours ? c : NextColour(current, null);
                current = NewSection(header.Name, colour, header.Collapsed);
            }
            sections[i] = current;
        }
        return sections;
    }

    /// <summary>The headers of the open playlist's sections, by where each starts in its own order.</summary>
    private IReadOnlyList<SectionHeader> PlaylistSections()
    {
        var headers = new List<SectionHeader>();
        Section? above = null;
        for (var i = 0; i < _originalOrder.Count; i++)
        {
            var section = SectionOf(_originalOrder[i]);
            if (section is not null && section != above)
            {
                headers.Add(new SectionHeader(i, section.Name, section.Colour, section.Collapsed));
            }
            above = section;
        }
        return headers;
    }

    /// <summary>The playlist's own order cut where the section changes: each run's section, or
    /// null for the rows above the first, and its rows.</summary>
    private List<(Section? Key, List<QueueEntry> Items)> Runs()
    {
        var runs = new List<(Section? Key, List<QueueEntry> Items)>();
        foreach (var entry in _originalOrder)
        {
            var section = SectionOf(entry);
            if (runs.Count == 0 || runs[runs.Count - 1].Key != section)
            {
                runs.Add((section, new List<QueueEntry>()));
            }
            runs[runs.Count - 1].Items.Add(entry);
        }
        return runs;
    }

    private Section? SectionOf(QueueEntry entry) => _rowSection.TryGetValue(entry.Id, out var section) ? section : null;

    /// <summary>Each row's section and whether a header is drawn above it, for the view: one is
    /// drawn where a row's section differs from the row above's. Under shuffle the rows are in
    /// play order, which runs across sections, so no header is drawn.</summary>
    private Dictionary<RowId, (SectionView View, bool Starts)> SectionRows()
    {
        var rows = new Dictionary<RowId, (SectionView, bool)>();
        if (_rowSection.Count == 0)
        {
            return rows;
        }
        // Unshuffled, the sections in the order shown, which is the order MoveSection moves in.
        var order = _settings.Shuffle ? [] : Runs().Select(r => r.Key).OfType<Section>().ToList();
        var views = new Dictionary<Section, SectionView>();
        Section? above = null;
        foreach (var entry in _queue.Entries)
        {
            var section = SectionOf(entry);
            if (section is not null)
            {
                if (!views.TryGetValue(section, out var view))
                {
                    var place = order.IndexOf(section);
                    views[section] = view = section.View with
                    {
                        CanMoveUp = place > 0,
                        CanMoveDown = place >= 0 && place < order.Count - 1,
                    };
                }
                rows[entry.Id] = (view, !_settings.Shuffle && section != above);
            }
            above = section;
        }
        return rows;
    }
}
