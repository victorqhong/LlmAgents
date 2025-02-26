namespace Simulation.Todo;

using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;

public class TodoDatabase
{
    public readonly string Database;

    private readonly SqliteConnection readConnection;

    private readonly SqliteConnection writeConnection;

    public TodoDatabase(string database, bool initializeSchema = true)
    {
        Database = database;

        readConnection = CreateConnection();
        writeConnection = CreateConnection();

        if (readConnection == null || writeConnection == null)
        {
            throw new ApplicationException("Could not initialize database connections");
        }

        if (initializeSchema)
        {
            Initialize();
        }
    }

    public bool CreateTodo(string name, string group, string? description)
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
                command.CommandText = "INSERT INTO todo_items (title, description, group_id, due_date) VALUES ($title, $description, $group_id, $due_date);";
                command.Parameters.AddWithValue("$title", name);
                command.Parameters.AddWithValue("$description", description ?? string.Empty);
                command.Parameters.AddWithValue("$group_id", todoGroup.id);
                command.Parameters.AddWithValue("$due_date", string.Empty);
                command.ExecuteNonQuery();
            }

            return true;
        }
        catch
        {
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
                        // result not found
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
                        // more than one result
                        return null;
                    }
                }
            }

            return todo;
        }
        catch
        {
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
                // could not find new group
                return false;
            }

            var todo = GetTodo(title, group);
            if (todo == null)
            {
                // could not find original todo
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
        catch
        {
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
                // could not find group
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
        catch
        {
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
        catch
        {
            return false;
        }
    }

    public TodoGroup? GetGroup(string name)
    {
        TodoGroup? group = null;
        using (var command = readConnection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM todo_groups WHERE name = $name;";
            command.Parameters.AddWithValue("$name", name);

            using (var reader = command.ExecuteReader())
            {
                if (!reader.Read())
                {
                    // result not found
                    return null;
                }

                group = new TodoGroup
                {
                    id = reader.GetInt32(0),
                    name = reader.GetString(1),
                    description = reader.GetString(2)
                };

                if (reader.Read())
                {
                    // more than one result was found
                    return null;
                }
            }
        }

        return group;
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
        catch
        {
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
        catch
        {
            return false;
        }
    }

    public TodoGroup[]? ListGroups()
    {
        try
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
                        description = reader.GetString(2)
                    });
                }
            }

            return groups.ToArray();
        }
        catch
        {
            return null;
        }
    }

    public void Close()
    {
        readConnection.Close();
        writeConnection.Close();
        SqliteConnection.ClearAllPools();
    }

    private void Initialize()
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
        var command = writeConnection.CreateCommand();
        command.CommandText = schema;
        command.ExecuteNonQuery();
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
