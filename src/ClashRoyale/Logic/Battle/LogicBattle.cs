using System;
using System.Collections.Generic;
using System.Linq;
using System.Timers;
using ClashRoyale.Core;
using ClashRoyale.Core.Cluster;
using ClashRoyale.Database;
using ClashRoyale.Extensions;
using ClashRoyale.Extensions.Utils;
using ClashRoyale.Files;
using ClashRoyale.Files.CsvLogic;
using ClashRoyale.Protocol.Messages.Server;
using ClashRoyale.Utilities.Models.Battle.Replay;
using ClashRoyale.Utilities.Netty;
using DotNetty.Buffers;
using SharpRaven;
using SharpRaven.Data;

namespace ClashRoyale.Logic.Battle
{
    public class LogicBattle : List<Player>
    {
        /// <summary>
        ///     1v1 Battle
        /// </summary>
        /// <param name="isFriendly"></param>
        /// <param name="arena"></param>
        public LogicBattle(bool isFriendly, int arena)
        {
            IsFriendly = isFriendly;
            Arena = arena;
            if(arena >= 7 && arena != 10)
            {
                Location = Csv.Tables.Get(Csv.Files.Locations)
                           .GetData<Locations>(Csv.Tables.Get(Csv.Files.Arenas)
                                .GetDataWithInstanceId<Arenas>(Arena).PvpLocation).GetInstanceId()+1;
            } else
            {
                Location = Csv.Tables.Get(Csv.Files.Locations)
                           .GetData<Locations>(Csv.Tables.Get(Csv.Files.Arenas)
                                .GetDataWithInstanceId<Arenas>(arena - 1).PvpLocation).GetInstanceId()+1;
            }
            Replay.Battle.Location = 15000000 + Location;

            BattleTimer.Elapsed += Tick;
        }

        /// <summary>
        ///     2v2 Battle
        /// </summary>
        /// <param name="isFriendly"></param>
        /// <param name="arena"></param>
        /// <param name="players"></param>
        public LogicBattle(bool isFriendly, int arena, IReadOnlyCollection<Player> players)
        {
            if (players.Count < 4)
            {
                Logger.Log("Not enough players to start a 2v2 battle.", GetType(), ErrorLevel.Error);
                return;
            }

            IsFriendly = isFriendly;
            Is2V2 = true;

            Arena = arena;
            if (arena >= 7 && arena != 10)
            {
                Location = Csv.Tables.Get(Csv.Files.Locations)
                           .GetData<Locations>(Csv.Tables.Get(Csv.Files.Arenas)
                                .GetDataWithInstanceId<Arenas>(arena).TeamVsTeamLocation).GetInstanceId() + 1;
            }
            else
            {
                Location = Csv.Tables.Get(Csv.Files.Locations)
                           .GetData<Locations>(Csv.Tables.Get(Csv.Files.Arenas)
                                .GetDataWithInstanceId<Arenas>(arena - 1).TeamVsTeamLocation).GetInstanceId() + 1;
            }

            Replay.Battle.Location = 15000000 + Location;

            AddRange(players);

            BattleTimer.Elapsed += Tick;
        }

        public int BattleTime => (int) DateTime.UtcNow.Subtract(StartTime).TotalSeconds * 2;
        public int BattleSeconds => BattleTime / 2;

        public bool IsRunning => BattleTimer.Enabled;
        public bool IsReady => Count >= (Is2V2 ? 4 : 2);

        public static int MinTrophies = 56;
        public static int MaxTrophy = 68;

        public async void Start()
        {
            if (!IsReady) return;

            try
            {
                NodeInfo server = null;
                if (Resources.Configuration.UseUdp)
                    server = Resources.NodeManager.GetServer();

                //var second = false;
                foreach (var player in this)
                {
                    Commands.Add(player.Home.Id, new Queue<byte[]>());

                    // Add decks to replay
                    /*if (!second)
                    {
                        Replay.Battle.Avatar0 = player.Home.BattleAvatar;
                        Replay.Battle.Deck1 = player.Home.BattleDeck;
                        second = true;
                    }
                    else
                    {
                        Replay.Battle.Avatar1 = player.Home.BattleAvatar;
                        Replay.Battle.Deck0 = player.Home.BattleDeck;
                    }*/

                    if (server != null)
                        await new UdpConnectionInfoMessage(player.Device)
                        {
                            ServerPort = server.Port,
                            ServerHost = server.Ip,
                            SessionId = BattleId,
                            Nonce = server.Nonce,
                            Index = (byte)IndexOf(player)
                        }.SendAsync();

                    await new SectorStateMessage(player.Device)
                    {
                        Battle = this
                    }.SendAsync();
                }

                StartTime = DateTime.UtcNow;

                if (!Resources.Configuration.UseUdp || server == null)
                    BattleTimer.Start();
            }
            catch (Exception)
            {
                Logger.Log("Couldn't start battle", GetType(), ErrorLevel.Error);
            }
        }

        public void Encode(IByteBuffer packet)
        {
            #region SectorState

            const int towers = 6;

            packet.WriteVInt(Location); // LocationData

            packet.WriteVInt(Count); // PlayerCount
            packet.WriteVInt(0); // NpcData
            packet.WriteVInt(Arena); // ArenaData

            foreach (var player in this)
            {
                packet.WriteVInt(player.Home.HighId);
                packet.WriteVInt(player.Home.LowId);
                packet.WriteVInt(0);
            }

            // ConstantSizeIntArray
            {
                packet.WriteVInt(1);
                packet.WriteVInt(0);
                packet.WriteVInt(0);

                packet.WriteVInt(7);
                packet.WriteVInt(0);
                packet.WriteVInt(0);
            }

            packet.WriteBoolean(false); // IsReplay / Type?
            packet.WriteBoolean(false); // IsEndConditionMatched
            packet.WriteBoolean(false);

            packet.WriteBoolean(false); // IsNpc

            packet.WriteBoolean(false); // isBattleEndedWithTimeOut
            packet.WriteBoolean(false);

            packet.WriteBoolean(false); // hasPlayerFinishedNpcLevel
            packet.WriteBoolean(false);

            packet.WriteBoolean(false); // isInOvertime
            packet.WriteBoolean(false); // isTournamentMode

            packet.WriteVInt(0);

            packet.WriteVInt(towers);
            packet.WriteVInt(towers);

            packet.WriteData(Csv.Tables.Get(Csv.Files.Buildings).GetDataWithInstanceId<Buildings>(1));
            packet.WriteData(Csv.Tables.Get(Csv.Files.Buildings).GetDataWithInstanceId<Buildings>(1));
            packet.WriteData(Csv.Tables.Get(Csv.Files.Buildings).GetDataWithInstanceId<Buildings>(1));
            packet.WriteData(Csv.Tables.Get(Csv.Files.Buildings).GetDataWithInstanceId<Buildings>(1));

            packet.WriteData(Csv.Tables.Get(Csv.Files.Buildings).GetDataWithInstanceId<Buildings>(0));
            packet.WriteData(Csv.Tables.Get(Csv.Files.Buildings).GetDataWithInstanceId<Buildings>(0));

            // LogicGameObject::encodeComponent
            packet.WriteVInt(1);
            packet.WriteVInt(0);
            packet.WriteVInt(1);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(1);

            for (var i = 0; i < towers; i++)
            {
                packet.WriteVInt(5);
                packet.WriteVInt(i);
            }

            var p = this[0].Home.ExpLevel - 1;
            var e = this[1].Home.ExpLevel - 1;

            // Player Right Princess Tower
            packet.WriteVInt(e);
            packet.WriteVInt(13);
            packet.WriteVInt(14500); // X
            packet.WriteVInt(25500); // Y
            packet.WriteHex("00007F00C07C0002000000000000");

            // Enemy Left Princess Tower
            packet.WriteVInt(p);
            packet.WriteVInt(13);
            packet.WriteVInt(3500); // X
            packet.WriteVInt(6500); // Y
            packet.WriteHex("00007F0080040001000000000000");

            // Player Left Princess Tower
            packet.WriteVInt(e);
            packet.WriteVInt(13);
            packet.WriteVInt(3500); // X
            packet.WriteVInt(25500); // Y
            packet.WriteHex("00007F00C07C0001000000000000");

            // Enemy Right Princess Tower
            packet.WriteVInt(p);
            packet.WriteVInt(13);
            packet.WriteVInt(14500); // X
            packet.WriteVInt(6500); // Y
            packet.WriteHex("00007F0080040002000000000000");

            // Enemy Crown Tower
            packet.WriteVInt(p);
            packet.WriteVInt(13);
            packet.WriteVInt(9000); // X
            packet.WriteVInt(3000); // Y
            packet.WriteHex("00007F0080040000000000000000");

            packet.WriteHex("000504077F7D7F0400050401007F7F0000");
            packet.WriteVInt(0); // Ms before regen mana
            packet.WriteVInt(6); // Mana Start 
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);

            packet.WriteHex("00007F7F7F7F7F7F7F7F00");

            // Player Crown Tower
            packet.WriteVInt(e);
            packet.WriteVInt(13);
            packet.WriteVInt(9000); // X
            packet.WriteVInt(29000); // Y
            packet.WriteHex("00007F00C07C0000000000000000");

            packet.WriteHex("00050401047D010400040706007F7F0000");
            packet.WriteVInt(0); // Ms before regen mana
            packet.WriteVInt(6); // Elexir Start Enemy
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);

            packet.WriteVInt(0);
            packet.WriteVInt(0);

            for (var index = 0; index < 8; index++)
                packet.WriteVInt(-1);

            for (var index = 0; index < 48; index++)
                packet.WriteVInt(0);

            // LogicHitpointComponent
            packet.WriteVInt(PrincessTowerHp[e]); // Enemy 
            packet.WriteVInt(0);
            packet.WriteVInt(PrincessTowerHp[p]); // Player
            packet.WriteVInt(0);
            packet.WriteVInt(PrincessTowerHp[e]); // Enemy
            packet.WriteVInt(0);
            packet.WriteVInt(PrincessTowerHp[p]); // Player
            packet.WriteVInt(0);
            packet.WriteVInt(KingTowerHp[p]); // Player
            packet.WriteVInt(0);
            packet.WriteVInt(KingTowerHp[e]); // Enemy
            packet.WriteVInt(0);

            // LogicCharacterBuffComponent
            for (var index = 0; index < towers; index++)
                packet.WriteHex("00000000000000A401A401");

            packet.WriteHex("FF01");
            this[0].Home.Deck.EncodeAttack(packet);

            packet.WriteVInt(0);

            packet.WriteHex("FE03");
            this[1].Home.Deck.EncodeAttack(packet);

            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(5);
            packet.WriteVInt(6);
            packet.WriteVInt(2);
            packet.WriteVInt(2);
            packet.WriteVInt(4);
            packet.WriteVInt(2);
            packet.WriteVInt(1);
            packet.WriteVInt(3);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(6);
            packet.WriteVInt(1);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(9);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(12);

            packet.WriteHex("000000F69686FF0A002A002B");

            packet.WriteVInt(0);
            packet.WriteVInt(13);
            packet.WriteVInt(14500);
            packet.WriteVInt(25500);
            packet.WriteHex("00007F00C07C0002000000000000");

            packet.WriteVInt(0);
            packet.WriteVInt(13);
            packet.WriteVInt(3500);
            packet.WriteVInt(6500);
            packet.WriteHex("00007F0080040001000000000000");

            packet.WriteVInt(0);
            packet.WriteVInt(13);
            packet.WriteVInt(3500);
            packet.WriteVInt(25500);
            packet.WriteHex("00007F00C07C0001000000000000");

            packet.WriteVInt(0);
            packet.WriteVInt(13);
            packet.WriteVInt(14500);
            packet.WriteVInt(6500);
            packet.WriteHex("00007F0080040002000000000000");

            packet.WriteVInt(0);
            packet.WriteVInt(13);
            packet.WriteVInt(9000);
            packet.WriteVInt(3000);
            packet.WriteHex("00007F0080040000000000000000");

            packet.WriteVInt(0);
            packet.WriteVInt(5);
            packet.WriteVInt(1);
            packet.WriteVInt(0);

            packet.WriteHex("7F000000007F7F0000000100000000007F7F7F7F7F7F7F7F");
            packet.WriteVInt(0);

            packet.WriteVInt(0);
            packet.WriteVInt(13);
            packet.WriteVInt(9000);
            packet.WriteVInt(29000);
            packet.WriteHex("00007F00C07C0000000000000000");

            packet.WriteVInt(0);
            packet.WriteVInt(5);
            packet.WriteVInt(4);
            packet.WriteVInt(0);
            packet.WriteVInt(1);
            packet.WriteVInt(4);

            packet.WriteHex(
                "7F020203007F7F0000000500000000007F7F7F7F7F7F7F7F0000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000");

            packet.WriteVInt(0);
            packet.WriteVInt(3350);

            packet.WriteVInt(0);
            packet.WriteVInt(560);

            packet.WriteVInt(0);
            packet.WriteVInt(3350);

            packet.WriteVInt(0);
            packet.WriteVInt(560);

            packet.WriteVInt(0);
            packet.WriteVInt(960);

            packet.WriteVInt(0);
            packet.WriteVInt(6400);

            for (var index = 0; index < towers; index++)
                packet.WriteHex("00000000000000A401A401");

            #endregion 
        }

        public void EncodeDuo(IByteBuffer packet)
        {
            #region DuoSectorState

            const int towers = 10;

            packet.WriteVInt(Location); // LocationData

            packet.WriteVInt(Count); // PlayerCount
            packet.WriteVInt(0); // NpcData
            packet.WriteVInt(Arena); // ArenaData

            for (var i = 0; i < Count; i++)
            {
                packet.WriteVInt(this[i].Home.HighId);
                packet.WriteVInt(this[i].Home.LowId);
                packet.WriteVInt(0);
            }

            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);

            packet.WriteVInt(84);
            packet.WriteVInt(84);
            packet.WriteVInt(0);
            packet.WriteVInt(0);

            packet.WriteVInt(towers);
            packet.WriteVInt(towers);

            // KingTower
            packet.WriteData(Csv.Tables.Get(Csv.Files.Buildings).GetDataWithInstanceId<Buildings>(1));
            packet.WriteData(Csv.Tables.Get(Csv.Files.Buildings).GetDataWithInstanceId<Buildings>(1));
            packet.WriteData(Csv.Tables.Get(Csv.Files.Buildings).GetDataWithInstanceId<Buildings>(1));
            packet.WriteData(Csv.Tables.Get(Csv.Files.Buildings).GetDataWithInstanceId<Buildings>(1));

            // PrincessTower
            packet.WriteData(Csv.Tables.Get(Csv.Files.Buildings).GetDataWithInstanceId<Buildings>(0));
            packet.WriteData(Csv.Tables.Get(Csv.Files.Buildings).GetDataWithInstanceId<Buildings>(0));
            packet.WriteData(Csv.Tables.Get(Csv.Files.Buildings).GetDataWithInstanceId<Buildings>(0));
            packet.WriteData(Csv.Tables.Get(Csv.Files.Buildings).GetDataWithInstanceId<Buildings>(0));

            // KingTowerMiddle
            packet.WriteData(Csv.Tables.Get(Csv.Files.Buildings).GetDataWithInstanceId<Buildings>(16));
            packet.WriteData(Csv.Tables.Get(Csv.Files.Buildings).GetDataWithInstanceId<Buildings>(16));

            // LogicGameObject::encodeComponent
            packet.WriteVInt(1);
            packet.WriteVInt(2);
            packet.WriteVInt(3);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(1);
            packet.WriteVInt(2);
            packet.WriteVInt(3);
            packet.WriteVInt(1);
            packet.WriteVInt(0);

            for (var i = 0; i < towers; i++)
            {
                packet.WriteVInt(5);
                packet.WriteVInt(i);
            }

            packet.WriteVInt(7);
            packet.WriteVInt(13);
            packet.WriteVInt(14500);
            packet.WriteVInt(25500);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(-1);
            packet.WriteVInt(0);
            packet.WriteVInt(-1);
            packet.WriteVInt(0);
            packet.WriteVInt(2);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);

            packet.WriteVInt(0);

            packet.WriteVInt(7);
            packet.WriteVInt(13);
            packet.WriteVInt(3500);
            packet.WriteVInt(6500);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(-1);
            packet.WriteVInt(0);
            packet.WriteVInt(-1);
            packet.WriteVInt(0);
            packet.WriteVInt(1);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);

            var home1 = this[0];
            var home2 = this[2];

            var enemy1 = this[1];
            var enemy2 = this[3];

            packet.WriteVInt(0);
            packet.WriteVInt(7);
            packet.WriteVInt(13);
            packet.WriteVInt(3500);
            packet.WriteVInt(25500);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(-1);
            packet.WriteVInt(0);
            packet.WriteVInt(-1);
            packet.WriteVInt(0);
            packet.WriteVInt(1);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);

            packet.WriteVInt(0);
            packet.WriteVInt(7);
            packet.WriteVInt(13);
            packet.WriteVInt(14500);
            packet.WriteVInt(6500);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(-1);
            packet.WriteVInt(0);
            packet.WriteVInt(-1);
            packet.WriteVInt(0);
            packet.WriteVInt(2);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);

            // Home duokingtower Enemy Right
            packet.WriteVInt(0);
            packet.WriteVInt(7);
            packet.WriteVInt(13);
            packet.WriteVInt(11000);
            packet.WriteVInt(3000);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(-1);
            packet.WriteVInt(0);
            packet.WriteVInt(-1);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(2);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(5);

            // Rotation
            packet.WriteByte(4);
            packet.WriteByte(0);
            packet.WriteByte(1);
            packet.WriteByte(1);
            packet.WriteByte(1);

            packet.WriteByte(4);
            for (var i = 4; i < 8; i++)
                packet.WriteByte(i);

            packet.WriteHex("007F7F00000005");

            packet.WriteHex(
                "00000000007F7F7F7F7F7F7F7F00");

            // home  duokingtower player Right
            packet.WriteVInt(7);
            packet.WriteVInt(13);
            packet.WriteVInt(11000);
            packet.WriteVInt(29000);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(-1);
            packet.WriteVInt(0);
            packet.WriteVInt(-1);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(2);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(5);

            // Rotation
            packet.WriteByte(4);
            packet.WriteByte(0);
            packet.WriteByte(1);
            packet.WriteByte(1);
            packet.WriteByte(1);

            packet.WriteByte(4);
            for (var i = 4; i < 8; i++)
                packet.WriteByte(i);

            packet.WriteHex("007F7F00000005");

            packet.WriteHex(
                "00000000007F7F7F7F7F7F7F7F00");

            // Home  duokingtower Enemy left
            packet.WriteVInt(7);
            packet.WriteVInt(13);
            packet.WriteVInt(7000);
            packet.WriteVInt(3000);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(-1);
            packet.WriteVInt(0);
            packet.WriteVInt(-1);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(1);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(5);

            // Rotation
            packet.WriteByte(4);
            packet.WriteByte(0);
            packet.WriteByte(1);
            packet.WriteByte(1);
            packet.WriteByte(1);

            packet.WriteByte(4);
            for (var i = 4; i < 8; i++)
                packet.WriteByte(i);

            packet.WriteHex("007F7F00000005");

            packet.WriteHex(
                "00000000007F7F7F7F7F7F7F7F00");

            // Home  duokingtower player left
            packet.WriteVInt(7);
            packet.WriteVInt(13);
            packet.WriteVInt(7000);
            packet.WriteVInt(29000);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(-1);
            packet.WriteVInt(0);
            packet.WriteVInt(-1);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(1);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(5);

            // Rotation
            packet.WriteByte(4);
            packet.WriteByte(0);
            packet.WriteByte(1);
            packet.WriteByte(1);
            packet.WriteByte(1);

            packet.WriteByte(4);
            for (var i = 4; i < 8; i++)
                packet.WriteByte(i);

            packet.WriteHex("007F7F00000005");

            packet.WriteHex(
                "00000000007F7F7F7F7F7F7F7F00");

            packet.WriteVInt(0);
            packet.WriteVInt(9);
            packet.WriteVInt(9000);
            packet.WriteVInt(29000);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(-1);
            packet.WriteVInt(0);
            packet.WriteVInt(-1);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);

            packet.WriteVInt(0);
            packet.WriteVInt(9);
            packet.WriteVInt(9000);
            packet.WriteVInt(3000);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(-1);
            packet.WriteVInt(0);
            packet.WriteVInt(-1);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);
            packet.WriteVInt(0);

            packet.WriteHex(
                "000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000");

            // LogicHitpointComponent
            packet.WriteVInt(8777);
            packet.WriteVInt(0);

            packet.WriteVInt(8777);
            packet.WriteVInt(0);

            packet.WriteVInt(8777);
            packet.WriteVInt(0);

            packet.WriteVInt(8777);
            packet.WriteVInt(0);

            packet.WriteVInt(21580);
            packet.WriteVInt(0);

            packet.WriteVInt(21580);
            packet.WriteVInt(0);

            packet.WriteVInt(21580);
            packet.WriteVInt(0);

            packet.WriteVInt(21580);
            packet.WriteVInt(0);

            for (var i = 0; i < towers; i++)
                packet.WriteHex("00000000000000A401A401");

            packet.WriteHex("FF01");
            home1.Home.Deck.EncodeAttack(packet);

            packet.WriteVInt(0);
            packet.WriteHex("FE01");
            home2.Home.Deck.EncodeAttack(packet);

            packet.WriteVInt(0);
            packet.WriteHex("FE03");
            enemy1.Home.Deck.EncodeAttack(packet);

            packet.WriteVInt(0);
            packet.WriteHex("FE03");
            enemy2.Home.Deck.EncodeAttack(packet);

            packet.WriteHex("00000506070802040202010300000000000000010200001800000C000000CCE9D7B507002A002B");

            #endregion 
        }

        /// <summary>
        ///     Stops the battle
        /// </summary>
        public void Stop()
        {
            if (!Resources.Configuration.UseUdp)
                BattleTimer.Stop();

            Resources.Battles.Remove(BattleId);

            //File.WriteAllText("replay.json", JsonConvert.SerializeObject(Replay));
        }

        /// <summary>
        ///     Checks wether the battle is over or we have to send sector heartbeat (TCP only)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        public async void Tick(object sender, ElapsedEventArgs args)
        {
            #region Tick

            try
            {
                foreach (var player in ToArray())
                    if (player.Device.IsConnected)
                    {

                        if (player.Device.SecondsSinceLastCommand > 2)
                        {
                            if (BattleSeconds <= 10) continue;

                            var rnd = new Random();
                            var trophies = IsFriendly || Is2V2 ? 0 : rnd.Next(56, 88);

                            if (!IsFriendly)
                            {
                                player.Home.AddCrowns(6);
                                player.Home.Arena.AddTrophies(trophies);
                            }

                            await new BattleResultMessage(player.Device)
                            {
                                TrophyReward = trophies
                            }.SendAsync();

                            Remove(player);
                        }
                        else
                        {
                            await new SectorHearbeatMessage(player.Device)
                            {
                                Turn = BattleTime,
                                Commands = GetOwnQueue(player.Home.Id)
                            }.SendAsync();
                        }
                    }
                    else
                    {
                        Remove(player);
                    }

                if (FindIndex(p => p?.Device.SecondsSinceLastCommand < 10) <= -1)
                    Stop();
            }
            catch (Exception)
            {
                Logger.Log("BattleTick failed.", GetType(), ErrorLevel.Error);
            }

            #endregion
        }

        /// <summary>
        ///     Remove a player from the battle and stop it when it's empty
        /// </summary>
        /// <param name="player"></param>
        public new void Remove(Player player)
        {
            if (Count <= 1)
                Stop();

            player.Battle = null;

            if (Is2V2)
            {
                var index = FindIndex(x => x?.Home.Id == player.Home.Id);
                if (index <= -1) return;

                this[index] = null;
                Commands[player.Home.Id] = null;
            }
            else
                base.Remove(player);
        }

        /// <summary>
        ///     Stops the battle for a specific player (only UDP)
        /// </summary>
        public async void Stop(byte index)
        {
            #region Stop

            if (Count <= index) return;

            var player = this[index];

            if (player == null) return;

            var rnd = new Random();
            var trophies = IsFriendly || Is2V2 ? 0 : rnd.Next(55, 78);

            if (!IsFriendly)
            {
                player.Home.AddCrowns(6);
                player.Home.Arena.AddTrophies(trophies);
            }

            await new BattleResultMessage(player.Device)
            {
                TrophyReward = trophies
            }.SendAsync();

            player.Battle = null;
            this[index] = null;

            if (this.All(x => x == null)) Stop();

            #endregion
        }

        #region CommandStorage 

        public Queue<byte[]> GetEnemyQueue(long userId)
        {
            return Commands.FirstOrDefault(cmd => cmd.Key != userId).Value;
        }

        public Queue<byte[]> GetOwnQueue(long userId)
        {
            return Commands.FirstOrDefault(cmd => cmd.Key == userId).Value;
        }

        public List<Queue<byte[]>> GetOtherQueues(long userId)
        {
            var cmds = new List<Queue<byte[]>>();

            foreach (var (key, value) in Commands)
                if (key != userId && value != null)
                    cmds.Add(value);

            return cmds;
        }

        public Device GetEnemy(long userId)
        {
            return this.FirstOrDefault(p => p.Home.Id != userId)?.Device;
        }

        public Player GetTeammate(long userId)
        {
            var index = FindIndex(x => x?.Home.Id == userId);
            return this[index % 2 == 0 ? index == 0 ? 2 : 0 : index == 1 ? 3 : 1];
        }

        public List<Player> GetAllOthers(long userId)
        {
            return this.Where(x => x?.Home.Id != userId).ToList();
        }

        #endregion

        #region Objects 

        public Timer BattleTimer = new Timer(500);
        public LogicReplay Replay = new LogicReplay();
        public Dictionary<long, Queue<byte[]>> Commands = new Dictionary<long, Queue<byte[]>>();
        public long BattleId { get; set; }
        private DateTime StartTime { get; set; }
        public bool Is2V2 { get; set; }
        public bool IsFriendly { get; set; }
        public int Arena { get; set; }
        public int Location { get; set; }

        public static int[] KingTowerHp =
        {
            6400, 7000, 7600, 8200, 8800, 9400, 10000, 10600, 11200, 11800, 12400, 13000, 15552
        };

        public static int[] DuoKingTowerHp =
        {
            8880, 9880, 10880, 11880, 12880, 13880, 14880, 21580, 21580, 21580, 21580, 21580, 21580
        };

        public static int[] PrincessTowerHp =
        {
            3350, 4000, 4400, 4600, 5000, 5400, 5800, 8777, 8777, 8777, 8777, 8777, 8777
        };

        #endregion
    }
}