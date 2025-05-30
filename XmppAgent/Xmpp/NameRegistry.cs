
using System.Xml.Linq;

namespace XmppAgent.Xmpp
{
    public static class NameRegistry
    {
        public const string NAMESPACE_XMPP_JINGLE = "urn:xmpp:jingle:1";

        public const string NAMESPACE_XMPP_JINGLE_APPS_FILETRANSFER = "urn:xmpp:jingle:apps:file-transfer:5";

        public const string NAMESPACE_XMPP_JINGLE_TRANSPORTS_IBB = "urn:xmpp:jingle:transports:ibb:1";

        public const string NAMESPACE_JABBER_PROTOCOL_IBB = "http://jabber.org/protocol/ibb";

        public const string NAMESPACE_XMPP_HASHES_2 = "urn:xmpp:hashes:2";

        public static readonly XName NameJingle = XName.Get("jingle", NAMESPACE_XMPP_JINGLE);

        public static readonly XName NameJingleContent = XName.Get("content", NAMESPACE_XMPP_JINGLE);

        public static readonly XName NameJingleReason = XName.Get("reason", NAMESPACE_XMPP_JINGLE);

        public static readonly XName NameJingleSuccess = XName.Get("success", NAMESPACE_XMPP_JINGLE);

        public static readonly XName NameJingleAppsFileTransferDescription = XName.Get("description", NAMESPACE_XMPP_JINGLE_APPS_FILETRANSFER);

        public static readonly XName NameJingleAppsFileTransferChecksum = XName.Get("checksum", NAMESPACE_XMPP_JINGLE_APPS_FILETRANSFER);

        public static readonly XName NameJingleAppsFileTransferFile = XName.Get("file", NAMESPACE_XMPP_JINGLE_APPS_FILETRANSFER);

        public static readonly XName NameJingleAppsFileTransferName = XName.Get("name", NAMESPACE_XMPP_JINGLE_APPS_FILETRANSFER);

        public static readonly XName NameJingleAppsFileTransferSize = XName.Get("size", NAMESPACE_XMPP_JINGLE_APPS_FILETRANSFER);

        public static readonly XName NameJingleAppsFileTransferMediaType = XName.Get("mediaType", NAMESPACE_XMPP_JINGLE_APPS_FILETRANSFER);

        public static readonly XName NameJingleTransportsIbbTransport = XName.Get("transport", NAMESPACE_XMPP_JINGLE_TRANSPORTS_IBB);

        public static readonly XName NameJabberIbbData = XName.Get("data", NAMESPACE_JABBER_PROTOCOL_IBB);

        public static readonly XName NameJabberIbbOpen = XName.Get("open", NAMESPACE_JABBER_PROTOCOL_IBB);

        public static readonly XName NameHashesHash = XName.Get("hash", NAMESPACE_XMPP_HASHES_2);
    }
}
