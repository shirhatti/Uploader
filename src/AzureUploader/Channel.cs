using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Serialization;
using Newtonsoft.Json;

namespace AzureUploader
{
    public class Channel
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("url")]
        public Uri Uri { get; set; }

        [JsonProperty("files")]
        public IDictionary<string,string> Files { get; set; }

        [JsonProperty(PropertyName = "lastModified", Required = Required.Default)]
        public DateTime LastModifieDateTime { get; set; }
    }
}