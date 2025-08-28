using ConsoleAgent.Commands;
using Microsoft.Extensions.Logging;
using System.CommandLine;

using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

var sessionsNewCommand = new SessionsNewCommand(loggerFactory);
var sessionsCommand = new SessionsCommand(loggerFactory)
{
    sessionsNewCommand
};

var agentsSetDefault = new AgentsSetDefault(loggerFactory);
var agentsGetDefault = new AgentsGetDefault(loggerFactory);
var agentsCommand = new AgentsCommand(loggerFactory)
{
    agentsSetDefault,
    agentsGetDefault
};

var defaultCommand = new DefaultCommand(loggerFactory)
{
    sessionsCommand,
    agentsCommand
};

return await defaultCommand.InvokeAsync(args);
