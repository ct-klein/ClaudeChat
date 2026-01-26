using System.Data;
using System.Text;
using Anthropic.SDK;
using Anthropic.SDK.Extensions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.AI;

// === CONFIGURATION ===
const string ConnectionString = "Server=DESKTOP-Q6C3SQQ\\SQLEXPRESS;Database=lahman2024;Trusted_Connection=True;TrustServerCertificate=True;";

// Store connection string for tool methods
DatabaseTools.ConnectionString = ConnectionString;

// === LOCAL TEST MODE ===
if (args.Contains("--test"))
{
    Console.WriteLine("=== LOCAL DATABASE TOOLS TEST ===\n");

    // Test 1: Schema retrieval
    Console.WriteLine("TEST 1: GetSchema()");
    Console.WriteLine(new string('-', 50));
    try
    {
        var schema = DatabaseTools.GetSchema();
        // Show first 2000 chars to keep output manageable
        Console.WriteLine(schema.Length > 2000 ? schema[..2000] + "\n... [truncated]" : schema);
        Console.WriteLine("\n[PASS] Schema retrieved successfully\n");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[FAIL] {ex.Message}\n");
    }

    // Test 2: Simple query
    Console.WriteLine("TEST 2: QueryDatabase() - Top 5 home run hitters in 1998");
    Console.WriteLine(new string('-', 50));
    try
    {
        var result = DatabaseTools.QueryDatabase(
            "SELECT TOP 5 p.nameFirst, p.nameLast, b.HR FROM Batting b JOIN People p ON b.playerID = p.playerID WHERE b.yearID = 1998 ORDER BY b.HR DESC");
        Console.WriteLine(result);
        Console.WriteLine("[PASS] Query executed successfully\n");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[FAIL] {ex.Message}\n");
    }

    // Test 3: Safety check - reject non-SELECT
    Console.WriteLine("TEST 3: QueryDatabase() - Safety check (should reject DELETE)");
    Console.WriteLine(new string('-', 50));
    var safetyResult = DatabaseTools.QueryDatabase("DELETE FROM People WHERE 1=1");
    Console.WriteLine(safetyResult);
    Console.WriteLine(safetyResult.Contains("Error") ? "[PASS] Correctly rejected\n" : "[FAIL] Should have rejected\n");

    Console.WriteLine("=== TESTS COMPLETE ===");
    return;
}

// === API KEY ===
var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
if (string.IsNullOrEmpty(apiKey))
{
    Console.Write("Enter your Anthropic API key: ");
    apiKey = Console.ReadLine();
    if (string.IsNullOrEmpty(apiKey))
    {
        Console.WriteLine("API key is required.");
        return;
    }
}

// === SETUP CLIENT WITH FUNCTION INVOCATION ===
IChatClient client = new AnthropicClient(apiKey).Messages
    .AsBuilder()
    .UseFunctionInvocation()
    .Build();

// Define tools using AIFunctionFactory
var tools = new List<AITool>
{
    AIFunctionFactory.Create(DatabaseTools.GetSchema, "get_schema",
        "Gets the database schema including all tables, columns, primary keys, and relationship documentation. Use this first to understand what data is available before writing queries."),
    AIFunctionFactory.Create(DatabaseTools.QueryDatabase, "query_database",
        "Executes a SQL SELECT query against the baseball database and returns results. Only SELECT queries are allowed for safety. Returns up to 50 rows.")
};

// === MODEL CONFIGURATION ===
// Model options with pricing (as of 2025)
var models = new Dictionary<string, (string Id, decimal InputPer1M, decimal OutputPer1M, string Description)>
{
    ["sonnet"] = ("claude-sonnet-4-20250514", 3.00m, 15.00m, "Best quality, lower rate limits"),
    ["haiku"] = ("claude-3-haiku-20240307", 0.25m, 1.25m, "Fast & cheap, higher rate limits")
};

// Start with Haiku by default (better for development/testing)
var currentModel = "haiku";

var options = new ChatOptions
{
    ModelId = models[currentModel].Id,
    MaxOutputTokens = 4096,
    Tools = tools
};

// === COST TRACKING ===
long sessionInputTokens = 0;
long sessionOutputTokens = 0;
decimal sessionCost = 0m;

static string FormatCost(decimal cost) => cost < 0.01m ? $"${cost:F4}" : $"${cost:F2}";

string FormatUsage(long input, long output, decimal cost) =>
    $"[{currentModel.ToUpper()} | Tokens: {input:N0} in / {output:N0} out | Cost: {FormatCost(cost)}]";

// === MAIN CHAT LOOP ===
var messages = new List<ChatMessage>
{
    new(ChatRole.System, @"You are a helpful baseball statistics assistant with access to the Lahman baseball database.

DATABASE: Microsoft SQL Server 2016 Express
SQL SYNTAX RULES (CRITICAL - follow exactly):
- Use TOP N instead of LIMIT (e.g., SELECT TOP 10 * FROM table)
- Use + for string concatenation (e.g., nameFirst + ' ' + nameLast)
- NO backticks - use [brackets] for reserved words if needed
- CTEs work: WITH cte AS (SELECT ...) SELECT * FROM cte
- Use ROW_NUMBER() OVER (PARTITION BY x ORDER BY y) for best-per-group queries
- Column aliases: SELECT col AS alias (not col alias)

EFFICIENCY: Use ONE well-crafted query when possible.

Key tables: People (players), Batting, Pitching, Fielding, Teams, Appearances (has position columns: G_p, G_c, G_1b, G_2b, G_3b, G_ss, G_lf, G_cf, G_rf).
Join on playerID to People, teamID+yearID+lgID to Teams.

EXAMPLE - Best hitter by position for a year (uses UNPIVOT pattern):
SELECT Position, nameFirst + ' ' + nameLast AS Player, HR, RBI, AVG
FROM (
  SELECT p.nameFirst, p.nameLast, b.HR, b.RBI,
         CAST(b.H AS FLOAT)/NULLIF(b.AB,0) AS AVG,
         CASE WHEN a.G_c >= a.G_1b AND a.G_c >= a.G_2b AND a.G_c >= a.G_3b AND a.G_c >= a.G_ss AND a.G_c >= a.G_lf AND a.G_c >= a.G_cf AND a.G_c >= a.G_rf THEN 'C'
              WHEN a.G_1b >= a.G_2b AND a.G_1b >= a.G_3b AND a.G_1b >= a.G_ss AND a.G_1b >= a.G_lf AND a.G_1b >= a.G_cf AND a.G_1b >= a.G_rf THEN '1B'
              WHEN a.G_2b >= a.G_3b AND a.G_2b >= a.G_ss AND a.G_2b >= a.G_lf AND a.G_2b >= a.G_cf AND a.G_2b >= a.G_rf THEN '2B'
              WHEN a.G_3b >= a.G_ss AND a.G_3b >= a.G_lf AND a.G_3b >= a.G_cf AND a.G_3b >= a.G_rf THEN '3B'
              WHEN a.G_ss >= a.G_lf AND a.G_ss >= a.G_cf AND a.G_ss >= a.G_rf THEN 'SS'
              WHEN a.G_lf >= a.G_cf AND a.G_lf >= a.G_rf THEN 'LF'
              WHEN a.G_cf >= a.G_rf THEN 'CF'
              ELSE 'RF' END AS Position,
         ROW_NUMBER() OVER (PARTITION BY
           CASE WHEN a.G_c >= a.G_1b AND a.G_c >= a.G_2b AND a.G_c >= a.G_3b AND a.G_c >= a.G_ss AND a.G_c >= a.G_lf AND a.G_c >= a.G_cf AND a.G_c >= a.G_rf THEN 'C'
                WHEN a.G_1b >= a.G_2b AND a.G_1b >= a.G_3b AND a.G_1b >= a.G_ss AND a.G_1b >= a.G_lf AND a.G_1b >= a.G_cf AND a.G_1b >= a.G_rf THEN '1B'
                WHEN a.G_2b >= a.G_3b AND a.G_2b >= a.G_ss AND a.G_2b >= a.G_lf AND a.G_2b >= a.G_cf AND a.G_2b >= a.G_rf THEN '2B'
                WHEN a.G_3b >= a.G_ss AND a.G_3b >= a.G_lf AND a.G_3b >= a.G_cf AND a.G_3b >= a.G_rf THEN '3B'
                WHEN a.G_ss >= a.G_lf AND a.G_ss >= a.G_cf AND a.G_ss >= a.G_rf THEN 'SS'
                WHEN a.G_lf >= a.G_cf AND a.G_lf >= a.G_rf THEN 'LF'
                WHEN a.G_cf >= a.G_rf THEN 'CF'
                ELSE 'RF' END
           ORDER BY b.HR + b.RBI DESC) AS rn
  FROM Batting b
  JOIN People p ON b.playerID = p.playerID
  JOIN Appearances a ON b.playerID = a.playerID AND b.yearID = a.yearID AND b.teamID = a.teamID
  WHERE b.yearID = 2000 AND b.AB > 100
) ranked WHERE rn = 1 ORDER BY Position")
};

Console.WriteLine("Baseball Stats Assistant");
Console.WriteLine("Ask questions about MLB statistics!");
Console.WriteLine($"Current model: {currentModel.ToUpper()} ({models[currentModel].Description})");
Console.WriteLine("Commands: 'quit', 'clear', 'usage', 'model haiku', 'model sonnet'\n");

while (true)
{
    Console.Write("You: ");
    var userInput = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(userInput)) continue;
    if (userInput.Equals("quit", StringComparison.OrdinalIgnoreCase)) break;
    if (userInput.Equals("clear", StringComparison.OrdinalIgnoreCase))
    {
        // Reset to just the system message
        messages.RemoveRange(1, messages.Count - 1);
        Console.WriteLine("[Conversation cleared]\n");
        continue;
    }
    if (userInput.Equals("usage", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine($"\n=== SESSION USAGE ===");
        Console.WriteLine($"Current model: {currentModel.ToUpper()}");
        Console.WriteLine($"Input tokens:  {sessionInputTokens:N0}");
        Console.WriteLine($"Output tokens: {sessionOutputTokens:N0}");
        Console.WriteLine($"Total tokens:  {sessionInputTokens + sessionOutputTokens:N0}");
        Console.WriteLine($"Est. cost:     {FormatCost(sessionCost)}");
        Console.WriteLine($"Messages:      {messages.Count - 1} (excluding system)\n");
        continue;
    }
    if (userInput.StartsWith("model ", StringComparison.OrdinalIgnoreCase))
    {
        var requestedModel = userInput[6..].Trim().ToLower();
        if (models.ContainsKey(requestedModel))
        {
            currentModel = requestedModel;
            options.ModelId = models[currentModel].Id;
            Console.WriteLine($"[Switched to {currentModel.ToUpper()}: {models[currentModel].Description}]\n");
        }
        else
        {
            Console.WriteLine($"[Unknown model. Available: {string.Join(", ", models.Keys)}]\n");
        }
        continue;
    }

    // Track message count before adding user message
    var messageCountBefore = messages.Count;
    messages.Add(new ChatMessage(ChatRole.User, userInput));

    try
    {
        // The client handles the tool invocation loop automatically
        var response = await client.GetResponseAsync(messages, options);

        // Add the response to conversation history
        messages.AddRange(response.Messages);

        // Display the final text response
        Console.WriteLine($"\nClaude: {response.Text}\n");

        // Track and display token usage
        if (response.Usage != null)
        {
            var inputTokens = response.Usage.InputTokenCount ?? 0;
            var outputTokens = response.Usage.OutputTokenCount ?? 0;
            var (_, inputRate, outputRate, _) = models[currentModel];
            var requestCost = (inputTokens * inputRate / 1_000_000m) +
                              (outputTokens * outputRate / 1_000_000m);

            sessionInputTokens += inputTokens;
            sessionOutputTokens += outputTokens;
            sessionCost += requestCost;

            Console.WriteLine(FormatUsage(inputTokens, outputTokens, requestCost));
            Console.WriteLine($"[Session total: {sessionInputTokens:N0} in / {sessionOutputTokens:N0} out | {FormatCost(sessionCost)}]\n");
        }
    }
    catch (Exception ex)
    {
        // On error, revert to state before this request to avoid corrupted history
        Console.WriteLine($"\nError: {ex.Message}");
        messages.RemoveRange(messageCountBefore, messages.Count - messageCountBefore);
        Console.WriteLine("[Conversation rolled back to prevent corruption. Try 'clear' if issues persist.]\n");
    }
}

Console.WriteLine("Goodbye!");

// === TOOL IMPLEMENTATIONS ===

public static class DatabaseTools
{
    public static string ConnectionString { get; set; } = "";

    // Only include essential tables to reduce token usage (~60% reduction)
    private static readonly HashSet<string> EssentialTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "People", "Batting", "Pitching", "Fielding", "Teams", "TeamsFranchises",
        "AwardsPlayers", "AllstarFull", "Appearances", "BattingPost", "PitchingPost"
    };

    public static string GetSchema()
    {
        var sb = new StringBuilder();
        using var conn = new SqlConnection(ConnectionString);
        conn.Open();

        // Concise relationship documentation
        sb.AppendLine("=== LAHMAN BASEBALL DATABASE - KEY TABLES ===");
        sb.AppendLine("Join keys: playerID→People, teamID+yearID+lgID→Teams, franchID→TeamsFranchises");
        sb.AppendLine("");

        // Build a dictionary of primary keys for essential tables only
        var primaryKeys = new Dictionary<string, List<string>>();
        using (var pkCmd = new SqlCommand(@"
            SELECT TABLE_NAME, COLUMN_NAME
            FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE
            WHERE CONSTRAINT_NAME LIKE 'PK_%'
            ORDER BY TABLE_NAME, ORDINAL_POSITION", conn))
        using (var pkReader = pkCmd.ExecuteReader())
        {
            while (pkReader.Read())
            {
                var table = pkReader["TABLE_NAME"].ToString()!;
                if (!EssentialTables.Contains(table)) continue;

                var column = pkReader["COLUMN_NAME"].ToString()!;
                if (!primaryKeys.ContainsKey(table))
                    primaryKeys[table] = new List<string>();
                primaryKeys[table].Add(column);
            }
        }

        // Only get essential tables
        foreach (var tableName in EssentialTables.OrderBy(t => t))
        {
            // Show table name with primary key info
            var pkInfo = primaryKeys.ContainsKey(tableName)
                ? $" (PK: {string.Join("+", primaryKeys[tableName])})"
                : "";
            sb.AppendLine($"\n{tableName}{pkInfo}");

            // Get columns for this table
            using var cmd = new SqlCommand(
                @"SELECT COLUMN_NAME, DATA_TYPE
                  FROM INFORMATION_SCHEMA.COLUMNS
                  WHERE TABLE_NAME = @table
                  ORDER BY ORDINAL_POSITION", conn);
            cmd.Parameters.AddWithValue("@table", tableName);

            using var reader = cmd.ExecuteReader();
            var columns = new List<string>();
            while (reader.Read())
            {
                var colName = reader["COLUMN_NAME"].ToString()!;
                var dataType = reader["DATA_TYPE"].ToString()!;
                var pkMarker = primaryKeys.ContainsKey(tableName) && primaryKeys[tableName].Contains(colName)
                    ? "*" : "";
                columns.Add($"{colName}{pkMarker}");
            }
            // Compact column listing
            sb.AppendLine($"  {string.Join(", ", columns)}");
        }

        sb.AppendLine("\nNote: Other tables exist (Salaries, Managers, Schools, etc.) - ask if needed.");
        return sb.ToString();
    }

    public static string QueryDatabase(string sql)
    {
        Console.WriteLine($"\n[Executing SQL: {sql}]");

        // Safety check - only allow SELECT
        if (!sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
        {
            return "Error: Only SELECT queries are allowed.";
        }

        try
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = new SqlCommand(sql, conn);
            cmd.CommandTimeout = 30;

            using var reader = cmd.ExecuteReader();
            var sb = new StringBuilder();
            var rowCount = 0;

            // Column headers
            var columns = new List<string>();
            for (int i = 0; i < reader.FieldCount; i++)
                columns.Add(reader.GetName(i));
            sb.AppendLine(string.Join(" | ", columns));
            sb.AppendLine(new string('-', columns.Count * 15));

            // Data rows (limit to 50)
            while (reader.Read() && rowCount < 50)
            {
                var values = new List<string>();
                for (int i = 0; i < reader.FieldCount; i++)
                    values.Add(reader.IsDBNull(i) ? "NULL" : reader[i]?.ToString() ?? "");
                sb.AppendLine(string.Join(" | ", values));
                rowCount++;
            }

            sb.AppendLine($"\n({rowCount} rows returned)");
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error executing query: {ex.Message}";
        }
    }
}
