using System.Globalization;
using Simulation.Core;
using Simulation.Hosting;

internal sealed record ResearchElectionPolicy(bool AlgorithmicHuman, string Ballot, bool ContinueAfterVictory)
{
    public static ResearchElectionPolicy? Read(Dictionary<string, string> options, string script)
    {
        if (!options.ContainsKey("--research-human") && !options.ContainsKey("--research-ballot") &&
            !options.ContainsKey("--research-continue-after-victory")) return null;
        if ((!options.ContainsKey("--new-game") || script != "baseline") && !options.ContainsKey("--load") ||
            options.ContainsKey("--replay") || options.ContainsKey("--human-ballot") || options.GetValueOrDefault("--profile") == "true")
            throw new ArgumentException("Research election policy requires a live algorithmic ordinary experiment or its checkpoint");
        string human = options.GetValueOrDefault("--research-human", "ballot-only");
        string ballot = options.GetValueOrDefault("--research-ballot", "self");
        string continuation = options.GetValueOrDefault("--research-continue-after-victory", "false");
        if (human is not ("ballot-only" or "algorithmic") || !bool.TryParse(continuation, out bool carryOn) ||
            ballot is not ("self" or "off") && (!long.TryParse(ballot, NumberStyles.None, CultureInfo.InvariantCulture, out long candidate) || candidate < 1))
            throw new ArgumentException("Invalid research human policy, ballot or victory setting");
        return new(human == "algorithmic", ballot, carryOn);
    }

    public ParticipantId Voter(ScenarioDefinition definition)
    {
        if (definition.NewGame is not { } game) throw new ArgumentException("Research election policy requires an ordinary world");
        var voter = new ParticipantId(game.HumanActorId);
        if (Ballot != "off" && (definition.Elections?.PauseForBallots != true || !definition.Participants.Any(p => p.Id == Choice(voter).Value)))
            throw new ArgumentException("Research ballot requires an existing candidate and elections with a voting barrier");
        return voter;
    }

    private ParticipantId Choice(ParticipantId voter) => Ballot == "self" ? voter : new(long.Parse(Ballot, CultureInfo.InvariantCulture));

    public void ResolveBoundary(GameSession session, ParticipantId voter, long target)
    {
        if (session.Engine.WaitingForBallots)
        {
            if (Ballot != "off" && session.SubmitConfiguredBallot(voter, Choice(voter)) is { Status: ResultStatus.Rejected } rejection)
                throw new InvalidOperationException("Research ballot rejected: " + rejection.ReasonCode);
            if (!session.PumpVoting()) throw new InvalidOperationException("ElectionWaitingForHumanBallot: enable automatic voting or use an algorithmic human participant");
        }
        if (session.Engine.PoliticalMode != ElectionMode.Victory) return;
        if (ContinueAfterVictory) session.ContinueResearchAfterVictory(voter);
        else if (session.Engine.Tick < target) throw new InvalidOperationException("ElectionVictory: continuation is disabled for this experiment");
    }
}
