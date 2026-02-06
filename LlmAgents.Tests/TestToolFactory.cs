using LlmAgents.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;

namespace LlmAgents.Tests;

[TestClass]
public class TestToolFactory
{
    [TestMethod]
    public void TestToolFactory_Resolve()
    {
        var logger = new LoggerFactory();
        var toolFactory = new ToolFactory(logger);

        try
        {
            Assert.IsNull(toolFactory.Resolve<ToolEventBus>());
        }
        catch (KeyNotFoundException kfe)
        {
            Assert.IsNotNull(kfe);
        }
        catch
        {
            Assert.Fail();
        }

        try
        {
            Assert.IsNull(toolFactory.Resolve<IToolEventBus>());
        }
        catch (KeyNotFoundException kfe)
        {
            Assert.IsNotNull(kfe);
        }
        catch
        {
            Assert.Fail();
        }

        var toolEventBus = new ToolEventBus();
        toolFactory.Register<IToolEventBus>(toolEventBus);

        var resolvedValue = toolFactory.Resolve<IToolEventBus>();
        Assert.IsNotNull(resolvedValue);
    }

    [TestMethod]
    public void TestToolFactory_ResolveNull()
    {
        var logger = new LoggerFactory();
        var toolFactory = new ToolFactory(logger);

        try
        {
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
            toolFactory.Register<IToolEventBus>(null);
#pragma warning restore CS8625 // Cannot convert null literal to non-nullable reference type.
        }
        catch (ArgumentNullException e)
        {
            Assert.IsNotNull(e);
        }
        catch
        {
            Assert.Fail();
        }

        try
        {
            var resolvedValue = toolFactory.Resolve<IToolEventBus>();
        }
        catch (KeyNotFoundException e)
        {
            Assert.IsNotNull(e);
        }
        catch
        {
            Assert.Fail();
        }

        var defaultValue = toolFactory.ResolveWithDefault<IToolEventBus>();
        Assert.IsNull(defaultValue);
    }
}