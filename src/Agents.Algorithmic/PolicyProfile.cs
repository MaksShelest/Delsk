using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Simulation.Core;

namespace Agents.Algorithmic;

// Legacy is an explicit compatibility mode for historical fixtures, never a fourth menu difficulty.
public enum NpcDifficulty { Legacy, Easy, Medium, Hard }
public record PolicyModule(string Id,int Version);
public record PolicyProfile(int Version,NpcDifficulty Difficulty,ImmutableArray<PolicyModule> Modules,int ExperienceSchemaVersion);

public sealed partial class AlgorithmicController
{
    public const string ModularPolicyKind="utility-modular-v3";
    public PolicyProfile? Profile=>_state.Profile;
    private static PolicyProfile? ResolveProfile(NpcDifficulty difficulty)=>difficulty==NpcDifficulty.Legacy?null:
        new(1,difficulty,[new("executor",1),new("simple",1),..(difficulty>=NpcDifficulty.Medium?new PolicyModule[]{new("economics",2)}:[]),
            ..(difficulty==NpcDifficulty.Hard?new PolicyModule[]{new("planning",2)}:[])],1);
    private bool SimplePolicy=>_state.Settings.Difficulty==NpcDifficulty.Easy;
    private static bool SimpleGoal(Candidate c)=>c.Steps.Length<=8 && c.Goal is not
        ("BuyExtraction" or "AutomateExtraction" or "BuyLogistics" or "SelectRecipe" or "PostInputOffer" or "NegotiateInputs" or "BuySupport");
    private string Explain(Candidate c)=>Profile is null?c.Reason:$"{_state.Settings.Difficulty}:"+c.Reason;
}
