﻿using System;
using System.Net.Sockets;
using System.Net;
using Bindings;

namespace ElecyServer
{

    class ServerTCP
    {
        public static bool Closed { get; private set; } = true;

        private static Socket _serverSocket;
        private static byte[] _buffer;

        public static void SetupServer()
        {
            _buffer = new byte[Constants.TCP_BUFFER_SIZE];
            Closed = false;
            _serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _serverSocket.Bind(new IPEndPoint(IPAddress.Any, Constants.PORT));
            _serverSocket.Listen(Constants.SERVER_LISTEN);
            _serverSocket.BeginAccept(new AsyncCallback(AcceptCallback), null);
            UDPConnector.WaitConnect();
            Global.serverForm.Debug("Server started!");
        }

        public static void ServerClose()
        {
            if (!Closed)
            {
                Closed = true;
                Global.ThreadsStop();
                Global.mysql.MySQLClose();
                Global.FinalGlobals();
                UDPConnector.Close();
                _serverSocket.Dispose();
                Global.serverForm.Debug("Server closed...");
            }
        }

        private static void AcceptCallback(IAsyncResult ar)
        {
            if (!Closed)
            {
                Socket socket = _serverSocket.EndAccept(ar);
                _serverSocket.BeginAccept(new AsyncCallback(AcceptCallback), null);
                ClientTCP client = new ClientTCP(socket, (socket.RemoteEndPoint as IPEndPoint).Address);
                Global.clientList.Add(client);
                ServerSendData.SendClientConnetionOK(client);
            }
        }

        #region SendData

        public static void SendClientConnection(ClientTCP client, byte[] data)
        {
            byte[] sizeinfo = new byte[4];
            sizeinfo[0] = (byte)data.Length;
            sizeinfo[1] = (byte)(data.Length >> 8);
            sizeinfo[2] = (byte)(data.Length >> 16);
            sizeinfo[3] = (byte)(data.Length >> 24);

            try
            {
                client.socket.Send(sizeinfo);
                client.socket.Send(data);
            }
            catch
            {
                client.Close();
            }
        }

        public static void SendDataToClient(ClientTCP client, byte[] data)
        {
            try
            {
                client.socket.Send(data);
            }
            catch
            {
                client.Close();
            }
        }

        public static void SendDataToAllClients(byte[] data)
        {
            try
            {
                foreach(ClientTCP client in Global.clientList)
                {
                    try
                    {
                        client.socket.Send(data);
                    }
                    catch
                    {
                        client.Close();
                    }
                }
            }
            catch (Exception ex)
            {
                Global.serverForm.Debug(ex + "");
            }
        }

        public static void SendChatMsgToAllClients(byte[] data)
        {
            try
            {
                foreach(ClientTCP client in Global.clientList)
                {
                    try
                    {
                        if (client.clientState == ClientTCPState.MainLobby)
                            client.socket.Send(data);
                    }
                    catch
                    {
                        client.Close();
                    }
                }
            }
            catch (Exception ex)
            {
                Global.serverForm.Debug(ex + "");
            }
        }

        public static void SendDataToBothClients(ClientTCP client1, ClientTCP client2, byte[] data)
        {
            try
            {
                try
                {
                    client1.socket.Send(data);
                }
                catch
                {
                    client1.Close();
                    throw new Exception();
                }
                try
                {
                    client2.socket.Send(data);
                }
                catch
                {
                    client2.Close();
                    throw new Exception();
                }
            }
            catch
            {

            }
        }

        #endregion
    }

    public class ClientTCP
    {

        #region Variables

        public readonly Socket socket;
        public readonly IPAddress ip;

        public ClientTCPState clientState;
        public NetPlayerState playerState;

        public string nickname;
        public int[] levels;
        public int[] ranks;

        public GameRoom room;
        public GamePlayerUDP playerUDP;
        public string race;
        public float load;

        private byte[] _buffer;

        #endregion

        #region Constructor

        public ClientTCP(Socket socket, IPAddress ip)
        {
            this.socket = socket;
            this.ip = ip;
            _buffer = new byte[Constants.TCP_BUFFER_SIZE];

            Receive(ClientTCPState.Entrance);
        }

        #endregion

        #region Receive

        public void Receive(ClientTCPState? state = null)
        {
            clientState = state ?? clientState;
            switch(clientState)
            {
                case ClientTCPState.Sleep:
                    break;
                case ClientTCPState.Entrance:
                    EntranceReceive();
                    break;
                case ClientTCPState.MainLobby:
                    MainLobbyReceive();
                    break;
                case ClientTCPState.GameRoom:
                    break;
            }
        }

        private void EntranceReceive()
        {
            Global.serverForm.AddClient(this);
            socket.BeginReceive(_buffer, 0, _buffer.Length, SocketFlags.None, new AsyncCallback(EntranceReceiveCallback), null);
        }

        private void EntranceReceiveCallback(IAsyncResult ar)
        {
            if(clientState == ClientTCPState.Entrance)
            {
                try
                { 
                    int received = socket.EndReceive(ar);
                    if (received <= 0)
                    {
                        Close();
                    }
                    else
                    {
                        byte[] dataBuffer = new byte[received];
                        Array.Copy(_buffer, dataBuffer, received);
                        ServerHandleData.HandleNetworkInformation(this, dataBuffer);
                        if (clientState == ClientTCPState.Entrance)
                            socket.BeginReceive(_buffer, 0, _buffer.Length, SocketFlags.None, new AsyncCallback(EntranceReceiveCallback), null);
                    }
                }
                catch (Exception ex)
                {
                    Global.serverForm.Debug(ex.Message + " " + ex.Source);
                    Close();
                }
            }
        }

        private void MainLobbyReceive()
        {
            socket.BeginReceive(_buffer, 0, _buffer.Length, SocketFlags.None, new AsyncCallback(MainLobbyReceiveCallback), null);
        }

        private void MainLobbyReceiveCallback(IAsyncResult ar)
        {
            if (clientState == ClientTCPState.MainLobby)
            {
                try
                {
                    int received = socket.EndReceive(ar);
                    if (received <= 0)
                    {
                        Close();
                    }
                    else
                    {
                        byte[] dataBuffer = new byte[received];
                        Array.Copy(_buffer, dataBuffer, received);
                        ServerHandleData.HandleNetworkInformation(this, dataBuffer);
                        if (clientState == ClientTCPState.MainLobby)
                            socket.BeginReceive(_buffer, 0, _buffer.Length, SocketFlags.None, new AsyncCallback(MainLobbyReceiveCallback), null);
                    }
                }
                catch (Exception ex)
                {
                    Global.serverForm.Debug(ex.Message + " " + ex.Source);
                    Close();
                }
            }
        }

        private void GameRoomReceive()
        {
            socket.BeginReceive(_buffer, 0, _buffer.Length, SocketFlags.None, new AsyncCallback(GameRoomReceiveCallback), null);
        }

        private void GameRoomReceiveCallback(IAsyncResult ar)
        {
            if (clientState == ClientTCPState.GameRoom)
            { 
                try
                {
                    int received = socket.EndReceive(ar);
                    if (received <= 0)
                    {
                        Close();
                    }
                    else
                    {
                        byte[] dataBuffer = new byte[received];
                        Array.Copy(_buffer, dataBuffer, received);
                        ServerHandleData.HandleNetworkInformation(this, dataBuffer);
                        if (clientState == ClientTCPState.GameRoom)
                            socket.BeginReceive(_buffer, 0, _buffer.Length, SocketFlags.None, new AsyncCallback(GameRoomReceiveCallback), null);
                    }
                }
                catch (Exception ex)
                {
                    Global.serverForm.Debug(ex + "");
                    Close();
                }
            }
        }

        #endregion

        #region Transitions

        public void LogIn(string username)
        {
            nickname = Global.data.GetAccountNickname(username);
            int[][] data = Global.data.GetAccountData(nickname);
            levels = data[0];
            ranks = data[1];
            playerState = NetPlayerState.InMainLobby;
            Receive(ClientTCPState.MainLobby);
            ServerSendData.SendLoginOk(nickname, data, this);
        }

        public void LeaveRoom()
        {
            playerState = NetPlayerState.InMainLobby;
            Receive(ClientTCPState.MainLobby);
            room.DeletePlayer(this);
        }

        public void Close()
        {
            if(clientState == ClientTCPState.Entrance)
            {
                try
                {
                    socket.Dispose();
                }
                catch (Exception ex)
                {
                    Global.serverForm.Debug(ex + "");
                }
                Global.clientList.Remove(this);
            }
            else if(clientState == ClientTCPState.MainLobby)
            {
                try
                {
                    socket.Dispose();
                }
                catch (Exception ex)
                {
                    Global.serverForm.Debug(ex + "");
                }
                Global.clientList.Remove(this);
                ServerSendData.SendGlChatMsg("Server", $"Player { nickname } disconnected.");
            }
            else if(clientState == ClientTCPState.GameRoom)
            {
                if (playerState == NetPlayerState.SearchingForMatch)
                {
                    room.DeletePlayer(this);
                }
                else if(playerState == NetPlayerState.Playing)
                {
                    room.AbortGameSession(this);
                }
                else if(playerState == NetPlayerState.EndPlaying)
                {
                    room.LeaveRoom(this);
                }
                Global.serverForm.Debug($"GamePlayer {nickname} lost connection");
                Global.clientList.Remove(this);
            }
            Global.serverForm.RemoveClient(this);
        }

        #endregion

    }

}
