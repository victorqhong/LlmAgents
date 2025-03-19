namespace LlmAgents.Todo;

public class TodoGroup
{
    public required int id;

    public required string name;

    public required string? description;

    public required Todo[] todos;
}
