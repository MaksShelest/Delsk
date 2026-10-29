using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

if(args.Length is <1 or >2 || !Directory.Exists(args[0]) || args.Length==2 && args[1]!="--no-debug-ui")throw new ArgumentException("AssemblyAudit DIRECTORY [--no-debug-ui]");
bool noDebugUi=args.Length==2;
string folder=Path.GetFullPath(args[0]);int checkedFiles=0;
if(File.Exists(Path.Combine(folder,"Simulation.Developer.dll")))throw new InvalidOperationException("Developer assembly in ordinary package");
foreach(string name in new[]{"Delsk.Client","Simulation.Core","Simulation.Hosting","Agents.Algorithmic","Agents.Llm"})
{
    string path=Path.Combine(folder,name+".dll");if(!File.Exists(path))throw new FileNotFoundException(path);
    using var stream=File.OpenRead(path);using var pe=new PEReader(stream);var metadata=pe.GetMetadataReader();
    foreach(var handle in metadata.AssemblyReferences)
        if(metadata.GetString(metadata.GetAssemblyReference(handle).Name)=="Simulation.Developer")throw new InvalidOperationException(name+": Developer reference");
    foreach(var handle in metadata.TypeDefinitions)
    {
        var type=metadata.GetTypeDefinition(handle);string typeName=metadata.GetString(type.Name),ns=metadata.GetString(type.Namespace);
        if(typeName is "WorldProportionsProbe" or "CityExperienceProbe" or "DeveloperPanel" or "DeveloperTools" or "DecisionLogEntry" or "VisualModelsProbe" or "HudGodProbe" or "TabletDesignProbe" or "OrdinarySetupProbe" or "StorageProbe" or "DemolitionProbe" or "WorkshopProbe" or "DevelopmentProbe" or "ProductionProbe" or "FleetProbe" or "RoadProbe" or "BillingProbe" or "ServiceProbe" or "ServiceRunProbe" || ns=="Simulation.Developer")throw new InvalidOperationException(name+": Developer screen/type");
        foreach(var method in type.GetMethods())
        {
            if(noDebugUi && metadata.GetString(metadata.GetMethodDefinition(method).Name)=="OpenDebug")
                throw new InvalidOperationException(name+": Debug UI entry point in release package");
            if(metadata.GetString(metadata.GetMethodDefinition(method).Name) is "DeveloperModify" or "ApplyResearchPlayback" or "DeveloperMenu" or "DeveloperFrame" or "ConfigureResearchPlayback" or "DeveloperDebug" or "DeveloperInput" or "OpenDeveloperPanel" or "CloseDeveloperPanel")
                throw new InvalidOperationException(name+": intervention entry point");
        }
    }
    foreach(var handle in metadata.ManifestResources)
        if(metadata.GetString(metadata.GetManifestResource(handle).Name).Contains("Developer",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException(name+": Developer resource");
    ++checkedFiles;
}
Console.WriteLine($"Ordinary package metadata verified: {checkedFiles} assemblies; no Developer entry points, screens, references or resources.");
if(noDebugUi)Console.WriteLine("Release Debug UI entry point is absent.");
