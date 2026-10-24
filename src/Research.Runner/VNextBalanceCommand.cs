using System.Text.Json;
using Simulation.Hosting;

internal static class VNextBalanceCommand
{
    public static int Run(string[] args)
    {
        var temporary=new List<string>();
        try
        {
            var options=new Dictionary<string,string>(StringComparer.Ordinal);
            for(int i=0;i<args.Length;i+=2)
                if(i+1>=args.Length || args[i] is not ("--validate-vnext-balance" or "--output" or "--source" or "--catalog" or "--baseline-economy") ||
                    string.IsNullOrWhiteSpace(args[i+1]) || args[i+1].StartsWith("--",StringComparison.Ordinal) || !options.TryAdd(args[i],args[i+1]))
                    throw new ArgumentException("Use --validate-vnext-balance PATH --output DIR [--source B1 --catalog A1] [--baseline-economy PROFILE]; each once.");
            if(!options.ContainsKey("--validate-vnext-balance") || !options.ContainsKey("--output") ||
                options.ContainsKey("--source")!=options.ContainsKey("--catalog") || options.ContainsKey("--baseline-economy")&&!options.ContainsKey("--source"))
                throw new ArgumentException("Balance/output required; source+catalog must be paired and are required for profile export.");
            string output=Path.GetFullPath(options["--output"]);
            if(Directory.Exists(output)&&Directory.EnumerateFileSystemEntries(output).Any())throw new IOException("Output must be new/empty; existing results were not changed.");
            Directory.CreateDirectory(output);
            var read=VNextBalanceReader.Load(options["--validate-vnext-balance"]);
            var errors=read.Errors.ToBuilder();
            VNextBalanceAnalysis? analysis=null; bool verified=false;string? catalogSha=null;EconomyProfile? profile=null;
            if(read.Balance is { } b)
            {
                analysis=VNextBalanceAnalyzer.Analyze(b);errors.AddRange(analysis.Errors);
                if(options.TryGetValue("--source",out var source))
                {
                    var catalog=VNextCatalogReader.Load(options["--catalog"]);errors.AddRange(catalog.Diagnostics);catalogSha=catalog.Sha256;
                    if(catalog.Catalog is { } c) {var provenance=VNextBalanceReader.VerifyProvenance(b,VNextBalanceReader.ReadBounded(source),c);errors.AddRange(provenance);verified=provenance.IsEmpty;}
                }
                if(errors.Count==0 && options.TryGetValue("--baseline-economy",out var baseline))
                {
                    var bytes=VNextBalanceReader.ReadBounded(baseline);
                    profile=VNextSupportedProfile.Create(b,EconomyProfile.Read(System.Text.Encoding.UTF8.GetString(bytes)),read.Sha256!,catalogSha!,VNextBalanceReader.Digest(bytes));
                }
            }
            bool valid=errors.Count==0;
            var report=new {
                status=valid?"succeeded":"failed",scope="semantic", balanced=(bool?)null,
                formatVersion=read.Balance?.FormatVersion,balanceId=read.Balance?.Id,
                sha256=read.Sha256,sourceSha256=read.Balance?.SourceSha256,catalogSha256=catalogSha,provenanceVerified=verified,
                provenanceScope=verified?"exact source sections and A1 subset verified":"declared only; use --source and --catalog to verify exact provenance",
                counts=read.Balance is {} data?new {items=data.Items.Length,recipes=data.Recipes.Length,industries=data.Industries.Length,sections=15}:null,
                coverage=analysis?.Coverage??VNextBalanceAnalyzer.Coverage(),errors=errors.ToImmutable(),warnings=analysis?.Warnings,
                construction=analysis?.Construction,batches=analysis?.Batches,bootstrap=analysis?.Bootstrap,startup=analysis?.Startup,
                profileExported=profile is not null,profileSha256=profile is not null?VNextBalanceReader.Digest(VNextSupportedProfile.Serialize(profile)):null
            };
            var json=new JsonSerializerOptions {PropertyNamingPolicy=JsonNamingPolicy.CamelCase,WriteIndented=true};
            void Write(string name,byte[] bytes)
            {
                string path=Path.Combine(output,".balance-"+Guid.NewGuid().ToString("N")+".tmp");temporary.Add(path);
                using(var f=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))f.Write(bytes);
                File.Move(path,Path.Combine(output,name),overwrite:false);temporary.Remove(path);
            }
            if(profile is not null)Write("economy.b1-supported.json",VNextSupportedProfile.Serialize(profile));
            Write("balance-validation.json",JsonSerializer.SerializeToUtf8Bytes(report,json));
            if(valid)Console.WriteLine("B1 static semantic checks passed; this is not dynamic balance acceptance.");
            else foreach(var d in errors)Console.Error.WriteLine($"{d.Code} {d.Path}: {d.Message}");
            return valid?0:2;
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or OverflowException or JsonException)
        {Console.Error.WriteLine("Balance validation: "+e.Message);return 2;}
        finally {foreach(string p in temporary)try{File.Delete(p);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
}
