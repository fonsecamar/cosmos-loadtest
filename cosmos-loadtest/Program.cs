using Bogus;
using cosmos_loadtest;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
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
                ApplicationPreferredRegions = config.preferredRegions,
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

    static async Task ExecutePointReadAsync(CosmosClient cosmosClient, LoadConfig loadConfig, int instanceNumber, CancellationToken cancellation)
    {
        var container = cosmosClient.GetContainer(config.databaseName, config.containerName);

        var paramId = loadConfig.pointRead.parameters.Where(x => x.name == loadConfig.pointRead.id).First();
        var paramPk = loadConfig.pointRead.parameters.Where(x => x.name == loadConfig.pointRead.partitionKey).First();

        bool isIdPkSame = loadConfig.pointRead.id == loadConfig.pointRead.partitionKey;
        var faker = new Faker();

        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                if (loadConfig.pointRead.parameters.Count > 2)
                    throw new Exception("Maximum of 2 parameters for Point Read operations!");

                var id = GenerateParamValue(paramId, faker, loadConfig.id).ToString();
                var pk = isIdPkSame ? id : GenerateParamValue(paramPk, faker, loadConfig.id).ToString();

                using (var response = await container.ReadItemStreamAsync(id, new PartitionKey(pk)))
                {
                    if (config.printResultRecord)
                    {
                        if (response.Content != null)
                        {
                            byte[] payload = new byte[response.Content.Length];
                            response.Content.Position = 0;
                            await response.Content.ReadAsync(payload, 0, (int)response.Content.Length);
                            Console.WriteLine($"Result: {string.Join("\n", Encoding.UTF8.GetString(payload))}");
                        }
                        else
                        {
                            Console.WriteLine("Result: Not found.");
                        }
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

    static async Task ExecuteQueryAsync(CosmosClient cosmosClient, LoadConfig loadConfig, int instanceNumber, CancellationToken cancellation)
    {
        var container = cosmosClient.GetContainer(config.databaseName, config.containerName);

        QueryDefinition query = new QueryDefinition(loadConfig.query.text);

        var faker = new Faker();

        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                Dictionary<string, JValue> values = new Dictionary<string, JValue>();

                foreach (var item in loadConfig.query.parameters)
                {
                    var val = GenerateParamValue(item, faker, loadConfig.id, values);
                    values.Add(item.name, val);
                    query.WithParameter(item.name, val.ToString());
                }

                using (var iterator = container.GetItemQueryStreamIterator(query))
                {
                    while (iterator.HasMoreResults)
                    {
                        var response = await iterator.ReadNextAsync();

                        if (config.printResultRecord)
                        {
                            if (response.Content != null)
                            {
                                byte[] payload = new byte[response.Content.Length];
                                response.Content.Position = 0;
                                await response.Content.ReadAsync(payload, 0, (int)response.Content.Length);
                                Console.WriteLine($"Result: {string.Join("\n", Encoding.UTF8.GetString(payload))}");
                            }
                            else
                            {
                                Console.WriteLine("Result: Not found.");
                            }
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

        var lConfig = loadConfig.create ?? loadConfig.upsert;

        var paths = MapParameters(lConfig.entity.Values());

        var faker = new Faker();

        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                var entity = JObject.FromObject(lConfig.entity);

                Dictionary<string, JValue> values = new Dictionary<string, JValue>();
                foreach (var param in lConfig.parameters)
                {
                    var val = GenerateParamValue(param, faker, loadConfig.id, values);
                    values.Add(param.name, val);
                    foreach (var path in paths[param.name])
                    {
                        ReplacePath(entity, path, val);
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
                    Console.WriteLine($"Timestamp: {DateTime.UtcNow}, Operation Name: {loadConfig.applicationName}_{instanceNumber}, Client time: {response.Diagnostics.GetClientElapsedTime()}, Regions: {string.Join(", ", response.Diagnostics.GetContactedRegions())}");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            await Task.Delay(loadConfig.intervalMS);
        }
    }

    static JValue GenerateParamValue(Param param, Faker faker, string configContext, Dictionary<string, JValue> values = null)
    {
        switch (param.type.ToLowerInvariant())
        {
            case "guid":
                return new JValue(Guid.NewGuid());
            case "datetime":
                return new JValue(DateTime.UtcNow);
            case "random_int":
                return new JValue(Random.Shared.NextInt64(param.start, param.end));
            case "random_int_as_string":
                return new JValue(Random.Shared.NextInt64(param.start, param.end).ToString());
            case "sequential_int":
                return new JValue(GetSequentialValueAsync($"{configContext}_{param.name}", param.start));
            case "sequential_int_as_string":
                return new JValue(GetSequentialValueAsync($"{configContext}_{param.name}", param.start).ToString());
            case "random_list":
                return new JValue(param.list[Random.Shared.Next(1, param.list.Count)]);
            case "random_bool":
                return new JValue(Random.Shared.Next(2) == 1);
            case "faker.firstname":
                return new JValue(faker.Name.FirstName());
            case "faker.lastname":
                return new JValue(faker.Name.LastName());
            case "faker.fullname":
                return new JValue(faker.Name.FullName());
            case "faker.dateofbirth":
                return new JValue(faker.Person.DateOfBirth.ToString("yyyy-MM-dd"));
            case "faker.address":
                return new JValue(faker.Address.StreetAddress());
            case "faker.phone":
                return new JValue(faker.Phone.PhoneNumber());
            case "faker.email":
                return new JValue(faker.Internet.ExampleEmail(faker.Name.FirstName(), faker.Name.LastName()));
            case "concat":
                var sb = new StringBuilder();
                var idx = 0;
                foreach (Match item in Regex.Matches(param.value, "\\{@\\w+\\}"))
                {
                    sb.Append(param.value.Substring(idx, item.Index - idx));
                    sb.Append(values[item.Value.Substring(1, item.Value.Length - 2)]);
                    idx = item.Index + item.Length;
                }
                if (param.value.Length > idx + 1) sb.Append(param.value.Substring(idx));
                return new JValue(sb.ToString());
            default:
                return new JValue("");
        }
    }
    static string GetSequentialValueAsync(string paramName, long startValue)
    {
        return sequentialValues.AddOrUpdate(paramName, startValue, (x, y) => Interlocked.Add(ref y, 1)).ToString();
    }

    static JObject ReplacePath<T>(JToken root, string path, T newValue)
    {
        if (root == null || path == null)
        {
            throw new ArgumentNullException();
        }

        foreach (var value in root.SelectTokens(path).ToList())
        {
            if (value == root)
            {
                root = JToken.FromObject(newValue);
            }
            else
            {
                value.Replace(JToken.FromObject(newValue));
            }
        }

        return (JObject)root;
    }

    static Dictionary<string, List<string>> MapParameters(IEnumerable<JToken> values)
    {
        var paths = new Dictionary<string, List<string>>();

        foreach (var val in values)
        {
            if (val.HasValues)
                paths = paths.Concat(MapParameters(val.Values())).GroupBy(d => d.Key).ToDictionary(d => d.Key, d => d.First().Value);
            else if (val.ToString().StartsWith("@"))
            {
                if (paths.ContainsKey(val.ToString()))
                    paths[val.ToString()].Add(val.Path);
                else
                    paths.Add(val.ToString(), new List<string>() { val.Path });
            }
        }

        return paths;
    }
}