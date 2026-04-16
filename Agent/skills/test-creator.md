---
name: test-creator
description: Creates comprehensive test suites following best practices for unit, integration, and end-to-end testing.
version: "1.0"
tags: [testing, QA, TDD]
triggers: ["write tests", "test", "unit test", "add tests", "test coverage", "TDD"]
---

# Test Creation Skill

## When to Use This Skill
Use this skill whenever you need to create or improve tests. Follow this framework to write comprehensive, maintainable test coverage.

## Step-by-Step Workflow

### 1. Understand What to Test
- Identify the function/module under test
- Understand its responsibilities and contracts
- Review existing tests to avoid duplication
- Identify edge cases and boundary conditions

### 2. Determine Test Types
Choose appropriate testing levels:

**Unit Tests**
- Test individual functions/methods in isolation
- Mock external dependencies
- Fast execution, focused assertions

**Integration Tests**
- Test component interactions
- Real database/services where needed
- Verify data flow between modules

**End-to-End Tests**
- Test complete user workflows
- Simulate real user interactions
- Verify system behavior as a whole

### 3. Write Tests Following Conventions
Follow this structure:

```language
[Describe the test class/module]

[Setup/Teardown if needed]

## Happy Path Tests
- Test primary functionality
- Verify expected outputs

## Edge Case Tests
- Empty/null inputs
- Boundary values
- Maximum sizes

## Error Handling Tests
- Invalid inputs
- Exceptional conditions
- Error recovery

## Edge Cases
- Race conditions
- Timeout scenarios
- Concurrency issues
```

### 4. Ensure Quality
- **Descriptive names**: Test names should describe what they verify
- **Single responsibility**: Each test should verify one behavior
- **Arrange-Act-Assert**: Structure tests clearly
- **Independent**: Tests should not depend on each other
- **Deterministic**: Same input always produces same result

## Test Coverage Goals
- Aim for meaningful coverage, not 100%
- Prioritize critical paths and edge cases
- Test error paths equally with success paths

## Output Format
```
## Test Plan

### Files to Create/Modify
[List files to create or update]

### Test Cases

#### [Test Name]
- **Purpose**: [What this test verifies]
- **Input**: [Test input data]
- **Expected**: [Expected outcome]
- **Code**: [Actual test code]
```

## Constraints / Rules
- Follow existing test conventions in the project
- Use descriptive test names that explain the scenario
- Include comments explaining non-obvious test logic
- Always test both success and failure paths
- Clean up any test data/state after tests