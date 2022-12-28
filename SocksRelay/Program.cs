// 
// (c) duketwo, cryomyst 2022
// This code is licensed under MIT license
//

using System;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Threading;

namespace SocksRelay
{
    public class Program
    {
        // Do not change
        private const string Localhost = "127.0.0.1";

        // The local port to listen for incoming connections
        private const int LocalPort = 41337;

        // The target SOCKS5 proxy host and port
        private const string ProxyHost = "1.2.3.4";
        private const int ProxyPort = 1337;

        // The username and password for authenticating with the target SOCKS5 proxy
        private const string ProxyUsername = "ProxyUsername";
        private const string ProxyPassword = "ProxyPassword";



        public static async Task GetRequest(ushort localPort)
        {
            var proxy = new WebProxy();
            proxy.Address = new Uri($"socks5://{Localhost}:{localPort}");
            //proxy.Credentials = new NetworkCredential(ProxyUsername,ProxyPassword); //Used to set Proxy logins. 
            var handler = new HttpClientHandler
            {
                Proxy = proxy
            };
            using (var httpClient = new HttpClient(handler))
            {
                var request = await httpClient.GetAsync("https://whoer.net/ip");
                var body = await request.Content.ReadAsStringAsync();
                Console.WriteLine($"Request response [{body}]");
            }
        }

        static async Task Main(string[] args)
        {
            using var socksRelay = new SocksRelay(
                ProxyHost,
                ProxyPort,
                ProxyUsername,
                ProxyPassword,
                (ushort)LocalPort);
            var localPort = await socksRelay.StartListening();


            _ = Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        await Task.Delay(1000);
                        await GetRequest(localPort);
                    }
                    catch { }
                    //break;
                }
            });

            // keep alive until disposal
            await Task.Delay(30000);
        }
    }
}