#if NETFRAMEWORK
namespace System.Runtime.CompilerServices;

/// <summary>The C# compiler's module initializer, which net48 does not ship: for
/// <see cref="Faderio.Shell.Tests.VisualStyles"/>.</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
internal sealed class ModuleInitializerAttribute : Attribute
{
}
#endif
