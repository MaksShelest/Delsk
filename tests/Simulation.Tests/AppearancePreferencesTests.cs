using System.Text.Json;
using Simulation.Hosting;
using Xunit;
namespace Simulation.Tests;
public class AppearancePreferencesTests
{
    [Fact] public void FirstLaunchUsesAdditionalSetAndTenRealNames()
    {
        var p=new AppearancePreferences();
        Assert.All(AppearancePreferences.ModelKeys,k=>Assert.Equal(AppearancePreferences.Cozy,p.ForModel(k)));
        Assert.Equal(AppearancePreferences.Cozy,p.PeopleSet);
        Assert.Equal(10,AppearanceCatalog.People.Length);
        Assert.Equal(10,AppearanceCatalog.People.Select(p=>p.RussianName).Distinct().Count());
        Assert.Equal("Алина",AppearanceCatalog.Name(0,"ru"));
        Assert.Equal("Михаил",AppearanceCatalog.Name(1,"ru"));
        Assert.Equal("Lin Mei",AppearanceCatalog.Name(3,"en"));
    }
    [Fact] public void PerModelAndActorOverridesRoundTripWithoutMutatingPreviousSelection()
    {
        var original=new AppearancePreferences();
        var changed=original.WithModel("capital",AppearancePreferences.Classic).WithActor(2,9);
        var loaded=JsonSerializer.Deserialize<AppearancePreferences>(JsonSerializer.Serialize(changed))!.Normalize();
        Assert.Equal(AppearancePreferences.Classic,loaded.ForModel("capital"));
        Assert.Equal(AppearancePreferences.Cozy,loaded.ForModel("bakery"));
        Assert.Equal(9,loaded.ForActor(2)); Assert.Equal(1,original.ForActor(2));
        Assert.Equal(AppearancePreferences.Cozy,original.ForModel("capital"));
    }
    [Fact] public void InvalidPreferencesFallBackToValidDefaults()
    {
        var p=new AppearancePreferences { WorldSet="missing",PeopleSet="",ModelSets=new(){{"capital","missing"},{"unknown","classic"}},ActorSkins=new(){{0,1},{1,10},{2,-1},{3,4}} }.Normalize();
        Assert.Equal("cozy",p.WorldSet);Assert.Equal("cozy",p.PeopleSet);
        Assert.Empty(p.ModelSets);Assert.Single(p.ActorSkins);Assert.Equal(4,p.ForActor(3));
        Assert.Throws<ArgumentException>(()=>p.WithModel("unknown","classic"));
        Assert.Throws<ArgumentOutOfRangeException>(()=>p.WithActor(1,10));
    }
    [Theory]
    [InlineData("wood",100,100,false,"Forest_Rich_Full")]
    [InlineData("wood",49,100,false,"Forest_Rich_Young")]
    [InlineData("wood",50,100,true,"Forest_Sparse_Full")]
    [InlineData("wood",0,100,true,"Forest_Sparse_Cut")]
    [InlineData("grain",0,100,false,"EmptyField")]
    [InlineData("beet",1,100,false,"BeetField")]
    [InlineData("milk",1,100,true,"Pasture_1Cow")]
    [InlineData("milk",1,100,false,"Pasture_2Cow")]
    public void ResourceModelsFollowPublicStockAndDensity(string item,long stock,long capacity,bool sparse,string model)
        =>Assert.Equal(model,AppearanceCatalog.ResourceModel(item,stock,capacity,sparse));
}
