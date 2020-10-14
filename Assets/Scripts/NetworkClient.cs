using UnityEngine;
using Unity.Collections;
using Unity.Networking.Transport;
using NetworkMessages;
using NetworkObjects;
using System;
using System.Text;
using System.ComponentModel.Design;
using System.Collections;
using System.Collections.Generic;
//using System.Diagnostics;

public class NetworkClient : MonoBehaviour
{
    public NetworkDriver m_Driver;
    public NetworkConnection m_Connection;
    public string serverIP;
    public ushort serverPort;

    public GameObject playerPrefab;
    PlayerUpdateMsg playerUpdateMsg;
    GameObject yourCharacter;

    Dictionary<string, GameObject> otherPlayers;

    void Start ()
    {
        m_Driver = NetworkDriver.Create();
        m_Connection = default(NetworkConnection);
        var endpoint = NetworkEndPoint.Parse(serverIP,serverPort);
        m_Connection = m_Driver.Connect(endpoint);

        otherPlayers = new Dictionary<string, GameObject>();

        // Init player
        playerUpdateMsg = new PlayerUpdateMsg();
        playerUpdateMsg.player.cubeColor = new Color(UnityEngine.Random.value, UnityEngine.Random.value, UnityEngine.Random.value);
        yourCharacter = Instantiate(playerPrefab);

        yourCharacter.GetComponent<Renderer>().material.color = playerUpdateMsg.player.cubeColor;
        yourCharacter.AddComponent<CharacterMovement>();
        playerUpdateMsg.player.cubPos = yourCharacter.transform.position;
    }
    
    void SendToServer(string message){
        var writer = m_Driver.BeginSend(m_Connection);
        NativeArray<byte> bytes = new NativeArray<byte>(Encoding.ASCII.GetBytes(message),Allocator.Temp);
        writer.WriteBytes(bytes);
        m_Driver.EndSend(writer);
    }

    void OnConnect(){
        Debug.Log("We are now connected to the server");

        StartCoroutine(SendUpdateToServer());

        //// Example to send a handshake message:
        // HandshakeMsg m = new HandshakeMsg();
        // m.player.id = m_Connection.InternalId.ToString();
        // SendToServer(JsonUtility.ToJson(m));
    }

    void OnData(DataStreamReader stream){
        NativeArray<byte> bytes = new NativeArray<byte>(stream.Length,Allocator.Temp);
        stream.ReadBytes(bytes);
        string recMsg = Encoding.ASCII.GetString(bytes.ToArray());
        NetworkHeader header = JsonUtility.FromJson<NetworkHeader>(recMsg);

        switch(header.cmd){
            case Commands.PLAYER_INIT:
                InitializeConnectionMsg piMsg = JsonUtility.FromJson<InitializeConnectionMsg>(recMsg);

                playerUpdateMsg.player.id = piMsg.yourID;

                HandshakeMsg m = new HandshakeMsg();
                m.player = playerUpdateMsg.player;
                m.player.id = piMsg.yourID;
                SendToServer(JsonUtility.ToJson(m));

                otherPlayers.Add(m.player.id, yourCharacter);

                Debug.Log("Initialization message received!  Your ID: " + piMsg.yourID);
                break; 
            case Commands.HANDSHAKE:
                HandshakeMsg hsMsg = JsonUtility.FromJson<HandshakeMsg>(recMsg);
                Debug.Log("Handshake message received!");
                break;
            case Commands.PLAYER_UPDATE:
                PlayerUpdateMsg puMsg = JsonUtility.FromJson<PlayerUpdateMsg>(recMsg);
                Debug.Log("Player update message received!");
                break;
            case Commands.SERVER_UPDATE:
                ServerUpdateMsg suMsg = JsonUtility.FromJson<ServerUpdateMsg>(recMsg);
                UpdateNetworkObjects(suMsg);
                Debug.Log("Server update message received!");
                break;
            case Commands.PLAYER_DROPPED:
                ConnectionDroppedMsg cdMsg = JsonUtility.FromJson<ConnectionDroppedMsg>(recMsg);

                // Destroy
                GameObject character = otherPlayers[cdMsg.droppedId];
                otherPlayers.Remove(cdMsg.droppedId);
                Destroy(character);

                Debug.Log("Player dropped message received! " + cdMsg.droppedId);
                break;
            default:
                Debug.Log("Unrecognized message received!");
                break;
        }
    }

    void Disconnect(){
        m_Connection.Disconnect(m_Driver);
        m_Connection = default(NetworkConnection);
    }

    void OnDisconnect(){
        Debug.Log("Client got disconnected from server");
        m_Connection = default(NetworkConnection);
    }

    void UpdateNetworkObjects(ServerUpdateMsg suMsg)
    {
        foreach (NetworkObjects.NetworkPlayer player in suMsg.players)
        {
            // Add new player
            if (!otherPlayers.ContainsKey(player.id) && otherPlayers.Count <= suMsg.players.Count)
            {
                CreateNetworkObject(player);
            }
            else if (player.id != playerUpdateMsg.player.id)
            {
                UpdateNetworkObject(player);
            }
        }
    }

    void CreateNetworkObject(NetworkObjects.NetworkPlayer player)
    {
        GameObject newPlayer = Instantiate(playerPrefab);

        newPlayer.GetComponent<Renderer>().material.color = player.cubeColor;
        newPlayer.transform.position = player.cubPos;

        otherPlayers.Add(player.id, newPlayer);
    }

    void UpdateNetworkObject(NetworkObjects.NetworkPlayer player)
    {
        if (otherPlayers.ContainsKey(player.id))
        {
            otherPlayers[player.id].transform.position = player.cubPos;
        }

        //Vector3 diff = transform.TransformDirection(new Vector3(player.cubPos.x, player.cubPos.y, player.cubPos.z) - otherPlayers[player.id].transform.position);
        //otherPlayers[player.id].GetComponent<CharacterController>().Move(diff);
    }

    public void OnDestroy()
    {
        m_Driver.Dispose();
    }

    IEnumerator SendUpdateToServer()
    {
        while (true)
        {
            // Send Server updates
            if (yourCharacter)
            {
                playerUpdateMsg.player.cubPos = yourCharacter.transform.position;
                SendToServer(JsonUtility.ToJson(playerUpdateMsg));
            }
            yield return new WaitForSeconds(0.3f);
        }
    }

    void Update()
    {
        m_Driver.ScheduleUpdate().Complete();

        if (!m_Connection.IsCreated)
        {
            return;
        }

        DataStreamReader stream;
        NetworkEvent.Type cmd;
        cmd = m_Connection.PopEvent(m_Driver, out stream);
        while (cmd != NetworkEvent.Type.Empty)
        {
            if (cmd == NetworkEvent.Type.Connect)
            {
                OnConnect();
            }
            else if (cmd == NetworkEvent.Type.Data)
            {
                OnData(stream);
            }
            else if (cmd == NetworkEvent.Type.Disconnect)
            {
                OnDisconnect();
            }

            cmd = m_Connection.PopEvent(m_Driver, out stream);
        }
    }
}