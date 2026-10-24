using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class VNextBalanceTests
{
    private static byte[] Bytes(string name)
    {using var s=typeof(VNextBalanceTests).Assembly.GetManifestResourceStream("Fixture."+name)!;using var m=new MemoryStream();s.CopyTo(m);return m.ToArray();}
    private static JsonNode Full()=>JsonNode.Parse(Bytes("vnext-balance.json"))!;
    private static VNextBalanceRead Read(JsonNode n)=>VNextBalanceReader.Read(Encoding.UTF8.GetBytes(n.ToJsonString()));
    private static VNextBalance Balance()=>VNextBalanceReader.Read(Bytes("vnext-balance.json")).Balance!;
    private static VNextBalanceAnalysis Analyze(JsonNode n) {var read=Read(n);Assert.True(read.IsValid,string.Join(";",read.Errors));return VNextBalanceAnalyzer.Analyze(read.Balance!);}
    private static void Error(JsonNode n,string code) {var a=Analyze(n);Assert.False(a.IsValid);Assert.Contains(a.Errors,e=>e.Code==code);}
    private static EconomyProfile Profile()=>EconomyProfile.Read(Encoding.UTF8.GetString(Bytes("vnext-supported.json")));
    private static ScenarioDefinition Scenario()=>Profile().Apply(JsonSerializer.Deserialize<ScenarioDefinition>(Bytes("vnext-production.json"),SimulationJson.Options)!);
    public static TheoryData<string> Sections=>new("source_metadata","rules","vehicles","enterprise_tiers","warehouses","recipe_versions","resources","additional_workshop_outfits","workshop_recipe_modernization","loading_platforms","vehicle_handling_kits","road_projects","economy_globals");
    private static JsonObject Entry(JsonNode root,string section)
    {
        var n=root[section]!;
        if(n is JsonArray a)return a[0]!.AsObject();
        if(section is "resources" or "additional_workshop_outfits")return n.AsObject().First().Value!.AsObject();
        return n.AsObject();
    }
    [Fact]
    public void FullB1HasAllFifteenTypedSectionsAndIndependentKnownValues()
    {
        var read=VNextBalanceReader.Read(Bytes("vnext-balance.json"));Assert.True(read.IsValid);var b=read.Balance!;
        Assert.Equal(34,b.Items.Length);Assert.Equal(23,b.Recipes.Length);Assert.Equal(6,b.Industries.Length);Assert.Equal(5,b.Vehicles.Length);Assert.Equal(8,b.Resources.Count);
        Assert.Equal(100,b.Rules.MoneyMinorPerCoin);Assert.Equal(80,b.Rules.BaseSpeedUnitsPerCellTick);
        Assert.Equal(new long[]{0,2500,4500,6500,8500,10000},b.Rules.StaffRateBp.ToArray());
        Assert.Equal(900000,b.EnterpriseTiers.Single(t=>t.Level==5).PriceMinor);
        Assert.Equal(60000,b.Warehouses.Single(t=>t.Level==3).PriceMinor);Assert.Equal(35000,b.AdditionalWorkshopOutfits["machinery"].PriceMinor);
        Assert.Equal(15000,b.EconomyGlobals.ResourceCellPriceMinor);Assert.Equal(1500,b.EconomyGlobals.ResourceLeasePriceMinor);
        Assert.Equal(2400,b.EconomyGlobals.BootstrapAsphaltRefreshTicks);
        Assert.Equal(2,b.RecipeVersions.Single(v=>v.Version==2).WorkMultiplierDen);Assert.False(b.Vehicles.Single(v=>v.Id=="trakt_d1").DeliveryServiceAllowed);
        Assert.Same(StringComparer.Ordinal,b.Resources.KeyComparer);Assert.Same(StringComparer.Ordinal,b.EnterpriseTiers[1].Materials.KeyComparer);
        var catalog=VNextCatalogReader.Read(Bytes("vnext-catalog.json")).Catalog!;
        Assert.Empty(VNextBalanceReader.VerifyProvenance(b,Bytes("vnext-design.json"),catalog));
        var round=VNextBalanceReader.Read(b.ToUtf8());Assert.True(round.IsValid);Assert.Equal(b.ToUtf8(),round.Balance!.ToUtf8());
        Assert.Equal(VNextBalanceReader.Digest(Bytes("vnext-balance.json")),read.Sha256);
        Assert.Equal(VNextBalanceReader.Digest(b.ToUtf8()),round.Sha256); // Actual input bytes, not a semantic hash.
        Assert.Equal(15,VNextBalanceAnalyzer.Coverage().Length);
    }
    [Fact]
    public void EverySourceLeafHasAnIndependentExactDestinationAndCoinConversion()
    {
        var source=JsonNode.Parse(Bytes("vnext-design.json"))!;var full=Full();
        void Compare(JsonNode a,JsonNode z,string path)
        {
            if(a is JsonObject obj)
                foreach(var (key,value) in obj)
                {
                    string dest=key=="coins"?(path=="industries[]"?"construction_price_minor":"price_minor"):key switch{"resource_cell_coins"=>"resource_cell_price_minor","resource_lease_coins"=>"resource_lease_price_minor",_=>key};
                    Assert.True(z.AsObject().ContainsKey(dest),path+"."+key);
                    if(key=="coins" || key.EndsWith("_coins",StringComparison.Ordinal))Assert.Equal(value!.GetValue<long>()*100,z[dest]!.GetValue<long>());
                    else Compare(value!,z[dest]!,path+"."+key);
                }
            else if(a is JsonArray arr)
            {Assert.Equal(arr.Count,z.AsArray().Count);for(int i=0;i<arr.Count;i++)Compare(arr[i]!,z[i]!,path.EndsWith("industries",StringComparison.Ordinal)?"industries[]":path+"[]");}
            else Assert.True(JsonNode.DeepEquals(a,z),path);
        }
        foreach(var (key,value) in source.AsObject())
        {
            if(key is "schema_version" or "status" or "date")Assert.True(JsonNode.DeepEquals(value,full["source_metadata"]![key]));
            else Compare(value!,full[key]!,key);
        }
        Assert.Equal(18,source.AsObject().Count);Assert.Equal(20,full.AsObject().Count);
        foreach(var v in full["vehicles"]!.AsArray())if(v!["id"]!.GetValue<string>()!="trakt_d1")Assert.True(v["delivery_service_allowed"]!.GetValue<bool>());
        Assert.Empty(full["enterprise_tiers"]![0]!["materials"]!.AsObject());
    }
    [Fact]
    public void EditedProjectionCannotRetainStaleProvenanceAndFixedCompositionIsExplicit()
    {
        var profile=Profile();var edited=profile with {World=profile.World with {StartingCashMinor=1}};
        Assert.Throws<ArgumentException>(()=>EconomyProfile.Read(Encoding.UTF8.GetString(VNextSupportedProfile.Serialize(edited))));
        var d=JsonSerializer.Deserialize<ScenarioDefinition>(Bytes("vnext-production.json"),SimulationJson.Options)!;
        Assert.Throws<ArgumentException>(()=>profile.Apply(d with {Participants=d.Participants[..3]}));
    }
    [Theory] [MemberData(nameof(Sections))]
    public void EveryNewSectionRejectsUnknownMissingNullAndDecodedDuplicateFields(string section)
    {
        foreach(string mode in new[]{"unknown","missing","null","duplicate"})
        {
            var n=Full();var entry=Entry(n,section);string key=entry.First().Key;
            if(mode=="unknown")entry["typo"]=1;
            if(mode=="missing")entry.Remove(key);
            if(mode=="null")entry[key]=null;
            string json=n.ToJsonString();
            if(mode=="duplicate") {string original=entry.ToJsonString();string encoded="\\u"+((int)key[0]).ToString("x4")+key[1..];json=json.Replace(original,original.Insert(1,"\""+encoded+"\":"+entry[key]!.ToJsonString()+","),StringComparison.Ordinal);}
            var result=VNextBalanceReader.Read(Encoding.UTF8.GetBytes(json));Assert.False(result.IsValid);
            Assert.Equal(mode switch{"null"=>"NullValue","duplicate"=>"DuplicateKey",_=>"InvalidJson"},Assert.Single(result.Errors).Code);
            Assert.Equal(result.Errors.ToArray(),VNextBalanceReader.Read(Encoding.UTF8.GetBytes(json)).Errors.ToArray());
        }
    }
    [Theory] [InlineData("1.0")] [InlineData("1e0")] [InlineData("-1")] [InlineData("9223372036854775808")] [InlineData("\"1\"")]
    public void NewScalarsRejectCoercionFractionsAndRange(string value)
    {
        var n=Full();n["recipe_versions"]![1]!["work_multiplier_den"]=JsonNode.Parse(value);
        Assert.False(Read(n).IsValid);
    }
    [Theory]
    [InlineData("vehicles","speed")] [InlineData("enterprise_tiers","input_capacity")]
    [InlineData("warehouses","capacity")] [InlineData("recipe_versions","work_multiplier_den")]
    [InlineData("resources","regen_one_every_ticks")] [InlineData("additional_workshop_outfits","price_minor")]
    [InlineData("workshop_recipe_modernization","ticks")] [InlineData("loading_platforms","throughput")]
    [InlineData("vehicle_handling_kits","throughput")] [InlineData("road_projects","ticks")]
    public void NewSectionsRejectZeroInPositiveDomains(string section,string field)
    {var n=Full();Entry(n,section)[field]=0;Assert.Contains(Read(n).Errors,e=>e.Code=="OutOfRange");}
    [Theory] [InlineData("warehouses")] [InlineData("enterprise_tiers")] [InlineData("recipe_versions")] [InlineData("vehicles")] [InlineData("vehicle_handling_kits")]
    public void DuplicateCategoryIdsAreRejected(string section)
    {var n=Full();n[section]![1]=n[section]![0]!.DeepClone();Assert.Contains(Read(n).Errors,e=>e.Code=="DuplicateId");}
    [Fact]
    public void AllMaterialAndResourceReferencesUseActualIds()
    {
        foreach(string section in new[]{"enterprise_tiers","warehouses","additional_workshop_outfits","workshop_recipe_modernization","loading_platforms","vehicle_handling_kits","road_projects"})
        {var n=Full();Entry(n,section)["materials"]!["missing"]=1;Assert.Contains(Read(n).Errors,e=>e.Code=="UnknownReference");}
        var r=Full();r["resources"]!["missing"]=r["resources"]!["wood"]!.DeepClone();Assert.Contains(Read(r).Errors,e=>e.Code=="UnknownReference");
    }
    [Theory] [InlineData("unsupported")][InlineData("")]
    public void UnknownFullFormatDoesNotBecomeA1OrRuntime(string format)
    {var n=Full();n["format_version"]=format;Assert.Contains(Read(n).Errors,e=>e.Code=="UnsupportedVersion");}
    [Fact]
    public void InputIsBoundedAndExactBytesIncludeBomAndWhitespace()
    {
        var raw=Bytes("vnext-balance.json");var bom=new byte[]{0xef,0xbb,0xbf}.Concat(raw).ToArray();var read=VNextBalanceReader.Read(bom);
        Assert.True(read.IsValid);Assert.Equal(VNextBalanceReader.Digest(bom),read.Sha256);
        Assert.NotEqual(VNextBalanceReader.Read(raw).Sha256,read.Sha256);
        Assert.Equal("TooLarge",Assert.Single(VNextBalanceReader.Read(new byte[1024*1024+1]).Errors).Code);
        string folder=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try{string path=Path.Combine(folder,"huge");using(var f=File.Create(path))f.SetLength(100L*1024*1024);var large=VNextBalanceReader.Load(path);Assert.Null(large.Sha256);Assert.Equal("TooLarge",Assert.Single(large.Errors).Code);Assert.Equal("IoError",Assert.Single(VNextBalanceReader.Load(Path.Combine(folder,"absent")).Errors).Code);}
        finally{Directory.Delete(folder,true);}
        byte[] deep=Encoding.UTF8.GetBytes(new string('[',33)+"0"+new string(']',33));Assert.False(VNextBalanceReader.Read(deep).IsValid);
    }
    [Fact]
    public void StaticB1ChecksReportExactVolumesAllVersionsAndCostsWithoutClaimingBalance()
    {
        var a=VNextBalanceAnalyzer.Analyze(Balance());Assert.True(a.IsValid,string.Join(";",a.Errors));Assert.Equal(new long[]{63,66,64,63,54,46},a.Construction.Select(c=>c.MaterialVolume).ToArray());
        Assert.Equal(69,a.Batches.Length);var v=a.Batches.Single(x=>x.RecipeId=="saw_planks"&&x.Version==1);
        Assert.Equal(4,v.InputVolume);Assert.Equal(16,v.WorkTicks);Assert.Equal(1000,v.InputValueMinor);Assert.Equal(3600,v.OutputValueMinor);Assert.Equal(1600,v.LaborMinor);Assert.Equal(32,v.ServiceMinor);Assert.Equal(968,v.DirectMarginMinor);
        var v2=a.Batches.Single(x=>x.RecipeId=="saw_planks"&&x.Version==2);Assert.Equal(16,v2.InputVolume);Assert.Equal(20,v2.OutputVolume);Assert.Equal(72,v2.WorkTicks);
        Assert.True(a.Bootstrap!.AllRequiredReachable);Assert.Equal(30,a.Bootstrap.Witness.Length);
        Assert.Equal(9,a.Bootstrap.Witness[0].FiniteSeedRemaining["steel"]);Assert.Equal(3,a.Bootstrap.Witness[1].FiniteSeedRemaining["steel"]);
        Assert.Contains(a.Warnings,w=>w.Code=="StaticOnly");Assert.Contains(a.Warnings,w=>w.Code=="RuntimeSubset");
    }
    [Fact]
    public void BadTierCapacityStaffWarehouseAndVersionSettingsFailSemantics()
    {
        var n=Full();n["enterprise_tiers"]![2]!["xp"]=500;Error(n,"TierMonotonicity");
        n=Full();n["enterprise_tiers"]![1]!["materials"]!["plank"]=100;Error(n,"ExpansionCapacity");
        n=Full();n["industries"]![0]!["materials"]!["wood"]=40;Error(n,"InitialMaterialsCapacity");
        n=Full();n["warehouses"]![1]!["capacity"]=301;Error(n,"WarehouseSequence");Error(n,"ConflictingSetting");
        n=Full();n["rules"]!["staff_rate_bp"]![2]=10001;Error(n,"StaffCurve");
        n=Full();n["recipe_versions"]![1]!["output_multiplier"]=1000;Error(n,"BatchCapacity");
        n=Full();n["recipe_versions"]![1]!["unlock_level"]=2;Error(n,"VersionUnlock");
    }
    [Fact]
    public void ExactRationalWorkUsesCeilingAndRejectsOverflowWithoutRoundingMoney()
    {
        var n=Full();n["recipes"]![0]!["work_ticks_full_staff"]=17;var a=Analyze(n);Assert.True(a.IsValid);Assert.Equal(77,a.Batches.Single(x=>x.RecipeId=="saw_planks"&&x.Version==2).WorkTicks);
        n=Full();n["recipe_versions"]![1]!["work_multiplier_num"]=long.MaxValue;Error(n,"Overflow");
        n=Full();n["warehouses"]![0]!["materials"]!["wood"]=long.MaxValue;Assert.Contains(Read(n).Errors,e=>e.Code=="Overflow");
        n=Full();n["economy_globals"]!["start_treasury_minor_per_4_participants"]=long.MaxValue;Error(n,"Overflow");
        n=Full();n["economy_globals"]!["bootstrap_steel"]=long.MaxValue;Error(n,"Overflow");
        n=Full();n["items"]!.AsArray().Single(i=>i!["id"]!.GetValue<string>()=="motor")!["xp_per_unit"]=long.MaxValue;Error(n,"Overflow");
    }
    [Fact]
    public void LowMarginIsAWarningAndNeverSilentlyChangesData()
    {
        var n=Full();var i=n["items"]!.AsArray().Single(x=>x!["id"]!.GetValue<string>()=="plank")!;i["reference_price_minor"]=1;
        var a=Analyze(n);Assert.True(a.IsValid);Assert.Contains(a.Warnings,w=>w.Code=="NonpositiveDirectMargin");Assert.Equal(-2628,a.Batches.Single(x=>x.RecipeId=="saw_planks"&&x.Version==1).DirectMarginMinor);
    }
    [Fact]
    public void FiniteBootstrapCannotBeSpentTwiceOrInventedAsPermanentImports()
    {
        var n=Full();n["economy_globals"]!["bootstrap_steel"]=0;Error(n,"UnreachableDependencies");
        n=Full();n["economy_globals"]!["bootstrap_steel"]=5;Error(n,"UnreachableDependencies");
        n=Full();n["economy_globals"]!["bootstrap_steel"]=6;var a=Analyze(n);Assert.True(a.IsValid);Assert.Equal("metallurgy",a.Bootstrap!.Witness[0].IndustryId); // Must avoid spending three on sawmill first.
    }
    [Fact]
    public void ClosedRecipeCycleFailsWhileGroundedCycleHasAnExternalEntry()
    {
        var n=Full();n["recipes"]![0]!["inputs"]=JsonNode.Parse("{\"plank\":1}");Error(n,"UnreachableDependencies");
        n=Full();var extra=n["recipes"]![0]!.DeepClone();extra["id"]="recycle_plank";extra["inputs"]=JsonNode.Parse("{\"plank\":1}");extra["output"]="wood";n["recipes"]!.AsArray().Add(extra);
        Assert.True(Analyze(n).IsValid); // Real wood source and ordinary wood->plank recipe ground the SCC.
    }
    [Fact]
    public void SmallFixtureDoesNotRequireTheShippedItemIndustryRecipeCounts()
    {
        var n=Full();n["industries"]=new JsonArray(n["industries"]![0]!.DeepClone());n["recipes"]=new JsonArray(n["recipes"]![0]!.DeepClone());
        n["additional_workshop_outfits"]=JsonNode.Parse("{\"woodworking\":{\"price_minor\":1,\"materials\":{\"wood\":1}}}");
        n["items"]=new JsonArray(n["items"]!.AsArray().Where(i=>new[]{"wood","plank","steel","glass","asphalt"}.Contains(i!["id"]!.GetValue<string>())).Select(i=>i!.DeepClone()).ToArray());
        foreach(var item in n["items"]!.AsArray())if(item!["id"]!.GetValue<string>() is not ("wood" or "plank")) {item["source"]="import";item["xp_per_unit"]=0;}
        n["resources"]=new JsonObject{["wood"]=n["resources"]!["wood"]!.DeepClone()};
        foreach(string section in new[]{"enterprise_tiers","warehouses","workshop_recipe_modernization","loading_platforms","vehicle_handling_kits","road_projects"})
            foreach(var entry in n[section]!.AsArray())if(entry!.AsObject().ContainsKey("materials") && entry["materials"]!.AsObject().Count>0)entry["materials"]=JsonNode.Parse("{\"wood\":1}");
        Assert.Equal(5,n["items"]!.AsArray().Count);
        var a=Analyze(n);Assert.True(a.IsValid,string.Join(";",a.Errors));Assert.Equal(3,a.Batches.Length);Assert.Equal(5,a.Bootstrap!.Witness.Length);
    }
    [Fact]
    public void ProvenanceRejectsStaleRawShaSubsetConflictAndChangesInFutureSections()
    {
        var b=Balance();var c=VNextCatalogReader.Read(Bytes("vnext-catalog.json")).Catalog!;var raw=Bytes("vnext-design.json");
        Assert.Contains(VNextBalanceReader.VerifyProvenance(b,raw.Concat(new byte[]{32}).ToArray(),c),e=>e.Code=="StaleSource");
        Assert.Contains(VNextBalanceReader.VerifyProvenance(b,raw,c with {Items=c.Items.SetItem(0,c.Items[0] with {ReferencePriceMinor=999})}),e=>e.Code=="SubsetConflict");
        var modified=b with {EconomyGlobals=b.EconomyGlobals with {ImportRefreshTicks=777}};
        Assert.Contains(VNextBalanceReader.VerifyProvenance(modified,raw,c),e=>e.Code=="SourceConflict" && e.Path=="$.economy_globals");
    }
    [Fact]
    public void SupportedProfileIsAnExplicitProjectionAndEveryActiveValueIsSaved()
    {
        var b=Balance();var profile=Profile();Assert.Equal(VNextSupportedProfile.ProfileId,profile.Id);
        var expected=VNextSupportedProfile.Create(b,EconomyProfile.Read(Encoding.UTF8.GetString(Bytes("economy.json"))),VNextBalanceReader.Digest(Bytes("vnext-balance.json")),VNextBalanceReader.Digest(Bytes("vnext-catalog.json")),VNextBalanceReader.Digest(Bytes("economy.json")));
        Assert.Equal(VNextSupportedProfile.Serialize(expected),Bytes("vnext-supported.json"));
        Assert.Equal(34,profile.World.Items.Length);Assert.Equal(23,profile.World.Recipes.Length);Assert.Equal(8,profile.World.Resources!.Length);
        Assert.Equal(new long[]{100,100,120,120,80,160,160,120},profile.World.Resources.Select(r=>r.Private.Capacity).ToArray());
        Assert.Equal(new[]{"beet","copper_ore","grain","iron_ore","milk","sand","stone","wood"},profile.World.Resources.Select(r=>r.ItemId).ToArray());
        Assert.DoesNotContain(profile.World.BuildingTypes,t=>t.Kind==BuildingKind.Production);
        Assert.Equal("presentation-economy-v6",EconomyProfile.Read(Encoding.UTF8.GetString(Bytes("economy.json"))).Id);
        var s=new GameSession(new(Scenario()));Assert.Equal(s.Engine.ConfigHash,CheckpointStore.Decode(CheckpointStore.Encode(s)).Engine.ConfigHash);
        Assert.Equal(10,profile.World.CapitalDemand.DemandDenominator);Assert.NotNull(profile.VNext);
        var unsupported=b with {Resources=b.Resources.SetItem("wood",b.Resources["wood"] with {ManualTicks=3})};
        Assert.Throws<ArgumentException>(()=>VNextSupportedProfile.Create(unsupported,profile,"x","y","z"));
    }
    [Fact]
    public void RealExecutorCompletesNewBatchOnceAndWipLoadReplayMatchesEveryTick()
    {
        var d=Scenario();var s=new GameSession(new(d));ScriptedScenarios.VNextSupportedProduction(s,d);
        Assert.Equal(50,s.Engine.Tick);Assert.Equal(17960,s.Engine.Observe(new(1)).Account.BalanceMinor);
        Assert.Equal(1,s.Engine.Observe(new(1)).OwnEnterprises.Single().CompletedBatches);Assert.Equal(5,s.Engine.Inspect().Flows.Single(f=>f.ItemId=="gravel").Produced);
        var playback=new GameSession(new(d));playback.ConfigurePlayback(s.CommandTape,50);playback.AdvanceTo(18);
        var e=playback.Engine.Observe(new(1)).OwnEnterprises.Single();Assert.True(e.WorkDone>0 && e.WorkDone<e.WorkRequired);Assert.Equal("crush_stone",e.BatchRecipeId);Assert.Equal(3,Assert.Single(e.BatchInputs).Quantity);
        var copy=CheckpointStore.Decode(CheckpointStore.Encode(playback));
        while(playback.Engine.Tick<50) {playback.Step();copy.Step();Assert.Equal(playback.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());copy.Engine.ValidateInvariants();}
        Assert.Equal(s.Engine.ComputeStateHash(),copy.Engine.ComputeStateHash());Assert.Equal(s.Engine.ComputeStateHash(),GameSession.Replay(d,s.CommandTape,50).Engine.ComputeStateHash());
        Assert.Single(copy.Engine.EventsSince(0),e=>e.Type=="ProductionCompleted");
        Assert.Equal(2000,copy.Engine.Ledger().Where(l=>l.Reason=="Wages").Sum(l=>l.AmountMinor));Assert.Equal(40,copy.Engine.Ledger().Where(l=>l.Reason=="Energy").Sum(l=>l.AmountMinor));
    }
    [Fact]
    public void ZeroCashRecoveryRequiresNoFactoryShopOrFutureBootstrapInventory()
    {
        var d=Scenario() with {Enterprises=[],CapitalDemand=Profile().World.CapitalDemand with {Terminal=new(1,1)},Participants=Scenario().Participants.Select(p=>p with {CashMinor=0}).ToArray()};
        var s=new GameSession(new(d));string harvest=Fixtures.Command(s,new(ActionKind.Harvest,ItemId:"stone",Quantity:1));Assert.Equal(ResultStatus.Accepted,s.Engine.Result(harvest)!.Status);Fixtures.UntilIdle(s);
        var offer=s.Engine.Observe(new(1)).CapitalOffers.First(o=>o.ItemId=="stone" && o.RemainingQuantity>0);
        string sell=Fixtures.Command(s,new(ActionKind.SellAvailableToCapital,ItemId:"stone",Quantity:1,OfferId:offer.Id));Assert.Equal(ResultStatus.Accepted,s.Engine.Result(sell)!.Status);Fixtures.UntilIdle(s);
        Assert.True(s.Engine.Observe(new(1)).Account.BalanceMinor>0);s.Engine.ValidateInvariants();
        Assert.Equal(s.Engine.ComputeStateHash(),GameSession.Replay(d,s.CommandTape,s.Engine.Tick).Engine.ComputeStateHash());
    }
}
