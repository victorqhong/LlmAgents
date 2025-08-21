namespace LlmAgents.Tools.Events;

internal class ChangeDirectoryEvent : ToolEvent
{
    public required string Directory;
}