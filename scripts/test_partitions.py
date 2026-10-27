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
ROADS = ('Simulation.Tests.RoadE2Tests',)
TRAFFIC = ('Simulation.Tests.RoadTrafficE2Tests',)
ROAD_FREIGHT = ('Simulation.Tests.RoadFreightE2Tests',)
PARTITIONS = (Partition('', exclude=ROADS + TRAFFIC + ROAD_FREIGHT + INTEGRATION + DEVELOPMENT + PRODUCTION + START + FLEET + FLEET_STOCK + PHYSICAL + MODELS), Partition('-integration', include=INTEGRATION), Partition('-d2', include=DEVELOPMENT), Partition('-d3', include=PRODUCTION), Partition('-d3-start', include=START), Partition('-e1', include=FLEET), Partition('-e1-stock', include=FLEET_STOCK), Partition('-physical', include=PHYSICAL), Partition('-models', include=MODELS), Partition('-e2-roads',include=ROADS), Partition('-e2-traffic',include=TRAFFIC), Partition('-e2-freight',include=ROAD_FREIGHT))

def run_suite(check, dotnet, assembly, output, prefix='tests', timeout=120):
    """All future classes enter the base partition. Reject skips, failures and overlaps in actual TRX."""
    output = Path(output)
    reports = []
    seen = set()
    for part in PARTITIONS:
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
