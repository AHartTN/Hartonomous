namespace Hartonomous.Shared.Models;

public class AnalyzeRequest
{
    public string Problem { get; set; } = string.Empty;
}

public class AnalyzeResponse
{
    public GodelPlan Plan { get; set; } = new();
}

public class GodelPlan
{
    public int TotalSteps { get; set; }
    public int SolvableSteps { get; set; }
    public int SubProblemsCount { get; set; }
    public int KnowledgeGapsCount { get; set; }
}
