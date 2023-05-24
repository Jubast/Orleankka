﻿using System;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Hosting;
using Orleans.ApplicationParts;

namespace Orleankka.Legacy.Cluster
{
    using Behaviors;

    public class LegacyOrleankkaClusterOptions
    {
        internal void Configure(IApplicationPartManager apm, IServiceCollection services)
        {
            var assemblies = apm.ApplicationParts
                .OfType<AssemblyPart>().Select(x => x.Assembly)
                .Distinct()
                .ToArray();

            var actors = assemblies.SelectMany(x => x.GetTypes())
                .Where(x => typeof(Actor).IsAssignableFrom(x) && x.IsClass && !x.IsAbstract)
                .ToArray();

            RegisterBehaviors(actors);
        }

        static void RegisterBehaviors(Type[] actors)
        {
            foreach (var each in actors)
                ActorBehavior.Register(each);
        }
    }

    public static class LegacySiloHostBuilderExtensions
    {
        public static ISiloHostBuilder UseOrleankkaLegacyFeatures(this ISiloHostBuilder builder) => 
            UseOrleankkaLegacyFeatures(builder, new LegacyOrleankkaClusterOptions());

        public static ISiloHostBuilder UseOrleankkaLegacyFeatures(this ISiloHostBuilder builder, Func<LegacyOrleankkaClusterOptions, LegacyOrleankkaClusterOptions> configure) => 
            UseOrleankkaLegacyFeatures(builder, configure(new LegacyOrleankkaClusterOptions()));

        public static ISiloHostBuilder UseOrleankkaLegacyFeatures(this ISiloHostBuilder builder, LegacyOrleankkaClusterOptions cfg) => 
            builder.ConfigureServices(services => UseOrleankkaLegacyFeatures(builder.GetApplicationPartManager(), services, cfg));

        public static ISiloBuilder UseOrleankkaLegacyFeatures(this ISiloBuilder builder) => 
            UseOrleankkaLegacyFeatures(builder, new LegacyOrleankkaClusterOptions());

        public static ISiloBuilder UseOrleankkaLegacyFeatures(this ISiloBuilder builder, Func<LegacyOrleankkaClusterOptions, LegacyOrleankkaClusterOptions> configure) => 
            UseOrleankkaLegacyFeatures(builder, configure(new LegacyOrleankkaClusterOptions()));

        public static ISiloBuilder UseOrleankkaLegacyFeatures(this ISiloBuilder builder, LegacyOrleankkaClusterOptions cfg) => 
            builder.ConfigureServices(services => UseOrleankkaLegacyFeatures(builder.GetApplicationPartManager(), services, cfg));

        static void UseOrleankkaLegacyFeatures(IApplicationPartManager apm, IServiceCollection services, LegacyOrleankkaClusterOptions cfg) => 
            cfg.Configure(apm, services);
    }
}