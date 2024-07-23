using System;
using System.Linq;
using ClashRoyale.Database;
using ClashRoyale.Logic;
using ClashRoyale.Logic.Clan.StreamEntry.Entries;
using ClashRoyale.Logic.Home.Decks;
using ClashRoyale.Protocol.Messages.Server;
using ClashRoyale.Utilities;
using ClashRoyale.Utilities.Netty;
using DotNetty.Buffers;

namespace ClashRoyale.Protocol.Messages.Client.Alliance
{
    public class ChatToAllianceStreamMessage : PiranhaMessage
    {
        public ChatToAllianceStreamMessage(Device device, IByteBuffer buffer) : base(device, buffer)
        {
            Id = 14315;
        }

        public string Message { get; set; }

        public override void Decode()
        {
            Message = Reader.ReadScString();
        }

        public override async void Process()
        {
            var info = Device.Player.Home.AllianceInfo;
            if (!info.HasAlliance) return;

            var alliance = await Resources.Alliances.GetAllianceAsync(info.Id);
            if (alliance == null) return;

            if (Message.StartsWith('/'))
            {
                var cmd = Message.Split(' ');
                var cmdType = cmd[0];
                var cmdValue = 0;

                if (cmd.Length > 1)
                    if (Message.Split(' ')[1].Any(char.IsDigit))
                        int.TryParse(Message.Split(' ')[1], out cmdValue);

                switch (cmdType)
                {
                    case "/up":
                    {
                        var deck = Device.Player.Home.Deck;

                        foreach (var card in Cards.GetAllCards())
                        {
                            deck.Add(card);

                            for (var i = 0; i < 14; i++) deck.UpgradeCard(card.ClassId, card.InstanceId, true);
                        }

                        await new ServerErrorMessage(Device)
                        {
                            Message = "解锁并升级卡牌"
                        }.SendAsync();

                        break;
                    }

                    case "/unlock":
                    {
                        var deck = Device.Player.Home.Deck;

                        foreach (var card in Cards.GetAllCards()) deck.Add(card);

                        await new ServerErrorMessage(Device)
                        {
                            Message = "已解锁全部可用卡牌"
                        }.SendAsync();

                        break;
                    }

                    case "/gold":
                    {
                            if (Device.Player.Home.Id == 2)
                            {
                                Device.Player.Home.Gold += cmdValue;
                                Device.Disconnect();
                            }
                            else
                            {
                                await new ServerErrorMessage(Device)
                                {
                                    Message = $"抱歉：没有管理权限"
                                }.SendAsync();
                            }
                            break;
                        }

                    case "/gems":
                    {
                        Device.Player.Home.Diamonds += cmdValue;
                        Device.Disconnect();
                        break;
                    }

                    case "/now":
                    {
                            var entry = new ChatStreamEntry
                            {
                                Message =
                                $"———服务信息·查询如下———\n 在线玩家数 : {Resources.Players.Count}\n 单人对战中 : {Resources.Battles.Count}\n 活动对战中 : {Resources.DuoBattles.Count}\n 总部落数量 : {await AllianceDb.CountAsync()}\n 总玩家数量 : {await PlayerDb.CountAsync()}"
                            };

                            entry.SetSender(Device.Player);

                            alliance.AddEntry(entry);
                            break;
                    }

                    case "/guanli":
                    {
                            var entry = new ChatStreamEntry
                            {
                                Message =
                                $"———管理查看———\n内存占用: {System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024) + " MB"}\n当前版本运行时: {DateTime.UtcNow.Subtract(Resources.StartTime).ToReadableString()}"
                            };

                            entry.SetSender(Device.Player);

                            alliance.AddEntry(entry);
                            break;
                    }

                    case "/free":
                    {
                        Device.Player.Home.FreeChestTime = Device.Player.Home.FreeChestTime.Subtract(TimeSpan.FromMinutes(245));
                        Device.Disconnect();
                        break;
                    }

                        /*case "/replay":
                        {
                            await new HomeBattleReplayDataMessage(Device).SendAsync();
                            break;
                        }*/

                        case "/trophies":
                        {
                            if(Device.Player.Home.Id == 2)
                            {
                                if (cmdValue >= 0)
                                    Device.Player.Home.Arena.AddTrophies(cmdValue);
                                else if (cmdValue < 0)
                                    Device.Player.Home.Arena.RemoveTrophies(cmdValue);
                                Device.Disconnect();
                            } else
                            {
                                await new ServerErrorMessage(Device)
                                {
                                    Message = $"没有管理权限"
                                }.SendAsync();
                            }
                            break;
                        }

                    case "/set":
                    {

                            if (Device.Player.Home.Id == 2)
                            {
                                Device.Player.Home.Arena.SetTrophies(cmdValue);
                                Device.Disconnect();
                            }
                            else
                            {
                                await new ServerErrorMessage(Device)
                                {
                                    Message = $"没有管理权限"
                                }.SendAsync();
                            }
                            break;    
                    }

                        case "/testcuihui":
                        {
                            var entry = new DonateStreamEntry
                            {
                                Message = Message,
                                TotalCapacity = 10
                            };
                            entry.SetSender(Device.Player);
                            alliance.AddEntry(entry);
                            break;
                        }

                     case "/help":
                    {
                            var help = new ChatStreamEntry
                            {
                                Message =
                                $"↓指令介绍👉都要以/（就是斜杠）开头↓\n/up - 一键升级所有卡牌\n/free - 刷新主页免费宝箱时间\n/unlock - 直接解锁剩余卡牌\n/now - 显示目前游戏状况\n/gems 99 - 获取宝石，空格后的99可改成你想要的数"
                            };

                            help.SetSender(Device.Player);

                            alliance.AddEntry(help);
                            break;
                    }
                    default:
                        var error = new ChatStreamEntry
                        {
                            Message =
                             $"你好,发送 /help 可查看指令~, 小写."
                        };

                        error.SetSender(Device.Player);

                        alliance.AddEntry(error);
                        break;
                }
            }
            else
            {
                var entry = new ChatStreamEntry
                {
                    Message = Message
                };

                entry.SetSender(Device.Player);

                alliance.AddEntry(entry);
            }
        }
    }
}