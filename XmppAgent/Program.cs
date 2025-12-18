using Microsoft.Extensions.Logging;
using System.CommandLine;
using XmppAgent.Commands;

using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

var agentCommand = new AgentCommand(loggerFactory);

var defaultCommand = new DefaultCommand(loggerFactory);
defaultCommand.AddCommand(agentCommand);

return await defaultCommand.InvokeAsync(args);
