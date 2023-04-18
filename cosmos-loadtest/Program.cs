using cosmos_loadtest;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Text;
using static cosmos_loadtest.LoadConfig;

public partial class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine($"Starting at {DateTime.Now.ToString("MM-dd-yyyy HH:mm:ss.FFF")}...");
        Console.WriteLine("To STOP press CTRL+C...");

        Console.CancelKeyPress += Console_CancelKeyPress;

        string text = File.ReadAllText($"config.json");
        config = JsonConvert.DeserializeObject<Config>(text);

        CancellationToken cancellation = tokenSource.Token;

        await RunAsync(cancellation).ConfigureAwait(false);

        Console.WriteLine($"Stopped at {DateTime.Now.ToString("MM-dd-yyyy HH:mm:ss.FFF")}.");
    }

    static Config config = new Config();
    static CancellationTokenSource tokenSource = new CancellationTokenSource();
    static ConcurrentDictionary<string, long> sequentialValues = new ConcurrentDictionary<string, long>();

    static void Console_CancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        Console.WriteLine("Stopping...");
        e.Cancel = true;
        tokenSource.Cancel();
    }

    static async Task RunAsync(CancellationToken cancellation)
    {
        List<Task> tasks = new List<Task>();

        List<(string, string)> containers = new List<(string, string)>
                {
                  (config.databaseName, config.containerName)
                };

        foreach (var item in config.loadConfig)
        {
            if (item.taskCount == 0)
                continue;

            var options = new CosmosClientOptions()
            {
                ApplicationName = item.applicationName,
                ApplicationPreferredRegions = new List<string>() { config.preferredRegion },
                EnableContentResponseOnWrite = config.printResultRecord,
                AllowBulkExecution = item.allowBulk
            };

            var cosmosClient = await CosmosClient.CreateAndInitializeAsync(config.cosmosConnection, containers, options);

            for (int i = 0; i < item.taskCount; i++)
            {
                if(item.query != null)
                    tasks.Add(Task.Factory.StartNew(() => ExecuteQueryAsync(cosmosClient, item, i, cancellation), TaskCreationOptions.LongRunning).Unwrap());
                else if (item.create != null || item.upsert != null)
                    tasks.Add(Task.Factory.StartNew(() => ExecuteCreateUpsertAsync(cosmosClient, item, i, cancellation), TaskCreationOptions.LongRunning).Unwrap());
                else if (item.pointRead != null)
                    tasks.Add(Task.Factory.StartNew(() => ExecutePointReadAsync(cosmosClient, item, i, cancellation), TaskCreationOptions.LongRunning).Unwrap());
            }
        }

        try
        {
            await Task.Delay(config.durationSec * 1000, cancellation);
            tokenSource.Cancel();
        }
        catch(TaskCanceledException) 
        {
            
        }        

        await Task.WhenAll(tasks);
    }

    static string GetSequentialValueAsync(string paramName, long startValue)
    {
        return sequentialValues.AddOrUpdate(paramName, startValue, (x, y) => Interlocked.Add(ref y, 1)).ToString();
    }

    static async Task ExecuteQueryAsync(CosmosClient cosmosClient, LoadConfig loadConfig, int instanceNumber, CancellationToken cancellation)
    {
        var container = cosmosClient.GetContainer(config.databaseName, config.containerName);

        QueryDefinition query = new QueryDefinition(loadConfig.query.text);

        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                foreach (var item in loadConfig.query.parameters)
                {
                    query.WithParameter(item.name, GenerateParamValue(item, loadConfig.id).ToString());
                }

                using (var iterator = container.GetItemQueryStreamIterator(query))
                {
                    while (iterator.HasMoreResults)
                    {
                        var response = await iterator.ReadNextAsync();
                        
                        if (config.printResultRecord)
                        {
                            byte[] payload = new byte[response.Content.Length];
                            response.Content.Position = 0;
                            await response.Content.ReadAsync(payload, 0, (int)response.Content.Length);
                            Console.WriteLine($"Result: {string.Join("\n", Encoding.UTF8.GetString(payload))}");
                        }

                        if (config.printClientStats)
                            Console.WriteLine($"Timestamp: {DateTime.UtcNow}, Operation Name: {loadConfig.applicationName}_{instanceNumber}, Client time: {response.Diagnostics.GetClientElapsedTime()}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            await Task.Delay(loadConfig.intervalMS);
        }
    }

    static async Task ExecuteCreateUpsertAsync(CosmosClient cosmosClient, LoadConfig loadConfig, int instanceNumber, CancellationToken cancellation)
    {
        var container = cosmosClient.GetContainer(config.databaseName, config.containerName);

        Dictionary<Param, List<string>> paths = new Dictionary<Param, List<string>>();

        var lConfig = loadConfig.create ?? loadConfig.upsert;

        foreach (var item in lConfig.parameters)
        {
            paths.Add(item, lConfig.entity.Values().Where(x => x.ToString() == item.name).Select(x => x.Path).ToList());
        }

        while (!cancellation.IsCancellationRequested)
        {
            var entity = JObject.FromObject(lConfig.entity);

            try
            {
                foreach (var item in paths)
                {
                    var val = GenerateParamValue(item.Key, loadConfig.id);
                    foreach (var path in item.Value)
                    {
                        entity[path] = val;
                    }
                }

                var pkBuilder = new PartitionKeyBuilder();

                foreach (var key in lConfig.partitionKey)
                {
                    pkBuilder.Add(entity[key].ToString());
                }

                var pk = pkBuilder.Build();

                var response = loadConfig.create != null ? await container.CreateItemAsync<JObject>(entity, pk) : await container.UpsertItemAsync<JObject>(entity, pk);

                if (config.printResultRecord)
                    Console.WriteLine($"Result: {string.Join("\n", response.Resource)}");

                if (config.printClientStats)
                    Console.WriteLine($"Timestamp: {DateTime.UtcNow}, Operation Name: {loadConfig.applicationName}_{instanceNumber}, Client time: {response.Diagnostics.GetClientElapsedTime()}");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            await Task.Delay(loadConfig.intervalMS);
        }
    }

    static JValue GenerateParamValue(Param param, string configContext)
    {
        switch (param.type.ToLowerInvariant())
        {
            case "guid":
                return new JValue(Guid.NewGuid());
            case "datetime":
                return new JValue(DateTime.UtcNow);
            case "random_int":
                return new JValue(Random.Shared.NextInt64(param.start, param.end).ToString());
            case "sequential_int":
                return new JValue(GetSequentialValueAsync($"{configContext}_{param.name}", param.start).ToString());
            case "random_list":
                return new JValue(param.list[Random.Shared.Next(1, param.list.Count)]);
            default:
                return new JValue("");
        }
    }

    static async Task ExecutePointReadAsync(CosmosClient cosmosClient, LoadConfig loadConfig, int instanceNumber, CancellationToken cancellation)
    {
        var container = cosmosClient.GetContainer(config.databaseName, config.containerName);

        var paramId = loadConfig.pointRead.parameters.Where(x => x.name == loadConfig.pointRead.id).First();
        var paramPk = loadConfig.pointRead.parameters.Where(x => x.name == loadConfig.pointRead.partitionKey).First();

        bool isIdPkSame = loadConfig.pointRead.id == loadConfig.pointRead.partitionKey;

        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                if (loadConfig.pointRead.parameters.Count > 2)
                    throw new Exception("Maximum of 2 parameters for Point Read operations!");

                var id = GenerateParamValue(paramId, loadConfig.id).ToString();
                var pk = isIdPkSame ? id : GenerateParamValue(paramPk, loadConfig.id).ToString();

                using (var response = await container.ReadItemStreamAsync(id, new PartitionKey(pk)))
                {
                    if (config.printResultRecord)
                    {
                        byte[] payload = new byte[response.Content.Length];
                        response.Content.Position = 0;
                        await response.Content.ReadAsync(payload, 0, (int)response.Content.Length);
                        Console.WriteLine($"Result: {string.Join("\n", Encoding.UTF8.GetString(payload))}");
                    }

                    if (config.printClientStats)
                        Console.WriteLine($"Timestamp: {DateTime.UtcNow}, Operation Name: {loadConfig.applicationName}_{instanceNumber}, Client time: {response.Diagnostics.GetClientElapsedTime()}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            await Task.Delay(loadConfig.intervalMS);
        }
    }
}