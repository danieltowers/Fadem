using System.Text;

namespace Fadem.Core;

/// <summary>The system's ANSI code page, built into .NET Framework; net8.0 must register a
/// provider for it (<c>Compat/Net8</c>).</summary>
internal static class CodePages
{
    public static Encoding SystemAnsi() => Encoding.GetEncoding(0);
}
