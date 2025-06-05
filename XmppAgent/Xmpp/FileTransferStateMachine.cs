namespace XmppAgent.Xmpp;

using LlmAgents.LlmApi.Content;
using System.Reactive.Linq;
using System.Security.Cryptography;
using System.Text;
using XmppDotNet;
using XmppDotNet.Xml;
using XmppDotNet.Xmpp;
using XmppDotNet.Xmpp.Client;

public class FileTransferStateMachine : XmppStateMachine<IEnumerable<MessageContentImageUrl>>
{
    private IDisposable? jingleContentSubscriber;

    private IDisposable? jingleChecksumSubscriber;

    private IDisposable? openSubscriber;

    private IDisposable? dataSubscriber;

    private readonly List<FileTransferSession> transferSessions = new List<FileTransferSession>();

    private readonly List<MessageContentImageUrl> imageURLs = new List<MessageContentImageUrl>();

    public FileTransferStateMachine(XmppClient xmppClient)
        : base(xmppClient)
    {
    }

    public override void Begin()
    {
        Result = null;

        if (jingleContentSubscriber == null)
        {
            jingleContentSubscriber = XmppClient.XmppXElementReceived
                .Where(WhereJingleContent)
                .Subscribe(SubscribeJingleContent);
        }
    }

    public override void End()
    {
        Result = imageURLs.ToArray();

        jingleContentSubscriber?.Dispose();
        jingleContentSubscriber = null;

        jingleChecksumSubscriber?.Dispose();
        jingleChecksumSubscriber = null;

        openSubscriber?.Dispose();
        openSubscriber = null;

        dataSubscriber?.Dispose();
        dataSubscriber = null;

        for (int i = 0; i < transferSessions.Count; i++)
        {
            transferSessions[i].Dispose();
        }

        transferSessions.Clear();
        imageURLs.Clear();
    }

    public override void Run()
    {
        foreach (var transferSession in transferSessions)
        {
            if (transferSession.SessionFinished())
            {
                continue;
            }

            for (int j = 0; j < transferSession.content?.Length; j++)
            {
                var content = transferSession.content[j];
                if (!content.ReceivedAllData())
                {
                    continue;
                }

                var memoryStream = new MemoryStream(content.fileSize);
                for (int i = 0; i < content.data.Count; i++)
                {
                    var bytes = Convert.FromBase64String(content.data[i]);
                    memoryStream.Write(bytes);
                }

                memoryStream.Position = 0;

                if (!string.IsNullOrEmpty(content.hashSha256))
                {
                    var hash = Convert.FromBase64String(content.hashSha256);
                    var hashString = HashToString(hash);

                    string computedHashString;
                    using (var sha256Hash = SHA256.Create())
                    {
                        var computedHash = sha256Hash.ComputeHash(memoryStream);
                        computedHashString = HashToString(computedHash);
                    }

                    if (!computedHashString.Equals(hashString))
                    {
                        // TODO: handle unexpected checksum failure
                        continue;
                    }
                }

                var contentBytes = memoryStream.ToArray();
                imageURLs.Add(new MessageContentImageUrl
                {
                    DataBase64 = Convert.ToBase64String(contentBytes),
                    MimeType = content.fileMediaType
                });

                content.Finished = true;
            }

            if (transferSession.SessionFinished())
            {
                var terminateReason = new XmppXElement(NameRegistry.NameJingleReason);
                terminateReason.Add(new XmppXElement(NameRegistry.NameJingleSuccess));

                var terminateJingle = new XmppXElement(NameRegistry.NameJingle)
                    .SetAttribute("action", "session-terminate")
                    .SetAttribute("sid", transferSession.sid);

                terminateJingle.Add(terminateReason);

                var terminate = new Iq(transferSession.From, transferSession.To, IqType.Set);
                terminate.GenerateId();

                terminate.Add(terminateJingle);

                Task.Run(async () => await XmppClient.SendAsync(terminate));
            }
        }
    }

    private bool WhereJingleContent(XmppXElement el)
    {
        if (!el.OfType<Iq>())
        {
            return false;
        }

        var jingle = el.Element(NameRegistry.NameJingle);
        if (jingle == null)
        {
            return false;
        }

        var content = jingle.Element(NameRegistry.NameJingleContent);
        if (content == null)
        {
            return false;
        }

        return true;
    }

    private void SubscribeJingleContent(XmppXElement el)
    {
        var toJid = el.GetAttributeJid("to");
        var fromJid = el.GetAttributeJid("from");
        var id = el.GetAttribute("id");

        var jingle = el.Element(NameRegistry.NameJingle);
        if (jingle == null)
        {
            return;
        }

        var jingleSid = jingle.Attribute("sid");
        if (jingleSid == null || string.IsNullOrEmpty(jingleSid.Value))
        {
            return;
        }

        var jingleAction = jingle.Attribute("action");
        if (jingleAction == null || string.IsNullOrEmpty(jingleAction.Value) || !"session-initiate".Equals(jingleAction.Value))
        {
            return;
        }

        var contentFiles = new List<JingleContentFile>();
        foreach (var content in jingle.Elements(NameRegistry.NameJingleContent))
        {
            var contentName = content.Attribute("name");
            if (contentName == null || string.IsNullOrEmpty(contentName.Value))
            {
                continue;
            }

            var contentCreator = content.Attribute("creator");
            if (contentCreator == null || string.IsNullOrEmpty(contentCreator.Value))
            {
                continue;
            }

            var description = content.Element(NameRegistry.NameJingleAppsFileTransferDescription);
            if (description == null)
            {
                continue;
            }

            var file = description.Element(NameRegistry.NameJingleAppsFileTransferFile);
            if (file == null)
            {
                continue;
            }

            var fileName = file.Element(NameRegistry.NameJingleAppsFileTransferName);
            if (fileName == null || string.IsNullOrEmpty(fileName.Value))
            {
                continue;
            }

            var fileSize = file.Element(NameRegistry.NameJingleAppsFileTransferSize);
            if (fileSize == null || string.IsNullOrEmpty(fileSize.Value))
            {
                continue;
            }

            var fileMediaType = file.Element(NameRegistry.NameJingleAppsFileTransferMediaType);
            if (fileMediaType == null || string.IsNullOrEmpty(fileMediaType.Value))
            {
                continue;
            }

            var transport = content.Element(NameRegistry.NameJingleTransportsIbbTransport);
            if (transport == null)
            {
                continue;
            }

            var transportSid = transport.Attribute("sid");
            if (transportSid == null || string.IsNullOrEmpty(transportSid.Value))
            {
                continue;
            }

            var transportBlockSize = transport.Attribute("block-size");
            if (transportBlockSize == null || string.IsNullOrEmpty(transportBlockSize.Value))
            {
                continue;
            }

            var contentFile = new JingleContentFile
            {
                creator = contentCreator.Value,
                name = contentName.Value,
                fileName = fileName.Value,
                fileSize = int.Parse(fileSize.Value),
                fileMediaType = fileMediaType.Value,
                transportSid = transportSid.Value,
                transportBlockSize = int.Parse(transportBlockSize.Value)
            };

            var contentSenders = content.Attribute("senders");
            if (contentSenders != null && !string.IsNullOrEmpty(contentSenders.Value))
            {
                contentFile.senders = contentSenders.Value;
            }

            contentFiles.Add(contentFile);
        }

        Task.Run(async () => await XmppClient.SendAsync(new Iq(fromJid, toJid, IqType.Result, id)));

        var responseJingle = new XmppXElement(NameRegistry.NameJingle)
            .SetAttribute("sid", jingleSid.Value)
            .SetAttribute("responder", toJid.ToString())
            .SetAttribute("action", "session-accept");

        foreach (var content in contentFiles)
        {
            var responseDescription = new XmppXElement(NameRegistry.NameJingleAppsFileTransferDescription);
            var responseTransport = new XmppXElement(NameRegistry.NameJingleTransportsIbbTransport)
                .SetAttribute("sid", content.transportSid);

            var responseContent = new XmppXElement(NameRegistry.NameJingleContent)
                .SetAttribute("name", content.name)
                .SetAttribute("creator", "initiator");
            responseContent.Add(responseDescription);
            responseContent.Add(responseTransport);

            responseJingle.Add(responseContent);
        }

        transferSessions.Add(new FileTransferSession
        {
            sid = jingleSid.Value,
            content = contentFiles.ToArray(),
            From = fromJid,
            To = toJid
        });

        var response = new Iq(fromJid, toJid, IqType.Set);
        response.GenerateId();
        response.Add(responseJingle);

        if (openSubscriber == null)
        {
            openSubscriber = XmppClient.XmppXElementReceived
                .Where(WhereOpen)
                .Subscribe(SubscribeOpen);
        }

        Task.Run(async () => await XmppClient.SendAsync(response));
    }

    private bool WhereOpen(XmppXElement el)
    {
        if (!el.OfType<Iq>())
        {
            return false;
        }

        if (!"set".Equals(el.GetAttribute("type")))
        {
            return false;
        }

        var open = el.Element(NameRegistry.NameJabberIbbOpen);
        if (open == null)
        {
            return false;
        }

        return true;
    }

    private void SubscribeOpen(XmppXElement el)
    {
        var open = el.Element(NameRegistry.NameJabberIbbOpen);
        if (open == null)
        {
            return;
        }

        var sid = open.Attribute("sid");
        var blockSize = open.Attribute("block-size");

        if (dataSubscriber == null)
        {
            dataSubscriber = XmppClient.XmppXElementReceived
                .Where(WhereData)
                .Subscribe(SubscribeData);
        }

        if (jingleChecksumSubscriber == null)
        {
            jingleChecksumSubscriber = XmppClient.XmppXElementReceived
                .Where(WhereJingleChecksum)
                .Subscribe(SubscribeJingleChecksum);
        }

        var toJid = el.GetAttributeJid("to");
        var fromJid = el.GetAttributeJid("from");
        var id = el.GetAttribute("id");

        Task.Run(async () => await XmppClient.SendAsync(new Iq(fromJid, toJid, IqType.Result, id)));
    }

    private bool WhereData(XmppXElement el)
    {
        if (!el.OfType<Iq>())
        {
            return false;
        }

        if (!"set".Equals(el.GetAttribute("type")))
        {
            return false;
        }

        var data = el.Element(NameRegistry.NameJabberIbbData);
        if (data == null)
        {
            return false;
        }

        return true;
    }

    private void SubscribeData(XmppXElement el)
    {
        var data = el.Element(NameRegistry.NameJabberIbbData);
        if (data == null)
        {
            return;
        }

        var sid = data.Attribute("sid");
        if (sid == null || string.IsNullOrEmpty(sid.Value))
        {
            return;
        }

        var seq = data.Attribute("seq");
        if (seq == null || string.IsNullOrEmpty(seq.Value))
        {
            return;
        }

        JingleContentFile? contentFile = null;
        foreach (var transferSession in transferSessions)
        {
            contentFile = transferSession.FindByTransportSid(sid.Value);
            if (contentFile != null)
            {
                break;
            }
        }
        
        if (contentFile == null)
        {
            return;
        }

        contentFile.data.Add(int.Parse(seq.Value), data.Value);

        var toJid = el.GetAttributeJid("to");
        var fromJid = el.GetAttributeJid("from");
        var id = el.GetAttribute("id");

        Task.Run(async () => await XmppClient.SendAsync(new Iq(fromJid, toJid, IqType.Result, id)));
    }

    private bool WhereJingleChecksum(XmppXElement el)
    {
        if (!el.OfType<Iq>())
        {
            return false;
        }

        var jingle = el.Element(NameRegistry.NameJingle);
        if (jingle == null)
        {
            return false;
        }

        var checksum = jingle.Element(NameRegistry.NameJingleAppsFileTransferChecksum);
        if (checksum == null)
        {
            return false;
        }

        return true;
    }

    private void SubscribeJingleChecksum(XmppXElement el)
    {
        var toJid = el.GetAttributeJid("to");
        var fromJid = el.GetAttributeJid("from");
        var id = el.GetAttribute("id");

        Task.Run(async () => await XmppClient.SendAsync(new Iq(fromJid, toJid, IqType.Result, id)));

        var jingle = el.Element(NameRegistry.NameJingle);
        if (jingle == null)
        {
            return;
        }

        var checksum = jingle.Element(NameRegistry.NameJingleAppsFileTransferChecksum);
        if (checksum == null)
        {
            return;
        }

        var name = checksum.Attribute("name");
        if (name == null || string.IsNullOrEmpty(name.Value))
        {
            return;
        }

        var file = checksum.Element(NameRegistry.NameJingleAppsFileTransferFile);
        if (file == null)
        {
            return;
        }

        var elHashSha256 = file.Elements(NameRegistry.NameHashesHash).Where(el =>
        {
            var algo = el.Attribute("algo");
            if (algo == null)
            {
                return false;
            }

            if (!"sha-256".Equals(algo.Value))
            {
                return false;
            }

            return true;
        });

        var hashSha256 = elHashSha256.First()?.Value;
        if (string.IsNullOrEmpty(hashSha256))
        {
            return;
        }

        JingleContentFile? content = null;
        foreach (var transferSession in transferSessions)
        {
            content = transferSession.FindByContentName(name.Value);
            if (content != null)
            {
                break;
            }
        }

        if (content == null)
        {
            return;
        }

        content.hashSha256 = hashSha256;
    }

    private static string HashToString(byte[] hash)
    {
        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < hash.Length; i++)
        {
            builder.Append(hash[i].ToString("x2"));
        }

        return builder.ToString();
    }

    private class FileTransferSession
    {
        public required string sid;

        public required Jid From;

        public required Jid To;

        public JingleContentFile[]? content;

        public void Dispose()
        {
            if (content == null)
            {
                return;
            }

            for (int i = 0; i < content.Length; i++)
            {
                content[i].Dispose();
            }
        }

        public JingleContentFile? FindByContentName(string name)
        {
            if (content == null)
            {
                return null;
            }

            for (int i = 0; i < content.Length; i++)
            {
                if (content[i].name.Equals(name))
                {
                    return content[i];
                }
            }

            return null;
        }

        public JingleContentFile? FindByTransportSid(string sid)
        {
            if (content == null)
            {
                return null;
            }

            for (int i = 0; i < content.Length; i++)
            {
                if (content[i].transportSid.Equals(sid))
                {
                    return content[i];
                }
            }

            return null;
        }

        public bool SessionFinished()
        {
            if (content == null)
            {
                return true;
            }

            for (int i = 0; i < content.Length; i++)
            {
                if (!content[i].Finished)
                {
                    return false;
                }
            }

            return true;
        }
    }

    private class JingleContentFile
    {
        public required string creator;

        public string disposition = "session";

        public required string name;

        public string senders = "both";

        public required string fileName;

        public required int fileSize;

        public required string fileMediaType;

        public required string transportSid;

        public required int transportBlockSize;

        public readonly Dictionary<int, string> data = new Dictionary<int, string>();

        public string? hashSha256;

        public bool Finished = false;

        public void Dispose()
        {
            data.Clear();
        }

        public bool ReceivedAllData()
        { 
            return data.Count >= (int)((float)fileSize / transportBlockSize + 1);
        }
    }
}
