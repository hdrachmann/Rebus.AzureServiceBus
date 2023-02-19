﻿using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Newtonsoft.Json;
using NUnit.Framework;
using Rebus.Activation;
using Rebus.AzureServiceBus.Tests.Bugs;
using Rebus.Bus;
using Rebus.Config;
using Rebus.Internals;
using Rebus.Logging;
using Rebus.Pipeline;
using Rebus.Routing.TypeBased;
using Rebus.Tests.Contracts;
using Rebus.Tests.Contracts.Extensions;
// ReSharper disable MethodSupportsCancellation
#pragma warning disable CS4014

#pragma warning disable CS1998

namespace Rebus.AzureServiceBus.Tests.Checks;

[TestFixture]
[Description("Simple check just to get some kind of idea of some numbers")]
[Explicit]
public class CheckSendLatency : FixtureBase
{
    [TestCase(1)]
    [TestCase(10)]
    [TestCase(100, Explicit = true)]
    [Repeat(5)]
    public async Task CheckEndToEndLatency(int count)
    {
        var receiveTimes = new ConcurrentQueue<ReceiveInfo>();

        async Task RegisterReceiveTime(IBus bus, IMessageContext messageContext, TimedEvent evt)
        {
            var actualReceiveTime = DateTimeOffset.Now;
            var receiveInfo = new ReceiveInfo(actualReceiveTime, evt.Time);
            receiveTimes.Enqueue(receiveInfo);
        }

        Using(new QueueDeleter("sender"));
        var sender = GetBus("sender", routing: r => r.Map<TimedEvent>("receiver"));

        Using(new QueueDeleter("receiver"));
        GetBus("receiver", handlers: activator => activator.Handle<TimedEvent>(RegisterReceiveTime));

        await Parallel.ForEachAsync(Enumerable.Range(0, count),
            async (_, _) => await sender.Send(new TimedEvent(DateTimeOffset.Now)));

        await receiveTimes.WaitUntil(q => q.Count == count, timeoutSeconds: 10 + count * 2);

        var latencies = receiveTimes.Select(a => a.Latency().TotalSeconds).ToList();

        var average = latencies.Average();
        var median = latencies.Median();
        var min = latencies.Min();
        var max = latencies.Max();

        Console.WriteLine($"AVG: {average:0.0} s, MED: {median:0.0} s, MIN: {min:0.0} s, MAX: {max:0.0} s");
    }

    [TestCase(1)]
    [TestCase(10)]
    [TestCase(100, Explicit = true)]
    [Repeat(5)]
    public async Task CheckEndToEndLatency_NoRebus_ServiceBusProcessor(int count)
    {
        var receiveTimes = new ConcurrentQueue<ReceiveInfo>();

        async Task RegisterReceiveTime(TimedEvent evt)
        {
            var actualReceiveTime = DateTimeOffset.Now;
            var receiveInfo = new ReceiveInfo(actualReceiveTime, evt.Time);
            receiveTimes.Enqueue(receiveInfo);
        }

        var client = new ServiceBusClient(AsbTestConfig.ConnectionString);
        var admin = new ServiceBusAdministrationClient(AsbTestConfig.ConnectionString);

        Using(new QueueDeleter("receiver"));
        await admin.CreateQueueIfNotExistsAsync("receiver");

        var sender = client.CreateSender("receiver");
        var receiver = client.CreateProcessor("receiver");

        receiver.ProcessMessageAsync += async args =>
        {
            var message = args.Message;
            try
            {
                var bytes = message.Body.ToArray();
                var json = Encoding.UTF8.GetString(bytes);
                var timedEvent = JsonConvert.DeserializeObject<TimedEvent>(json);

                await RegisterReceiveTime(timedEvent);

                await args.CompleteMessageAsync(message);
            }
            catch
            {
                await args.AbandonMessageAsync(message);
                throw;
            }
        };
        receiver.ProcessErrorAsync += async _ => { };

        try
        {
            await receiver.StartProcessingAsync();

            await Parallel.ForEachAsync(Enumerable.Range(0, count),
                async (_, token) =>
                    await sender.SendMessageAsync(
                        new ServiceBusMessage(JsonConvert.SerializeObject(new TimedEvent(DateTimeOffset.Now))), token));

            await receiveTimes.WaitUntil(q => q.Count == count, timeoutSeconds: 30 + count * 5);

            var latencies = receiveTimes.Select(a => a.Latency().TotalSeconds).ToList();

            var average = latencies.Average();
            var median = latencies.Median();
            var min = latencies.Min();
            var max = latencies.Max();

            Console.WriteLine($"AVG: {average:0.0} s, MED: {median:0.0} s, MIN: {min:0.0} s, MAX: {max:0.0} s");
        }
        finally
        {
            await receiver.StopProcessingAsync();
        }
    }

    [TestCase(1)]
    [TestCase(10)]
    [TestCase(100, Explicit = true)]
    [Repeat(5)]
    public async Task CheckEndToEndLatency_NoRebus_ServiceBusReceiver(int count)
    {
        var receiveTimes = new ConcurrentQueue<ReceiveInfo>();

        async Task RegisterReceiveTime(TimedEvent evt)
        {
            var actualReceiveTime = DateTimeOffset.Now;
            var receiveInfo = new ReceiveInfo(actualReceiveTime, evt.Time);
            receiveTimes.Enqueue(receiveInfo);
        }

        var client = new ServiceBusClient(AsbTestConfig.ConnectionString);
        var admin = new ServiceBusAdministrationClient(AsbTestConfig.ConnectionString);

        Using(new QueueDeleter("receiver"));
        await admin.CreateQueueIfNotExistsAsync("receiver");

        var sender = client.CreateSender("receiver");
        var receiver = client.CreateReceiver("receiver");

        using var stopReceiver = new CancellationTokenSource();
        var cancellationToken = stopReceiver.Token;

        Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var message = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(50), cancellationToken);

                    try
                    {
                        var bytes = message.Body.ToArray();
                        var json = Encoding.UTF8.GetString(bytes);
                        var timedEvent = JsonConvert.DeserializeObject<TimedEvent>(json);

                        await RegisterReceiveTime(timedEvent);

                        await receiver.CompleteMessageAsync(message);
                    }
                    catch
                    {
                        await receiver.AbandonMessageAsync(message);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // we're exiting
                }
                catch (Exception exception)
                {
                    Console.WriteLine($"Unhandled exception in receiver loop: {exception}");
                }
            }
        }, cancellationToken);

        try
        {
            await Parallel.ForEachAsync(Enumerable.Range(0, count),
                async (_, token) =>
                    await sender.SendMessageAsync(
                        new ServiceBusMessage(JsonConvert.SerializeObject(new TimedEvent(DateTimeOffset.Now))), token));

            await receiveTimes.WaitUntil(q => q.Count == count, timeoutSeconds: 30 + count * 5);

            var latencies = receiveTimes.Select(a => a.Latency().TotalSeconds).ToList();

            var average = latencies.Average();
            var median = latencies.Median();
            var min = latencies.Min();
            var max = latencies.Max();

            Console.WriteLine($"AVG: {average:0.0} s, MED: {median:0.0} s, MIN: {min:0.0} s, MAX: {max:0.0} s");
        }
        finally
        {
            stopReceiver.Cancel();
        }
    }

    record TimedEvent(DateTimeOffset Time);

    record ReceiveInfo(DateTimeOffset ReceiveTime, DateTimeOffset SendTime)
    {
        public TimeSpan Latency() => ReceiveTime - SendTime;
    }

    IBus GetBus(string queueName, Action<BuiltinHandlerActivator> handlers = null, Action<TypeBasedRouterConfigurationExtensions.TypeBasedRouterConfigurationBuilder> routing = null)
    {
        var activator = Using(new BuiltinHandlerActivator());

        handlers?.Invoke(activator);

        Configure.With(activator)
            .Logging(l => l.Console(minLevel: LogLevel.Warn))
            .Transport(t => t.UseAzureServiceBus(AsbTestConfig.ConnectionString, queueName))
            .Routing(r => routing?.Invoke(r.TypeBased()))
            .Options(o => o.SetMaxParallelism(10))
            .Start();

        return activator.Bus;
    }
}