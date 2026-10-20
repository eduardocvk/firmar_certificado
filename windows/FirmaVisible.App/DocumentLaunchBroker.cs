using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FirmaVisible.App;

/// <summary>Delivers Explorer openings to the existing window, using a pipe restricted to this Windows user.</summary>
public sealed class DocumentLaunchBroker : IDisposable
{
    private readonly Mutex mutex;
    private readonly string pipeName;
    private readonly CancellationTokenSource stopping=new();
    public bool IsPrimary { get; }
    public DocumentLaunchBroker(string identity)
    {
        string id=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserName+"|"+identity)))[..24];
        pipeName="FirmaPDF-"+id;
        mutex=new Mutex(true,@"Local\"+pipeName,out bool created);IsPrimary=created;
    }
    public void Listen(Action<string[]> receive)=>_=Task.Run(async()=>
    {
        while(!stopping.IsCancellationRequested)
        {
            try
            {
                await using var pipe=new NamedPipeServerStream(pipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stopping.Token);
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);timeout.CancelAfter(TimeSpan.FromSeconds(10));
                byte[] header=new byte[4];await pipe.ReadExactlyAsync(header,timeout.Token);int length=BinaryPrimitives.ReadInt32LittleEndian(header);
                if(length is <2 or >262144)continue;
                byte[] bytes=new byte[length];await pipe.ReadExactlyAsync(bytes,timeout.Token);
                var paths=JsonSerializer.Deserialize<string[]>(bytes)??[];
                if(paths.Length>1000)continue;
                paths=paths.Select(LaunchDocument.Validate).ToArray();
                receive(paths);await pipe.WriteAsync(new byte[]{1},timeout.Token);await pipe.FlushAsync(timeout.Token);
            }
            catch(OperationCanceledException) { }
            catch(IOException) { }
            catch(ArgumentException) { }
            catch(JsonException) { }
        }
    });
    public async Task SendAsync(string[] paths)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await using var pipe=new NamedPipeClientStream(".",pipeName,PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(timeout.Token);
        byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(paths);if(bytes.Length>262144)throw new IOException("La lista de archivos es demasiado grande.");
        byte[] header=new byte[4];BinaryPrimitives.WriteInt32LittleEndian(header,bytes.Length);
        await pipe.WriteAsync(header,timeout.Token);await pipe.WriteAsync(bytes,timeout.Token);await pipe.FlushAsync(timeout.Token);
        byte[] reply=new byte[1];await pipe.ReadExactlyAsync(reply,timeout.Token);if(reply[0]!=1)throw new IOException("No se pudo abrir la lista de PDF.");
    }
    public void Dispose()
    {stopping.Cancel();if(IsPrimary)mutex.ReleaseMutex();mutex.Dispose();stopping.Dispose();}
}
