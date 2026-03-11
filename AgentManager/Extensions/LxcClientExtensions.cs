using System.Security.Cryptography.X509Certificates;
using AgentManager.Configuration;
using AgentManager.Models.Containers;
using AgentManager.Services;

namespace AgentManager.Extensions;

public static class LxcClientExtensions
{
    public static void ConfigureProvisioning(this WebApplicationBuilder builder)
    {
        var userConfigFile = builder.Configuration["Provisioning:UserConfigFile"];
        var apiConfigFile = builder.Configuration["Provisioning:ApiConfigFile"];
        var containerImage = builder.Configuration["Provisioning:ContainerImage"];
        var xmppTargetJid = builder.Configuration["Provisioning:XmppTargetJid"];

        ArgumentException.ThrowIfNullOrEmpty(userConfigFile);
        ArgumentException.ThrowIfNullOrEmpty(apiConfigFile);
        ArgumentException.ThrowIfNullOrEmpty(containerImage);
        ArgumentException.ThrowIfNullOrEmpty(xmppTargetJid);

        var provisioningOptions = new ProvisioningOptions
        {
            UserConfigFile = userConfigFile,
            ApiConfigFile = apiConfigFile,
            ContainerImage = containerImage,
            XmppTargetJid = xmppTargetJid
        };

        builder.Services.AddSingleton(provisioningOptions);
    }

    public static void ConfigureLxcClient(this WebApplicationBuilder builder)
    {
        var clientCertFilePath = builder.Configuration["Lxc:ClientCertFilePath"];
        var clientKeyFilePath = builder.Configuration["Lxc:ClientKeyFilePath"];
        var serverCertFilePath = builder.Configuration["Lxc:ServerCertFilePath"];
        var baseAddress = builder.Configuration["Lxc:BaseAddress"];

        ArgumentException.ThrowIfNullOrEmpty(clientCertFilePath);
        ArgumentException.ThrowIfNullOrEmpty(clientKeyFilePath);
        ArgumentException.ThrowIfNullOrEmpty(serverCertFilePath);
        ArgumentException.ThrowIfNullOrEmpty(baseAddress);

        var lxcOptions = new LxcOptions
        {
            BaseAddress = baseAddress,
            ClientCertFilePath = clientCertFilePath,
            ClientKeyFilePath = clientKeyFilePath,
            ServerCertFilePath = serverCertFilePath
        };

        builder.Services.AddSingleton(lxcOptions);

        builder.Services.AddHttpClient<ContainerService.LxcHttpClient>(client =>
        {
            client.BaseAddress = new Uri(lxcOptions.BaseAddress);
        })
        .ConfigurePrimaryHttpMessageHandler(sp =>
        {
            var clientCertificate = X509Certificate2.CreateFromPem(File.ReadAllText(lxcOptions.ClientCertFilePath), File.ReadAllText(lxcOptions.ClientKeyFilePath));
            var serverCert = X509Certificate2.CreateFromPem(File.ReadAllText(lxcOptions.ServerCertFilePath));

            var handler = new SocketsHttpHandler();

            handler.SslOptions.ClientCertificates = [clientCertificate];
            handler.SslOptions.RemoteCertificateValidationCallback = (sender, cert, chain, errors) =>
            {
                if (errors == System.Net.Security.SslPolicyErrors.None)
                {
                    return true;
                }

                if (cert == null)
                {
                    return false;
                }

                if (chain == null)
                {
                    return false;
                }

                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.Add(serverCert);
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

                return chain.Build((X509Certificate2)cert);
            };

            return handler;
        });
    }
}
