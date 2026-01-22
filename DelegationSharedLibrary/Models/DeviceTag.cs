using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;

namespace DelegationStationShared.Models
{
    public class DeviceTag
    {
        [Required]
        [JsonProperty(PropertyName = "id")]
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public List<RoleDelegation> RoleDelegations { get; set; }
        public List<DeviceUpdateAction> UpdateActions { get; set; }
        public DateTime? Modified { get; set; }
        public string ModifiedBy { get; set; }

        public string AllowedUserPrincipalName { get; set; }

        [Required(AllowEmptyStrings = false)]
        public string PartitionKey { get; set; }

        public bool CorpIDSyncEnabled { get; set; }

        public bool DeviceRenameEnabled { get; set; }
        public string DeviceNameRegex { get; set; }
        public string DeviceNameRegexDescription { get; set; } = string.Empty;

        public DeviceTag()
        {
            Id = Guid.NewGuid();
            Name = string.Empty;
            Description = string.Empty;
            RoleDelegations = new List<RoleDelegation>();
            UpdateActions = new List<DeviceUpdateAction>();
            PartitionKey = typeof(DeviceTag).Name;
            ModifiedBy = string.Empty;
            AllowedUserPrincipalName = string.Empty;
        }

        public DeviceTag DeepCopyKeepId()
        {
            DeviceTag other = (DeviceTag)this.MemberwiseClone();
            other.RoleDelegations = new List<RoleDelegation>();
            foreach (RoleDelegation roleDelegation in this.RoleDelegations)
            {
                other.RoleDelegations.Add(roleDelegation.DeepCopyKeepId());
            }
            other.UpdateActions = new List<DeviceUpdateAction>();
            foreach (DeviceUpdateAction updateAction in this.UpdateActions)
            {
                other.UpdateActions.Add(updateAction.DeepCopyKeepId());
            }
            return other;
        }
    }
}
