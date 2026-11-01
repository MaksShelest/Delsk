using Xunit;
namespace Simulation.Tests;
public class H2FreightMovementTests
{[Fact]public void AcceleratedRealFreightKeepsCargoPayrollAllStagesAndBypassReplay()=>RoadFreightE2Tests.CheckFreight(true);}
public class H2ExtractionMovementTests
{[Fact]public void AcceleratedExistingExtractionBodyUsesSafeTrafficAndKeepsMiningTimers()=>RoadFreightE2Tests.CheckExtraction(true);}
