using LlmAgents.Tools.Todo;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Simulation.Tests;

[TestClass]
public sealed class TestTodoDatabase
{
    private TodoDatabase? db;

    [TestInitialize]
    public void CreateDatabase()
    {
        var loggerFactory = LoggerFactory.Create(builder => { });
        db = new TodoDatabase(loggerFactory, ":memory:");
    }

    [TestMethod]
    public void TestInitialize()
    {
        Assert.IsNotNull(db);
        db.Close();
    }

    [TestMethod]
    public void TestCreateGroup()
    {
        var result = db.CreateGroup("test");
        Assert.IsTrue(result);
        db.Close();
    }

    [TestMethod]
    public void TestGetGroup()
    {
        db.CreateGroup("test");

        var group = db.GetGroup("test");

        Assert.IsNotNull(group);
        Assert.AreEqual("test", group.name);

        db.Close();
    }

    [TestMethod]
    public void TestGetGroup_WithTodos()
    {
        db.CreateGroup("test");
        db.CreateTodo("testtodo", "test");

        var group = db.GetGroup("test", true);
        Assert.IsNotNull(group);
        Assert.IsTrue(group.todos.Length > 0);
        Assert.AreEqual("testtodo", group.todos[0].title);

        db.Close();
    }

    [TestMethod]
    public void TestUpdateGroup()
    {
        db.CreateGroup("test");

        var result = db.UpdateGroup("test", "newTest", "newDescription");
        Assert.IsTrue(result);

        var group = db.GetGroup("newTest");

        Assert.IsNotNull(group);
        Assert.AreEqual("newTest", group.name);

        db.Close();
    }

    [TestMethod]
    public void TestDeleteGroup()
    {
        db.CreateGroup("test");

        var result = db.DeleteGroup("test");
        Assert.IsTrue(result);

        var group = db.GetGroup("test");

        Assert.IsNull(group);

        db.Close();
    }

    [TestMethod]
    public void TestListGroups_WithTodos()
    {
        try
        {
            db.CreateGroup("test");
            db.CreateTodo("testtodo", "test");
            var groups = db.ListGroups(true);
            Assert.IsNotNull(groups);
            Assert.IsTrue(groups.Length == 1);
            Assert.AreEqual("test", groups[0].name);
            Assert.IsTrue(groups[0].todos.Length == 1);
            Assert.AreEqual("testtodo", groups[0].todos[0].title);
        }
        catch
        {
            Assert.Fail();
        }
        finally
        {
            db.Close();
        }
    }

    [TestMethod]
    public void TestCreateTodo()
    {
        db.CreateGroup("test");

        var result = db.CreateTodo("testtodo", "test", "this is a test");
        Assert.IsTrue(result);

        result = db.CreateTodo("anothertodo", "doesn't exist");
        Assert.IsFalse(result);

        db.Close();
    }

    [TestMethod]
    public void TestGetTodo()
    {
        db.CreateGroup("test");

        var result = db.CreateTodo("testtodo", "test", "this is a test");
        Assert.IsTrue(result);

        var todo = db.GetTodo("testtodo", "test");
        Assert.IsNotNull(todo);
        Assert.AreEqual("testtodo", todo.title);

        db.Close();
    }

    [TestMethod]
    public void TestUpdateTodo()
    {
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
    public void TestDeleteTodo()
    {
        db.CreateGroup("test");

        db.Close();
    }
}
