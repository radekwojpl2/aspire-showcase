using System.Reflection;

namespace AspireShowcase.ArchitectureTests;

/// <summary>
/// The clean architecture inside each module: Domain depends on nothing technical, Application
/// only on Domain and EF Core's abstractions, Web never on Infrastructure. Applies to every
/// assembly named AspireShowcase.{Module}.{Layer}, BuildingBlocks too.
/// </summary>
public class LayerTests
{
    // Prefixes of assembly names a layer may not reference, besides other layers.
    static readonly string[] Technical = ["Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "MassTransit", "Npgsql"];
    static readonly string[] WebAndBus = ["Microsoft.AspNetCore", "MassTransit", "Npgsql"];
    static readonly string[] Storage = ["Microsoft.EntityFrameworkCore", "MassTransit", "Npgsql"];

    public static TheoryData<string> AssembliesOf(string layer) =>
        [.. Modules.Assemblies.Select(assembly => assembly.GetName()).Where(name => Modules.PartOf(name) == layer).Select(name => name.Name!)];

    [Theory]
    [MemberData(nameof(AssembliesOf), "Domain")]
    public void Domain_depends_on_no_framework_and_no_other_layer(string assembly)
    {
        Assert.Empty(Forbidden(assembly, Technical, ["Application", "Infrastructure", "Web"]));
    }

    [Theory]
    [MemberData(nameof(AssembliesOf), "Application")]
    public void Application_depends_on_neither_the_web_nor_the_bus_nor_infrastructure(string assembly)
    {
        Assert.Empty(Forbidden(assembly, WebAndBus, ["Infrastructure", "Web"]));
    }

    [Theory]
    [MemberData(nameof(AssembliesOf), "Web")]
    public void Web_depends_on_no_storage_and_no_infrastructure(string assembly)
    {
        Assert.Empty(Forbidden(assembly, Storage, ["Infrastructure"]));
    }

    [Theory]
    [MemberData(nameof(AssembliesOf), "Infrastructure")]
    public void Infrastructure_depends_on_no_web_layer(string assembly)
    {
        Assert.Empty(Forbidden(assembly, [], ["Web"]));
    }

    // The references of an assembly to forbidden frameworks, or to forbidden layers of any module.
    static IEnumerable<string?> Forbidden(string assemblyName, string[] frameworks, string[] layers) =>
        Modules.Assemblies.Single(assembly => assembly.GetName().Name == assemblyName)
            .GetReferencedAssemblies()
            .Where(reference =>
                frameworks.Any(prefix => reference.Name!.StartsWith(prefix, StringComparison.Ordinal)) ||
                (Modules.ModuleOf(reference) is not null && layers.Contains(Modules.PartOf(reference))))
            .Select(reference => reference.Name);
}
