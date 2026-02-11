using ConsoleAgent.Commands;
using Microsoft.Extensions.Logging;

using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

var sessionsCommand = new SessionsCommand(loggerFactory);

var defaultCommand = new DefaultCommand(loggerFactory);
defaultCommand.Add(sessionsCommand);

return await defaultCommand.Parse(args).InvokeAsync();
