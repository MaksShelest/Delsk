using Simulation.Hosting;

internal static class StorageProfileCommand
{
    public static int Run(string[] args)
    {
        try
        {
            if(args.Length!=6||args[0] is not ("--export-storage-profile" or "--export-workshop-profile")||args[2]!="--balance"||args[4]!="--output")throw new ArgumentException("Use --export-storage-profile A2_PROFILE --balance FULL_BALANCE --output NEW_DIRECTORY");
            string output=Path.GetFullPath(args[5]);if(Directory.Exists(output)&&Directory.EnumerateFileSystemEntries(output).Any())throw new ArgumentException("Output must be new/empty");
            var basis=EconomyProfile.Load(args[1]);var read=VNextBalanceReader.Load(args[3]);if(!read.IsValid||read.Sha256!=basis.VNext?.FullBalanceSha256)throw new ArgumentException("Full balance/profile mismatch");
            bool workshops=args[0]=="--export-workshop-profile";
            var profile=workshops?VNextWorkshopProfile.Create(basis,read.Balance!):VNextStorageProfile.Create(basis,read.Balance!);EconomyProfile.Read(System.Text.Encoding.UTF8.GetString(VNextSupportedProfile.Serialize(profile)));
            Directory.CreateDirectory(output);File.WriteAllBytes(Path.Combine(output,workshops?"economy.d1-workshops.json":"economy.c1-storage.json"),VNextSupportedProfile.Serialize(profile));return 0;
        }
        catch(Exception e)when(e is ArgumentException or System.Text.Json.JsonException or IOException or InvalidOperationException)
        {Console.Error.WriteLine(e.Message);return 2;}
    }
}
