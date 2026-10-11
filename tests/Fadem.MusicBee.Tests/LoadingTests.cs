using Fadem.Core.Tests;
using MusicBeePlugin;

namespace Fadem.MusicBee.Tests;

/// <summary>
/// How MusicBee loads a plugin: it reflects over every type of <c>Plugins\mb_*.dll</c> with
/// <c>Assembly.GetTypes</c> before it calls <c>Initialise</c>, so before the plugin's resolver
/// can find its dependencies. A type that cannot load without Core fails the whole plugin, and
/// MusicBee drops it (found in the first smoke run of issue 03).
/// </summary>
public sealed class LoadingTests
{
    [Fact]
    public void EveryPluginType_LoadsWithoutItsDependencies_AsMusicBeeReflectsOverThem()
    {
        // A folder holding the plugin alone, as MusicBee's Plugins folder does before the resolver
        // is registered: nothing it depends on can be found.
        var folder = Path.Combine(TestTemp.Root, "musicbee", "types", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var plugin = Copy(typeof(Plugin).Assembly.Location, folder);
        var probe = Copy(typeof(TypeLoadProbe).Assembly.Location, folder);
        var musicBee = AppDomain.CreateDomain("MusicBee", null, new AppDomainSetup { ApplicationBase = folder });
        try
        {
            var reflector = (TypeLoadProbe)musicBee.CreateInstanceFromAndUnwrap(probe, typeof(TypeLoadProbe).FullName!);

            Assert.Empty(reflector.Failures(plugin));
        }
        finally
        {
            AppDomain.Unload(musicBee);
        }
    }

    private static string Copy(string file, string folder)
    {
        var copy = Path.Combine(folder, Path.GetFileName(file));
        File.Copy(file, copy);
        return copy;
    }
}

/// <summary>Runs in the MusicBee domain: what <c>GetTypes</c> could not load, and why. It names
/// no type but the framework's, so it loads there itself.</summary>
public sealed class TypeLoadProbe : MarshalByRefObject
{
    public string[] Failures(string assembly)
    {
        var module = System.Reflection.Assembly.LoadFrom(assembly).ManifestModule;
        var failures = new List<string>();
        try
        {
            module.Assembly.GetTypes();
        }
        catch (System.Reflection.ReflectionTypeLoadException)
        {
            // Name each type that fails: type definitions are numbered from 0x02000002 (1 is <Module>).
            for (var token = 0x02000002; ; token++)
            {
                try
                {
                    module.ResolveType(token);
                }
                catch (ArgumentOutOfRangeException)
                {
                    break;
                }
                catch (Exception e)
                {
                    failures.Add($"type 0x{token:X8}: {e.GetType().Name}: {e.Message}");
                }
            }
            if (failures.Count == 0)
            {
                failures.Add("GetTypes failed, but every type resolves on its own");
            }
        }
        return failures.ToArray();
    }
}
