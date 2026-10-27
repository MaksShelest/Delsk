using System.Collections.Immutable;

namespace Simulation.Core;

public sealed record BillingRules(int InvoicePeriod=120,int GraceTicks=1000,int PenaltyPeriod=1000,
    int PenaltyBasisPoints=2000,int CityShareBasisPoints=1000,long LightIncrementMinor=1)
{
    public ImmutableSortedDictionary<string,int> ClassFactors {get;init;}=ImmutableSortedDictionary<string,int>.Empty;
    public void Validate()
    {
        if(ClassFactors.Count==0||ClassFactors.Any(x=>string.IsNullOrWhiteSpace(x.Key)||x.Value is <1 or >100000))throw new ArgumentException("Invalid vehicle toll classes");
        if(InvoicePeriod is <1 or >100000||GraceTicks is <1 or >1000000||PenaltyPeriod is <1 or >1000000||
            PenaltyBasisPoints is <1 or >10000||CityShareBasisPoints is <0 or >10000||LightIncrementMinor is <1 or >100000)
            throw new ArgumentException("Invalid billing rules");
    }
}
// Units = 1/800 cell * 1/10000 class * 1/10000 share of a minor unit. No floating point.
public sealed record RoadAccrual(ParticipantId Payer,ParticipantId Recipient,long Units);
public sealed record InvoiceClaim(ParticipantId Recipient,long PrincipalMinor,long PaidMinor=0);
public sealed record RoadInvoice(long Id,ParticipantId Payer,long CreatedTick,long DueTick,long NextPenaltyTick,
    ImmutableArray<InvoiceClaim> Claims,long PenaltyMinor=0,long PenaltyPaidMinor=0,long PenaltyRemainder=0,bool Viewed=false)
{
    public long PrincipalMinor=>Claims.Sum(c=>c.PrincipalMinor);
    public long PrincipalRemaining=>Claims.Sum(c=>c.PrincipalMinor-c.PaidMinor);
    public long PenaltyRemaining=>PenaltyMinor-PenaltyPaidMinor;
    public long Remaining=>checked(PrincipalRemaining+PenaltyRemaining);
}
public sealed record BillingPreference(ParticipantId Actor,bool AutoPay=true);
public sealed record BillingState(long ClearingAccount,ImmutableArray<RoadAccrual> Accruals,
    ImmutableArray<RoadInvoice> Invoices,ImmutableArray<BillingPreference> Preferences);
public sealed record AccountOperation(long Ordinal,long Tick,long AmountMinor,string Kind,ParticipantId? Counterparty,string CounterpartyKind);
public sealed record PersonalAccount(AccountView Account,bool AutoPay,ImmutableArray<RoadInvoice> Invoices,
    ImmutableArray<AccountOperation> Operations,long AccruedMinor,long ReceivableMinor,BillingRules? Rules,
    long DebtMinor,int UnreadCount,int InvoiceCount,int OperationCount,long IncomeMinor,long ExpensesMinor,long EscrowMinor,long Tick);
