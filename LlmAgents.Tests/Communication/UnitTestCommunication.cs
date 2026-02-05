
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LlmAgents.Communication;
using LlmAgents.LlmApi.Content;

namespace LlmAgents.Tests.Communication;

public class UnitTestCommunication : IAgentCommunication
{
    public readonly List<string> Output = [];

    public readonly List<string> Input = [];

    public Task SendMessage(string message, bool newLine)
    {
        Output.Add(message);
        if (newLine)
        {
            Output.Add(Environment.NewLine);
        }
        return Task.CompletedTask;
    }

    public Task<IEnumerable<IMessageContent>?> WaitForContent(CancellationToken cancellationToken = default)
    {
        if (Input.Count < 1)
        {
            return Task.FromResult<IEnumerable<IMessageContent>?>(null);
        }

        var content = Input[0];
        Input.RemoveAt(0);

        return Task.FromResult<IEnumerable<IMessageContent>?>([new MessageContentText { Text = content }]);
    }
}
