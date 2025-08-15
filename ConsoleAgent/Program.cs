using ConsoleAgent.Commands;
using Microsoft.Extensions.Logging;
using System.CommandLine;

using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

var sessionsCommand = new SessionsCommand(loggerFactory);

var defaultCommand = new DefaultCommand(loggerFactory);
defaultCommand.AddCommand(sessionsCommand);

return await defaultCommand.InvokeAsync(args);
