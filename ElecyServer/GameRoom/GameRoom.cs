﻿using System;
using System.Collections.Generic;
using System.Threading;
using Bindings;

namespace ElecyServer
{
    public class GameRoom
    {

        #region Variables

        public ClientTCP player1;
        public ClientTCP player2;

        public GamePlayerUDP playerUDP1;
        public GamePlayerUDP playerUDP2;
        public RoomPlayer[] roomPlayers = new RoomPlayer[2];
        public GameObjectList ObjectsList { get; private set; }
        public ArenaRandomGenerator Spawner { get; private set; }
        public RoomState Status { get; private set; }

        private int mapIndex;
        private Timer closeTimer;
        private Timer _updateTimer;
        private bool p1Loaded;
        private bool p2Loaded;
        private float scaleX;
        private float scaleZ;
        private float[][] firstPlayerSpawnTransform;
        private float[][] secondPlayerSpawnTransform;

        #region Randomed

        private enum Spawned
        {
            unspawned = 0,
            spawning = 1,
            spawned = 2
        }

        private Spawned _playersSpawned;
        private Spawned _rockSpawned;
        private Spawned _treeSpawned;

        private object expectant;

        #endregion

        #endregion

        #region Initialization

        public GameRoom(ClientTCP client)
        {
            Status = RoomState.Searching;
            player1 = client;
            player1.room = this;
            ObjectsList = new GameObjectList();
            mapIndex = new Random().Next(1, 1/*2 + Constants.MAPS_COUNT*/);
            _playersSpawned = Spawned.unspawned;
            _rockSpawned = Spawned.unspawned;
            _treeSpawned = Spawned.unspawned;
            expectant = new object();
            p1Loaded = false;
            p2Loaded = false;
            Global.serverForm.AddGameRoom(this);
        }

        public void AddPlayer(ClientTCP client)
        {
            Status = RoomState.Closed;
            player2 = client;
            player2.room = this;
            StartLoad();
        }

        public void DeletePlayer(ClientTCP client)
        {
            client.race = null;
            client.room = null;
            if (player1 != null && client.Equals(player1))
                player1 = null;
            else
                player2 = null;
            if (Status == RoomState.Searching)
            {
                Global.serverForm.RemoveGameRoom(this);
                Global.roomsList.Remove(this);
            }
        }

        private void StartLoad()
        {
            player1.clientState = ClientTCPState.GameRoom;
            player1.playerState = NetPlayerState.Playing;
            player2.clientState = ClientTCPState.GameRoom;
            player2.playerState = NetPlayerState.Playing;
            ServerSendData.SendMatchFound(player1, player2);
        }

        #endregion

        #region Loading

        public void SetGameLoadData(ClientTCP client)
        {

            lock(expectant)
            {
                if(Status != RoomState.Loading)
                {
                    int[] scale = Global.data.GetMapScale(mapIndex);

                    scaleX = scale[0] * 10f;
                    scaleZ = scale[1] * 10f;

                    Status = RoomState.Loading;
                }
            }
            ServerSendData.SendMapData(mapIndex, client);

        }

        public void SpawnPlayers(ClientTCP client)
        {
            lock(expectant)
            {
                if(_playersSpawned == Spawned.unspawned)
                {
                    float[][] spawnPos = Global.data.GetSpawnPos(mapIndex);
                    float[][] spawnRot = Global.data.GetSpawnRot(mapIndex);

                    firstPlayerSpawnTransform = new float[][] { spawnPos[0], spawnRot[0]};
                    secondPlayerSpawnTransform = new float[][] { spawnPos[1], spawnRot[1] };
                    roomPlayers[0] = new RoomPlayer(firstPlayerSpawnTransform[0]);
                    roomPlayers[1] = new RoomPlayer(secondPlayerSpawnTransform[0]);
                    Spawner = new ArenaRandomGenerator(scaleX, scaleZ, firstPlayerSpawnTransform[0], secondPlayerSpawnTransform[0]);
                    _playersSpawned = Spawned.spawned;
                }
            }
            ServerSendData.SendPlayersSpawned(client, player1.nickname, player2.nickname, firstPlayerSpawnTransform, secondPlayerSpawnTransform);

        }

        public void SpawnRock(ClientTCP client, int rockCount, bool bigRock, bool mediumRock, bool smallRock)
        {
            lock (expectant)
            {
                if(_rockSpawned == Spawned.unspawned)
                {
                    _rockSpawned = Spawned.spawning;
                    ObjectsList.Add(NetworkGameObject.ObjectType.rock, this, rockCount, bigRock, mediumRock, smallRock);
                    _rockSpawned = Spawned.spawned;
                }
            }
            ServerSendData.SendRockSpawned(client, ObjectsList.GetRange(NetworkGameObject.ObjectType.rock));
        }

        public void SpawnTree(ClientTCP client, int treeCount, bool bigTree, bool mediumTree, bool smallTree)
        {
            lock (expectant)
            {
                if (_treeSpawned == Spawned.unspawned)
                {
                    _treeSpawned = Spawned.spawning;
                    ObjectsList.Add(NetworkGameObject.ObjectType.tree, this, treeCount, bigTree, mediumTree, smallTree);
                    _treeSpawned = Spawned.spawned;
                }
            }
            ServerSendData.SendTreeSpawned(client, ObjectsList.GetRange(NetworkGameObject.ObjectType.tree));
        }

        public void LoadSpells(ClientTCP client)
        {
            short[] spellsToLoadFirst = Global.data.GetSkillBuildData(client.room.player1.nickname, client.room.player1.race);
            short[] spellsToLoadSecond = Global.data.GetSkillBuildData(client.room.player2.nickname, client.room.player2.race);
            ServerSendData.SendLoadSpells(client, spellsToLoadFirst, spellsToLoadSecond);
        }

        public void LoadComplete(ClientTCP client)
        {
            if (client.Equals(player1))
            {
                p1Loaded = true;
            }
            else
            {
                p2Loaded = true;
            }

            if(p1Loaded && p2Loaded)
            {
                p1Loaded = false;
                p2Loaded = false;
                ServerSendData.SendRoomStart(player1, player2);
                Thread.Sleep(5000);
                StartUpdate();
            }
        }

        public void SetLoadProgress(ClientTCP client, float loadProgress)
        {
            if(client.Equals(player1))
            {
                ServerSendData.SendEnemyProgress(player2, client.load = loadProgress);
            }
            else
            {
                ServerSendData.SendEnemyProgress(player1, client.load = loadProgress);
            }
        }

        private void LoadPulse(object o)
        {
            lock (expectant)
                Monitor.Pulse(expectant);
        }

        #endregion

        public void StartUpdate()
        {
            _updateTimer = new Timer(UpdateTimerCallback, null, 0, 1000 / Constants.UPDATE_RATE);
        }

        public void UpdateTimerCallback(object o)
        {
            roomPlayers[0].Update(playerUDP1);
            roomPlayers[1].Update(playerUDP2);
        }

        #region Finalization

        public void Surrended(ClientTCP client)
        {
            Status = RoomState.MatchEnded;
            player1.playerState = NetPlayerState.EndPlaying;
            player2.playerState = NetPlayerState.EndPlaying;
            closeTimer = new Timer(EndGameSession, null, 300000, Timeout.Infinite);
            if (client.Equals(player1))
            {
                ServerSendData.SendMatchEnded(player2, player1);
            }
            else
            {
                ServerSendData.SendMatchEnded(player1, player2);
            }
        }

        public void AbortGameSession(ClientTCP client)
        {
            if (Status != RoomState.MatchEnded)
            {
                Status = RoomState.MatchEnded;
                closeTimer = new Timer(EndGameSession, null, 300000, Timeout.Infinite);
                if (client.Equals(player1))
                {
                    player1 = null;
                    player2.playerState = NetPlayerState.EndPlaying;
                    ServerSendData.SendMatchEnded(player2);
                }
                else
                {
                    player2 = null;
                    player1.playerState = NetPlayerState.EndPlaying;
                    ServerSendData.SendMatchEnded(player1);
                }
            }
            else
            {
                if (player1 == null)
                {
                    player2 = null;
                }
                else
                {
                    player1 = null;
                }
                if (player1 == null && player2 == null)
                {
                    CloseRoom();
                }
            }
        }

        public void LeaveRoom(ClientTCP client)
        {
            if(player1 != null && client.Equals(player1))
            {
                player1.LeaveRoom();
            }
            else
            {
                player2.LeaveRoom();
            }

            if(player1 == null && player2 == null)
            {
                CloseRoom();
            }
        }

        public void CloseRoom()
        {
            StopTimers();
            Global.serverForm.RemoveGameRoom(this);
            Global.roomsList.Remove(this);
        }

        private void StopTimers()
        {
            if (closeTimer != null)
                closeTimer.Dispose();
        }

        private void EndGameSession(Object o)
        {
            closeTimer.Dispose();
            if (player1 != null)
            {
                ServerSendData.SendRoomLogOut(player1);
                LeaveRoom(player1);
            }
            if (player2 != null)
            {
                ServerSendData.SendRoomLogOut(player2);
                LeaveRoom(player2);
            }
            CloseRoom();
        }

        #endregion

    }

    public class RoomPlayer
    {
        float[] _currentPosition;
        int _currentIndex = 1;

        protected Dictionary<int, MovementUpdate> positionUpdate = new Dictionary<int, MovementUpdate>();

        public RoomPlayer(float[] StartPosition)
        {
            positionUpdate.Add(1, new MovementUpdate(StartPosition));
            _currentPosition = StartPosition;
        }

        protected struct MovementUpdate
        {
            float[] position;
            bool sent;

            public MovementUpdate(float[] Position)
            {
                position = Position;
                sent = false;
            }
        }

        public void SetPosition(float[] Position, int Index)
        {
            if (_currentIndex < Index)
            {
                if (positionUpdate.Count > 20)
                {
                    if (positionUpdate.TryGetValue(1, out MovementUpdate buffer))
                    {
                        positionUpdate.Clear();
                        positionUpdate.Add(1, buffer);
                    }
                    else
                        Global.serverForm.Debug("There is no start position in memory");
                }
                _currentIndex = Index;
                _currentPosition[0] = Position[0];
                _currentPosition[1] = Position[1];
                positionUpdate.Add(_currentIndex, new MovementUpdate(_currentPosition));
            }
            else
            {
                positionUpdate.Add(Index, new MovementUpdate(Position));
            }
        }

        public void Update(GamePlayerUDP player)
        {
            SendDataUDP.SendTransformUpdate(player, 1, player.ID, _currentPosition, _currentIndex);
        }

        public void UdpateStepBack(int Index)
        {
            if (positionUpdate.TryGetValue(1, out MovementUpdate buffer))
            {
                if (positionUpdate.TryGetValue(Index, out MovementUpdate stepBackBuffer))
                {
                    _currentIndex = Index;
                    positionUpdate.Clear();
                    positionUpdate.Add(1, buffer);
                    positionUpdate.Add(Index, stepBackBuffer);
                }
                else
                {
                    Global.serverForm.Debug("There is no stepback point");
                }

            }
            else
            {
                Global.serverForm.Debug("There is no start point");
            }
        }
    }

    public class RoomObject
    {

    }
}
