"""Measured, exhaustive xUnit partitions; no test is disabled or assigned by timing at runtime.
Author: Шелестов Максим Евгеньевич.
"""
from dataclasses import dataclass
from pathlib import Path
import xml.etree.ElementTree as ET

INTEGRATION = tuple('Simulation.Tests.' + n for n in ('ControllerTests', 'StorageC1Tests', 'FoodChainTests', 'MapRuntimeTests'))
DEVELOPMENT = ('Simulation.Tests.DevelopmentD2Tests', 'Simulation.Tests.ExpansionD2Tests')

@dataclass(frozen=True)
class Partition:
    name: str
    include: tuple[str, ...] = ()
    exclude: tuple[str, ...] = ()

    def matches(self, name):
        return (not self.include or name in self.include) and name not in self.exclude

    def filters(self):
        return [v for flag, names in (('-class', self.include), ('-class-', self.exclude)) for name in names for v in (flag, name)]

# Separate existing long-running physical and model scenarios without changing any test or deadline.
PHYSICAL = tuple('Simulation.Tests.'+name for name in (
    'EconomicCycleTests','CommandsAndEconomyTests','TimingAndMarketTests','LandAndContentTests','LaborTests',
    'EquipmentTests','CheckpointTests','MarketTests','PickupTests','DeliveryTests','CapitalDemandTests',
    'ElectionTests','StoppedElectionTests','SupportTests','LogisticsTests','LogisticsUpgradeTests',
    'LogisticsQueueTests','LogisticsReturnTests','OwnStorageTests','OwnProductionTests','InboundTests',
    'PhysicalResourcesTests','PhysicalMapTests','PhysicalAutomationTests','VolumeContractTests',
    'TravelTests','ManualHarvestTests','DemolitionC2Tests','WorkshopD1Tests'))
MODELS = tuple('Simulation.Tests.'+name for name in (
    'HybridControllerTests','ModelRuntimeTests','ModelIntentTests','OllamaAdapterTests','OrdinaryModelTests',
    'BallotModelTests','StrategicPlanningTests','DifficultyTests','HiringEconomicsTests','NegotiationPolicyTests','BaselineTests'))
PRODUCTION = ('Simulation.Tests.ProductionD3Tests',)
START = ('Simulation.Tests.ProductionD3StartTests',)
FLEET = ('Simulation.Tests.FleetE1Tests',)
FLEET_STOCK = ('Simulation.Tests.FleetE1StockTests',)
ROAD_HISTORY = ('Simulation.Tests.RoadHistoryE2Tests',)
ROADS = ('Simulation.Tests.RoadE2Tests',)
TRAFFIC = ('Simulation.Tests.RoadTrafficE2Tests',)
ROAD_FREIGHT = ('Simulation.Tests.RoadFreightE2Tests',)
BILLING_TIMING = tuple("Simulation.Tests."+n for n in ("BillingPenaltyE3Tests","BillingBoundaryE3Tests","BillingPauseE3Tests","BillingReceiptE3Tests","BillingRoundingE3Tests","BillingTrafficE3Tests"))
BILLING = ("Simulation.Tests.BillingE3Tests",)
BILLING_DISTRIBUTION = ("Simulation.Tests.BillingDistributionE3Tests",)
SERVICES=("Simulation.Tests.ServiceF1Tests","Simulation.Tests.ServiceRightsF1Tests","Simulation.Tests.ServiceCompetitionF1Tests")
UX=("Simulation.Tests.CityMapG2Tests","Simulation.Tests.CityExperienceG2Tests","Simulation.Tests.WorldProportionsG2Tests")
NPC=tuple("Simulation.Tests."+n for n in ("NpcEconomyG1Tests","NpcDeliveryG1Tests","NpcProductionG1Tests","NpcInfrastructureG1Tests","NpcRecoveryG1Tests","NpcCheckpointG1Tests"))
RUNS=tuple("Simulation.Tests."+n for n in ("ServiceF2Tests","ServiceSettlementF2Tests","ServiceControlF2Tests","ServicePointsF2Tests","ServiceGoodsF2Tests"))
PARTITIONS = (Partition('', exclude=UX + NPC + RUNS + SERVICES + BILLING_TIMING + BILLING + BILLING_DISTRIBUTION + ROAD_HISTORY + ROADS + TRAFFIC + ROAD_FREIGHT + INTEGRATION + DEVELOPMENT + PRODUCTION + START + FLEET + FLEET_STOCK + PHYSICAL + MODELS), Partition('-integration', include=INTEGRATION), Partition('-d2', include=DEVELOPMENT), Partition('-d3', include=PRODUCTION), Partition('-d3-start', include=START), Partition('-e1', include=FLEET), Partition('-e1-stock', include=FLEET_STOCK), Partition('-physical', include=PHYSICAL), Partition('-models', include=MODELS), Partition('-e2-history',include=ROAD_HISTORY), Partition('-e2-roads',include=ROADS), Partition('-e2-traffic',include=TRAFFIC), Partition('-e2-freight',include=ROAD_FREIGHT),Partition('-f1-services',include=(SERVICES[0],)),Partition('-f1-rights',include=(SERVICES[1],)),Partition('-f1-competition',include=(SERVICES[2],)),Partition('-e3-accounts',include=BILLING),Partition('-e3-distribution',include=BILLING_DISTRIBUTION),*(Partition('-e3-timing-'+str(n),include=(name,)) for n,name in enumerate(BILLING_TIMING)))

PARTITIONS+=tuple(Partition("-g2-"+str(n),include=(name,)) for n,name in enumerate(UX))
PARTITIONS+=tuple(Partition("-g1-"+str(n),include=(name,)) for n,name in enumerate(NPC))
PARTITIONS+=tuple(Partition("-f2-"+str(n),include=(name,)) for n,name in enumerate(RUNS))

def selected_partitions(suite='all'):
    if suite not in ('all','previous','roads','billing','services','runs','npc','ux'):raise ValueError('Unknown suite')
    return tuple(p for p in PARTITIONS if suite=='all' or suite=='ux' and p.name.startswith('-g2-') or suite=='npc' and p.name.startswith('-g1-') or suite=='runs' and p.name.startswith('-f2-') or suite=='services' and p.name.startswith('-f1-') or suite=='roads' and p.name.startswith('-e2-') or suite=='billing' and p.name.startswith('-e3-') or suite=='previous' and not p.name.startswith(('-e2-','-e3-','-f1-','-f2-','-g1-','-g2-')))

def run_suite(check, dotnet, assembly, output, prefix='tests', timeout=120, suite='all'):
    """All future classes enter the base partition. Reject skips, failures and overlaps in actual TRX."""
    output = Path(output)
    reports = []
    seen = set()
    for part in selected_partitions(suite):
        path = output / ('tests' + part.name + '.trx')
        check(prefix + part.name, timeout, [dotnet, str(assembly), '-noColor', '-noLogo', '-parallelMode', 'none', *part.filters(), '-result-trx', str(path)])
        root = ET.parse(path).getroot()
        counters = next(e for e in root.iter() if e.tag.endswith('Counters')).attrib
        passed = int(counters['passed'])
        if not passed or int(counters['failed']) or int(counters.get('notExecuted', 0)):
            raise SystemExit('Incomplete test partition: ' + part.name)
        classes = {e.attrib['id']: next(m for m in e.iter() if m.tag.endswith('TestMethod')).attrib['className'].split(',')[0] for e in root.iter() if e.tag.endswith('UnitTest')}
        results = [e for e in root.iter() if e.tag.endswith('UnitTestResult')]
        identities = {e.attrib['testId'] for e in results}
        if len(results) != passed or len(identities) != passed or identities & seen or any(not part.matches(classes[e.attrib['testId']]) for e in results):
            raise SystemExit('Invalid or overlapping test partition: ' + part.name)
        seen |= identities
        reports.append(dict(file=path.name, passed=passed))
    return reports
