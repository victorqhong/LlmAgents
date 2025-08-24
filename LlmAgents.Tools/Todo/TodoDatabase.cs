namespace LlmAgents.Tools.Todo;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

public class TodoDatabase
{
    private readonly ILogger Log;

    public readonly string Database;

    private readonly SqliteConnection readConnection;

    private readonly SqliteConnection writeConnection;

    public TodoDatabase(ILoggerFactory LoggerFactory, string database)
    {
        Database = database;

        Log = LoggerFactory.CreateLogger(nameof(TodoDatabase));

        readConnection = CreateConnection();
        writeConnection = CreateConnection();

        if (readConnection == null || writeConnection == null)
        {
            throw new ApplicationException("Could not initialize database connections");
        }

        Initialize();
    }

    public bool CreateTodo(string name, string group, string? description = null, int priority = 10)
    {
        try
        {
            var todoGroup = GetGroup(group);
            if (todoGroup == null)
            {
                return false;
            }

            using (var command = writeConnection.CreateCommand())
            {
                command.CommandText = "INSERT INTO todo_items (title, description, group_id, due_date, priority) VALUES ($title, $description, $group_id, $due_date, $priority);";
                command.Parameters.AddWithValue("$title", name);
                command.Parameters.AddWithValue("$description", description ?? string.Empty);
                command.Parameters.AddWithValue("$group_id", todoGroup.id);
                command.Parameters.AddWithValue("$due_date", string.Empty);
                command.Parameters.AddWithValue("$priority", priority);
                command.ExecuteNonQuery();
            }

            return true;
        }
        catch (Exception e)
        {
            Log.LogError(e, "Exception while creating todo");
            return false;
        }
    }

    public Todo? GetTodo(string title, string group)
    {
        try
        {
            var todoGroup = GetGroup(group);
            if (todoGroup == null)
            {
                return null;
            }

            Todo? todo = null;
            using (var command = writeConnection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM todo_items WHERE title = $title AND group_id = $group_id;";
                command.Parameters.AddWithValue("$title", title);
                command.Parameters.AddWithValue("$group_id", todoGroup.id);

                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        Log.LogInformation("Could not find todo: title={title}, group={group}", title, group);
                        return null;
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
                        return null;
                    }
                }
            }

            return todo;
        }
        catch (Exception e)
        {
            Log.LogError(e, "Exception while getting todo");
            return null;
        }
    }

    public bool UpdateTodo(string title, string group, string? newTitle = null, string? newGroup = null, string? newDescription = null, string? newDueDate = null, bool? newCompleted = null)
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
            var todoGroup = GetGroup(group);
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
                newTodoGroup = GetGroup(newGroup);
            }

            if (newTodoGroup == null)
            {
                return false;
            }

            var todo = GetTodo(title, group);
            if (todo == null)
            {
                Log.LogInformation("Could not find todo: title={title}, group={group}", title, group);
                return false;
            }

            using (var command = readConnection.CreateCommand())
            {
                command.CommandText = "UPDATE todo_items SET title = $newTitle, description = $newDescription, due_date = $newDueDate, completed = $newCompleted WHERE title = $title;";
                command.Parameters.AddWithValue("$title", title);
                command.Parameters.AddWithValue("$newTitle", newTitle ?? todo.title);
                command.Parameters.AddWithValue("$newDescription", newDescription ?? todo.description);
                command.Parameters.AddWithValue("$newDueDate", newDueDate ?? todo.dueDate);
                command.Parameters.AddWithValue("$newCompleted", newCompleted ?? todo.completed);
                command.ExecuteNonQuery();
            }

            return true;
        }
        catch (Exception e)
        {
            Log.LogError(e, "Exception while updating todo");
            return false;
        }
    }

    public bool DeleteTodo(string title, string group)
    {
        try
        {
            var todoGroup = GetGroup(group);
            if (todoGroup == null)
            {
                return false;
            }

            using (var command = readConnection.CreateCommand())
            {
                command.CommandText = "DELETE FROM todo_items WHERE title = $title AND group_id = $group_id;";
                command.Parameters.AddWithValue("$title", title);
                command.Parameters.AddWithValue("$group_id", todoGroup.id);
                command.ExecuteNonQuery();
            }

            return true;
        }
        catch (Exception e)
        {
            Log.LogError(e, "Exception while deleting todo");
            return false;
        }
    }

    public bool CreateGroup(string name, string? description = null)
    {
        try
        {
            using (var command = writeConnection.CreateCommand())
            {
                command.CommandText = "INSERT INTO todo_groups (name, description) VALUES ($name, $description);";
                command.Parameters.AddWithValue("$name", name);
                command.Parameters.AddWithValue("$description", description ?? string.Empty);
                command.ExecuteNonQuery();
            }

            return true;
        }
        catch (Exception e)
        {
            Log.LogError(e, "Exception while creating todo group");
            return false;
        }
    }

    public TodoGroup? GetGroup(string name, bool getTodos = false)
    {
        try
        {
            int groupId = -1;
            string? groupName = null;
            string? groupDescription = null;
            using (var command = readConnection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM todo_groups WHERE name = $name;";
                command.Parameters.AddWithValue("$name", name);

                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        Log.LogInformation("Could not find group: {name}", name);
                        return null;
                    }

                    groupId = reader.GetInt32(0);
                    groupName = reader.GetString(1);
                    groupDescription = reader.GetString(2);

                    if (reader.Read())
                    {
                        Log.LogInformation("More than one group found: {name}", name);
                        return null;
                    }
                }
            }

            List<Todo> groupTodos = [];
            using (var command = readConnection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM todo_items WHERE group_id = $group_id";
                command.Parameters.AddWithValue("$group_id", groupId);

                using (var reader = command.ExecuteReader())
                {
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
                }
            }

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

    public bool UpdateGroup(string name, string? newName, string? newDescription)
    {
        try
        {
            using (var command = readConnection.CreateCommand())
            {
                command.CommandText = "UPDATE todo_groups SET name = $newName, description = $newDescription WHERE name = $name;";
                command.Parameters.AddWithValue("$name", name);
                command.Parameters.AddWithValue("$newName", newName);
                command.Parameters.AddWithValue("$newDescription", newDescription ?? string.Empty);
                command.ExecuteNonQuery();
            }

            return true;
        }
        catch (Exception e)
        {
            Log.LogError(e, "Exception while updating todo group");
            return false;
        }
    }

    public bool DeleteGroup(string name)
    {
        try
        {
            using (var command = readConnection.CreateCommand())
            {
                command.CommandText = "DELETE FROM todo_groups WHERE name = $name;";
                command.Parameters.AddWithValue("$name", name);
                command.ExecuteNonQuery();
            }

            return true;
        }
        catch (Exception e)
        {
            Log.LogError(e, "Exception while deleting todo group");
            return false;
        }
    }

    public TodoGroup[]? ListGroups(bool getTodos = false)
    {
        try
        {
            if (getTodos)
            {
                Dictionary<int, List<Todo>> groupTodos = [];
                using (var command = readConnection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM todo_items;";
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

                        if (!groupTodos.ContainsKey(todoGroup))
                        {
                            groupTodos.Add(todoGroup, []);
                        }

                        groupTodos[todoGroup].Add(new Todo
                        {
                            id = todoId,
                            groupId = todoGroup,
                            title = todoTitle,
                            description = todoDescription,
                            dueDate = todoDueDate,
                            completed = todoCompleted,
                        });
                    }
                }

                List<TodoGroup> groups = [];
                foreach (var kvp in groupTodos)
                {
                    using (var command = readConnection.CreateCommand())
                    {
                        command.CommandText = "SELECT * FROM todo_groups WHERE id = $id;";
                        command.Parameters.AddWithValue("$id", kvp.Key);

                        using var reader = command.ExecuteReader();

                        if (!reader.Read())
                        {
                            Log.LogInformation("Could not find group with id: {id}", kvp.Key);
                            continue;
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
                            continue;
                        }

                        groups.Add(group);
                    }
                }

                return groups.ToArray();
            }
            else
            {
                List<TodoGroup> groups = [];
                using (var command = readConnection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM todo_groups;";
                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        groups.Add(new TodoGroup
                        {
                            id = reader.GetInt32(0),
                            name = reader.GetString(1),
                            description = reader.GetString(2),
                            todos = new Todo[0]
                        });
                    }
                }

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
        try
        {
            using (var command = readConnection.CreateCommand())
            {
                command.CommandText = "SELECT 1 FROM sqlite_master where type = 'table' AND name = $table;";
                command.Parameters.AddWithValue("$table", "todo_groups");
                var result = command.ExecuteScalar();
                return result != null;
            }
        }
        catch (Exception e)
        {
            Log.LogError(e, "Could not determine if table exists: {table}", table);
        }

        return false;
    }

    public void Close()
    {
        readConnection.Close();
        writeConnection.Close();
        SqliteConnection.ClearAllPools();
    }

    private void Initialize()
    {
        var tablesCreated = TableExists("todo_groups") || TableExists("todo_items");
        if (tablesCreated)
        {
            return;
        }

        using (var command = writeConnection.CreateCommand())
        {
            var schema =
@"CREATE TABLE todo_groups (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL UNIQUE,
    description TEXT
);

CREATE TABLE todo_items (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    group_id INTEGER NOT NULL,
    title TEXT NOT NULL,
    description TEXT,
    due_date DATETIME,
    completed BOOLEAN DEFAULT 0,
    priority INTEGER,
    FOREIGN KEY (group_id) REFERENCES todo_groups (id) ON DELETE CASCADE
    UNIQUE (group_id, title)
);
";

            command.CommandText = schema;
            command.ExecuteNonQuery();
        }
    }

    private SqliteConnection CreateConnection()
    {
        var builder = new SqliteConnectionStringBuilder()
        {
            DataSource = Database,
            ForeignKeys = true,
            Cache = SqliteCacheMode.Shared,
            Mode = Database.Equals(":memory:") ? SqliteOpenMode.Memory : SqliteOpenMode.ReadWriteCreate
        };

        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        return connection;
    }
}
