using Microsoft.VisualStudio.TestTools.UnitTesting;
using Simulation.Todo;

namespace Simulation.Tests;

[TestClass]
public sealed class TestTodoDatabase
{
    [TestMethod]
    public void TestTodoDatabase_Initialize()
    {
        TodoDatabase db = new TodoDatabase(":memory:");
        Assert.IsNotNull(db);
        db.Close();
    }

    [TestMethod]
    public void TestTodoDatabase_CreateGroup()
    {
        TodoDatabase db = new TodoDatabase(":memory:");
        var result = db.CreateGroup("test");
        Assert.IsTrue(result);
        db.Close();
    }

    [TestMethod]
    public void TestTodoDatabase_GetGroup()
    {
        TodoDatabase db = new TodoDatabase(":memory:");
        db.CreateGroup("test");

        var group = db.GetGroup("test");

        Assert.IsNotNull(group);
        Assert.AreEqual("test", group.name);

        db.Close();
    }

    [TestMethod]
    public void TestTodoDatabase_UpdateGroup()
    {
        TodoDatabase db = new TodoDatabase(":memory:");
        db.CreateGroup("test");

        var result = db.UpdateGroup("test", "newTest", "newDescription");
        Assert.IsTrue(result);

        var group = db.GetGroup("newTest");

        Assert.IsNotNull(group);
        Assert.AreEqual("newTest", group.name);

        db.Close();
    }

    [TestMethod]
    public void TestTodoDatabase_DeleteGroup()
    {
        TodoDatabase db = new TodoDatabase(":memory:");
        db.CreateGroup("test");

        var result = db.DeleteGroup("test");
        Assert.IsTrue(result);

        var group = db.GetGroup("test");

        Assert.IsNull(group);

        db.Close();
    }

    [TestMethod]
    public void TestTodoDatabase_CreateTodo()
    {
        TodoDatabase db = new TodoDatabase(":memory:");
        db.CreateGroup("test");

        var result = db.CreateTodo("testtodo", "test", "this is a test");
        Assert.IsTrue(result);

        db.Close();
    }

    [TestMethod]
    public void TestTodoDatabase_GetTodo()
    {
        TodoDatabase db = new TodoDatabase(":memory:");
        db.CreateGroup("test");

        var result = db.CreateTodo("testtodo", "test", "this is a test");
        Assert.IsTrue(result);

        var todo = db.GetTodo("testtodo", "test");
        Assert.IsNotNull(todo);
        Assert.AreEqual("testtodo", todo.title);

        db.Close();
    }

    [TestMethod]
    public void TestTodoDatabase_UpdateTodo()
    {
        TodoDatabase db = new TodoDatabase(":memory:");
        db.CreateGroup("test");

        var result = db.CreateTodo("testtodo", "test", "this is a test");
        Assert.IsTrue(result);

        var result2 = db.UpdateTodo("testtodo", "test", "newtitle");
        Assert.IsTrue(result2);

        var todo = db.GetTodo("newtitle", "test");
        Assert.IsNotNull(todo);
        Assert.AreEqual("newtitle", todo.title);

        db.Close();
    }

    [TestMethod]
    public void TestTodoDatabase_DeleteTodo()
    {
        TodoDatabase db = new TodoDatabase(":memory:");
        db.CreateGroup("test");

        db.Close();
    }
}
