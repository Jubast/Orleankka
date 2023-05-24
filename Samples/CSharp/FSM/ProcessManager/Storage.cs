﻿using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

using Orleans;
using Orleans.Runtime;
using Orleans.Storage;

namespace ProcessManager
{ 
    class Storage : IGrainStorage
    {
        public static string Init() => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "orleankka_durable_fsm_example")).FullName;

        readonly Type type;
        readonly string folder;
        readonly ILogger logger;

        internal Storage(IServiceProvider services, Type type, string folder)
        {
            this.type = type;
            this.folder = folder;
            logger = services.GetRequiredService<ILoggerFactory>().CreateLogger($"{type.Name}Storage");
        }

        internal Storage(ILogger logger, Type type, string folder)
        {
            this.type = type;
            this.folder = folder;
            this.logger = logger;
        }

        public async Task ReadStateAsync<T>(string grainType, GrainId id, IGrainState<T> grainState)
        {
            var blob = GetBlob(id);
            if (!File.Exists(blob))
                return;

            try
            {
                var json = await File.ReadAllTextAsync(blob, Encoding.UTF8);
                grainState.State = (T)JsonConvert.DeserializeObject(json, type);
            }
            catch (FileNotFoundException)
            {
                logger.LogWarning($"File {blob} disappear between checking it exists and reading");
            }
        }

        public async Task WriteStateAsync<T>(string grainType, GrainId id, IGrainState<T> grainState)
        {
            var state = grainState.State;
            var blob = GetBlob(id);
            var json = JsonConvert.SerializeObject(state);

            try
            {
                await File.WriteAllTextAsync(blob, json);
            }
            catch (IOException ex)
            {
                var message = $"File {blob} might have been changed by other write";
                logger.LogError(ex, message);
                throw new InconsistentStateException(message);
            }
        }

        public Task ClearStateAsync<T>(string grainType, GrainId id, IGrainState<T> grainState)
        {
            var blob = GetBlob(id);

            try
            {
                File.Delete(blob);
                grainState.State = (T)((object)new CopierState());
            }
            catch (IOException ex)
            {
                var message = $"File {blob} might have been changed by other write";
                logger.LogError(ex, message);
                throw new InconsistentStateException(message);
            }

            return Task.CompletedTask;
        }

        string GetBlob(GrainId id)
        {
            return Path.Combine(folder, $"{id}.blob");
        }
    }
}