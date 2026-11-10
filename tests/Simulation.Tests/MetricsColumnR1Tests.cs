using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Simulation.Core;
using Simulation.Hosting;
using Xunit;

namespace Simulation.Tests;

public class MetricsColumnR1Tests
{
    [Fact]
    public void TreasuryColumnIsTheTreasuryAccountAndKeepsTheHistoricalCheckpointName()
    {
        var session = Fixtures.Session();
        session.Step();
        var state = session.Engine.Inspect();
        long treasury = state.Accounts.Single(account => account.Kind == "Treasury").BalanceMinor;
        long wages = state.Accounts.Single(account => account.Kind == "WagesSink").BalanceMinor;
        long cash = state.Accounts.Single(account => account.Id == state.Participants[0].AccountId).BalanceMinor;
        var sample = session.Samples.Single();
        Assert.Equal(treasury, sample.TreasuryBalanceMinor);
        string saved = JsonSerializer.Serialize(sample, SimulationJson.Options);
        Assert.Contains("\"energyExpenseMinor\":", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("treasuryBalanceMinor", saved, StringComparison.Ordinal);
        string directory = Path.Combine(Path.GetTempPath(), "delsk-metrics-" + Guid.NewGuid().ToString("N"));
        try
        {
            RunArtifacts.Export(session, session.Engine.Configuration(), directory, "headless", "cycle", 1);
            string[] lines = File.ReadAllLines(Path.Combine(directory, "metrics.csv"));
            Assert.Equal("tick,participant_cash_minor,wages_minor,treasury_minor,harvested,produced,exported,state_hash,population,unemployed", lines[0]);
            string[] cells = lines[1].Split(',');
            Assert.Equal(cash.ToString(CultureInfo.InvariantCulture), cells[1]);
            Assert.Equal(wages.ToString(CultureInfo.InvariantCulture), cells[2]);
            Assert.Equal(treasury.ToString(CultureInfo.InvariantCulture), cells[3]);
            Assert.Equal(state.Population.ToString(CultureInfo.InvariantCulture), cells[8]);
            Assert.Equal(state.Unemployed.ToString(CultureInfo.InvariantCulture), cells[9]);
            var format = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "metrics-format.json")))!;
            Assert.Equal(1, format["version"]!.GetValue<int>());
            Assert.Equal("treasury_minor", format["treasuryColumn"]!.GetValue<string>());
            Assert.Contains("Treasury", format["treasuryMeaning"]!.GetValue<string>(), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
