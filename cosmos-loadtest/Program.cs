using cosmos_loadtest;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using System.Linq;
using System.Threading.Channels;

public partial class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("To STOP press CTRL+C...");

        Console.CancelKeyPress += Console_CancelKeyPress;

        var configBuilder = new ConfigurationBuilder()
             .SetBasePath(Directory.GetCurrentDirectory())
             .AddJsonFile($"config.json")
             .Build();

        config = configBuilder.Get<Config>();

        RunAsync().GetAwaiter().GetResult();

        Console.WriteLine("Stopped!");
    }

    static volatile bool cancel = false;

    static Config config = new Config();
    static void Console_CancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        Console.WriteLine("Stopping...");
        cancel = e.Cancel = true;
    }

    static async Task RunAsync()
    {
        List<Task> tasks = new List<Task>();

        foreach (var item in config.loadConfig)
        {
            tasks.Add(ConfigOperation(item));
        }

        await Task.WhenAll(tasks);
    }

    static async Task ConfigOperation(LoadConfig loadConfig)
    {
        var cosmosClient = new CosmosClient(config.cosmosConnection, new CosmosClientOptions()
        {
            ApplicationName = loadConfig.applicationName,
            ApplicationPreferredRegions = new List<string>() { config.preferredRegion }
        });

        var container = cosmosClient.GetContainer(config.databaseName, config.containerName);

        List<Task> tasks = new List<Task>();

        for (int i = 0; i < loadConfig.taskCount; i++)
        {
            tasks.Add(ExecuteOperation(container, loadConfig, i));
        }

        await Task.WhenAll(tasks);
    }

    static async Task ExecuteOperation(Container container, LoadConfig loadConfig, int instanceNumber)
    {
        while (!cancel)
        {
            QueryDefinition query = new QueryDefinition(loadConfig.query.text);

            foreach (var item in loadConfig.query.parameters)
            {
                query.WithParameter(item.Key, (item.Value.type == "random_int" ? Random.Shared.NextInt64(item.Value.start, item.Value.end).ToString() : item.Value.list[Random.Shared.Next(1, item.Value.list.Count)]));
            }

            using (var iterator = container.GetItemQueryIterator<dynamic>(query))
            {
                while (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    
                    if(config.printResultRecord)
                        Console.WriteLine($"Result: {string.Join("\n", response.Resource)}");

                    if(config.printClientStats)
                        Console.WriteLine($"Timestamp: {DateTime.UtcNow}, Operation Name: {loadConfig.applicationName}_{instanceNumber}, Count: {response.Resource.Count()}, Request Chage: {response.RequestCharge}, Client time: {response.Diagnostics.GetClientElapsedTime()}");
                }
            }

            await Task.Delay(loadConfig.intervalMS);
        }
    }
}