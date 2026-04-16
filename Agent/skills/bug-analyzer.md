---
name: bug-analyzer
description: Performs systematic bug analysis with root cause identification and fix recommendations.
version: "1.0"
tags: [bug, debug, analysis]
triggers: ["bug", "crash", "error", "exception", "not working", "fix this", "debug", "broken"]
---

# Bug Analysis Skill

## When to Use This Skill
Use this skill whenever the user reports a bug, crash, error, or unexpected behavior. Follow this systematic approach to identify and diagnose issues.

## Step-by-Step Workflow

### 1. Gather Information
- Get the error message or stack trace
- Identify when the bug occurs (user action, time, conditions)
- Note the environment (OS, version, configuration)
- Check if the bug is reproducible

### 2. Analyze the Error
- Parse the stack trace to find the failure point
- Identify the exception type and message
- Trace back through call stack to understand context

### 3. Identify Root Causes
Consider these common categories:

**Logic Errors**
- Wrong conditionals or operators
- Incorrect assumptions about data
- Off-by-one errors
- State management issues

**Data Issues**
- Null/undefined values
- Type mismatches
- Invalid input validation
- Corrupted data

**Environment Issues**
- Missing dependencies
- Configuration problems
- Resource exhaustion
- Permission issues

**Concurrency Issues**
- Race conditions
- Deadlocks
- Shared state conflicts

### 4. Provide Fix Recommendations
Structure your output as:

```
## Bug Summary
[Brief description of the problem]

## Error Analysis
```
[Relevant error message or stack trace]
```

## Root Cause(s)
[Explain what is causing the bug]

## Recommended Fix
[Specific code changes or actions to fix the issue]

## Prevention
[Suggestions to prevent similar bugs in the future]
- Testing strategies
- Code patterns
- Validation checks
```

## Constraints / Rules
- Always provide specific, actionable fixes
- Include corrected code where possible
- Suggest tests to verify the fix
- Consider edge cases and similar patterns in the codebase