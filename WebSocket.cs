﻿using System;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using ftw_msgr.Crypto;
using Open.Nat;

namespace ftw_msgr.WebSocket;

public class MsgrServer
{
    TcpClient? socket;
    NetworkStream? netStream;
    BinaryReader? reader;
    BinaryWriter? writer;
    List<string> messages; 
    bool handleStarted;
    Crypt myCrypt;

    public MsgrServer(string arg = "")
    {   
        // Console.Clear();
        myCrypt = new Crypt();
        messages = new List<string>();
        string? Line = arg;
        while (Line != "client" && Line != "server")  
        {
            Console.Write("client or server? ");            
            Line = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(Line)) { Line = ""; }
        }
        switch (Line)
        {
            case "client": SetupClient(); break;
            case "server": SetupServer(); break;
        }
    }
    private void SendKey()
    {
        if (myCrypt.localPublicKey_Q.X is not null && myCrypt.localPublicKey_Q.Y is not null)
        {
            SendMsg<string>("ECC_PUB_KEY", false);
            SendMsg<int>(myCrypt.localPublicKey_Q.X.Length, false);
            SendMsg<byte[]>(myCrypt.localPublicKey_Q.X, false);
            SendMsg<byte[]>(myCrypt.localPublicKey_Q.Y, false);
        } 
        else
        {
            Console.WriteLine("Local public key not available.");
        }
    }

    public void SendMsg<T>(T msg, bool encrypt = true)
    {
        while (!handleStarted)
        {
            continue;
        }

        if (writer is null || msg is null)
        {
            Console.WriteLine("writer not initialized.");
        }
        else
        {
            Type myType = typeof(T);
            switch (myType)
            {
                case Type when myType == typeof(byte[]):
                    byte[]? myBytes = msg as byte[];
                    if (myBytes is null) { myBytes = Array.Empty<byte>(); }
                    char[]? myChars = new char[myBytes.Length*2];
                    if (encrypt)
                    {
                        Convert.ToBase64CharArray(myBytes, 0, myBytes.Length, myChars, 0);
                        myChars = myCrypt.EncryptMessage<char[]>(myChars);
                        if (myChars is null)
                        {
                            myBytes = Array.Empty<byte>();
                        } else
                        {
                            myBytes = Convert.FromBase64CharArray(myChars, 0, myChars.Length);
                        }
                    }
                    writer.Write(myBytes);
                    break;
                case Type when myType == typeof(int):
                    int myInt = 0;
                    if (msg is not null) 
                    {  
                        myInt = Convert.ToInt32(msg);
                    }
                    writer.Write(myInt);
                    break;
                case Type _ when myType == typeof(string):
                    string? myStr = msg as string;
                    if (myStr is null) { myStr = ""; }
                    if (encrypt)
                    {
                        myStr = myCrypt.EncryptMessage<string>(myStr);
                        if (myStr is null)
                        {
                            myStr = "";
                        }
                    }
                    writer.Write(myStr);
                    if (msg as string == "exit") { socket?.Close(); }
                    break;
                default:
                    break;
            }
        }
    }

    public List<string> MsgHistory => messages;

    private async void SetupServer()
    {
        var discoverer = new NatDiscoverer();
        try
        {
            // using SSDP protocol, it discovers NAT device.
            var device = await discoverer.DiscoverDeviceAsync();

            // display the NAT's IP address
            Console.WriteLine("The external IP Address is: {0} ", await device.GetExternalIPAsync());

            // create a new mapping in the router [external_ip:1702 -> host_machine:1602]
            await device.CreatePortMapAsync(new Mapping(Protocol.Tcp, 50001, 1702, "For testing"));
        } catch (NatDeviceNotFoundException e)
        {
            Console.WriteLine(e.Message);
        }

        var listener = new TcpListener(IPAddress.Any, 50001);
        listener.Start();
        socket = listener.AcceptTcpClient();
        InitComs();
        Console.WriteLine($"Connected to client from {socket.Client.RemoteEndPoint?.ToString()}...");

        SendKey();
    }

    private void SetupClient(string ip = "")
    {
        try
        {
            Console.WriteLine("Enter an IP address to connect to...");
            var line = Console.ReadLine();
            ip = string.IsNullOrWhiteSpace(line) ? "localhost" : line;
            if (ip != "localhost") 
            {
                try
                {
                    socket = new TcpClient(ip, 1702);
                } catch(SocketException e)
                {
                    socket = new TcpClient(ip, 50001);
                }    
            }
            else
            {
                socket = new TcpClient(ip, 50001);
            }
            InitComs();

            SendKey();
            Console.WriteLine("Connected to server...");
        }
        catch (SocketException e)
        {
            Console.WriteLine(e.ToString());
        }

    }

    private void InitComs()
    {
        if (socket is not null)
        {
            netStream = socket.GetStream();
            reader = new BinaryReader(netStream);
            writer = new BinaryWriter(netStream);

            Task.Run(() => HandleRequest()); 
        } else
        {
            Console.WriteLine("Socket not initialized.");
        }
    }

    private void HandleRequest()
    {
        handleStarted = true;
        ECPoint myECPoint;
        while (socket is not null && socket.Connected && reader is not null) 
        {         
            var cmd = reader.ReadString();
            //Console.Clear();
            if (cmd is not null)
            {
                if (cmd.Equals("ECC_PUB_KEY"))
                {
                    int byteCount = reader.ReadInt32();
                    byte[]? myX = reader.ReadBytes(byteCount);
                    byte[]? myY = reader.ReadBytes(byteCount);
                    myECPoint.X = myX;
                    myECPoint.Y = myY;
                    myCrypt.InitRemotePublicKey(myECPoint);
                }
                else if (cmd is not null)
                {
                    cmd = myCrypt.DecryptMessage(cmd);
                    messages.Add(string.Format("Client: {0}", cmd));
                }
            }
            
            foreach (var msg in messages)
            {
                Console.WriteLine(msg);
            }

            Console.Write("Send: ");

            switch (cmd)
            {
                case "exit":
                    socket.Close();
                    break;
            }
        }
    }
}
