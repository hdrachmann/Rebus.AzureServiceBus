﻿using System.Threading.Tasks;
using NUnit.Framework;
using Rebus.Activation;
using Rebus.AzureServiceBus.Tests.Bugs;
using Rebus.AzureServiceBus.Tests.Factories;
using Rebus.Config;
using Rebus.Tests.Contracts;
using Rebus.Tests.Contracts.Utilities;

#pragma warning disable 1998

namespace Rebus.AzureServiceBus.Tests;

[TestFixture]
public class WorksWhenEnablingPartitioning : FixtureBase
{
    readonly string _queueName = TestConfig.GetName("input");
    readonly string _connectionString = AsbTestConfig.ConnectionString;

    [Test]
    public async Task YesItDoes()
    {
        Using(new QueueDeleter(_queueName));

        using var activator = new BuiltinHandlerActivator();
        
        var counter = new SharedCounter(1);

        Using(counter);

        activator.Handle<string>(async str => counter.Decrement());

        var bus = Configure.With(activator)
            .Transport(t => t.UseAzureServiceBus(_connectionString, _queueName).EnablePartitioning())
            .Start();

        await bus.SendLocal("hej med dig min ven!!!");

        counter.WaitForResetEvent();
    }
}