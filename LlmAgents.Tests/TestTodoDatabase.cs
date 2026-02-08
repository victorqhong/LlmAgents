using LlmAgents.State;
using LlmAgents.Tools.Todo;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LlmAgents.Tests;

[TestClass]
public sealed class TestTodoDatabase
{
    private readonly ILoggerFactory loggerFactory;

    private readonly Session session;

    private readonly StateDatabase stateDb;

    private TodoDatabase db;

    public TestTodoDatabase()
    {
        loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        session = Session.New();

        stateDb = new StateDatabase(loggerFactory, ":memory:");
        stateDb.CreateSession(session);

        db = CreateDatabase();
    }

    private TodoDatabase CreateDatabase()
    {
        return new TodoDatabase(loggerFactory, stateDb);
    }

    [TestInitialize]
    public void NewDatabase()
    {
        db = CreateDatabase();
    }

    [TestCleanup]
    public void CloseDatabase()
    {
        stateDb.Dispose();
    }

    [TestMethod]
    [TestCategory(Constants.TestCategory_Unit)]
    public void TestCreateGroup()
    {
        var result = db.CreateGroup(session, "test");
        Assert.IsTrue(result);
    }

    [TestMethod]
    [TestCategory(Constants.TestCategory_Unit)]
    public void TestGetGroup()
    {
        db.CreateGroup(session, "test");

        var group = db.GetGroup(session, "test");

        Assert.IsNotNull(group);
        Assert.AreEqual("test", group.name);
    }

    [TestMethod]
    [TestCategory(Constants.TestCategory_Unit)]
    public void TestGetGroup_WithTodos()
    {
        db.CreateGroup(session, "test");
        db.CreateTodo(session, "testtodo", "test");

        var group = db.GetGroup(session, "test", true);
        Assert.IsNotNull(group);
        Assert.IsTrue(group.todos.Length > 0);
        Assert.AreEqual("testtodo", group.todos[0].title);
    }

    [TestMethod]
    [TestCategory(Constants.TestCategory_Unit)]
    public void TestUpdateGroup()
    {
        db.CreateGroup(session, "test");

        var result = db.UpdateGroup(session, "test", "newTest", "newDescription");
        Assert.IsTrue(result);

        var group = db.GetGroup(session, "newTest");

        Assert.IsNotNull(group);
        Assert.AreEqual("newTest", group.name);
    }

    [TestMethod]
    [TestCategory(Constants.TestCategory_Unit)]
    public void TestDeleteGroup()
    {
        db.CreateGroup(session, "test");

        var result = db.DeleteGroup(session, "test");
        Assert.IsTrue(result);

        var group = db.GetGroup(session, "test");

        Assert.IsNull(group);
    }

    [TestMethod]
    [TestCategory(Constants.TestCategory_Unit)]
    public void TestListGroups_WithTodos()
    {
        try
        {
            db.CreateGroup(session, "test");
            db.CreateTodo(session, "testtodo", "test");
            var groups = db.ListGroups(session, true);
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
    }

    [TestMethod]
    [TestCategory(Constants.TestCategory_Unit)]
    public void TestCreateTodo()
    {
        db.CreateGroup(session, "test");

        var result = db.CreateTodo(session, "testtodo", "test", "this is a test");
        Assert.IsTrue(result);

        result = db.CreateTodo(session, "anothertodo", "doesn't exist");
        Assert.IsFalse(result);
    }

    [TestMethod]
    [TestCategory(Constants.TestCategory_Unit)]
    public void TestGetTodo()
    {
        db.CreateGroup(session, "test");

        var result = db.CreateTodo(session, "testtodo", "test", "this is a test");
        Assert.IsTrue(result);

        var todo = db.GetTodo(session, "testtodo", "test");
        Assert.IsNotNull(todo);
        Assert.AreEqual("testtodo", todo.title);
    }

    [TestMethod]
    [TestCategory(Constants.TestCategory_Unit)]
    public void TestUpdateTodo()
    {
        db.CreateGroup(session, "test");

        var result = db.CreateTodo(session, "testtodo", "test", "this is a test");
        Assert.IsTrue(result);

        var result2 = db.UpdateTodo(session, "testtodo", "test", "newtitle");
        Assert.IsTrue(result2);

        var todo = db.GetTodo(session, "newtitle", "test");
        Assert.IsNotNull(todo);
        Assert.AreEqual("newtitle", todo.title);
    }

    [TestMethod]
    [TestCategory(Constants.TestCategory_Unit)]
    public void TestUpdateTodo_Group()
    {
        db.CreateGroup(session, "test");
        db.CreateGroup(session, "test2");

        var newGroup = db.GetGroup(session, "test2");
        Assert.IsNotNull(newGroup);

        var result = db.CreateTodo(session, "testtodo", "test", "this is a test");
        Assert.IsTrue(result);

        var result2 = db.UpdateTodo(session, "testtodo", "test", "newtitle", "test2");
        Assert.IsTrue(result2);

        var todo = db.GetTodo(session, "newtitle", "test2");
        Assert.IsNotNull(todo);
        Assert.AreEqual("newtitle", todo.title);
        Assert.AreEqual(newGroup.id, todo.groupId);
    }

    [TestMethod]
    [TestCategory(Constants.TestCategory_Unit)]
    public void TestDeleteTodo()
    {
        db.CreateGroup(session, "test");
    }
}
