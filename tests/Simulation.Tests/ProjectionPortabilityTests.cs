using System.Text;
using System.Text.Json;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class ProjectionPortabilityTests
{
    [Theory]
    [InlineData("vnext-supported.json")]
    [InlineData("economy.c1-storage.json")]
    [InlineData("economy.d1-workshops.json")]
    [InlineData("economy.d2-development.json")]
    [InlineData("economy.d3-production.json")]
    public void CheckedProfilesHaveTheSameCanonicalPayloadWithEitherInputLineEnding(string resource)
    {
        string lf=OrdinaryNewGameTests.Text(resource).Replace("\r\n","\n",StringComparison.Ordinal);
        var linux=EconomyProfile.Read(lf);var windows=EconomyProfile.Read(lf.Replace("\n","\r\n",StringComparison.Ordinal));
        var payload=linux with{VNext=null};byte[] canonical=VNextSupportedProfile.Serialize(payload);
        Assert.DoesNotContain("\r",Encoding.UTF8.GetString(canonical),StringComparison.Ordinal);
        Assert.Equal(linux.VNext!.ProjectionPayloadSha256,VNextBalanceReader.Digest(canonical));
        Assert.Equal(canonical,VNextSupportedProfile.Serialize(windows with{VNext=null}));
        byte[] platformDependent=JsonSerializer.SerializeToUtf8Bytes(payload,new JsonSerializerOptions(SimulationJson.Options){WriteIndented=true,NewLine="\r\n"});
        Assert.NotEqual(VNextBalanceReader.Digest(platformDependent),VNextBalanceReader.Digest(canonical));
    }
}
