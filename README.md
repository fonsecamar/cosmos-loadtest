# Azure Cosmos DB NoSQL - Load Test (alpha version)

## Introduction

Cosmos Load Test enables you to try/benchmark Azure Cosmos DB for NoSQL using your own entities.
This guide will help you configure the `config.json` file for the `cosmos-loadtest` project.

## Prerequisites

- A text editor
- A Cosmos DB account
- A Cosmos DB database and container
- .NET Runtime 6.0

## Steps

1. Download the latest release from <a href=https://github.com/fonsecamar/cosmos-loadtest/releases>here</a>.
2. Extract `cosmos-loadtest_v0.1.zip` and open the `config.json` file in your text editor.
3. Configure the parameters according to your requirements (options detailed below).
4. Run `cosmos-loadtest.exe`.

> For better performance, run the load tester in a VM in the same Azure Region as your Azure Cosmos DB account.

## Configuration Options

| Attribute | Type | Description | Required |
| --- | --- | --- | --- |
| cosmosConnection | string | Connection string of your Cosmos DB account | yes |
| databaseName | string | Database name | yes |
| containerName | string | Container Name | yes |
| preferredRegion | string | If using multi region account, use to set the preferred region | no |
| durationSec | integer | Test duration in seconds | yes |
| printClientStats | boolean | Flag to print client statistics (noisy in massive tests) | yes |
| printResultRecord | boolean | Flag to print the results either for queries or creates/upserts (noisy in massive tests)  | yes |
| loadConfig | array | Array of tests to run | yes |

## Query Options

| Attribute | Type | Description | Required |
| --- | --- | --- | --- |
| taskCount | integer | Number of concurrent tasks to run | yes |
| intervalMS | integer | Interval/delay between interations within the same task | yes |
| applicationName | string | Application name used for CosmosClient. Helpful for tracing using Diagnostic Settings | no |
| query.text | string | Your query to be executed. Include parameters to bind. | yes |
| query.parameters | array | Array of parameters to bind in query | yes if query has any |

## Point Read Options

| Attribute | Type | Description | Required |
| --- | --- | --- | --- |
| taskCount | integer | Number of concurrent tasks to run | yes |
| intervalMS | integer | Interval/delay between interations within the same task | yes |
| applicationName | string | Application name used for CosmosClient. Helpful for tracing using Diagnostic Settings | no |
| pointRead.partitionKey | string | Partition Key value or parameter to bind. | yes |
| pointRead.id | string | Document Id value or parameter to bind. | yes |
| pointRead.parameters | array | Array of parameter to bind in `partitionKey` and `id` | yes if parameters used |

## Create Options

| Attribute | Type | Description | Required |
| --- | --- | --- | --- |
| taskCount | integer | Number of concurrent tasks to run | yes |
| intervalMS | integer | Interval/delay between interations within the same task | yes |
| applicationName | string | Application name used for CosmosClient. Helpful for tracing using Diagnostic Settings | no |
| allowBulk | boolean | Flag to use Bulk Mode | yes |
| create.entity | JSON object | Any JSON object to create. Include parameters to bind. | yes |
| create.partitionKey | array | Specify container partition. Single or multiple (hierarchical partition) | yes |
| create.parameters | array | Array of parameters to bind in entity | yes |

## Upsert Options

| Attribute | Type | Description | Required |
| --- | --- | --- | --- |
| taskCount | integer | Number of concurrent tasks to run | yes |
| intervalMS | integer | Interval/delay between interations within the same task | yes |
| applicationName | string | Application name used for CosmosClient. Helpful for tracing using Diagnostic Settings | no |
| allowBulk | boolean | Flag to use Bulk Mode | yes |
| upsert.entity | JSON object | Any JSON object to create. Include parameters to bind. | yes |
| upsert.partitionKey | array | Specify container partition. Single or multiple (hierarchical partition) | yes |
| upsert.parameters | array | Array of parameters to bind in entity | yes |

## Parameter Options

| Attribute | Type | Description | Required |
| --- | --- | --- | --- |
| name | string | Parameter name matching the ones used in the test config | yes |
| type | string | Parameter type. Allowed values: `guid`, `datetime`, `random_int`, `sequential_int`, `random_list` | yes |
| start | int | Range start for `random_int` or `sequential_int` | yes if `random_int` or `sequential_int` |
| end | int | Range end for `random_int` | yes if `random_int` |
| list | array | List of values to be randomly picked | yes if `random_list` |

> Currently, all parameters are always returned as string.

<br/>

## Monitoring

- You can use Insights pane or Metrics to monitor your test. <a href="https://learn.microsoft.com/en-us/azure/cosmos-db/use-metrics"> Monitor and debug with insights in Azure Cosmos DB</a>
- You can use Diagnostic settings for richer analysis. <a href="https://learn.microsoft.com/en-us/azure/cosmos-db/monitor-resource-logs?tabs=azure-portal">Monitor Azure Cosmos DB data by using diagnostic settings in Azure</a>

<br/>

## Clean Up

1. `CTRL + C` to stop load tester
2. Remember to delete or scale down any provisioned resource.

<br/>

# How to Contribute

If you find any errors or have suggestions for changes, please be part of this project!

1. Create your branch: `git checkout -b my-new-feature`
2. Add your changes: `git add .`
3. Commit your changes: `git commit -m '<message>'`
4. Push your branch to Github: `git push origin my-new-feature`
5. Create a new Pull Request 😄