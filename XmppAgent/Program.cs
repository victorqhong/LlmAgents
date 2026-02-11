using Microsoft.Extensions.Logging;
using XmppAgent.Commands;

using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

var agentCommand = new AgentCommand(loggerFactory);

var defaultCommand = new DefaultCommand(loggerFactory);
defaultCommand.Add(agentCommand);

return await defaultCommand.Parse(args).InvokeAsync();
