using System.Runtime.CompilerServices;

namespace Faderio.Shell.Tests;

/// <summary>Common controls 6 for every test here, as both hosts run: list groups collapse and
/// report their bounds only under it. <see cref="Application.EnableVisualStyles"/> counts only
/// before the first window of the process, so it is called as the assembly loads, never from a
/// test, where any earlier test's control would have come first (the sections' tests failed
/// whenever another headless test ran before them).</summary>
internal static class VisualStyles
{
    [ModuleInitializer]
    internal static void Enable() => Application.EnableVisualStyles();
}
