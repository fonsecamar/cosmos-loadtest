using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace cosmos_loadtest
{
    public class Config
    {
        public string cosmosConnection { get; set; }
        public string databaseName { get; set; }
        public string containerName { get; set; }
        public List<string> preferredRegions { get; set; }
        public int durationSec { get; set; }
        public bool printClientStats { get; set; }
        public bool printResultRecord { get; set; }
        
        public List<LoadConfig> loadConfig { get; set; }

    }
}
