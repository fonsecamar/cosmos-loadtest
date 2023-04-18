using Newtonsoft.Json.Linq;

namespace cosmos_loadtest
{
    public class LoadConfig
    {
        public int taskCount { get; set; }
        public int intervalMS { get; set; }
        public string applicationName { get; set; }
        public bool allowBulk { get; set; }

        public Query query { get; set; }
        public PointRead pointRead { get; set; }
        public Create create { get; set; }
        public Create upsert { get; set; }

        public bool isQuery { get { return query != null; } }

        public class Query
        {
            public string text { get; set; }
            public Dictionary<string, Param> parameters { get; set; }
        }

        public class Create
        {
            public JObject entity { get; set; }
            public Dictionary<string, Param> parameters { get; set; }
            public List<string> partitionKey { get; set; }
        }

        public class Param
        {
            public string type { get; set; }
            public long start { get; set; }
            public long end { get; set; }
            public List<string> list { get; set; }

        }

        public class PointRead
        {
            public Param partitionKey { get; set; }
            public Param id { get; set; }
        }
    }
}
