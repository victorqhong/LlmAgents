using System.Linq;
using LlmAgents.Agents.Autonomy;
using LlmAgents.State;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LlmAgents.Tests;

[TestClass]
public class AutonomyTaskStoreTests
{
    private static readonly ILoggerFactory LoggerFactory = new LoggerFactory();

    [TestMethod]
    public void EnqueueTask_RoundTripsWithSteps()
    {
        using var db = new StateDatabase(LoggerFactory, ":memory:");
        var store = new AutonomousTaskStore(LoggerFactory, db);
        var coordinator = new AutonomyCoordinator(store);

        var task = coordinator.EnqueueTaskFromUserInput("Implement autonomous execution", "test-agent", "session-1");
        var loaded = store.GetTask(task.Id);

        Assert.IsNotNull(loaded);
        Assert.AreEqual(TaskState.Pending, loaded.State);
        Assert.AreEqual("test-agent", loaded.AgentId);
        Assert.AreEqual(3, loaded.Steps.Count);
    }

    [TestMethod]
    public void AcquireTask_MarksTaskRunning()
    {
        using var db = new StateDatabase(LoggerFactory, ":memory:");
        var store = new AutonomousTaskStore(LoggerFactory, db);
        var coordinator = new AutonomyCoordinator(store);

        var task = coordinator.EnqueueTaskFromUserInput("Run task", "test-agent");
        var acquired = store.TryAcquireNextRunnableTask();
        var loaded = store.GetTask(task.Id);

        Assert.IsNotNull(acquired);
        Assert.AreEqual(task.Id, acquired.Id);
        Assert.IsNotNull(loaded);
        Assert.AreEqual(TaskState.Running, loaded.State);
    }

    [TestMethod]
    public void CancelAndResumeTask_TransitionsState()
    {
        using var db = new StateDatabase(LoggerFactory, ":memory:");
        var store = new AutonomousTaskStore(LoggerFactory, db);
        var coordinator = new AutonomyCoordinator(store);

        var task = coordinator.EnqueueTaskFromUserInput("Run task", "test-agent");
        var cancelledTask = store.CancelTask(task.Id);
        var cancelled = store.GetTask(task.Id);

        Assert.IsTrue(cancelledTask);
        Assert.IsNotNull(cancelled);
        Assert.AreEqual(TaskState.Cancelled, cancelled.State);
        Assert.IsTrue(cancelled.Steps.All(step => step.State == TaskStepState.Cancelled));

        var resumedTask = store.ResumeTask(task.Id);
        var resumed = store.GetTask(task.Id);

        Assert.IsTrue(resumedTask);
        Assert.IsNotNull(resumed);
        Assert.AreEqual(TaskState.Pending, resumed.State);
        Assert.IsTrue(resumed.Steps.All(step => step.State == TaskStepState.Pending));
    }

    [TestMethod]
    public void ListTaskEvents_ReturnsLifecycleEvents()
    {
        using var db = new StateDatabase(LoggerFactory, ":memory:");
        var store = new AutonomousTaskStore(LoggerFactory, db);
        var coordinator = new AutonomyCoordinator(store);

        var task = coordinator.EnqueueTaskFromUserInput("Run task", "test-agent");
        store.CancelTask(task.Id);
        store.ResumeTask(task.Id);
        var events = store.ListTaskEvents(task.Id, 10);

        CollectionAssert.IsSubsetOf(
            new[] { "task_created", "task_enqueued", "task_cancelled", "task_resumed" },
            events.Select(taskEvent => taskEvent.EventType).ToArray());
    }

    [TestMethod]
    public void StepRetry_IncrementsRetryCountWithWaitingState()
    {
        using var db = new StateDatabase(LoggerFactory, ":memory:");
        var store = new AutonomousTaskStore(LoggerFactory, db);
        var coordinator = new AutonomyCoordinator(store);

        var task = coordinator.EnqueueTaskFromUserInput("Run task", "test-agent");
        var step = task.Steps.First();

        store.MarkStepRunning(task.Id, step.Id);
        var retryCount = store.MarkStepWaitingForRetry(task.Id, step.Id, "Transient failure");
        var loaded = store.GetTask(task.Id);

        Assert.AreEqual(1, retryCount);
        Assert.IsNotNull(loaded);
        var loadedStep = loaded.Steps.First(s => s.Id == step.Id);
        Assert.AreEqual(TaskStepState.Waiting, loadedStep.State);
        Assert.AreEqual(1, loadedStep.RetryCount);
        Assert.AreEqual(step.Id, loaded.CurrentStepId);
    }
}
