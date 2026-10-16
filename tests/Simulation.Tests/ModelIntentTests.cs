using System.Text.Json.Nodes;
using Agents.Llm;
using Xunit;

namespace Simulation.Tests;

public class ModelIntentTests
{
    [Fact]
    public void ExactIdentityAndListedImmutablePlanAreRequired()
    {
        var r = ModelTestFixtures.Request();
        Assert.True(StrictIntentParser.TryParse(ModelTestFixtures.Intent(r), r, out var intent, out _));
        Assert.Equal(r.Options[0].Id, intent!.OptionId);
    }
    [Theory]
    [InlineData("actorId")] [InlineData("observationVersion")] [InlineData("sessionEpoch")]
    [InlineData("decisionId")] [InlineData("optionId")]
    public void ForgedForeignOrStaleIdentityAndUnlistedPlanAreRejected(string key)
    {
        var r = ModelTestFixtures.Request(); var json = JsonNode.Parse(ModelTestFixtures.Intent(r))!;
        json[key] = key.EndsWith("Id", StringComparison.Ordinal) && key != "actorId" ? JsonValue.Create("forged") : JsonValue.Create(999);
        Assert.False(StrictIntentParser.TryParse(json.ToJsonString(), r, out _, out _));
    }
    [Theory]
    [InlineData("{\"quantity\":-1}")] [InlineData("[]")] [InlineData("null")]
    [InlineData("```json\n{}\n```")] [InlineData("{\"intent\":\"MutationPlan\"}")]
    public void MalformedRefusalOrPrivilegedPayloadNeverBecomesACommand(string text)
        => Assert.False(StrictIntentParser.TryParse(text, ModelTestFixtures.Request(), out _, out _));
    [Theory]
    [InlineData("quantity")] [InlineData("maximumPriceMinor")] [InlineData("actor")]
    public void ExtraFieldsCannotEditTrustedPricesQuantitiesOrAuthentication(string field)
    {
        var r = ModelTestFixtures.Request(); var json = JsonNode.Parse(ModelTestFixtures.Intent(r))!; json[field] = -1;
        Assert.False(StrictIntentParser.TryParse(json.ToJsonString(), r, out _, out _));
    }
    [Fact]
    public void DuplicateFieldsFloatingIntegersOversizedAndControlTextFailClosed()
    {
        var r = ModelTestFixtures.Request(); string valid = ModelTestFixtures.Intent(r);
        Assert.False(StrictIntentParser.TryParse(valid[..^1] + ",\"actorId\":1}", r, out _, out _));
        Assert.False(StrictIntentParser.TryParse(valid.Replace("\"actorId\":1", "\"actorId\":1.5", StringComparison.Ordinal), r, out _, out _));
        var json = JsonNode.Parse(valid)!; json["reasonSummary"] = new string('a', 257);
        Assert.False(StrictIntentParser.TryParse(json.ToJsonString(), r, out _, out _));
        json["reasonSummary"] = "secret\u0000text";
        Assert.False(StrictIntentParser.TryParse(json.ToJsonString(), r, out _, out _));
        Assert.False(StrictIntentParser.TryParse(new string(' ', 5000) + valid, r, out _, out _));
    }
}
