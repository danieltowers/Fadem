using System.Text;

namespace Fadem.Core;

/// <summary>The system's ANSI code page. net8.0 must register the code-page provider first;
/// net48 has them built in (<c>Compat/Net48</c>).</summary>
internal static class CodePages
{
    public static Encoding SystemAnsi()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(0);
    }
}
