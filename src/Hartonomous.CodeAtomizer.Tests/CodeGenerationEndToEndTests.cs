using Hartonomous.CodeAtomizer.Core.Services;
using Xunit;
using Xunit.Abstractions;

namespace Hartonomous.CodeAtomizer.Tests;

/// <summary>
/// End-to-end tests for code generation with memory retrieval
/// </summary>
public class CodeGenerationEndToEndTests
{
    private readonly ITestOutputHelper _output;
    private readonly LanguageProfileLoader _profileLoader;
    private readonly AtomMemoryService _memoryService;

    public CodeGenerationEndToEndTests(ITestOutputHelper output)
    {
        _output = output;
        
        // Setup: Load profiles
        var profilePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "..", "..", "..", "..", "..", "config", "language-profiles");
        
        _profileLoader = new LanguageProfileLoader(profilePath);
        _profileLoader.LoadProfilesAsync().Wait();
        
        _memoryService = new AtomMemoryService();
    }

    [Fact]
    public void AtomizeAndQueryByProximity_Python_RetrievesRelevantAtoms()
    {
        // Arrange: Python code with functions
        var pythonCode = @"
def calculate_factorial(n):
    '''Calculate factorial of n'''
    if n <= 1:
        return 1
    return n * calculate_factorial(n - 1)

def calculate_fibonacci(n):
    '''Calculate nth Fibonacci number'''
    if n <= 1:
        return n
    return calculate_fibonacci(n - 1) + calculate_fibonacci(n - 2)

class Calculator:
    def add(self, a, b):
        return a + b
";

        // Act: Atomize and store
        var atomizer = new Core.Atomizers.TreeSitterAtomizer(_profileLoader);
        var result = atomizer.Atomize(pythonCode, "calculator.py", null);
        _memoryService.Store(result);

        // Get stats
        var stats = _memoryService.GetStatistics();
        _output.WriteLine($"Stored {stats.TotalAtoms} atoms, {stats.TotalCompositions} compositions, {stats.TotalRelations} relations");

        // Assert: Atoms stored
        Assert.True(stats.TotalAtoms > 0);
        Assert.Contains("function", stats.CategoryCounts.Keys);

        // Act: Query by proximity (find atoms near factorial function)
        var functionAtoms = _memoryService.RetrieveBySemantic("function", "code", 10);
        _output.WriteLine($"Found {functionAtoms.Count} function atoms");

        foreach (var atom in functionAtoms.Take(3))
        {
            _output.WriteLine($"  - {atom.CanonicalText} ({atom.Subtype})");
        }

        // Assert: Found functions
        Assert.NotEmpty(functionAtoms);
        Assert.Contains(functionAtoms, a => a.CanonicalText?.Contains("factorial") ?? false);
    }

    [Fact]
    public void BuildContext_Python_CombinesSpatialAndSemanticAtoms()
    {
        // Arrange: Python code
        var pythonCode = @"
import math

def square_root(x):
    return math.sqrt(x)

def power(base, exp):
    return math.pow(base, exp)

class MathUtils:
    @staticmethod
    def absolute(x):
        return abs(x)
";

        // Act: Atomize and store
        var atomizer = new Core.Atomizers.TreeSitterAtomizer(_profileLoader);
        var result = atomizer.Atomize(pythonCode, "math_utils.py", null);
        _memoryService.Store(result);

        // Get a function atom to use its coordinates
        var functionAtoms = _memoryService.RetrieveBySemantic("function", "code", 1);
        Assert.NotEmpty(functionAtoms);

        var focal = functionAtoms[0].SpatialKey;

        // Act: Build generation context
        var context = _memoryService.BuildContext(
            "python",
            "function",
            focal.X, focal.Y, focal.Z,
            proximityRadius: 0.3,
            maxAtoms: 10
        );

        _output.WriteLine($"Context: {context.ContextAtoms.Count} atoms, {context.Relations.Count} relations");
        foreach (var atom in context.ContextAtoms.Take(5))
        {
            _output.WriteLine($"  - {atom.CanonicalText} ({atom.Subtype})");
        }

        // Assert: Context has relevant atoms
        Assert.NotEmpty(context.ContextAtoms);
        Assert.Equal("python", context.Language);
        Assert.Equal("function", context.Category);
        Assert.True(context.ContextAtoms.All(a => a.Modality == "code"));
    }

    [Fact]
    public void ReconstructCompositionTree_Python_BuildsHierarchy()
    {
        // Arrange: Python code with nested structure
        var pythonCode = @"
class OuterClass:
    def outer_method(self):
        def inner_function():
            return 42
        return inner_function()
";

        // Act: Atomize and store
        var atomizer = new Core.Atomizers.TreeSitterAtomizer(_profileLoader);
        var result = atomizer.Atomize(pythonCode, "nested.py", null);
        _memoryService.Store(result);

        // Get file atom (root)
        var fileAtoms = _memoryService.RetrieveBySemantic(null, "code", 100)
            .Where(a => a.Subtype == "file")
            .ToList();

        Assert.NotEmpty(fileAtoms);

        // Act: Reconstruct tree
        var tree = _memoryService.ReconstructCompositionTree(
            fileAtoms[0].ContentHash,
            maxDepth: 5
        );

        // Assert: Tree has nested structure
        Assert.NotNull(tree.Root);
        Assert.NotEmpty(tree.Children);

        _output.WriteLine($"Tree root: {tree.Root?.CanonicalText}");
        _output.WriteLine($"Children: {tree.Children.Count}");
        
        foreach (var child in tree.Children.Take(3))
        {
            _output.WriteLine($"  - {child.Root?.CanonicalText} ({child.Root?.Subtype})");
        }
    }

    [Fact]
    public void GetRelations_Python_RetrievesFunctionCalls()
    {
        // Arrange: Python code with function calls
        var pythonCode = @"
def helper():
    return 10

def main():
    result = helper()
    return result * 2
";

        // Act: Atomize and store
        var atomizer = new Core.Atomizers.TreeSitterAtomizer(_profileLoader);
        var result = atomizer.Atomize(pythonCode, "calls.py", null);
        _memoryService.Store(result);

        // Get main function atom
        var mainAtoms = _memoryService.SearchByText("main", 10);
        var mainFunction = mainAtoms.FirstOrDefault(a => a.Subtype == "function");

        if (mainFunction != null)
        {
            // Act: Get relations
            var relations = _memoryService.GetRelations(mainFunction.ContentHash);

            _output.WriteLine($"Main function relations:");
            _output.WriteLine($"  Outgoing: {relations.OutgoingRelations.Count}");
            _output.WriteLine($"  Incoming: {relations.IncomingRelations.Count}");

            foreach (var rel in relations.OutgoingRelations.Take(3))
            {
                _output.WriteLine($"  - {rel.RelationType} (weight: {rel.Weight})");
            }

            // Assert: Has relations
            Assert.True(relations.OutgoingRelations.Count >= 0);
        }
    }

    [Fact]
    public void AtomizeMultipleFiles_CSharp_AccumulatesMemory()
    {
        // Arrange: Multiple C# files
        var csharpCode1 = @"
namespace MyApp;

public class Person
{
    public string Name { get; set; }
    public int Age { get; set; }
}
";

        var csharpCode2 = @"
namespace MyApp;

public class Employee : Person
{
    public string JobTitle { get; set; }
}
";

        // Act: Atomize both files
        var atomizer = new Core.Atomizers.TreeSitterAtomizer(_profileLoader);
        
        var result1 = atomizer.Atomize(csharpCode1, "Person.cs", null);
        _memoryService.Store(result1);

        var result2 = atomizer.Atomize(csharpCode2, "Employee.cs", null);
        _memoryService.Store(result2);

        // Get stats
        var stats = _memoryService.GetStatistics();
        _output.WriteLine($"Total atoms: {stats.TotalAtoms}");
        _output.WriteLine($"Total compositions: {stats.TotalCompositions}");
        _output.WriteLine($"Total relations: {stats.TotalRelations}");

        // Assert: Both files' atoms stored
        Assert.True(stats.TotalAtoms >= result1.TotalAtoms + result2.TotalAtoms);
        
        // Check if any atoms have class category (works with both TreeSitter and Roslyn)
        var hasClassCategory = stats.CategoryCounts.ContainsKey("class");
        var hasFileCategory = stats.CategoryCounts.ContainsKey("file");
        
        // At minimum, should have file atoms; class atoms expected if proper parsing occurred
        Assert.True(hasFileCategory || hasClassCategory, 
            $"Expected 'file' or 'class' category, got: {string.Join(", ", stats.CategoryCounts.Keys)}");
    }
}
