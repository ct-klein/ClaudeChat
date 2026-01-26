# AI Integration & Prompt Engineering Learning Journey

## Building a Baseball Statistics Assistant with Claude

This document summarizes my hands-on learning experience integrating Claude's API into a C# application, exploring key concepts in AI-powered software development.

---

## Project Overview

**Goal:** Build an interactive console application that uses Claude to answer natural language questions about baseball statistics by querying a SQL Server database.

**Tech Stack:**
- C# / .NET 9.0
- Anthropic SDK for .NET (v5.9.0)
- Microsoft SQL Server 2016 Express
- Lahman Baseball Database (comprehensive MLB statistics)

**Architecture:**
```
User Question → Claude AI → SQL Query Generation → Database → Results → Natural Language Response
```

---

## Key AI Integration Concepts Learned

### 1. Function Calling (Tool Use)

Claude can call functions defined in your application to interact with external systems.

```csharp
// Define tools using AIFunctionFactory
var tools = new List<AITool>
{
    AIFunctionFactory.Create(DatabaseTools.GetSchema, "get_schema",
        "Gets the database schema..."),
    AIFunctionFactory.Create(DatabaseTools.QueryDatabase, "query_database",
        "Executes a SQL SELECT query...")
};
```

**Learning:** The AI doesn't just generate text—it can decide when to call your functions, pass appropriate parameters, and use the results to formulate responses.

### 2. System Prompts & Context Engineering

The system prompt shapes how the AI behaves. We evolved it through several iterations:

**Version 1 - Basic:**
```
You are a helpful baseball statistics assistant...
```

**Version 2 - With SQL dialect guidance:**
```
DATABASE: Microsoft SQL Server 2016 Express
SQL SYNTAX RULES (CRITICAL - follow exactly):
- Use TOP N instead of LIMIT
- Use + for string concatenation
- NO backticks - use [brackets] for reserved words
```

**Version 3 - With few-shot example:**
```
EXAMPLE - Best hitter by position for a year:
SELECT Position, nameFirst + ' ' + nameLast AS Player...
[Complete working query example]
```

**Learning:** More capable models (Sonnet) can infer from minimal instructions. Less capable models (Haiku) need explicit rules and examples.

### 3. Token Management & Cost Optimization

Every API call has a cost based on tokens (roughly 4 characters = 1 token).

```csharp
// Track and display token usage
var inputTokens = response.Usage.InputTokenCount ?? 0;
var outputTokens = response.Usage.OutputTokenCount ?? 0;
var requestCost = (inputTokens * inputRate / 1_000_000m) +
                  (outputTokens * outputRate / 1_000_000m);
```

**Optimizations implemented:**
| Technique | Token Savings | Description |
|-----------|---------------|-------------|
| Focused schema | ~60% | Only include essential tables |
| Compact format | ~50% | Comma-separated columns vs. one per line |
| Model selection | Variable | Use Haiku for simple queries, Sonnet for complex |

### 4. Model Selection Trade-offs

Different models have different capabilities and costs:

| Model | Input $/1M | Output $/1M | Best For |
|-------|------------|-------------|----------|
| Claude 3 Haiku | $0.25 | $1.25 | Simple queries, development, higher rate limits |
| Claude Sonnet 4 | $3.00 | $15.00 | Complex reasoning, better SQL generation |

```csharp
// Runtime model switching
if (userInput.StartsWith("model ", StringComparison.OrdinalIgnoreCase))
{
    currentModel = requestedModel;
    options.ModelId = models[currentModel].Id;
}
```

**Learning:** Not every request needs the most powerful model. Matching model capability to task complexity reduces costs and avoids rate limits.

### 5. Error Handling & Conversation State

API calls can fail (rate limits, network issues). Proper handling prevents corrupted conversation state:

```csharp
try
{
    var response = await client.GetResponseAsync(messages, options);
    messages.AddRange(response.Messages);
}
catch (Exception ex)
{
    // Rollback to prevent "tool_use without tool_result" errors
    messages.RemoveRange(messageCountBefore, messages.Count - messageCountBefore);
    Console.WriteLine("[Conversation rolled back...]");
}
```

**Learning:** When tool calls fail mid-conversation, the message history can become invalid. Always implement rollback mechanisms.

---

## Challenges Encountered & Solutions

### Challenge 1: Rate Limits
**Problem:** Complex queries with multiple tool calls exceeded 30,000 input tokens/minute.

**Solutions:**
1. Reduced schema size (28 tables → 11 essential tables)
2. Added model selection (use Haiku for development)
3. Optimized system prompt to encourage single efficient queries

### Challenge 2: SQL Dialect Mismatch
**Problem:** Claude generated MySQL/PostgreSQL syntax (LIMIT, backticks) for SQL Server.

**Solutions:**
1. Added explicit SQL Server syntax rules to system prompt
2. Provided working example queries (few-shot prompting)
3. Used more capable model (Sonnet) for complex queries

### Challenge 3: Model Availability
**Problem:** Model IDs changed between SDK versions.

**Solution:** Used stable model identifiers:
- `claude-sonnet-4-20250514`
- `claude-3-haiku-20240307`

### Challenge 4: Position Data Complexity
**Problem:** The Lahman database stores positions as separate columns (G_c, G_1b, G_2b...) not a single field.

**Solution:** Provided a complete working example in the system prompt showing how to determine primary position using CASE statements.

---

## Application Features

### Commands
| Command | Description |
|---------|-------------|
| `quit` | Exit the application |
| `clear` | Reset conversation history |
| `usage` | Show session token usage and cost |
| `model haiku` | Switch to fast/cheap model |
| `model sonnet` | Switch to high-quality model |

### Sample Interaction
```
Baseball Stats Assistant
Ask questions about MLB statistics!
Current model: HAIKU (Fast & cheap, higher rate limits)

You: Who led the league in home runs in 1998?

[Executing SQL: SELECT TOP 5 p.nameFirst, p.nameLast, b.HR
FROM Batting b JOIN People p ON b.playerID = p.playerID
WHERE b.yearID = 1998 ORDER BY b.HR DESC]

Claude: In 1998, Mark McGwire led MLB with 70 home runs,
followed by Sammy Sosa with 66. This was the famous
home run chase that captivated baseball fans!

[HAIKU | Tokens: 1,234 in / 456 out | Cost: $0.0009]
[Session total: 2,456 in / 892 out | $0.0018]
```

---

## Code Architecture

```
Program.cs
├── Configuration
│   ├── Database connection string
│   └── API key handling
├── Tool Definitions (AIFunctionFactory)
│   ├── get_schema - Returns database structure
│   └── query_database - Executes SQL queries
├── Model Configuration
│   ├── Sonnet (quality)
│   └── Haiku (speed/cost)
├── Cost Tracking
│   └── Token counting and pricing
├── Main Chat Loop
│   ├── Command handling (quit, clear, usage, model)
│   ├── API calls with error recovery
│   └── Response display
└── DatabaseTools Class
    ├── GetSchema() - Focused schema with relationships
    └── QueryDatabase() - Safe SQL execution
```

---

## Key Takeaways

1. **Prompt Engineering is Iterative** - Start simple, add specificity as you discover what the model needs.

2. **Context is Expensive** - Every token in the conversation history is re-sent with each request. Manage it carefully.

3. **Right-Size Your Model** - Use cheaper/faster models for simple tasks, powerful models for complex reasoning.

4. **Few-Shot Examples Beat Instructions** - Showing the model a working example is often more effective than explaining what to do.

5. **Plan for Failure** - API calls can fail. Build rollback mechanisms to maintain consistent state.

6. **Schema Design Matters** - How you present data to the AI affects its ability to generate correct queries.

---

## Future Enhancements

- **Streaming Responses** - Show responses as they're generated
- **Prompt Caching** - Reduce costs by caching the schema
- **Structured Outputs** - Force JSON responses for specific use cases
- **Multiple Database Support** - Abstract the database layer

---

## Resources

- [Anthropic API Documentation](https://docs.anthropic.com/)
- [Anthropic.SDK for .NET](https://github.com/tghamm/Anthropic.SDK)
- [Lahman Baseball Database](http://www.seanlahman.com/baseball-archive/statistics/)
- [Microsoft.Extensions.AI](https://learn.microsoft.com/en-us/dotnet/ai/)

---

*Document generated from hands-on learning session with Claude Code*
