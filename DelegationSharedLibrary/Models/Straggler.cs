

namespace DelegationStationShared.Models
{
    public class Straggler
    {
        public Guid id { get; set; }
        public string PartitionKey { get; set; }

        //
        // Settings from InTune
        //
        public string ManagedDeviceID { get; set; }
        public DateTime EnrollmentDateTime { get; set; }

        // 
        // Fields related to UpdateDevices function
        //

        // count of times this device was seen without M/M/SN by UpdateDevices function
        public int UDAttemptCount { get; set; }
        // Time stamp of when this entry is created
        public DateTime CreatedDateTime { get; set; }

        // Time stamp updated by UpdateDevices function each time it sees this host
        public DateTime LastUDUpdateDateTime { get; set; }

        //
        // Fields related to StragglerHandler function
        //

        // Last time this host is seen w/o M/M/SN
        public DateTime LastSeenDateTime { get; set; }
        public DateTime LastSHAttemptDateTime { get; set; }
        public int SHAttemptCount { get; set; }
        public int SHErrorCount { get; set; }

        public Straggler()
        {
            id = Guid.NewGuid();
            PartitionKey = "Straggler";
            ManagedDeviceID = string.Empty;
            UDAttemptCount = 1;
            SHAttemptCount = 0;
            SHErrorCount = 0;
            CreatedDateTime = DateTime.UtcNow;
            LastUDUpdateDateTime = DateTime.UtcNow;
            LastSeenDateTime = DateTime.UtcNow;
        }

      }
}
