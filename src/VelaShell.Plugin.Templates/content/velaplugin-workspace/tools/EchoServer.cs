// 本机试跑用的回显服务:收到什么就原样发回去。插件装进 VelaShell 之后,
// 新建一条本协议的连接指向 127.0.0.1:7007,敲的每个字都会被它弹回终端。
//
//     dotnet run --file tools/EchoServer.cs            # 默认端口 7007
//     dotnet run --file tools/EchoServer.cs -- 9000    # 换个端口
//
// 这是 .NET 的单文件程序(file-based app),不属于插件工程:工程文件里用
// DefaultItemExcludes 把 tools/ 排除在编译之外,它也不会进 .vpx。
// `--file` 不能省:当前目录里有插件的 .csproj,省掉的话 dotnet run 会去跑那个工程,
// 把这个文件名当成它的参数。
using System.Net;
using System.Net.Sockets;

int port = args.Length > 0 && int.TryParse(args[0], out int parsed) ? parsed : 7007;
var listener = new TcpListener(IPAddress.Loopback, port);
listener.Start();
Console.WriteLine($"Echo server listening on 127.0.0.1:{port}. Press Ctrl+C to stop.");

while (true)
{
    TcpClient client = await listener.AcceptTcpClientAsync();
    _ = Task.Run(async () =>
    {
        using (client)
        {
            Console.WriteLine($"+ {client.Client.RemoteEndPoint}");
            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[4096];
            try
            {
                int read;
                while ((read = await stream.ReadAsync(buffer)) > 0)
                {
                    await stream.WriteAsync(buffer.AsMemory(0, read));
                }
            }
            catch (IOException)
            {
                // 对端断开。
            }
            Console.WriteLine("- disconnected");
        }
    });
}
