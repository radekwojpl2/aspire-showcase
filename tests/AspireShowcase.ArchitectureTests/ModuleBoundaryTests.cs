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
        ["AspireShowcase.BusinessSetup"] = ["BusinessSetupModule"],
        ["AspireShowcase.Scheduling"] = ["SchedulingModule"],
        // Identity's assembly is also what other modules use of it, until it has a PublicClient.
        ["AspireShowcase.Identity"] =
        [
            "IdentityAccess", "IOwnerRoles", "OwnerRoleUnavailableException", "IUserProfiles", "UserProfile",
            "UserProfilesUnavailableException",
        ],
    };

    /// <summary>Assemblies other modules may use besides public clients, until they're split.</summary>
    static readonly string[] SharedUntilSplit = ["AspireShowcase.Identity"];

    public static TheoryData<string> ModuleAssemblies() => [.. Modules.Assemblies.Select(assembly => assembly.GetName().Name!)];

    [Fact]
    public void Every_module_folder_has_assemblies_in_the_api_host()
    {
        Assert.All(Modules.Names, module =>
            Assert.Contains(Modules.Assemblies, assembly => Modules.ModuleOf(assembly.GetName()) == module));
    }

    [Theory]
    [MemberData(nameof(ModuleAssemblies))]
    public void A_module_uses_another_module_only_through_its_public_client(string assemblyName)
    {
        var assembly = Load(assemblyName);
        var module = Modules.ModuleOf(assembly.GetName());

        var forbidden = assembly.GetReferencedAssemblies()
            .Where(reference => Modules.ModuleOf(reference) is { } other && other != module && other != Modules.BuildingBlocks)
            .Where(reference => Modules.PartOf(reference) != "PublicClient" && !SharedUntilSplit.Contains(reference.Name))
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
