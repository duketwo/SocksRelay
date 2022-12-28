// 
// (c) duketwo, cryomyst 2022
// This code is licensed under MIT license
//

using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SocksRelay
{

    class SocksRelay : IDisposable
    {
        private readonly string _remoteAddress;
        private readonly ushort _remotePort;
        private readonly string _remoteUsername;
        private readonly string _remotePassword;

        private ushort? _localPort;
        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private CancellationToken _cancellationToken = default;
        private TcpListener _localListener;

        public SocksRelay(
            string remoteAddress,
            ushort remotePort,
            string remoteUsername,
            string remotePassword,
            ushort? localPort = null)
        {
            _remoteAddress = remoteAddress;
            _remotePort = remotePort;
            _remoteUsername = remoteUsername;
            _remotePassword = remotePassword;
            _localPort = localPort;
        }

        public static string ByteArrayToString(byte[] ba)
        {
            StringBuilder hex = new StringBuilder(ba.Length * 2);
            foreach (byte b in ba)
                hex.AppendFormat("{0:x2}", b);
            return hex.ToString();
        }

        public async Task<ushort> StartListening()
        {
            _cancellationToken = _cancellationTokenSource.Token;
            var localPort = await SetupLocalListener();
            _ = Task.Factory.StartNew(Listen, TaskCreationOptions.LongRunning);
            return localPort;
        }

        private async Task Listen()
        {
            while (!_cancellationToken.IsCancellationRequested)
            {
                var client = await _localListener.AcceptTcpClientAsync(_cancellationToken);
                _ = HandleClient(client);
            }
        }


        private async Task<ushort> SetupLocalListener()
        {
            _localListener = new TcpListener(IPAddress.Any, _localPort ?? 0);
            _localListener.Start();
            return (ushort)((IPEndPoint)_localListener.LocalEndpoint).Port;
        }

        private async Task<TcpClient> SetupRemoteClient()
        {
            var remoteClient = new TcpClient();
            await remoteClient.ConnectAsync(_remoteAddress, _remotePort);
            var remoteClientStream = remoteClient.GetStream();
            Console.WriteLine($"Connected to proxy {_remoteAddress}:{_remotePort}");

            // Authenticate with the target SOCKS5 proxy
            var authRequest = new byte[]
            {
                    0x05, // Socks version
                    0x02, // Count of methods avaiable for auth below
                    0x00, // X'00' NO AUTHENTICATION REQUIRED
                    0x02  // X'02' USERNAME/PASSWORD
            };
            await remoteClientStream.WriteAsync(authRequest);
            var authResponse = new byte[2];
            await remoteClientStream.ReadAsync(authResponse, 0, authResponse.Length);
            // Check if the server supports username/password authentication
            if (authResponse[1] != 0x02)
            {
                throw new Exception($"Username/Password authentication method not supported by proxy. Response was [{ByteArrayToString(authResponse)}]");
            }

            Console.WriteLine($"AuthResponse was [{ByteArrayToString(authResponse)}]");

            // Send the authentication credentials
            var usernameBytes = System.Text.Encoding.ASCII.GetBytes(_remoteUsername);
            var passwordBytes = System.Text.Encoding.ASCII.GetBytes(_remotePassword);


            var authCredentials = new byte[3 + usernameBytes.Length + passwordBytes.Length];
            authCredentials[0] = 0x01; // authentication method: username/password
            authCredentials[1] = (byte)usernameBytes.Length; // username length
            Array.Copy(usernameBytes, 0, authCredentials, 2, usernameBytes.Length); // username

            authCredentials[2 + usernameBytes.Length] = (byte)passwordBytes.Length; // password length
            Array.Copy(passwordBytes, 0, authCredentials, 2 + usernameBytes.Length + 1, passwordBytes.Length); // password

            Console.WriteLine($"authCredentials {ByteArrayToString(authCredentials)}");

            await remoteClientStream.WriteAsync(authCredentials, 0, authCredentials.Length);
            var authResult = new byte[2];
            var readBytes = await remoteClientStream.ReadAsync(authResult, 0, authResult.Length);


            if (readBytes != 2)
                throw new Exception("Bad response received from proxy server.");
            if (authResult[1] != 0x00)
                throw new Exception("Bad Usernaem/Password.");


            Console.WriteLine($"Proxy authentication successful. Response was [{ByteArrayToString(authResult)}]");
            return remoteClient;
        }

        private async Task HandleClient(TcpClient client)
        {
            try
            {
                var remoteClient = await SetupRemoteClient();
                var clientStream = client.GetStream();
                var authRequest = new byte[2];
                var authRequestByteCount = await clientStream.ReadAsync(authRequest, 0, 2);
                Console.WriteLine(($"Data received from the client: [{ByteArrayToString(authRequest)}]"));
                if (authRequestByteCount != 2)
                {
                    throw new Exception($"Server didn't respond with 2 bytes.");
                }

                if (authRequest[0] != 0x05)
                {
                    throw new Exception($"Client is not using the Socks5 protocol.");
                }

                var numberOfAuthTypes = authRequest[1];
                var authTypes = new byte[numberOfAuthTypes];
                var numerOfAuthTypesRead = client.GetStream().Read(authTypes, 0, numberOfAuthTypes); // read the available methods into an empty buffer so the client doesn't get stuck
                Console.WriteLine(($"Data received from the client: [{ByteArrayToString(authTypes)}] Read bytes [{numerOfAuthTypesRead}]"));

                var authResponse = new byte[]
                {
                        0x05, // Socks version
                        0x00  // Selected Auth method, 0 is unauthenticated
                };
                await clientStream.WriteAsync(authResponse, 0, authResponse.Length);

                var clientCancellationTokenSource = new CancellationTokenSource();
                var clientCancellationTokenLinked = CancellationTokenSource.CreateLinkedTokenSource(
                    clientCancellationTokenSource.Token,
                    _cancellationToken);
                var remoteClientStream = remoteClient.GetStream();

                async Task ClientToServer()
                {
                    await clientStream.CopyToAsync(remoteClientStream, clientCancellationTokenLinked.Token);
                }

                async Task ServerToClient()
                {
                    await remoteClientStream.CopyToAsync(clientStream, clientCancellationTokenLinked.Token);
                }

                await Task.WhenAny(ClientToServer(), ServerToClient());
                clientCancellationTokenSource.Cancel();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            finally
            {
                client?.Dispose();
            }
        }

        public void Stop()
        {
            _cancellationTokenSource?.Cancel();
            _localListener.Stop();
        }

        public void Dispose()
        {
            Stop();
        }
    }

}