﻿using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus.Administration;
using NUnit.Framework;
using Rebus.Activation;
using Rebus.Config;
using Rebus.Logging;
using Rebus.Tests.Contracts;
#pragma warning disable 1998

namespace Rebus.AzureServiceBus.Tests.Bugs;

[TestFixture]
public class FastSubscriber : FixtureBase
{
    [TestCase(10)]
    public async Task TakeTime(int topicCount)
    {
        Using(new QueueDeleter("my-input-queue"));

        using var activator = new BuiltinHandlerActivator();

        var bus = Configure.With(activator)
            .Logging(l => l.Console(LogLevel.Warn))
            .Transport(t => t.UseAzureServiceBus(AsbTestConfig.ConnectionString, "my-input-queue"))
            .Start();

        var stopwatch = Stopwatch.StartNew();

        Task.WaitAll(Enumerable.Range(0, topicCount)
            .Select(async n =>
            {
                var topicName = $"topic-{n}";

                Using(new TopicDeleter(topicName));

                await bus.Advanced.Topics.Subscribe(topicName);
            })
            .ToArray());

        var elapsedSeconds = stopwatch.Elapsed.TotalSeconds;

        Console.WriteLine($"Subscribing to {topicCount} topics took {elapsedSeconds:0.0}s - that's {elapsedSeconds / topicCount:0.0} s/subscription");
    }

    [Test]
    [Explicit("run manually")]
    public async Task DeleteAllTopics()
    {
        var managementClient = new ServiceBusAdministrationClient(AsbTestConfig.ConnectionString);

        await foreach (var topic in managementClient.GetTopicsAsync())
        {
            Console.Write($"Deleting '{topic.Name}'... ");
            await managementClient.DeleteTopicAsync(topic.Name);
            Console.WriteLine("OK");
        }
    }

    [Test]
    [Explicit("run manually")]
    public async Task DeleteAllQueues()
    {
        var managementClient = new ServiceBusAdministrationClient(AsbTestConfig.ConnectionString);

        await foreach (var queue in managementClient.GetQueuesAsync())
        {
            Console.Write($"Deleting '{queue.Name}'... ");
            await managementClient.DeleteQueueAsync(queue.Name);
            Console.WriteLine("OK");
        }
    }
}