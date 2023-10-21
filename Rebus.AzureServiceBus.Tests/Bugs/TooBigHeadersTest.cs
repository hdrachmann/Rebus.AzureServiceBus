﻿using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Rebus.Activation;
using Rebus.AzureServiceBus.Messages;
using Rebus.AzureServiceBus.NameFormat;
using Rebus.Config;
using Rebus.Logging;
using Rebus.Messages;
using Rebus.Retry.Simple;
using Rebus.Tests.Contracts;
using Rebus.Threading.TaskParallelLibrary;
using Rebus.Transport;

namespace Rebus.AzureServiceBus.Tests.Bugs;

[TestFixture]
public class TooBigHeadersTest : FixtureBase
{
    AzureServiceBusTransport errorQueueTransport;

    protected override void SetUp()
    {
        var loggerFactory = new ConsoleLoggerFactory(false);

        errorQueueTransport = new AzureServiceBusTransport(AsbTestConfig.ConnectionString, "error", loggerFactory, new TplAsyncTaskFactory(loggerFactory), new DefaultNameFormatter(), new DefaultMessageConverter());

        Using(errorQueueTransport);

        errorQueueTransport.Initialize();
        errorQueueTransport.PurgeInputQueue();
    }

    [Test]
    public async Task ItWorks()
    {
        const int MaxDeliveryAttempts = 3;

        var queueName = TestConfig.GetName("test-queue");

        Using(new QueueDeleter(queueName));

        var activator = new BuiltinHandlerActivator();

        Using(activator);

        var done = new ManualResetEvent(false);

        Using(done);

        activator.Handle<string>(message =>
        {
            var exceptionMessage = new string('a', 30 * 1024); // 30 KB * 3 < 256KB max message size.

            throw new InvalidOperationException(exceptionMessage);
        });

        Configure.With(activator)
            .Logging(l => l.Console(minLevel: LogLevel.Error))
            .Transport(t => t.UseAzureServiceBus(AsbTestConfig.ConnectionString, queueName))
            .Options(o => o.RetryStrategy(maxDeliveryAttempts: MaxDeliveryAttempts))
            .Start();

        await activator.Bus.SendLocal("Hello World");

        var nextMessage = await GetNextMessageFromErrorQueue(timeoutSeconds: 10);

        Assert.That(nextMessage.Headers, Contains.Key(Headers.ErrorDetails));

        var errorDetails = nextMessage.Headers[Headers.ErrorDetails];

        Console.WriteLine($@"------------------------------------------------------------------------------------------------
The following error details got attached to the message:

{errorDetails}");
    }

    async Task<TransportMessage> GetNextMessageFromErrorQueue(int timeoutSeconds)
    {
        var cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = cancellationTokenSource.Token;
        var timeout = TimeSpan.FromSeconds(timeoutSeconds);

        cancellationTokenSource.CancelAfter(timeout);

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using (var scope = new RebusTransactionScope())
                {
                    var message = await errorQueueTransport.Receive(scope.TransactionContext, cancellationToken);

                    try
                    {
                        if (message != null) return message;
                    }
                    finally
                    {
                        await scope.CompleteAsync();
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Did not receive message in queue '{errorQueueTransport.Address}' within timeout of {timeout}");
        }
    }
}