using System.Reflection;

namespace AspireShowcase.ArchitectureTests;

/// <summary>
/// The modules as they're built: one folder per module under src/Modules, each with assemblies
/// named AspireShowcase.{Module}[.{Part}], all brought in by the API host.
/// </summary>
static class Modules
{
    /// <summary>Shared by every module, so any module may use it.</summary>
    public const string BuildingBlocks = "BuildingBlocks";

    /// <summary>The module names: the folders under src/Modules.</summary>
    public static readonly IReadOnlyList<string> Names = Directory
        .GetDirectories(Path.Combine(RepositoryRoot(), "src", "Modules"))
        .Select(Path.GetFileName)
        .OfType<string>()
        .Order()
        .ToList();

    /// <summary>Every assembly of every module, loaded from the test's output.</summary>
    public static readonly IReadOnlyList<Assembly> Assemblies = Directory
        .GetFiles(AppContext.BaseDirectory, "AspireShowcase.*.dll")
        .Select(file => Assembly.LoadFrom(file))
        .Where(assembly => ModuleOf(assembly.GetName()) is not null)
        .OrderBy(assembly => assembly.GetName().Name)
        .ToList();

    /// <summary>The module an assembly belongs to, or null for one that isn't a module's.</summary>
    public static string? ModuleOf(AssemblyName assembly) =>
        assembly.Name?.Split('.') is ["AspireShowcase", var module, ..] && Names.Contains(module) ? module : null;

    /// <summary>The part of a module an assembly is, such as "PublicClient" or "Domain"; "" for the module's own.</summary>
    public static string PartOf(AssemblyName assembly) =>
        string.Join('.', assembly.Name!.Split('.').Skip(2));

    // Recorded by the project file at build time.
    static string RepositoryRoot() =>
        typeof(Modules).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "RepositoryRoot").Value!;
}
