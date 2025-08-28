using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace UpdatePreferredHostnames.Models
{
    public class CosmosDevice
    {
        public Guid Id { get; set; }
        public string PreferredHostname { get; set; }
        public string Tag { get; set; }
        public string Make { get; set; }
        public string Model { get; set; }
        public string SerialNumber { get; set; }
        public string OS { get; set; }
        public string Action { get; set; }
        public CosmosDevice()
        {
            Id = Guid.NewGuid();
            PreferredHostname = "";
        }
        public CosmosDevice(Guid id, string hostname)
        {
            Id = id;
            PreferredHostname = hostname;
        }

    }
}
