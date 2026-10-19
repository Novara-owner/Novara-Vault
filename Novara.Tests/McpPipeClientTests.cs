using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;
using NovaraMCP;
using Xunit;

namespace Novara.Tests;

public class McpPipeClientTests
{
    private const string HelloReply = "{\"ok\":true}";
    private const string CallReply = "{\"ok\":true,\"result\":\"1\"}";

    [Fact]
    public async Task PipeClient_UndeliveredWrite_IsRetriedOnce_AndNeverDuplicated()
    {
        var pipeName = Novara.Ipc.McpIpcName.Current;
        var requests = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var sessionsServed = 0;

        var stop = new System.Threading.CancellationTokenSource();
        var serverTask = Task.Run(async () =>
        {
            try
            {
                NamedPipeServerStream NewServer() => new(
                    pipeName, PipeDirection.InOut, 2, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);


                var first = NewServer();
                await first.WaitForConnectionAsync(stop.Token);
                using (var reader = new StreamReader(first, Encoding.UTF8, false, 4096, leaveOpen: true))
                await using (var writer = new StreamWriter(first, new UTF8Encoding(false), 4096, leaveOpen: true)
                             { AutoFlush = true, NewLine = "\n" })
                {
                    var hello = await reader.ReadLineAsync(stop.Token);
                    if (hello == null) return;
                    await writer.WriteLineAsync(HelloReply);

                    var call = await reader.ReadLineAsync(stop.Token);
                    if (call == null) return;
                    requests.Enqueue(call);
                    await writer.WriteLineAsync(CallReply);
                    sessionsServed++;
                }





                var second = NewServer();
                var accepted = second.WaitForConnectionAsync(stop.Token);
                first.Dispose();
                await Task.Delay(150);


                await accepted;
                using var reader2 = new StreamReader(second, Encoding.UTF8, false, 4096, leaveOpen: true);
                await using var writer2 = new StreamWriter(second, new UTF8Encoding(false), 4096, leaveOpen: true)
                { AutoFlush = true, NewLine = "\n" };

                var hello2 = await reader2.ReadLineAsync(stop.Token);
                if (hello2 == null) return;
                await writer2.WriteLineAsync(HelloReply);

                var call2 = await reader2.ReadLineAsync(stop.Token);
                if (call2 == null) return;
                requests.Enqueue(call2);
                await writer2.WriteLineAsync(CallReply);
                sessionsServed++;
            }
            catch (OperationCanceledException) { }
        }, stop.Token);


        await Task.Delay(100);

        var client = new McpPipeClient(null);
        try
        {
            Assert.Equal("\"1\"", client.Call("read_x", null));



            await Task.Delay(400);





            Assert.Equal("\"1\"", client.Call("create_memo", new JsonObject { ["name"] = "n" }));
            Assert.Equal(2, sessionsServed);
            Assert.Equal(1, requests.Count(r => r.Contains("create_memo")));
            Assert.Equal(1, requests.Count(r => r.Contains("read_x")));
        }
        finally
        {
            stop.Cancel();
        }
        try { await serverTask; } catch (OperationCanceledException) { }
    }
}
