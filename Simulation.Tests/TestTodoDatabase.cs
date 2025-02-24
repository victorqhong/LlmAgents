using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using Simulation.Todo;
using System.Collections.Generic;

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
    public void TestTodoDatabase_CreateContainer()
    {
        TodoDatabase db = new TodoDatabase(":memory:");
        var result = db.CreateContainer("test");
        Assert.IsTrue(result);
        db.Close();
    }

    [TestMethod]
    public void TestTodoDatabase_GetContainer()
    {
        TodoDatabase db = new TodoDatabase(":memory:");
        db.CreateContainer("test");

        var container = db.GetContainer("test");

        Assert.IsNotNull(container);
        Assert.AreEqual("test", container.name);

        db.Close();
    }

    [TestMethod]
    public void TestTodoDatabase_UpdateContainer()
    {
        TodoDatabase db = new TodoDatabase(":memory:");
        db.CreateContainer("test");

        var result = db.UpdateContainer("test", "newTest", "newDescription");
        Assert.IsTrue(result);

        var container = db.GetContainer("newTest");

        Assert.IsNotNull(container);
        Assert.AreEqual("newTest", container.name);

        db.Close();
    }

    [TestMethod]
    public void TestTodoDatabase_DeleteContainer()
    {
        TodoDatabase db = new TodoDatabase(":memory:");
        db.CreateContainer("test");

        var result = db.DeleteContainer("test");
        Assert.IsTrue(result);

        var container = db.GetContainer("test");

        Assert.IsNull(container);

        db.Close();
    }

    [TestMethod]
    public void TestTodoDatabase_CreateTodo()
    {
        TodoDatabase db = new TodoDatabase(":memory:");
        db.CreateContainer("test");

        var result = db.CreateTodo("testtodo", "test", "this is a test");
        Assert.IsTrue(result);

        db.Close();
    }

    [TestMethod]
    public void TestTodoDatabase_GetTodo()
    {
        TodoDatabase db = new TodoDatabase(":memory:");
        db.CreateContainer("test");

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
        db.CreateContainer("test");

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
        db.CreateContainer("test");

        db.Close();
    }
}
