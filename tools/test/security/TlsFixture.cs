using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

internal static class TlsFixture
{
    public static async Task RunAsync(string receipt)
    {
        using RSA key = RSA.Create(2048);
        CertificateRequest request = new("CN=cert-failure.test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        SubjectAlternativeNameBuilder names = new(); names.AddDnsName("cert-failure.test");
        request.CertificateExtensions.Add(names.Build());
        using X509Certificate2 generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        // Windows SChannel requires an imported key for this synthetic server credential.
        using X509Certificate2 cert = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null);
        TcpListener listener = new(IPAddress.Loopback, 9442); listener.Start();
        Console.WriteLine("Synthetic invalid-certificate TLS fixture on loopback port 9442.");
        int connections = 0, requests = 0;
        object gate = new();
        while (true)
        {
            TcpClient client = await listener.AcceptTcpClientAsync();
            Interlocked.Increment(ref connections);
            _ = Task.Run(async () => {
            using TcpClient ownedClient = client;
            using SslStream ssl = new(client.GetStream(), false);
            string outcome;
            using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(5));
            try
            {
                await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = cert }, deadline.Token);
                byte[] buffer = new byte[4096];
                int size = await ssl.ReadAsync(buffer, deadline.Token);
                if (size > 0)
                {
                    Interlocked.Increment(ref requests);
                    byte[] body = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 25\r\nConnection: close\r\n\r\nUnexpected TLS navigation");
                    await ssl.WriteAsync(body);
                }
                outcome = "closed";
            }
            catch (Exception ex) when (ex is IOException or System.Security.Authentication.AuthenticationException or OperationCanceledException)
            { outcome = ex.GetType().Name + ": " + ex.Message + " Inner: " + ex.InnerException?.Message; }
            lock (gate) File.WriteAllText(receipt, JsonSerializer.Serialize(new { connections, requests, outcome }));
            });
        }
    }
}
