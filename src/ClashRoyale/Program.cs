using System;
using System.Threading;
using ClashRoyale.Utilities.Utils;

namespace ClashRoyale
{
    public static class Program
    {
        private static void Main()
        {
            Console.Title = "ClashRoyale Server Emulator";

            Console.WriteLine(
"\n ______________           ______________\r\n/\\__________   \\         /\\   ________  \\\r\n\\/_________/   /         \\ \\  \\______/\\  \\\r\n          /   /      __   \\ \\  \\_____\\/  /\r\n         /   /      /\\_\\   \\ \\   ______  \\\r\n        /   /       \\/_/    \\ \\  \\____/\\  \\\r\n       /   /                 \\ \\  \\   \\ \\  \\\r\n      /   /__________         \\ \\  \\   \\ \\  \\___\r\n     /\\______________\\         \\ \\__\\   \\ \\_____\\\r\nHi:  \\/______________/          \\/__/    \\/_____/\n\n-欢迎-\n");

            Resources.Initialize();

            if (ServerUtils.IsLinux())
            {
                Thread.Sleep(Timeout.Infinite);
            }
            else
            {
                Logger.Log("按任意键关闭服务器.", null);
                Console.Read();
            }

            Shutdown();
        }

        public static async void Shutdown()
        {
            Console.WriteLine("关停中...");

            await Resources.Netty.Shutdown();

            try
            {
                Console.WriteLine("保存玩家中...");

                lock (Resources.Players.SyncObject)
                {
                    foreach (var player in Resources.Players.Values) player.Save();
                }

                Console.WriteLine("所有玩家已保存√");
            }
            catch (Exception)
            {
                Console.WriteLine("未能保存所有玩家.");
            }

            await Resources.Netty.ShutdownWorkers();
        }

        public static void Exit()
        {
            Environment.Exit(0);
        }
    }
}