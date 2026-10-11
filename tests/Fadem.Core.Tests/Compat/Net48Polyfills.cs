// Compiled on net48 only (phase 5 issue 08): what the tests use that net48 lacks. Core's own
// stand-ins (Compat, CompatExtensions, the compiler polyfills) reach the tests through
// InternalsVisibleTo; this file holds only what the tests need beyond them.

namespace System.Runtime.Versioning
{
    /// <summary>An analyser hint on net8.0; nothing on net48, where every platform is Windows.</summary>
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
    internal sealed class SupportedOSPlatformAttribute(string platformName) : Attribute
    {
        public string PlatformName { get; } = platformName;
    }
}
