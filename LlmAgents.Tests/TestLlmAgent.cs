using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Simulation.Tests;

[TestClass]
public sealed class TestLlmAgent
{
    [TestMethod]
    [Timeout(10000)]
    public async Task TestWork()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var userInput1 = new FlexibleAgentWork("user_input")
        {
            WorkFunction = ct =>
            {
                ct.ThrowIfCancellationRequested();

                return Task.FromResult<ICollection<JObject>>([JObject.FromObject(new { role = "user", content = "hi" })]);
            },
            OnCompletedFunction = (messages, ct) =>
            {
                ct.ThrowIfCancellationRequested();

                var m = messages.ToArray();
                Assert.IsTrue(m.Length == 1);
                Assert.AreEqual("user", m[0].Value<string>("role"));
                Assert.AreEqual("hi", m[0].Value<string>("content"));

                tcs.SetResult();

                return Task.CompletedTask;
            },
            GetStateFunction = ct => Task.FromResult<ICollection<JObject>?>(null)
        };

        var cts = new CancellationTokenSource();
        _ = userInput1.StartAsync(cts.Token);

        _ = Task.Run(async () =>
        {
            await Task.Delay(9000);
            cts.Cancel();
        });

        await tcs.Task;
    }

    [TestMethod]
    [Timeout(100_000)]
    public async Task TestBackgroundWork()
    {
        var countingWork = new CountingWork();

        var cts = new CancellationTokenSource();
        _ = countingWork.StartAsync(cts.Token);
        _ = Task.Run(async () =>
        {
            await Task.Delay(20_000);
            cts.Cancel();
        });

        var progress1 = await countingWork.GetState(cts.Token);
        Console.WriteLine(new JArray(progress1));

        await Task.Delay(5_000, cts.Token);

        var progress2 = await countingWork.GetState(cts.Token);
        Console.WriteLine(new JArray(progress2));

        Assert.Fail();
    }

    // [TestMethod]
    // public async Task TestConversation()
    // {
    //     var tasks = new List<AgentWork>();
    //
    //     var userInput1 = new AgentWork
    //     {
    //         OperationName = "user_input",
    //         Work = async _ =>
    //         {
    //             IEnumerable<JObject> result = [JObject.FromObject(new { role = "user", content = "hi" })];
    //             return result;
    //         },
    //         OnCompleted = async (messages, cancellationToken) => {}
    //     };
    //
    //     var processInput1 = new AgentWork
    //     {
    //         OperationName = "process_input",
    //         Work = async ct =>
    //         {
    //             return [JObject.FromObject(new { role = "assistant", content = "hi how can I help?" })];
    //         },
    //         OnCompleted = async (messages, ct) => {}
    //     };
    //
    //     var userInput2 = new AgentWork
    //     {
    //         OperationName = "user_input",
    //         Work = async ct =>
    //         {
    //             return [JObject.FromObject(new { role = "user", content = "count to 10" })];
    //         },
    //         OnCompleted = async (messages, ct) => {}
    //     };
    //
    //     var processInput2 = new AgentWork
    //     {
    //         OperationName = "process_input",
    //         Work = async ct =>
    //         {
    //             await Task.Delay(10_000, ct);
    //             return [JObject.FromObject(new { role = "assistant", content = "okay i've counted to 10. what now?" })];
    //         },
    //         OnCompleted = async (messages, ct) => {}
    //     };
    //
    //     tasks.Add(userInput1);
    //     tasks.Add(processInput1);
    //     tasks.Add(userInput2);
    //     tasks.Add(processInput2);
    //
    //     var messages = tasks.SelectMany(work => work.Messages);
    //     var conversation = new JArray(messages.ToArray());
    //
    //     Console.WriteLine(conversation);
    //     Assert.IsTrue(conversation.Count > 0);
    // }
}

internal abstract class AgentWork(string operationName)
{
    public readonly string OperationName = operationName;

    public abstract Task<ICollection<JObject>> Work(CancellationToken ct);

    public abstract Task OnCompleted(ICollection<JObject> messages, CancellationToken ct);

    public abstract Task<ICollection<JObject>?> GetState(CancellationToken ct);

    public ICollection<JObject>? Result { get; private set; }

    public bool Completed { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Result = await Work(cancellationToken).ConfigureAwait(false);
        Completed = true;
        await OnCompleted(Result, cancellationToken).ConfigureAwait(false);
    }
}

internal class FlexibleAgentWork(string operationName) : AgentWork(operationName)
{
    public required Func<CancellationToken, Task<ICollection<JObject>>> WorkFunction { get; init; }

    public required Func<ICollection<JObject>, CancellationToken, Task> OnCompletedFunction { get; init; }

    public required Func<CancellationToken, Task<ICollection<JObject>?>> GetStateFunction { get; init; }

    public override async Task<ICollection<JObject>?> GetState(CancellationToken ct)
    {
        return await GetStateFunction(ct);
    }

    public override async Task OnCompleted(ICollection<JObject> messages, CancellationToken ct)
    {
        await OnCompletedFunction(messages, ct);
    }

    public override async Task<ICollection<JObject>> Work(CancellationToken ct)
    {
        return await WorkFunction(ct);
    }
}

internal class CountingWork : AgentWork
{
    private volatile int index;

    public CountingWork()
        : base("counting")
    {
    }

    public override Task<ICollection<JObject>?> GetState(CancellationToken ct)
    {
        return Task.FromResult<ICollection<JObject>?>([JObject.FromObject(new { role = "assistant", content = $"i've counted to {index}" })]);
    }

    public override Task OnCompleted(ICollection<JObject> messages, CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    public override async Task<ICollection<JObject>> Work(CancellationToken ct)
    {
        for (index = 0; index < 100; index++)
        {
            await Task.Delay(1000, ct);
        }

        return [JObject.FromObject(new { role = "assistant", content = "i've finished counting to 100" })];
    }
}

