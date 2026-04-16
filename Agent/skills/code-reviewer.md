---
name: code-reviewer
description: Performs structured code reviews with categorized feedback, security checks, and improvement suggestions.
version: "1.0"
tags: [code, review, quality]
triggers: ["review this code", "code review", "check PR", "review the code", "analyze code"]
---

# Code Review Skill

## When to Use This Skill
Use this skill whenever the user asks to review code, a pull request, or a specific file/function. Always activate it for changes involving security, performance, or maintainability.

## Step-by-Step Workflow

### 1. Understand the Context
- Read the provided code or diff carefully
- Identify the programming language and framework
- Note the purpose of the changes
- Check for related files (tests, configuration, documentation)

### 2. Analyze Categories
Evaluate the code across these dimensions:

**Correctness & Bugs**
- Logic errors and edge cases
- Missing null checks or validation
- Error handling issues
- Off-by-one errors in loops/arrays

**Security**
- SQL injection vulnerabilities
- Authentication/authorization issues
- Exposure of secrets or sensitive data
- Input validation
- Race conditions

**Performance**
- Inefficient loops or algorithms
- Memory leaks
- Unnecessary allocations
- Database query optimization

**Readability & Style**
- Clear naming conventions
- Appropriate comments
- Consistency with project standards
- Code organization

**Best Practices**
- Language idioms and patterns
- Framework conventions
- Testability
- Maintainability

### 3. Provide Feedback
Structure your output as:

```
## Summary
[One paragraph overview of the changes and overall quality]

## Issues Found

### 🔴 Critical
[List any critical security or correctness issues - must be fixed]

### 🟠 High
[List significant issues that should be addressed]

### 🟡 Medium
[List minor issues or suggestions for improvement]

### 🟢 Low
[List optional improvements or nice-to-haves]

## Positive Aspects
[What was done well]

## Suggested Improvements
[Specific suggestions with code examples where helpful]
```

### 4. Output Format
- Use Markdown with clear sections and code blocks
- Quote relevant code snippets when discussing issues
- Be constructive and specific
- Prioritize issues by severity

## Constraints / Rules
- Never approve obviously insecure code without flagging it
- Suggest tests when relevant
- Keep feedback actionable and specific
- Balance criticism with recognition of good practices
- Consider the context and constraints the developer was working under