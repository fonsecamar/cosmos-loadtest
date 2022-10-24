using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace cosmos_loadtest
{
    internal class LoadConfig
    {
        public int taskCount { get; set; }
        public int intervalMS { get; set; }
        public string applicationName { get; set; }

        public Query query { get; set; }
        public PointRead pointRead { get; set; }

        public bool isQuery { get { return query != null; } }

        internal class Query
        {
            public string text { get; set; }
            public Dictionary<string, Param> parameters { get; set; }

            internal class Param
            {
                public string type { get; set; }
                public long start { get; set; }
                public long end { get; set; }
                public List<string> list { get; set; }

            }
        }

        internal class PointRead
        {
            public string partitionKeyValue { get; set; }
            public string id { get; set; }
        }
    }
}
