using System.Text.Json;
using Simulation.Core;
using Simulation.Hosting;

internal static class MapDesignCommand
{
    // Reproducible data adaptation for the laboratory snapshots. No controller/model/world.
    public static int Run(string[] args)
    {
        try
        {
            if(args.Length!=6 || args[0]!="--export-map-design" || args[2]!="--economy" || args[4]!="--output")throw new ArgumentException("Use --export-map-design SOURCE --economy A2_PROFILE --output NEW_DIRECTORY");
            string directory=Path.GetFullPath(args[5]);if(Directory.Exists(directory)&&Directory.EnumerateFileSystemEntries(directory).Any())throw new ArgumentException("Output must be new or empty");
            if(new FileInfo(args[1]).Length>MapDesignReader.MaximumBytes)throw new ArgumentException("Map design too large");
            var economy=EconomyProfile.Load(args[3]);var scenario=economy.Apply(MapDesignReader.Read(File.ReadAllBytes(args[1]),economy));
            ScenarioValidation.Validate(scenario);Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory,"scenario.json"),JsonSerializer.Serialize(scenario,new JsonSerializerOptions(SimulationJson.Options){WriteIndented=true})+"\n");
            Console.WriteLine($"Resolved map {scenario.Id}; source {scenario.Layout!.SourceSha256}");return 0;
        }
        catch(Exception e)when(e is ArgumentException or JsonException or IOException or InvalidOperationException or OverflowException)
        {Console.Error.WriteLine("Map design rejected: "+e.Message);return 2;}
    }
}
