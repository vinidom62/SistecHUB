using System.Reflection;

namespace SistecHub.Core;

/// <summary>
/// Descobre implementações de <see cref="IAppModule"/> no assembly principal, sem registo manual.
/// </summary>
public static class ModuleLoader
{
    public static IReadOnlyList<IAppModule> DiscoverModules()
    {
        var asm = Assembly.GetExecutingAssembly();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<IAppModule>();

        foreach (var type in asm.GetTypes())
        {
            if (type.IsAbstract || type.IsInterface)
                continue;
            if (!typeof(IAppModule).IsAssignableFrom(type))
                continue;
            if (type.GetConstructor(Type.EmptyTypes) is null)
                continue;

            var instance = (IAppModule)Activator.CreateInstance(type)!;

            if (!seenIds.Add(instance.Id))
                throw new InvalidOperationException(
                    $"Dois módulos usam o mesmo Id \"{instance.Id}\" ({type.Name}).");

            list.Add(instance);
        }

        return list
            .OrderBy(m => m.MenuText, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
