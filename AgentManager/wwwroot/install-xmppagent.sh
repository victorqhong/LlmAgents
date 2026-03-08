apt install -y curl git libicu76
curl -L https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 9.0 -i /opt/dotnet

git clone https://github.com/victorqhong/LlmAgents /opt/LlmAgents
/opt/dotnet/dotnet build /opt/LlmAgents

cat <<EOF > /etc/systemd/system/XmppAgent.service
[Unit]
Description=Agent Manager
After=network.target

[Service]
ExecStart=/opt/LlmAgents/XmppAgent/bin/Debug/net9.0/XmppAgent
Restart=always
User=root
WorkingDirectory=/root
Environment="DOTNET_ROOT=/opt/dotnet"
Environment="PATH=/opt/dotnet:/opt/dotnet/tools:/root/.dotnet/tools"

[Install]
WantedBy=multi-user.target
EOF
systemctl daemon-reload
