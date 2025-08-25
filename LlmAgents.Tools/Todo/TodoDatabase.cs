namespace LlmAgents.Tools.Todo;

using LlmAgents.State;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

public class TodoDatabase
{
    private readonly ILogger Log;

    private readonly StateDatabase stateDatabase;

    public TodoDatabase(ILoggerFactory LoggerFactory, StateDatabase stateDatabase)
    {
        Log = LoggerFactory.CreateLogger(nameof(TodoDatabase));

        this.stateDatabase = stateDatabase;

        Initialize();
    }

    public bool CreateTodo(Session session, string name, string group, string? description = null, int priority = 10)
    {
        try
        {
            var todoGroup = GetGroup(session, group);
            if (todoGroup == null)
            {
                return false;
            }

            stateDatabase.Write(command =>
            {
                command.CommandText = "INSERT INTO todo_items (title, description, group_id, due_date, priority, session_id) VALUES ($title, $description, $group_id, $due_date, $priority, $sessionId);";
                command.Parameters.AddWithValue("$title", name);
                command.Parameters.AddWithValue("$description", description ?? string.Empty);
                command.Parameters.AddWithValue("$group_id", todoGroup.id);
                command.Parameters.AddWithValue("$due_date", string.Empty);
                command.Parameters.AddWithValue("$priority", priority);
                command.Parameters.AddWithValue("$sessionId", session.SessionId);
                command.ExecuteNonQuery();
            });

            return true;
        }
        catch (Exception e)
        {
            Log.LogError(e, "Exception while creating todo");
            return false;
        }
    }

    public Todo? GetTodo(Session session, string title, string group)
    {
        try
        {
            var todoGroup = GetGroup(session, group);
            if (todoGroup == null)
            {
                return null;
            }

            Todo? todo = null;
            stateDatabase.Write(command =>
            {
                command.CommandText = "SELECT * FROM todo_items WHERE title = $title AND group_id = $groupId AND session_id = $sessionId;";
                command.Parameters.AddWithValue("$title", title);
                command.Parameters.AddWithValue("$groupId", todoGroup.id);
                command.Parameters.AddWithValue("$sessionId", session.SessionId);

                using var reader = command.ExecuteReader();
                if (!reader.Read())
                {
                    Log.LogInformation("Could not find todo: title={title}, group={group}", title, group);
                    return;
                }

                todo = new Todo
                {
                    id = reader.GetInt32(0),
                    groupId = reader.GetInt32(1),
                    title = reader.GetString(2),
                    description = reader.GetString(3),
                    dueDate = reader.GetString(4),
                    completed = reader.GetBoolean(5),
                };

                if (reader.Read())
                {
                    Log.LogInformation("Found more than one todo: title={title}, group={group}", title, group);
                    return;
                }
            });

            return todo;
        }
        catch (Exception e)
        {
            Log.LogError(e, "Exception while getting todo");
            return null;
        }
    }

    public bool UpdateTodo(Session session, string title, string group, string? newTitle = null, string? newGroup = null, string? newDescription = null, string? newDueDate = null, bool? newCompleted = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(title);
        ArgumentException.ThrowIfNullOrEmpty(group);

        if (newTitle == null && newGroup == null && newDescription == null && newDueDate == null && newCompleted == null)
        {
            // nothing to update
            return true;
        }

        try
        {
            var todoGroup = GetGroup(session, group);
            if (todoGroup == null)
            {
                return false;
            }

            TodoGroup? newTodoGroup = null;
            if (newGroup == null)
            {
                newTodoGroup = todoGroup;
            }
            else if (string.Equals(newGroup, group))
            {
                newTodoGroup = todoGroup;
            }
            else
            {
                newTodoGroup = GetGroup(session, newGroup);
            }

            if (newTodoGroup == null)
            {
                return false;
            }

            var todo = GetTodo(session, title, group);
            if (todo == null)
            {
                Log.LogInformation("Could not find todo: title={title}, group={group}", title, group);
                return false;
            }

            stateDatabase.Write(command =>
            {
                command.CommandText = "UPDATE todo_items SET title = $newTitle, description = $newDescription, due_date = $newDueDate, completed = $newCompleted WHERE title = $title AND session_id = $sessionId;";
                command.Parameters.AddWithValue("$title", title);
                command.Parameters.AddWithValue("$newTitle", newTitle ?? todo.title);
                command.Parameters.AddWithValue("$newDescription", newDescription ?? todo.description);
                command.Parameters.AddWithValue("$newDueDate", newDueDate ?? todo.dueDate);
                command.Parameters.AddWithValue("$newCompleted", newCompleted ?? todo.completed);
                command.Parameters.AddWithValue("$sessionId", session.SessionId);
                command.ExecuteNonQuery();
            });

            return true;
        }
        catch (Exception e)
        {
            Log.LogError(e, "Exception while updating todo");
            return false;
        }
    }

    public bool DeleteTodo(Session session, string title, string group)
    {
        try
        {
            var todoGroup = GetGroup(session, group);
            if (todoGroup == null)
            {
                return false;
            }

            stateDatabase.Write(command =>
            {
                command.CommandText = "DELETE FROM todo_items WHERE title = $title AND group_id = $groupId AND session_id = $sessionId;";
                command.Parameters.AddWithValue("$title", title);
                command.Parameters.AddWithValue("$groupId", todoGroup.id);
                command.Parameters.AddWithValue("$sessionId", session.SessionId);
                command.ExecuteNonQuery();
            });

            return true;
        }
        catch (Exception e)
        {
            Log.LogError(e, "Exception while deleting todo");
            return false;
        }
    }

    public bool CreateGroup(Session session, string name, string? description = null)
    {
        try
        {
            stateDatabase.Write(command =>
            {
                command.CommandText = "INSERT INTO todo_groups (name, description, session_id) VALUES ($name, $description, $sessionId);";
                command.Parameters.AddWithValue("$name", name);
                command.Parameters.AddWithValue("$description", description ?? string.Empty);
                command.Parameters.AddWithValue("$sessionId", session.SessionId);
                command.ExecuteNonQuery();
            });

            return true;
        }
        catch (Exception e)
        {
            Log.LogError(e, "Exception while creating todo group");
            return false;
        }
    }

    public TodoGroup? GetGroup(Session session, string name, bool getTodos = false)
    {
        try
        {
            int groupId = -1;
            string? groupName = null;
            string? groupDescription = null;
            stateDatabase.Read(command =>
            {
                command.CommandText = "SELECT * FROM todo_groups WHERE name = $name AND session_id = $sessionId;";
                command.Parameters.AddWithValue("$name", name);
                command.Parameters.AddWithValue("$sessionId", session.SessionId);

                using var reader = command.ExecuteReader();
                if (!reader.Read())
                {
                    Log.LogInformation("Could not find group: {name}", name);
                    return;
                }

                groupId = reader.GetInt32(0);
                groupName = reader.GetString(1);
                groupDescription = reader.GetString(2);

                if (reader.Read())
                {
                    Log.LogInformation("More than one group found: {name}", name);
                    return;
                }
            });

            if (groupId == -1 || groupName == null || groupDescription == null)
            {
                return null;
            }

            List<Todo> groupTodos = [];
            stateDatabase.Read(command =>
            {
                command.CommandText = "SELECT * FROM todo_items WHERE group_id = $groupId AND session_id = $sessionId";
                command.Parameters.AddWithValue("$groupId", groupId);
                command.Parameters.AddWithValue("$sessionId", session.SessionId);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    groupTodos.Add(new Todo
                    {
                        id = reader.GetInt32(0),
                        groupId = reader.GetInt32(1),
                        title = reader.GetString(2),
                        description = reader.GetString(3),
                        dueDate = reader.GetString(4),
                        completed = reader.GetBoolean(5),
                    });
                }
            });

            return new TodoGroup
            {
                id = groupId,
                name = groupName,
                description = groupDescription,
                todos = groupTodos.ToArray()
            };
        }
        catch (Exception e)
        {
            Log.LogError(e, "Exception while getting todo group");
            return null;
        }
    }

    public bool UpdateGroup(Session session, string name, string? newName, string? newDescription)
    {
        try
        {
            stateDatabase.Write(command =>
            {
                command.CommandText = "UPDATE todo_groups SET name = $newName, description = $newDescription WHERE name = $name AND session_id = $sessionId;";
                command.Parameters.AddWithValue("$name", name);
                command.Parameters.AddWithValue("$newName", newName);
                command.Parameters.AddWithValue("$newDescription", newDescription ?? string.Empty);
                command.Parameters.AddWithValue("$sessionId", session.SessionId); 
                command.ExecuteNonQuery();
            });

            return true;
        }
        catch (Exception e)
        {
            Log.LogError(e, "Exception while updating todo group");
            return false;
        }
    }

    public bool DeleteGroup(Session session, string name)
    {
        try
        {
            stateDatabase.Write(command =>
            {
                command.CommandText = "DELETE FROM todo_groups WHERE name = $name AND session_id = $sessionId;";
                command.Parameters.AddWithValue("$name", name);
                command.Parameters.AddWithValue("$sessionId", session.SessionId);
                command.ExecuteNonQuery();
            });

            return true;
        }
        catch (Exception e)
        {
            Log.LogError(e, "Exception while deleting todo group");
            return false;
        }
    }

    public TodoGroup[]? ListGroups(Session session, bool getTodos = false)
    {
        try
        {
            if (getTodos)
            {
                Dictionary<int, List<Todo>> groupTodos = [];
                stateDatabase.Read(command =>
                {
                    command.CommandText = "SELECT * FROM todo_items WHERE session_id = $sessionId;";
                    command.Parameters.AddWithValue("$sessionId", session.SessionId);

                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        var todoId = reader.GetInt32(0);
                        var todoGroup = reader.GetInt32(1);
                        var todoTitle = reader.GetString(2);
                        var todoDescription = reader.GetString(3);
                        var todoDueDate = reader.GetString(4);
                        var todoCompleted = reader.GetBoolean(5);
                        var todoPriority = reader.GetInt32(6);

                        if (!groupTodos.TryGetValue(todoGroup, out List<Todo>? value))
                        {
                            value = [];
                            groupTodos.Add(todoGroup, value);
                        }

                        value.Add(new Todo
                        {
                            id = todoId,
                            groupId = todoGroup,
                            title = todoTitle,
                            description = todoDescription,
                            dueDate = todoDueDate,
                            completed = todoCompleted,
                        });
                    }
                });

                List<TodoGroup> groups = [];
                foreach (var kvp in groupTodos)
                {
                    stateDatabase.Read(command =>
                    {
                        command.CommandText = "SELECT * FROM todo_groups WHERE id = $id AND session_id = $sessionId;";
                        command.Parameters.AddWithValue("$id", kvp.Key);
                        command.Parameters.AddWithValue("$sessionId", session.SessionId);

                        using var reader = command.ExecuteReader();

                        if (!reader.Read())
                        {
                            Log.LogInformation("Could not find group with id: {id}", kvp.Key);
                            return;
                        }

                        var group = new TodoGroup
                        {
                            id = reader.GetInt32(0),
                            name = reader.GetString(1),
                            description = reader.GetString(2),
                            todos = kvp.Value.ToArray()
                        };

                        if (reader.Read())
                        {
                            Log.LogInformation("More than one result found for id: {id}", kvp.Key);
                            return;
                        }

                        groups.Add(group);
                    });
                }

                return groups.ToArray();
            }
            else
            {
                List<TodoGroup> groups = [];
                stateDatabase.Read(command =>
                {
                    command.CommandText = "SELECT * FROM todo_groups WHERE session_id = $sessionId;";
                    command.Parameters.AddWithValue("$sessionId", session.SessionId);

                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        groups.Add(new TodoGroup
                        {
                            id = reader.GetInt32(0),
                            name = reader.GetString(1),
                            description = reader.GetString(2),
                            todos = []
                        });
                    }
                });

                return groups.ToArray();
            }
        }
        catch (Exception e)
        {
            Log.LogError(e, "Exception while listing todo groups");
            return null;
        }
    }

    private bool TableExists(string table)
    {
        var exists = false;
        try
        {
            stateDatabase.Read(command =>
            {
                command.CommandText = "SELECT 1 FROM sqlite_master where type = 'table' AND name = $table;";
                command.Parameters.AddWithValue("$table", table);
                var result = command.ExecuteScalar();
                exists = result != null;
            });
        }
        catch (Exception e)
        {
            Log.LogError(e, "Could not determine if table exists: {table}", table);
        }

        return exists;
    }

    private void Initialize()
    {
        var tablesCreated = TableExists("todo_groups") || TableExists("todo_items");
        if (tablesCreated)
        {
            return;
        }

        stateDatabase.Write(command =>
        {
            var schema =
@"CREATE TABLE todo_groups (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL UNIQUE,
    description TEXT,
    session_id TEXT NOT NULL,
    FOREIGN KEY (session_id) REFERENCES sessions (session_id) ON DELETE CASCADE
);

CREATE TABLE todo_items (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    group_id INTEGER NOT NULL,
    title TEXT NOT NULL,
    description TEXT,
    due_date DATETIME,
    completed BOOLEAN DEFAULT 0,
    priority INTEGER,
    session_id TEXT NOT NULL,
    FOREIGN KEY (group_id) REFERENCES todo_groups (id) ON DELETE CASCADE,
    FOREIGN KEY (session_id) REFERENCES sessions (session_id) ON DELETE CASCADE,
    UNIQUE (group_id, title)
);
";

            command.CommandText = schema;
            command.ExecuteNonQuery();
        });
    }
}
