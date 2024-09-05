﻿using Newtonsoft.Json;
using System;
using System.ComponentModel.DataAnnotations;

namespace UpdateDevices.Models
{
    public class FunctionSettings
    {
        [Required]
        [JsonProperty(PropertyName = "id")]
        public Guid Id { get; set; }
        public string PartitionKey { get; set; }
        public DateTime? LastRun { get; set; }

        public FunctionSettings()
        {
            Id = Guid.Parse("f990517b-3927-4429-82b0-712d4856110e");
            PartitionKey = "FunctionSettings";
            LastRun = null;
        }

        public override string ToString()
        {
            string output = $"LastRun: {LastRun}";
            return output;
        }
    }
}
