using System.Reflection;

namespace AspireShowcase.ArchitectureTests;

/// <summary>
/// The rules between modules: a module uses another one only through its public client, and
/// keeps everything else to itself.
/// </summary>
public class ModuleBoundaryTests
{
    /// <summary>
    /// What each module's assemblies may make public: the entry points the API host calls. Its
    /// PublicClient and BuildingBlocks are public on purpose, so they aren't listed.
    /// </summary>
    static readonly Dictionary<string, string[]> AllowedPublicTypes = new()
    {
        ["AspireShowcase.BusinessSetup.Infrastructure"] = ["BusinessSetupModule"],
        ["AspireShowcase.BusinessSetup.Web"] = ["BusinessSetupWeb"],
        ["AspireShowcase.Scheduling.Infrastructure"] = ["SchedulingModule"],
        ["AspireShowcase.Scheduling.Web"] = ["SchedulingWeb"],
        ["AspireShowcase.Identity.Infrastructure"] = ["IdentityAccess"],
    };

    public static TheoryData<string> ModuleAssemblies() => [.. Modules.Assemblies.Select(assembly => assembly.GetName().Name!)];

    [Fact]
    public void Every_module_folder_has_assemblies()
    {
        Assert.All(Modules.Names, module =>
            Assert.Contains(Modules.Assemblies, assembly => Modules.ModuleOf(assembly.GetName()) == module));
    }

    // The project file lists the modules' projects one by one, so a new one could be left out
    // and never checked.
    [Fact]
    public void Every_project_of_a_module_is_checked()
    {
        var projects = Directory
            .GetFiles(Path.Combine(Modules.RepositoryRoot(), "src", "Modules"), "*.csproj", SearchOption.AllDirectories)
            .Select(Path.GetFileNameWithoutExtension);

        Assert.All(projects, project =>
            Assert.Contains(Modules.Assemblies, assembly => assembly.GetName().Name == project));
    }

    [Theory]
    [MemberData(nameof(ModuleAssemblies))]
    public void A_module_uses_another_module_only_through_its_public_client(string assemblyName)
    {
        var assembly = Load(assemblyName);
        var module = Modules.ModuleOf(assembly.GetName());

        var forbidden = assembly.GetReferencedAssemblies()
            .Where(reference => Modules.ModuleOf(reference) is { } other && other != module && other != Modules.BuildingBlocks)
            .Where(reference => Modules.PartOf(reference) != "PublicClient")
            .Select(reference => reference.Name);

        Assert.Empty(forbidden);
    }

    [Theory]
    [MemberData(nameof(ModuleAssemblies))]
    public void A_module_makes_public_only_its_entry_points(string assemblyName)
    {
        var assembly = Load(assemblyName);
        var part = Modules.PartOf(assembly.GetName());
        if (part == "PublicClient" || Modules.ModuleOf(assembly.GetName()) == Modules.BuildingBlocks)
        {
            return;
        }

        var allowed = AllowedPublicTypes.GetValueOrDefault(assemblyName, []);
        var unexpected = assembly.GetExportedTypes().Select(type => type.Name).Except(allowed);

        Assert.Empty(unexpected);
    }

    static Assembly Load(string name) => Modules.Assemblies.Single(assembly => assembly.GetName().Name == name);
}
