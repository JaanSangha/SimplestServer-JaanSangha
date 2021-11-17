using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using System.IO;
using UnityEngine.UI;

public class NetworkedServer : MonoBehaviour
{
    int maxConnections = 1000;
    int reliableChannelID;
    int unreliableChannelID;
    int hostID;
    int socketPort = 5491;
    LinkedList<PlayerAccount> playerAccounts;
    const int playerAccountRecord = 1;
    string playerAccountsFilePath;
    int playerWaitingForMatchWithID = -1;
    LinkedList<GameRoom> gameRooms;
    
    // Start is called before the first frame update
    void Start()
    {
        NetworkTransport.Init();
        ConnectionConfig config = new ConnectionConfig();
        reliableChannelID = config.AddChannel(QosType.Reliable);
        unreliableChannelID = config.AddChannel(QosType.Unreliable);
        HostTopology topology = new HostTopology(config, maxConnections);
        hostID = NetworkTransport.AddHost(topology, socketPort, null);
        playerAccountsFilePath = Application.dataPath + Path.DirectorySeparatorChar + "PlayerAccounts.txt";
        playerAccounts = new LinkedList<PlayerAccount>();
        LoadPlayerAccounts();

        gameRooms = new LinkedList<GameRoom>();
    }

    // Update is called once per frame
    void Update()
    {

        int recHostID;
        int recConnectionID;
        int recChannelID;
        byte[] recBuffer = new byte[1024];
        int bufferSize = 1024;
        int dataSize;
        byte error = 0;

        NetworkEventType recNetworkEvent = NetworkTransport.Receive(out recHostID, out recConnectionID, out recChannelID, recBuffer, bufferSize, out dataSize, out error);

        switch (recNetworkEvent)
        {
            case NetworkEventType.Nothing:
                break;
            case NetworkEventType.ConnectEvent:
                Debug.Log("Connection, " + recConnectionID);
                break;
            case NetworkEventType.DataEvent:
                string msg = Encoding.Unicode.GetString(recBuffer, 0, dataSize);
                ProcessRecievedMsg(msg, recConnectionID);
                break;
            case NetworkEventType.DisconnectEvent:
                Debug.Log("Disconnection, " + recConnectionID);
                break;
        }

    }

    public void SendMessageToClient(string msg, int id)
    {
        byte error = 0;
        byte[] buffer = Encoding.Unicode.GetBytes(msg);
        NetworkTransport.Send(hostID, id, reliableChannelID, buffer, msg.Length * sizeof(char), out error);
    }

    private void ProcessRecievedMsg(string msg, int id)
    {
        Debug.Log("msg recieved = " + msg + ".  connection id = " + id);
        string[] csv = msg.Split(',');
        int Signifier = int.Parse(csv[0]);
        if (Signifier == ClientToServerSignifier.CreateAccount)
        {
            string n = csv[1];
            string p = csv[2];
            bool nameInUse = false;
            foreach (PlayerAccount pa in playerAccounts)
            {
                if (pa.name == n)
                {
                    nameInUse = true;
                    break;
                }
            }
            if (nameInUse)
            {
                SendMessageToClient(ServerToClientSignifier.AccountCreationFailed + "", id);
            }
            else
            {
                PlayerAccount newPlayerAccount = new PlayerAccount(n, p);
                playerAccounts.AddLast(newPlayerAccount);
                SendMessageToClient(ServerToClientSignifier.AccountCreationComplete + "", id);
                Debug.Log("account creation complete");
                SavePlayerAccount();
            }
        }
        else if (Signifier == ClientToServerSignifier.Login)
        {
            bool nameFound = false;
            bool msgBeenSentToClient = false;
            string n = csv[1];
            string p = csv[2];
            foreach (PlayerAccount pa in playerAccounts)
            {
                if (pa.name == n)
                {
                    nameFound = true;
                    if (pa.password == p)
                    {
                        SendMessageToClient(ServerToClientSignifier.LoginComplete + "", id);
                        Debug.Log("login complete");

                        msgBeenSentToClient = true;
                    }
                    else
                    {
                        SendMessageToClient(ServerToClientSignifier.LoginFailed + "", id);
                        msgBeenSentToClient = true;
                    }
                }
            }
            if (!nameFound)
            {
                if (!msgBeenSentToClient)
                {
                    SendMessageToClient(ServerToClientSignifier.LoginFailed + "", id);
                }
            }
        }
        else if (Signifier == ClientToServerSignifier.JoinQueueForGame)
        {
            if (playerWaitingForMatchWithID == -1)
            {
                playerWaitingForMatchWithID = id;
            }
            else
            {
                GameRoom gr = new GameRoom(playerWaitingForMatchWithID, id);
                gameRooms.AddLast(gr);

                SendMessageToClient(ServerToClientSignifier.GameStart + "", gr.playerID1);
                SendMessageToClient(ServerToClientSignifier.GameStart + "", gr.playerID2);

                playerWaitingForMatchWithID = -1;
            }
        }
        else if (Signifier == ClientToServerSignifier.TicTacToeSomethingPlay)
        {
            GameRoom gr = GetGameRoomWithClientID(id);

            if(gr != null)
            {
                if(gr.playerID1 ==id)
                {
                    SendMessageToClient(ServerToClientSignifier.OpponentPlay + "", gr.playerID2);
                }
                else
                {
                    SendMessageToClient(ServerToClientSignifier.OpponentPlay + "", gr.playerID1);
                }
            }
        }



        }
    public void SavePlayerAccount()
    {
        StreamWriter sw = new StreamWriter(playerAccountsFilePath);
        foreach (PlayerAccount pa in playerAccounts)
        {
            sw.WriteLine(playerAccountRecord + "," + pa.name + "," + pa.password);
        }
        sw.Close();
    }
    public void LoadPlayerAccounts()
    {
        if (File.Exists(playerAccountsFilePath))
        {
            StreamReader sr = new StreamReader(playerAccountsFilePath);
            string line;
            while ((line = sr.ReadLine()) != null)
            {
                string[] csv = line.Split(',');
                int Signifier = int.Parse(csv[0]);
                if (Signifier == playerAccountRecord)
                {
                    PlayerAccount pa = new PlayerAccount(csv[1], csv[2]);
                    playerAccounts.AddLast(pa);
                }
            }
            sr.Close();
        }
    }

    private GameRoom GetGameRoomWithClientID(int id)
    {
        foreach(GameRoom gr in gameRooms)
        {
            if (gr.playerID1 == id || gr.playerID2 == id)
                return gr;
        }
        return null;
    }
}
public static class ClientToServerSignifier
{
    public const int CreateAccount = 1;
    public const int Login = 2;
    public const int JoinQueueForGame = 3; 
    public const int TicTacToeSomethingPlay = 4;
}
public static class ServerToClientSignifier
{
    public const int LoginComplete = 1;
    public const int LoginFailed = 2;
    public const int AccountCreationComplete = 3;
    public const int AccountCreationFailed = 4;
    public const int GameStart = 5;
    public const int OpponentPlay = 6;
}
public class PlayerAccount
{
    public string name, password;
    public PlayerAccount(string name, string password)
    {
        this.name = name;
        this.password = password;
    }
}

public class GameRoom
{
    public int playerID1, playerID2;

    public GameRoom(int PlayerID1, int PlayerID2)
    {
        playerID1 = PlayerID1;
        playerID2 = PlayerID2;
    }
}