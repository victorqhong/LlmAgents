using System;
using LlmAgents.Agents.Autonomy;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LlmAgents.Tests;

[TestClass]
public class AutonomousTaskGuardrailsTests
{
    [TestMethod]
    public void NormalizePolicy_UsesDefaultsForNonPositiveValues()
    {
        var normalized = AutonomousTaskGuardrails.NormalizePolicy(new TaskPolicy
        {
            MaxRetriesPerStep = 0,
            MaxToolCalls = -1,
            MaxTokens = 0,
            MaxRuntimeMinutes = -5
        });

        Assert.AreEqual(3, normalized.MaxRetriesPerStep);
        Assert.AreEqual(200, normalized.MaxToolCalls);
        Assert.AreEqual(200000, normalized.MaxTokens);
        Assert.AreEqual(240, normalized.MaxRuntimeMinutes);
    }

    [TestMethod]
    public void RetryBackoff_GrowsExponentiallyAndCaps()
    {
        Assert.AreEqual(TimeSpan.FromSeconds(1), AutonomousTaskGuardrails.GetRetryBackoffDelay(1));
        Assert.AreEqual(TimeSpan.FromSeconds(2), AutonomousTaskGuardrails.GetRetryBackoffDelay(2));
        Assert.AreEqual(TimeSpan.FromSeconds(4), AutonomousTaskGuardrails.GetRetryBackoffDelay(3));
        Assert.AreEqual(TimeSpan.FromSeconds(32), AutonomousTaskGuardrails.GetRetryBackoffDelay(7));
    }

    [TestMethod]
    public void Guardrails_TrackRuntimeAndBudgets()
    {
        var startedAt = new DateTime(2024, 01, 01, 00, 00, 00, DateTimeKind.Utc);
        var guardrails = new AutonomousTaskGuardrails(new TaskPolicy
        {
            MaxRuntimeMinutes = 1,
            MaxToolCalls = 2,
            MaxTokens = 10
        }, startedAt);

        guardrails.RecordToolCallCycle();
        guardrails.RecordToolCallCycle();
        guardrails.RecordToolCallCycle();
        guardrails.RecordTokenUsage(8);
        guardrails.RecordTokenUsage(3);

        Assert.IsTrue(guardrails.IsToolCallBudgetExceeded());
        Assert.IsTrue(guardrails.IsTokenBudgetExceeded());
        Assert.IsFalse(guardrails.IsDeadlineExceeded(startedAt.AddSeconds(30)));
        Assert.IsTrue(guardrails.IsDeadlineExceeded(startedAt.AddMinutes(2)));
    }
}
