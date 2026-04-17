using ConsoleAgent.Commands;
using Microsoft.Extensions.Logging;
using System.CommandLine;

using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

var defaultCommand = new DefaultCommand(loggerFactory);

var sessionsCommand = new SessionsCommand(loggerFactory);
defaultCommand.Add(sessionsCommand);

var invocationConfiguration = new InvocationConfiguration
{
    ProcessTerminationTimeout = null
};

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (sender, eventArgs) =>
{
    eventArgs.Cancel = true;
    cts.Cancel();
};

return await defaultCommand.Parse(args).InvokeAsync(invocationConfiguration, cts.Token);
