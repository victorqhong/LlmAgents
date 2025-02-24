namespace Simulation.Todo;

using Microsoft.Data.Sqlite;
using System;
using System.IO;

public class TodoDatabase
{
    public readonly string Database;

    private readonly SqliteConnection readConnection;

    private readonly SqliteConnection writeConnection;

    public TodoDatabase(string database, bool initializeSchema = false)
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

    public bool CreateTodo(string name, string container, string? description)
    {
        try
        {
            var todoContainer = GetContainer(container);
            if (todoContainer == null)
            {
                return false;
            }

            using (var command = writeConnection.CreateCommand())
            {
                command.CommandText = "INSERT INTO todo_items (title, description, container_id, due_date) VALUES ($title, $description, $container_id, $due_date);";
                command.Parameters.AddWithValue("$title", name);
                command.Parameters.AddWithValue("$description", description ?? string.Empty);
                command.Parameters.AddWithValue("$container_id", todoContainer.id);
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

    public Todo? GetTodo(string title, string container)
    {
        try
        {
            var todoContainer = GetContainer(container);
            if (todoContainer == null)
            {
                return null;
            }

            Todo? todo = null;
            using (var command = writeConnection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM todo_items WHERE title = $title AND container_id = $container_id;";
                command.Parameters.AddWithValue("$title", title);
                command.Parameters.AddWithValue("$container_id", todoContainer.id);

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
                        containerId = reader.GetInt32(1),
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

    public bool UpdateTodo(string title, string container, string? newTitle = null, string? newContainer = null, string? newDescription = null, string? newDueDate = null, bool? newCompleted = null)
    {
        ArgumentNullException.ThrowIfNullOrEmpty(title);
        ArgumentNullException.ThrowIfNullOrEmpty(container);

        if (newTitle == null && newContainer == null && newDescription == null && newDueDate == null && newCompleted == null)
        {
            // nothing to update
            return true;
        }

        try
        {
            var todoContainer = GetContainer(container);
            if (todoContainer == null)
            {
                return false;
            }

            TodoContainer? newTodoContainer = null;
            if (newContainer == null)
            {
                newTodoContainer = todoContainer;
            }
            else if (string.Equals(newContainer, container))
            {
                newTodoContainer = todoContainer;
            }
            else
            {
                newTodoContainer = GetContainer(newContainer);
            }

            if (newTodoContainer == null)
            {
                // could not find new container
                return false;
            }

            var todo = GetTodo(title, container);
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

    public bool DeleteTodo(string title, string container)
    {
        try
        {
            var todoContainer = GetContainer(container);
            if (todoContainer == null)
            {
                // could not find container
                return false;
            }

            using (var command = readConnection.CreateCommand())
            {
                command.CommandText = "DELETE FROM todo_items WHERE title = $title AND container_id = $container_id;";
                command.Parameters.AddWithValue("$title", title);
                command.Parameters.AddWithValue("$container_id", todoContainer.id);
                command.ExecuteNonQuery();
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool CreateContainer(string name, string? description = null)
    {
        try
        {
            using (var command = writeConnection.CreateCommand())
            {
                command.CommandText = "INSERT INTO todo_containers (name, description) VALUES ($name, $description);";
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

    public TodoContainer? GetContainer(string name)
    {
        TodoContainer? container = null;
        using (var command = readConnection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM todo_containers WHERE name = $name;";
            command.Parameters.AddWithValue("$name", name);

            using (var reader = command.ExecuteReader())
            {
                if (!reader.Read())
                {
                    // result not found
                    return null;
                }

                container = new TodoContainer
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

        return container;
    }

    public bool UpdateContainer(string name, string? newName, string? newDescription)
    {
        try
        {
            using (var command = readConnection.CreateCommand())
            {
                command.CommandText = "UPDATE todo_containers SET name = $newName, description = $newDescription WHERE name = $name;";
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

    public bool DeleteContainer(string name)
    {
        try
        {
            using (var command = readConnection.CreateCommand())
            {
                command.CommandText = "DELETE FROM todo_containers WHERE name = $name;";
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

    public void Close()
    {
        readConnection.Close();
        writeConnection.Close();
        SqliteConnection.ClearAllPools();
    }

    private void Initialize()
    {
        var schema =
@"CREATE TABLE todo_containers (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL UNIQUE,
    description TEXT
);

CREATE TABLE todo_items (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    container_id INTEGER NOT NULL,
    title TEXT NOT NULL,
    description TEXT,
    due_date DATETIME,
    completed BOOLEAN DEFAULT 0,
    FOREIGN KEY (container_id) REFERENCES todo_containers (id) ON DELETE CASCADE
    UNIQUE (container_id, title)
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
