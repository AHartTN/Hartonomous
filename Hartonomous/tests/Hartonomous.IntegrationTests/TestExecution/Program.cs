using Hartonomous.IntegrationTests.TestExecution;

// Execute comprehensive integration tests
var demo = new SimpleTestDemo();
var result = await demo.ExecuteComprehensiveIntegrationTestsAsync();

Console.WriteLine($"\nIntegration tests completed with {result.Summary.OverallSuccessRate:P2} success rate");
Console.WriteLine($"Platform status: {(result.Summary.OverallStatus == TestStatus.Passed ? "READY FOR PRODUCTION" : "REQUIRES ATTENTION")}");

// Return appropriate exit code
Environment.Exit(result.Summary.OverallStatus == TestStatus.Passed ? 0 : 1);