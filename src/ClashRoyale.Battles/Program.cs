using System;

namespace ClashRoyale.Battles
{
    public class Program
    {
        private static void Main(string[] args)
        {
            Console.Title = "ClashRoyale Battle Server Emulator";

            Console.WriteLine(
"\n ______________           ______________\r\n/\\__________   \\         /\\   ________  \\\r\n\\/_________/   /         \\ \\  \\______/\\  \\\r\n          /   /      __   \\ \\  \\_____\\/  /\r\n         /   /      /\\_\\   \\ \\   ______  \\\r\n        /   /       \\/_/    \\ \\  \\____/\\  \\\r\n       /   /                 \\ \\  \\   \\ \\  \\\r\n      /   /__________         \\ \\  \\   \\ \\  \\___\r\n     /\\______________\\         \\ \\__\\   \\ \\_____\\\r\nHi:  \\/______________/          \\/__/    \\/_____/\n\n-光临-\n");

            Resources.Initialize();

            Console.Read();
        }
    }
}