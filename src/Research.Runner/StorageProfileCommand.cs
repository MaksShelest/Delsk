using Simulation.Hosting;

internal static class StorageProfileCommand
{
    public static int Run(string[] args)
    {
        try
        {
            if(args.Length!=6||args[0] is not ("--export-service-runs-profile" or "--export-service-profile" or "--export-billing-profile" or "--export-road-profile" or "--export-fleet-profile" or "--export-storage-profile" or "--export-workshop-profile" or "--export-development-profile" or "--export-production-profile")||args[2]!="--balance"||args[4]!="--output")throw new ArgumentException("Use --export-storage-profile A2_PROFILE --balance FULL_BALANCE --output NEW_DIRECTORY");
            string output=Path.GetFullPath(args[5]);if(Directory.Exists(output)&&Directory.EnumerateFileSystemEntries(output).Any())throw new ArgumentException("Output must be new/empty");
            var basis=EconomyProfile.Load(args[1]);var read=VNextBalanceReader.Load(args[3]);if(!read.IsValid||read.Sha256!=basis.VNext?.FullBalanceSha256)throw new ArgumentException("Full balance/profile mismatch");
            bool workshops=args[0]=="--export-workshop-profile",development=args[0]=="--export-development-profile";
            bool production=args[0]=="--export-production-profile";
            bool road=args[0]=="--export-road-profile";
            bool fleet=args[0]=="--export-fleet-profile";
            bool runs=args[0]=="--export-service-runs-profile";
            bool services=args[0]=="--export-service-profile";
            bool billing=args[0]=="--export-billing-profile";
            var profile=runs?VNextServiceRunProfile.Create(basis,read.Balance!):services?VNextServiceProfile.Create(basis,read.Balance!):billing?VNextBillingProfile.Create(basis,read.Balance!):road?VNextRoadProfile.Create(basis,read.Balance!):fleet?VNextFleetProfile.Create(basis,read.Balance!):production?VNextProductionProfile.Create(basis,read.Balance!):development?VNextDevelopmentProfile.Create(basis,read.Balance!):workshops?VNextWorkshopProfile.Create(basis,read.Balance!):VNextStorageProfile.Create(basis,read.Balance!);EconomyProfile.Read(System.Text.Encoding.UTF8.GetString(VNextSupportedProfile.Serialize(profile)));
            Directory.CreateDirectory(output);File.WriteAllBytes(Path.Combine(output,runs?"economy.f2-services.json":services?"economy.f1-services.json":billing?"economy.e3-accounts.json":road?"economy.e2-roads.json":fleet?"economy.e1-fleet.json":production?"economy.d3-production.json":development?"economy.d2-development.json":workshops?"economy.d1-workshops.json":"economy.c1-storage.json"),VNextSupportedProfile.Serialize(profile));return 0;
        }
        catch(Exception e)when(e is ArgumentException or System.Text.Json.JsonException or IOException or InvalidOperationException)
        {Console.Error.WriteLine(e.Message);return 2;}
    }
}
