using System.Runtime.CompilerServices;

namespace Fadem.Shell.Tests;

/// <summary><see cref="Cursors.Hand"/> is made on first use, without a lock. Test classes run in
/// parallel on their own STA threads, so two of them reaching it at once each made one, and a
/// control kept the instance the property no longer returned (<c>AboutBoxTests</c> failed beside
/// <c>LogoTests</c> 14 runs in 16). On net48 the two do not even share a handle: each loads its
/// own copy of the cursor. Making it as the assembly loads, before any test, leaves one.</summary>
internal static class SharedCursors
{
    [ModuleInitializer]
    internal static void Make() => _ = Cursors.Hand;
}
