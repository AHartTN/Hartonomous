using System.CommandLine;
using System.Net.Http.Json;
using Hartonomous.Shared.Models;
using Spectre.Console;

var rootCommand = new RootCommand("Hartonomous CLI - Interface to the Semantic Engine");
var hostOption = new Option<string>("--host", () => "http://localhost:5000", "The API base URL");
rootCommand.AddGlobalOption(hostOption);

// =============================================================================
//  Ingest Command
// =============================================================================
var ingestCommand = new Command("ingest", "Ingest data into the semantic substrate");

var textArgument = new Argument<string>("content", "The text to ingest");
var ingestTextCmd = new Command("text", "Ingest raw text") { textArgument };
ingestTextCmd.SetHandler(async (host, text) => await IngestText(host, text), hostOption, textArgument);

var fileArgument = new Argument<FileInfo>("file", "The file to ingest");
var ingestFileCmd = new Command("file", "Ingest a file") { fileArgument };
ingestFileCmd.SetHandler(async (host, file) => await IngestFile(host, file), hostOption, fileArgument);

ingestCommand.AddCommand(ingestTextCmd);
ingestCommand.AddCommand(ingestFileCmd);
rootCommand.AddCommand(ingestCommand);

// =============================================================================
//  Ask Command
// =============================================================================
var askCommand = new Command("ask", "Ask a question to the Walk Engine");
var promptArgument = new Argument<string>("prompt", "The question or prompt");
askCommand.AddArgument(promptArgument);
askCommand.SetHandler(async (host, prompt) => await Ask(host, prompt), hostOption, promptArgument);
rootCommand.AddCommand(askCommand);

// =============================================================================
//  Godel Command
// =============================================================================
var godelCommand = new Command("godel", "Godel Analysis Tools");
var problemArgument = new Argument<string>("problem", "The problem statement");
var analyzeCmd = new Command("analyze", "Analyze problem solvability") { problemArgument };
analyzeCmd.SetHandler(async (host, problem) => await Analyze(host, problem), hostOption, problemArgument);
godelCommand.AddCommand(analyzeCmd);
rootCommand.AddCommand(godelCommand);

// =============================================================================
//  REPL Command
// =============================================================================
var replCommand = new Command("repl", "Interactive Shell");
replCommand.SetHandler(async (host) => await RunRepl(host), hostOption);
rootCommand.AddCommand(replCommand);

return await rootCommand.InvokeAsync(args);

// =============================================================================
//  Implementation
// =============================================================================

static HttpClient CreateClient(string host) => new() { BaseAddress = new Uri(host) };

static async Task IngestText(string host, string text)
{
    await AnsiConsole.Status()
        .StartAsync("Ingesting text...", async ctx =>
        {
            try
            {
                using var client = CreateClient(host);
                var response = await client.PostAsJsonAsync("/api/ingestion/text", new IngestTextRequest { Text = text });
                response.EnsureSuccessStatusCode();
                var stats = await response.Content.ReadFromJsonAsync<IngestStats>();
                PrintStats(stats);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/]{ex.Message}");
            }
        });
}

static async Task IngestFile(string host, FileInfo file)
{
    if (!file.Exists)
    {
        AnsiConsole.MarkupLine($"[red]File not found:[/]{file.FullName}");
        return;
    }
    
    await AnsiConsole.Status()
        .StartAsync($"Ingesting {file.Name}...", async ctx =>
        {
            try
            {
                using var client = CreateClient(host);
                var response = await client.PostAsJsonAsync("/api/ingestion/file", new IngestFileRequest { FilePath = file.FullName });
                response.EnsureSuccessStatusCode();
                var stats = await response.Content.ReadFromJsonAsync<IngestStats>();
                PrintStats(stats);
            }
            catch (Exception ex)
            {
                 AnsiConsole.MarkupLine($"[red]Error:[/]{ex.Message}");
            }
        });
}

static async Task Ask(string host, string prompt)
{
    try
    {
        using var client = CreateClient(host);
        var request = new ChatCompletionRequest
        {
            Messages = [new() { Role = "user", Content = prompt }],
            Temperature = 0.7,
            MaxTokens = 200,
            Stream = true
        };

        // Streaming support in CLI
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions");
        httpRequest.Content = JsonContent.Create(request);
        
        using var response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        AnsiConsole.Markup("[bold cyan]Hartonomous:[/]");
        
        // Simple SSE parser for CLI
        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.StartsWith("data: "))
            {
                var data = line["data: ".Length..];
                if (data == "[DONE]") break;
                
                try
                {
                    var chunk = System.Text.Json.JsonSerializer.Deserialize<ChatCompletionChunk>(data);
                    var content = chunk?.Choices?.FirstOrDefault()?.Delta?.Content;
                    if (!string.IsNullOrEmpty(content))
                    {
                        AnsiConsole.Write(content);
                    }
                }
                catch { /* ignore json errors */ }
            }
        }
        Console.WriteLine();
        Console.WriteLine();
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[red]Error:[/]{ex.Message}");
    }
}

static async Task Analyze(string host, string problem)
{
    await AnsiConsole.Status()
        .StartAsync("Analyzing problem geometry...", async ctx =>
        {
            try
            {
                using var client = CreateClient(host);
                var response = await client.PostAsJsonAsync("/api/godel/analyze", new AnalyzeRequest { Problem = problem });
                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadFromJsonAsync<AnalyzeResponse>();
                
                if (result?.Plan is { } plan)
                {
                    var table = new Table();
                    table.Border(TableBorder.Rounded);
                    table.AddColumn("Metric");
                    table.AddColumn("Value");

                    table.AddRow("Total Steps", plan.TotalSteps.ToString());
                    table.AddRow("Solvable Steps", $"[green]{plan.SolvableSteps}[/]");
                    table.AddRow("Sub-Problems", plan.SubProblemsCount.ToString());
                    table.AddRow("Knowledge Gaps", plan.KnowledgeGapsCount > 0 ? $"[yellow]{plan.KnowledgeGapsCount}[/]" : "[green]0[/]");

                    AnsiConsole.Write(new Rule("[yellow]Godel Analysis[/]"));
                    AnsiConsole.Write(table);

                    if (plan.KnowledgeGapsCount > 0)
                        AnsiConsole.MarkupLine("\n[yellow]Status: INCOMPLETE KNOWLEDGE[/] - Axioms missing.");
                    else
                        AnsiConsole.MarkupLine("\n[green]Status: THEORETICALLY SOLVABLE[/] - Path exists in S3.");
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/]{ex.Message}");
            }
        });
}

static void PrintStats(IngestStats? stats)
{
    if (stats == null) return;
    
    var table = new Table();
    table.Border(TableBorder.Rounded);
    table.AddColumn("Metric");
    table.AddColumn("Value");
    
    table.AddRow("Atoms", $"[blue]{stats.Atoms}[/]");
    table.AddRow("Compositions", $"[green]{stats.Compositions}[/]");
    table.AddRow("Relations", $"[yellow]{stats.Relations}[/]");
    table.AddRow("Time", $"{stats.TimeMs:F2} ms");

    AnsiConsole.Write(new Rule("[green]Ingestion Stats[/]"));
    AnsiConsole.Write(table);
}

static async Task RunRepl(string host)
{
    AnsiConsole.Write(new FigletText("Hartonomous").Color(Color.Cyan1));
    AnsiConsole.MarkupLine("[grey]Interactive Semantic Shell v1.0[/]");
    AnsiConsole.MarkupLine($"Connected to: [blue]{host}[/]");
    AnsiConsole.MarkupLine("Type [green]/help[/] for commands or just type to chat.");
    AnsiConsole.WriteLine();

    while (true)
    {
        var input = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]>[/]")
                .AllowEmpty());

        if (string.IsNullOrWhiteSpace(input)) continue;

        if (input.StartsWith("/"))
        {
            var parts = input.Split(' ', 2);
            var cmd = parts[0].ToLower();
            var arg = parts.Length > 1 ? parts[1] : "";

            switch (cmd)
            {
                case "/exit":
                case "/quit":
                    return;
                case "/help":
                    AnsiConsole.MarkupLine("Commands:");
                    AnsiConsole.MarkupLine("  [green]/godel [problem][/] - Analyze a problem");
                    AnsiConsole.MarkupLine("  [green]/ingest [text][/]   - Ingest text");
                    AnsiConsole.MarkupLine("  [green]/exit[/]             - Quit");
                    break;
                case "/godel":
                    if (string.IsNullOrWhiteSpace(arg)) 
                        AnsiConsole.MarkupLine("[red]Usage: /godel <problem>[/]");
                    else
                        await Analyze(host, arg);
                    break;
                case "/ingest":
                    if (string.IsNullOrWhiteSpace(arg)) 
                         AnsiConsole.MarkupLine("[red]Usage: /ingest <text>[/]");
                    else
                        await IngestText(host, arg);
                    break;
                default:
                    AnsiConsole.MarkupLine($"[red]Unknown command: {cmd}[/]");
                    break;
            }
        }
        else
        {
            // Default to chat
            await Ask(host, input);
        }
    }
}
