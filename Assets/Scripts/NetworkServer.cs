using UnityEngine;
using UnityEngine.Assertions;
using Unity.Collections;
using System.Collections.Generic;
using Unity.Networking.Transport;
using NetworkMessages;
using System;
using System.Text;
//using System.IO.Ports;
using System.Collections;

public class NetworkServer : MonoBehaviour
{
    public NetworkDriver m_Driver;
    public ushort serverPort;
    private NativeList<NetworkConnection> m_Connections;

    private List<NetworkObjects.NetworkPlayer> players;

    void Start ()
    {
        m_Driver = NetworkDriver.Create();
        var endpoint = NetworkEndPoint.AnyIpv4;
        endpoint.Port = serverPort;
        if (m_Driver.Bind(endpoint) != 0)
            Debug.Log("Failed to bind to port " + serverPort);
        else
            m_Driver.Listen();

        m_Connections = new NativeList<NetworkConnection>(16, Allocator.Persistent);

        players = new List<NetworkObjects.NetworkPlayer>();
        // Send players updates
        StartCoroutine(SendUpdateToAllClient());
    }

    private IEnumerator SendHandshakeToAllClient()
    {
        while (true)
        {
            for (int i = 0; i < m_Connections.Length; i++)
            {
                if (!m_Connections[i].IsCreated)
                    continue;

                HandshakeMsg m = new HandshakeMsg();
                m.player.id = m_Connections[i].InternalId.ToString();
                SendToClient(JsonUtility.ToJson(m), m_Connections[i]);
            }
            yield return new WaitForSeconds(2);
        }
    }

    private IEnumerator SendUpdateToAllClient()
    {
        while (true)
        {
            for (int i = 0; i < m_Connections.Length; i++)
            {
                if (!m_Connections[i].IsCreated)
                    continue;

                ServerUpdateMsg m = new ServerUpdateMsg();
                m.players = players;
                SendToClient(JsonUtility.ToJson(m), m_Connections[i]);
            }
            yield return new WaitForSeconds(0.3f);
        }
    }

    void SendToClient(string message, NetworkConnection c){
        var writer = m_Driver.BeginSend(NetworkPipeline.Null, c);
        NativeArray<byte> bytes = new NativeArray<byte>(Encoding.ASCII.GetBytes(message),Allocator.Temp);
        writer.WriteBytes(bytes);
        m_Driver.EndSend(writer);
    }
    public void OnDestroy()
    {
        m_Driver.Dispose();
        m_Connections.Dispose();
    }

    void OnConnect(NetworkConnection c){
        m_Connections.Add(c);
        var newPlayersId = c.InternalId;
        var connectionMsg = new InitializeConnectionMsg();
        connectionMsg.yourID = newPlayersId.ToString();

        SendToClient(JsonUtility.ToJson(connectionMsg), c);

        Debug.Log("Accepted a connection");

        //// Example to send a handshake message:
        // HandshakeMsg m = new HandshakeMsg();
        // m.player.id = c.InternalId.ToString();
        // SendToClient(JsonUtility.ToJson(m),c);        
    }

    void OnData(DataStreamReader stream, int i){
        NativeArray<byte> bytes = new NativeArray<byte>(stream.Length,Allocator.Temp);
        stream.ReadBytes(bytes);
        string recMsg = Encoding.ASCII.GetString(bytes.ToArray());
        NetworkHeader header = JsonUtility.FromJson<NetworkHeader>(recMsg);

        switch (header.cmd){
            case Commands.HANDSHAKE:
                HandshakeMsg hsMsg = JsonUtility.FromJson<HandshakeMsg>(recMsg);
                players.Add(hsMsg.player);
                Debug.Log("Handshake message received!");
                break;
            case Commands.PLAYER_UPDATE:
                PlayerUpdateMsg puMsg = JsonUtility.FromJson<PlayerUpdateMsg>(recMsg);
                UpdatePlayer(puMsg);
                Debug.Log("Player update message received!");
                break;
            case Commands.SERVER_UPDATE:
                ServerUpdateMsg suMsg = JsonUtility.FromJson<ServerUpdateMsg>(recMsg);
                Debug.Log("Server update message received!");
                break;
            default:
                Debug.Log("SERVER ERROR: Unrecognized message received!");
                break;
        }
    }

    void UpdatePlayer(PlayerUpdateMsg puMsg)
    {
        foreach (NetworkObjects.NetworkPlayer player in players)
        {
            if (puMsg.player.id == player.id)
            {
                player.cubPos = puMsg.player.cubPos;
            }
        }
        //players[m_Connections[int.Parse(puMsg.player.id)].InternalId].cubPos = puMsg.player.cubPos;
        Debug.Log("Player " + puMsg.player.id + " Position: " + puMsg.player.cubPos);
    }

    void OnDisconnect(int i){
        Debug.Log("Client disconnected from server");

        // Create drop message
        ConnectionDroppedMsg cdMsg = new ConnectionDroppedMsg();
        cdMsg.droppedId = i.ToString();

        m_Connections[i] = default(NetworkConnection);
        players.RemoveAt(i);

        for (int j = 0; j < m_Connections.Length; j++)
        {
            if (!m_Connections[j].IsCreated)
                continue;

            SendToClient(JsonUtility.ToJson(cdMsg), m_Connections[j]);
        }
    }

    void Update ()
    {
        m_Driver.ScheduleUpdate().Complete();

        // CleanUpConnections
        for (int i = 0; i < m_Connections.Length; i++)
        {
            if (!m_Connections[i].IsCreated)
            {

                m_Connections.RemoveAtSwapBack(i);
                --i;
            }
        }

        // AcceptNewConnections
        NetworkConnection c = m_Driver.Accept();
        while (c  != default(NetworkConnection))
        {            
            OnConnect(c);

            // Check if there is another new connection
            c = m_Driver.Accept();
        }


        // Read Incoming Messages
        DataStreamReader stream;
        for (int i = 0; i < m_Connections.Length; i++)
        {
            Assert.IsTrue(m_Connections[i].IsCreated);
            
            NetworkEvent.Type cmd;
            cmd = m_Driver.PopEventForConnection(m_Connections[i], out stream);
            while (cmd != NetworkEvent.Type.Empty)
            {
                if (cmd == NetworkEvent.Type.Data)
                {
                    OnData(stream, i);
                }
                else if (cmd == NetworkEvent.Type.Disconnect)
                {
                    OnDisconnect(i);
                }

                cmd = m_Driver.PopEventForConnection(m_Connections[i], out stream);
            }
        }
    }
}