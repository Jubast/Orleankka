﻿using System;
using System.Threading.Tasks;

using Microsoft.WindowsAzure.Storage;
using Microsoft.Extensions.DependencyInjection;

using Orleankka;
using Orleankka.Cluster;

namespace Demo
{
    using Microsoft.Extensions.Hosting;

    public static class Program
    {
        static App app;

        public static async Task Main()
        {
            Console.WriteLine("Make sure you've started Azure storage emulator!");
            Console.WriteLine("Running demo. Booting cluster might take some time ...\n");

            var account = CloudStorageAccount.DevelopmentStorageAccount;
            var storage = await TopicStorage.Init(account);

            var builder = new HostBuilder()
                .ConfigureServices(s => s
                    .AddSingleton(storage))
                .UseOrleans(c => c
                    .ConfigureServer()
                    .UseOrleankka());

            var host = await builder.StartAsync();
            var system = host.ActorSystem();

            app = new App(system, system.CreateObservable());
            app.Run();

            Console.WriteLine("Press Enter to terminate ...");
            Console.ReadLine();

            host.Dispose();
        }
    }
}
