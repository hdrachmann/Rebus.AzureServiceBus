﻿using System;
using NUnit.Framework;
using Rebus.Activation;
using Rebus.AzureServiceBus.Tests.Bugs;
using Rebus.Config;
using Rebus.Tests.Contracts;

namespace Rebus.AzureServiceBus.Tests;

[TestFixture]
public class AzureServiceBusContentTypeTest : FixtureBase
{
    static readonly string ConnectionString = AsbTestConfig.ConnectionString;

    [Test]
    public void LooksGood()
    {
        Using(new QueueDeleter("contenttypetest"));

        using var activator = new BuiltinHandlerActivator();
        
        Console.WriteLine(ConnectionString);

        var bus = Configure.With(activator)
            .Transport(t => t.UseAzureServiceBus(ConnectionString, "contenttypetest"))
            .Options(o => o.SetNumberOfWorkers(0))
            .Start();

        bus.Advanced.Workers.SetNumberOfWorkers(0);

        var message = new RigtigBesked
        {
            Text = "hej med dig min ven! DER ER JSON HERI!!!",
            Embedded = new RigtigEmbedded
            {
                Whatever = new[] {1, 2, 3},
                Message = "I'm in here!!"
            }
        };

        bus.SendLocal(message).Wait();
    }
}

public class RigtigEmbedded
{
    public string Message { get; set; }
    public int[] Whatever { get; set; }
}

public class RigtigBesked
{
    public string Text { get; set; }
    public RigtigEmbedded Embedded { get; set; }
}