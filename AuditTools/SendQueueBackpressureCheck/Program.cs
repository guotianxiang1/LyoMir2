using System.Net;
using System.Net.Sockets;

await Run("FIFO and complete async sends", TestFifoAndCompleteSend);
await Run("bounded queue fails explicitly", TestBoundedQueue);
await Run("Stop wakes blocked reader and writer", TestStopWakesWaiters);
await Run("Stop cancels an in-flight send", TestStopCancelsSend);

Console.WriteLine("SendQueue backpressure checks passed.");

static async Task Run(string name, Func<Task> test)
{
    await test();
    Console.WriteLine($"PASS {name}");
}

static async Task TestFifoAndCompleteSend()
{
    var (listener, receiver, sender) = await ConnectLoopback();
    using (listener)
    using (receiver)
    using (sender)
    {
        sender.SendBufferSize = 1024;
        receiver.ReceiveBufferSize = 1024;

        var queue = new GameSvr.SendQueue(sender, capacity: 4);
        var sendTask = queue.ProcessSendQueue();
        var first = MakePayload(2 * 1024 * 1024, 17);
        var second = MakePayload(257 * 1024, 91);

        queue.AddToQueue(first);
        await queue.AddToQueueAsync(second);
        var received = await ReadExactly(receiver.GetStream(), first.Length + second.Length)
            .WaitAsync(TimeSpan.FromSeconds(15));

        Check(received.AsSpan(0, first.Length).SequenceEqual(first), "first frame changed");
        Check(received.AsSpan(first.Length).SequenceEqual(second), "second frame changed");

        queue.Stop();
        await sendTask.WaitAsync(TimeSpan.FromSeconds(2));
        Check(sendTask.IsCompletedSuccessfully, "send loop did not stop cleanly");
    }
}

static async Task TestBoundedQueue()
{
    var (listener, receiver, sender) = await ConnectLoopback();
    using (listener)
    using (receiver)
    using (sender)
    {
        var queue = new GameSvr.SendQueue(sender, capacity: 2);
        queue.AddToQueue(new byte[] { 1 });
        queue.AddToQueue(new byte[] { 2 });

        var failedExplicitly = false;
        try
        {
            queue.AddToQueue(new byte[] { 3 });
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("full"))
        {
            failedExplicitly = true;
        }

        Check(failedExplicitly, "full queue silently accepted or dropped a frame");
        Check(queue.GetQueueCount == 2, "bounded queue exceeded capacity");
        queue.Stop();
    }
}

static async Task TestStopWakesWaiters()
{
    var (listener, receiver, sender) = await ConnectLoopback();
    using (listener)
    using (receiver)
    using (sender)
    {
        var readerQueue = new GameSvr.SendQueue(sender, capacity: 1);
        var readerTask = readerQueue.ProcessSendQueue();
        readerQueue.Stop();
        await readerTask.WaitAsync(TimeSpan.FromSeconds(2));
        Check(readerTask.IsCompletedSuccessfully, "blocked reader faulted during Stop");

        var writerQueue = new GameSvr.SendQueue(sender, capacity: 1);
        writerQueue.AddToQueue(new byte[] { 1 });
        var writerTask = writerQueue.AddToQueueAsync(new byte[] { 2 }).AsTask();
        await Task.Delay(50);
        Check(!writerTask.IsCompleted, "writer did not apply backpressure");

        writerQueue.Stop();
        var woke = false;
        try
        {
            await writerTask.WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (OperationCanceledException)
        {
            woke = true;
        }
        Check(woke, "Stop did not wake the blocked writer");
    }
}

static async Task TestStopCancelsSend()
{
    var (listener, receiver, sender) = await ConnectLoopback();
    using (listener)
    using (receiver)
    using (sender)
    {
        sender.SendBufferSize = 1024;
        receiver.ReceiveBufferSize = 1024;
        var queue = new GameSvr.SendQueue(sender, capacity: 1);
        var sendTask = queue.ProcessSendQueue();

        queue.AddToQueue(new byte[32 * 1024 * 1024]);
        await Task.Delay(100);
        queue.Stop();

        await sendTask.WaitAsync(TimeSpan.FromSeconds(2));
        Check(sendTask.IsCompletedSuccessfully, "canceled send loop faulted");
    }
}

static async Task<(TcpListener Listener, TcpClient Receiver, Socket Sender)> ConnectLoopback()
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    var receiver = new TcpClient();
    var acceptTask = listener.AcceptSocketAsync();
    await receiver.ConnectAsync(IPAddress.Loopback, port);
    return (listener, receiver, await acceptTask);
}

static byte[] MakePayload(int length, int seed)
{
    var payload = new byte[length];
    for (var i = 0; i < payload.Length; i++) payload[i] = unchecked((byte)(i * 31 + seed));
    return payload;
}

static async Task<byte[]> ReadExactly(NetworkStream stream, int length)
{
    var buffer = new byte[length];
    var offset = 0;
    while (offset < length)
    {
        var read = await stream.ReadAsync(buffer.AsMemory(offset, length - offset));
        if (read == 0) throw new EndOfStreamException($"socket closed at {offset}/{length}");
        offset += read;
    }
    return buffer;
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
