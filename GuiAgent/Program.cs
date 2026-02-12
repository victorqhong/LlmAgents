using GuiAgent.Commands;
using Microsoft.Extensions.Logging;

using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
var defaultCommand = new DefaultCommand(loggerFactory);

return await defaultCommand.Parse(args).InvokeAsync();
