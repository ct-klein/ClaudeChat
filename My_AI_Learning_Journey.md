# My AI Learning Journey: Teaching a Computer to Talk Baseball

## What I Set Out to Do

I wanted to learn how AI-powered applications actually work - not just use ChatGPT, but build something myself. So I decided to create a baseball statistics assistant: an app where I could ask questions like "Who hit the most home runs in 1998?" and get real answers from a database.

The idea was simple: I type a question in plain English, an AI figures out what database query to run, gets the data, and explains the answer back to me in natural language.

---

## The Building Blocks

I used Claude (Anthropic's AI) as the brain of my application, connected it to a comprehensive baseball database with over 100 years of MLB statistics, and wrote the connecting code in C#.

The magic happens through something called "function calling" - I can tell Claude "here are some tools you can use" and it figures out when to use them. In my case, the tools were:
1. **Get the database structure** - so Claude knows what tables and columns exist
2. **Run a database query** - so Claude can fetch actual data

When I ask "Who led the league in home runs in 1998?", Claude decides it needs to query the database, writes the appropriate query, runs it, gets back "Mark McGwire, 70 home runs", and then explains it to me in a conversational way.

---

## What I Learned Along the Way

### Lesson 1: AI Models Have Limits (And Costs)

Every question I ask costs money - not much, fractions of a penny - but it adds up. The cost is based on "tokens" (roughly, the number of words going back and forth). I learned to track this:

> "That question cost you 1,234 tokens in, 456 tokens out, about $0.01"

More importantly, there are rate limits. Ask too many questions too quickly, and the API says "slow down." This taught me that efficiency matters - one well-crafted question is better than five sloppy ones.

### Lesson 2: Different AI Models for Different Jobs

I discovered there are multiple Claude models:
- **Haiku** - Fast and cheap, good for simple questions
- **Sonnet** - Smarter but more expensive, better for complex analysis

It's like choosing between a calculator and a mathematician. For "how many home runs did Babe Ruth hit?", the calculator is fine. For "who was the best player at each position in 1999?", you want the mathematician.

I built in the ability to switch between them on the fly.

### Lesson 3: You Have to Speak the AI's Language

This was the biggest lesson. When my queries weren't working, I realized Claude was generating database code for the wrong type of database. It was like asking someone to write in British English when the reader only understands American English.

I had to be explicit in my instructions:
- "Use TOP 10 instead of LIMIT 10"
- "Use + to combine text, not ||"
- "Don't use backticks"

**The takeaway:** AI models are incredibly capable, but they need clear guidance. The more specific your instructions, the better the results.

### Lesson 4: Show, Don't Just Tell

When Claude kept struggling with a complex query about finding the best player at each position, I tried explaining what I wanted in more detail. It didn't help much.

What finally worked? I gave it a complete working example. Instead of explaining the logic, I showed it: "Here's exactly what a working query looks like for this type of question."

The AI could then adapt that pattern to similar questions. This is called "few-shot prompting" - giving examples rather than just instructions.

### Lesson 5: Plan for Things Going Wrong

API calls can fail - network issues, rate limits, the AI getting confused mid-response. I learned to build in recovery mechanisms. If something goes wrong, roll back to a clean state rather than leaving things broken.

---

## The Challenges I Faced

**Challenge: Running out of API quota**
My complex questions were using too many tokens. Solution: I trimmed down the database information I was sending - instead of describing all 28 tables, I focused on the 11 most important ones for baseball questions.

**Challenge: Wrong database syntax**
Claude kept writing queries that didn't work with my database type. Solution: Added explicit rules to my instructions and provided working examples.

**Challenge: Questions with no good answer**
Some questions required Claude to make multiple attempts before getting it right. Solution: Added better error handling so failed attempts didn't break the conversation.

---

## What the Final Product Does

My baseball assistant can:
- Answer natural language questions about MLB history
- Show me the actual database queries it's running (so I can learn SQL too!)
- Track how much each conversation costs
- Switch between faster/cheaper and smarter/pricier AI modes
- Recover gracefully when things go wrong

Example conversation:
> **Me:** Who led the league in home runs in 1998?
>
> **Assistant:** In 1998, Mark McGwire led MLB with 70 home runs, followed by Sammy Sosa with 66. This was the famous home run chase that captivated baseball fans throughout the summer!

---

## The Bigger Picture

This project taught me that integrating AI into applications isn't magic - it's engineering. You need to:

1. **Understand the costs** - Every API call has a price
2. **Choose the right tool** - Not every question needs the most powerful model
3. **Be specific** - Vague instructions get vague results
4. **Show examples** - Demonstrating is often better than explaining
5. **Handle failures** - Things will go wrong; plan for it

The technology is powerful, but it requires thoughtful implementation. The AI doesn't "just work" - you have to guide it, constrain it, and design around its limitations.

---

## What's Next

I'm planning to explore:
- **Streaming responses** - See the AI's answer as it types, not all at once
- **Caching** - Reduce costs by remembering common information
- **Other databases** - Apply the same patterns to different data sources

The skills I learned here - prompt engineering, token management, error handling, model selection - apply to any AI integration project, not just baseball statistics.

---

*This journey started with a simple question: "Can I build something that talks to an AI?" The answer is yes - and the learning along the way was the best part.*
