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
    const int playerMoveRecord = 1;
    string playerAccountsFilePath;
    string playerMovesFilePath;
    int playerWaitingForMatchWithID = -1;
    List<GameRoom> gameRooms;
    LinkedList<PlayerMoves> pressedButtons;
    List<int> slotsPlayed;
    int turn = 1;
    int arrayNum = 0;
    
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

        playerMovesFilePath = Application.dataPath + Path.DirectorySeparatorChar + "PlayerMoves.txt";
        pressedButtons = new LinkedList<PlayerMoves>();

        gameRooms = new List<GameRoom>();
        File.Delete(Application.dataPath + Path.DirectorySeparatorChar + "PlayerMoves.txt");
#if UNITY_EDITOR
        UnityEditor.AssetDatabase.Refresh();
#endif
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
            if (gameRooms.Count == 0)
            {
                if (playerWaitingForMatchWithID == -1)
                {
                    playerWaitingForMatchWithID = id;
                }
                else
                {
                    GameRoom gr = new GameRoom(playerWaitingForMatchWithID, id);
                    gameRooms.Add(gr);

                    SendMessageToClient(ServerToClientSignifier.GameStart + "", gr.playerID1);
                    SendMessageToClient(ServerToClientSignifier.GameStart + "", gr.playerID2);

                    playerWaitingForMatchWithID = -1;
                }
            }
            else
            {
                //SendMessageToClient(ServerToClientSignifier.ObserveStart + "",id);
                gameRooms[Random.Range(0, gameRooms.Count)].observers.Add(id);
                Debug.Log(gameRooms[Random.Range(0, gameRooms.Count)].observers + " : observer");
                //gr.observers.Add(id);
                SendMessageToClient(ServerToClientSignifier.ObserveStart + "", id);
                GameRoom gr = GetGameRoomWithClientID(id);
            }
        }
        else if (Signifier == ClientToServerSignifier.TicTacToeSomethingPlay)
        {
            GameRoom gr = GetGameRoomWithClientID(id);

            if (gr != null)
            {
                if (gr.playerID1 == id)
                {
                    SendMessageToClient(ServerToClientSignifier.OpponentPlay + "", gr.playerID2);
                }
                else
                {
                    SendMessageToClient(ServerToClientSignifier.OpponentPlay + "", gr.playerID1);
                }
            }
        }
        else if (Signifier == ClientToServerSignifier.QuickChatOne)
        {
            GameRoom gr = GetGameRoomWithClientID(id);

            if (gr != null)
            {
                if (gr.playerID1 == id)
                {
                    SendMessageToClient(ServerToClientSignifier.QuickChatOneRecieved + "", gr.playerID2);
                    SendMessageToClient(ServerToClientSignifier.QuickChatOneSent + "", gr.playerID1);
                    if (gr.observers.Count > 0)
                        SendMessageToClient(ServerToClientSignifier.QuickChatOneObserver + "", gr.observers[0]);
                }
                else if (gr.playerID2 == id)
                {
                    SendMessageToClient(ServerToClientSignifier.QuickChatOneRecieved + "", gr.playerID1);
                    SendMessageToClient(ServerToClientSignifier.QuickChatOneSent + "", gr.playerID2);
                    if (gr.observers.Count > 0)
                        SendMessageToClient(ServerToClientSignifier.QuickChatOneObserver + "", gr.observers[0]);
                }
            }
        }
        else if (Signifier == ClientToServerSignifier.QuickChatTwo)
        {
            GameRoom gr = GetGameRoomWithClientID(id);

            if (gr != null)
            {
                if (gr.playerID1 == id)
                {
                    SendMessageToClient(ServerToClientSignifier.QuickChatTwoRecieved + "", gr.playerID2);
                    SendMessageToClient(ServerToClientSignifier.QuickChatTwoSent + "", gr.playerID1);
                    if (gr.observers.Count > 0)
                        SendMessageToClient(ServerToClientSignifier.QuickChatTwoObserver + "", gr.observers[0]);
                }
                else if (gr.playerID2 == id)
                {
                    SendMessageToClient(ServerToClientSignifier.QuickChatTwoRecieved + "", gr.playerID1);
                    SendMessageToClient(ServerToClientSignifier.QuickChatTwoSent + "", gr.playerID2);
                    if (gr.observers.Count > 0)
                        SendMessageToClient(ServerToClientSignifier.QuickChatTwoObserver + "", gr.observers[0]);
                }
            }
        }
        else if (Signifier == ClientToServerSignifier.QuickChatThree)
        {
            GameRoom gr = GetGameRoomWithClientID(id);

            if (gr != null)
            {
                if (gr.playerID1 == id)
                {
                    SendMessageToClient(ServerToClientSignifier.QuickChatThreeRecieved + "", gr.playerID2);
                    SendMessageToClient(ServerToClientSignifier.QuickChatThreeSent + "", gr.playerID1);
                    if (gr.observers.Count > 0)
                        SendMessageToClient(ServerToClientSignifier.QuickChatThreeObserver + "", gr.observers[0]);
                }
                else if (gr.playerID2 == id)
                {
                    SendMessageToClient(ServerToClientSignifier.QuickChatThreeRecieved + "", gr.playerID1);
                    SendMessageToClient(ServerToClientSignifier.QuickChatThreeSent + "", gr.playerID2);
                    if (gr.observers.Count > 0)
                        SendMessageToClient(ServerToClientSignifier.QuickChatThreeObserver + "", gr.observers[0]);
                }
            }
        }
        else if (Signifier == ClientToServerSignifier.QuickChatFour)
        {
            GameRoom gr = GetGameRoomWithClientID(id);

            if (gr != null)
            {
                if (gr.playerID1 == id)
                {
                    SendMessageToClient(ServerToClientSignifier.QuickChatFourRecieved + "", gr.playerID2);
                    SendMessageToClient(ServerToClientSignifier.QuickChatFourSent + "", gr.playerID1);
                    if (gr.observers.Count > 0)
                        SendMessageToClient(ServerToClientSignifier.QuickChatFourObserver + "", gr.observers[0]);
                    //foreach (int Observer in gr.observers)
                    //{
                    //    SendMessageToClient(ServerToClientSignifier.QuickChatFourObserver + "", gr.observers[Observer-1]);

                    //}

                }
                else if (gr.playerID2 == id)
                {
                    SendMessageToClient(ServerToClientSignifier.QuickChatFourRecieved + "", gr.playerID1);
                    SendMessageToClient(ServerToClientSignifier.QuickChatFourSent + "", gr.playerID2);
                    if (gr.observers.Count > 0)
                        SendMessageToClient(ServerToClientSignifier.QuickChatFourObserver + "", gr.observers[0]);
                    //foreach (GameRoom gameRoom in gameRooms)
                    //{
                    //    foreach (int Observer in gameRoom.observers)
                    //    {
                    //        SendMessageToClient(ServerToClientSignifier.QuickChatFourObserver + "", gameRoom.observers[Observer]);

                    //    }
                    //}
                }
            }
        }
        else if (Signifier == ClientToServerSignifier.JoinQueueToObserve)
        {
            if (gameRooms.Count > 0)
            {
                gameRooms[Random.Range(0, gameRooms.Count)].observers.Add(id);
                SendMessageToClient(ServerToClientSignifier.ObserveStart + "", id);
                GameRoom gr = GetGameRoomWithClientID(id);
            }
        }
        else if (Signifier == ClientToServerSignifier.MessageSent)
        {
            GameRoom gr = GetGameRoomWithClientID(id);
            if (gr.playerID1 == id)
            {
                SendMessageToClient(ServerToClientSignifier.MessageSent + "," + csv[1] + ":" + csv[2], gr.playerID1);
                SendMessageToClient(ServerToClientSignifier.MessageRecieved + "," + csv[1] + ":" + csv[2], gr.playerID2);

                if (gr.observers.Count > 0)
                    SendMessageToClient(ServerToClientSignifier.MessageRecieved + "," + csv[1] + ":" + csv[2], gr.observers[0]);
            }
            else if (gr.playerID2 == id)
            {
                SendMessageToClient(ServerToClientSignifier.MessageRecieved + "," + csv[1] + ":" + csv[2], gr.playerID1);
                SendMessageToClient(ServerToClientSignifier.MessageSent + "," + csv[1] + ":" + csv[2], gr.playerID2);

                if (gr.observers.Count > 0)
                    SendMessageToClient(ServerToClientSignifier.MessageRecieved + "," + csv[1] + ":" + csv[2], gr.observers[0]);
            }

        }
        else if (Signifier == ClientToServerSignifier.SendButton)
        {
            GameRoom gr = GetGameRoomWithClientID(id);

            int temp = int.Parse(csv[1]);

            if (turn == 1 && gr.playerID1 == id)
            {
                turn = 2;
                PlayerMoves newPlayerMoves = new PlayerMoves(id.ToString(), temp.ToString());
                pressedButtons.AddLast(newPlayerMoves);
                SavePlayerMoves();

                SendMessageToClient(ServerToClientSignifier.SlotRecieved + "," + 1 + "," + temp + ",", gr.playerID1);
                SendMessageToClient(ServerToClientSignifier.SlotRecieved + "," + 1 + "," + temp + ",", gr.playerID2);

                if (gr.observers.Count > 0)

                    SendMessageToClient(ServerToClientSignifier.SlotRecieved + "," + 1 + "," + temp + ",", gr.observers[0]);

            }
            else if (turn == 2 && gr.playerID2 == id)
            {
                turn = 1;
                PlayerMoves newPlayerMoves = new PlayerMoves(id.ToString(), temp.ToString());
                pressedButtons.AddLast(newPlayerMoves);
                SavePlayerMoves();

                SendMessageToClient(ServerToClientSignifier.SlotRecieved + "," + 2 + "," + temp + ",", gr.playerID1);
                SendMessageToClient(ServerToClientSignifier.SlotRecieved + "," + 2 + "," + temp + ",", gr.playerID2);
                if (gr.observers.Count > 0)

                    SendMessageToClient(ServerToClientSignifier.SlotRecieved + "," + 2 + "," + temp + ",", gr.observers[0]);

            }
        }
        else if (Signifier == ClientToServerSignifier.ReplayButton)
        {
            LoadPlayerMoves();
            GameRoom gr = GetGameRoomWithClientID(id);

            foreach (PlayerMoves pa in pressedButtons)
            {
                SendMessageToClient(ServerToClientSignifier.PlayReplay + "," + pa.playerID + "," + pa.slot + "," + arrayNum + ",", gr.playerID1);
                SendMessageToClient(ServerToClientSignifier.PlayReplay + "," + pa.playerID + "," + pa.slot + "," + arrayNum + ",", gr.playerID2);
                arrayNum++;
            }
        }

    }
    public void SavePlayerMoves()
    {
        StreamWriter sw = new StreamWriter(playerMovesFilePath);
        foreach (PlayerMoves pa in pressedButtons)
        {
            sw.WriteLine(playerMoveRecord + "," + pa.playerID + "," + pa.slot);
        }
        sw.Close();
    }

    public void LoadPlayerMoves()
    {
        if (File.Exists(playerAccountsFilePath))
        {
            StreamReader sr = new StreamReader(playerMovesFilePath);
            string line;
            while ((line = sr.ReadLine()) != null)
            {
                string[] csv = line.Split(',');
                int Signifier = int.Parse(csv[0]);
                if (Signifier == playerMoveRecord)
                {
                    PlayerMoves pa = new PlayerMoves(csv[1], csv[2]);
                    pressedButtons.AddLast(pa);
                }
            }
            sr.Close();
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
    public const int QuickChatOne = 5;
    public const int QuickChatTwo = 6;
    public const int QuickChatThree = 7;
    public const int QuickChatFour = 8;
    public const int JoinQueueToObserve = 9;
    public const int MessageSent = 10;
    public const int SendButton = 11;
    public const int ReplayButton = 12;
}
public static class ServerToClientSignifier
{
    public const int LoginComplete = 1;
    public const int LoginFailed = 2;
    public const int AccountCreationComplete = 3;
    public const int AccountCreationFailed = 4;
    public const int GameStart = 5;
    public const int OpponentPlay = 6;
    public const int QuickChatOneRecieved = 7;
    public const int QuickChatTwoRecieved = 8;
    public const int QuickChatThreeRecieved = 9;
    public const int QuickChatFourRecieved = 10;
    public const int QuickChatOneSent = 11;
    public const int QuickChatTwoSent = 12;
    public const int QuickChatThreeSent = 13;
    public const int QuickChatFourSent = 14;
    public const int ObserveStart = 15;
    public const int QuickChatOneObserver = 16;
    public const int QuickChatTwoObserver = 17;
    public const int QuickChatThreeObserver = 18;
    public const int QuickChatFourObserver = 19;
    public const int MessageRecieved = 20;
    public const int MessageSent = 21;
    public const int MessageObserver = 22;
    public const int SlotRecieved = 23;
    public const int PlayReplay = 24;
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

public class PlayerMoves
{
    public string playerID, slot;
    public PlayerMoves(string playerID, string slot)
    {
        this.playerID = playerID;
        this.slot = slot;
    }
}
public class GameRoom
{
    public int playerID1, playerID2;
    public List<int> observers;

    public GameRoom(int PlayerID1, int PlayerID2)
    {
        playerID1 = PlayerID1;
        playerID2 = PlayerID2;
        observers = new List<int>();
    }
}