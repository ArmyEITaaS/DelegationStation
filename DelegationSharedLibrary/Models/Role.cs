using DelegationStationShared.Enums;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;

namespace DelegationStationShared.Models
{
    public class Role
    {
        [Required]
        [JsonProperty(PropertyName = "id")]
        public Guid Id { get; set; }
        public string Name { get; set; }
        public List<AllowedAttributes> Attributes { get; set; }
        public bool SecurityGroups { get; set; }
        public bool AdministrativeUnits { get; set; }
        public string PartitionKey { get; set; }

        public Role()
        {
            Id = Guid.NewGuid();
            Name = string.Empty;
            Attributes = new List<AllowedAttributes>();
            SecurityGroups = false;
            AdministrativeUnits = false;
            PartitionKey = typeof(Role).Name;
        }

        public Role DeepCopyKeepId()
        {
            Role other = (Role)this.MemberwiseClone();
            other.Attributes = new List<AllowedAttributes>(this.Attributes);
            return other;
        }

        public Role GetRole(List<string> userGroups, DeviceTag tag)
        {
            Role userRole = GetDefaultRole();
            if (tag == null || userGroups == null || userGroups?.Count == 0)
            {
                return userRole;
            }


            foreach (RoleDelegation roleDelegation in tag.RoleDelegations)
            {
                foreach (string group in userGroups!)
                {
                    if (group == roleDelegation.SecurityGroupId)
                    {
                        userRole.Id = new Guid();
                        // Add all attributes from the role to the userRole
                        foreach (AllowedAttributes attribute in roleDelegation.Role.Attributes)
                        {
                            if (userRole.Attributes.Where(a => a == attribute).Count() == 0)
                            {
                                userRole.Attributes.Add(attribute);
                            }
                        }

                        // If the role has security groups or administrative units, set the userRole to true
                        if (roleDelegation.Role.SecurityGroups)
                        {
                            userRole.SecurityGroups = true;
                        }
                        if (roleDelegation.Role.AdministrativeUnits)
                        {
                            userRole.AdministrativeUnits = true;
                        }
                    }
                }
            }

            return userRole;
        }
        public Role GetRole(List<string> userGroups, string defaultAdminGroup, DeviceTag tag)
        {
            Role userRole = GetDefaultRole();

            if (tag == null)
            {
                return userRole;
            }

            if (userGroups?.Count == 0)
            {
                return userRole;
            }

            if (userGroups == null)
            {
                return userRole;
            }

            if (userGroups.Any(g => g == defaultAdminGroup))
            {
                return userRole.GetAdminRole();
            }

            foreach (RoleDelegation roleDelegation in tag.RoleDelegations)
            {
                foreach (string group in userGroups!)
                {
                    if (group == roleDelegation.SecurityGroupId)
                    {
                        userRole.Id = new Guid();
                        // Add all attributes from the role to the userRole
                        foreach (AllowedAttributes attribute in roleDelegation.Role.Attributes)
                        {
                            if (userRole.Attributes.Where(a => a == attribute).Count() == 0)
                            {
                                userRole.Attributes.Add(attribute);
                            }
                        }

                        // If the role has security groups or administrative units, set the userRole to true
                        if (roleDelegation.Role.SecurityGroups)
                        {
                            userRole.SecurityGroups = true;
                        }
                        if (roleDelegation.Role.AdministrativeUnits)
                        {
                            userRole.AdministrativeUnits = true;
                        }
                    }
                }
            }

            return userRole;
        }

        public Role GetDefaultRole()
        {
            return new Role()
            {
                Id = Guid.Parse("96c95f35-edee-4565-b30b-c7ddb19405ce"),
                Name = "None",
                Attributes = new List<AllowedAttributes>() { },
                SecurityGroups = false,
                AdministrativeUnits = false
            };
        }

        public Role GetAdminRole()
        {
            return new Role()
            {
                Id = Guid.Parse("b1a13567-256c-41d8-8ec0-80671a9e909d"),
                Name = "Admin",
                Attributes = new List<AllowedAttributes>() { AllowedAttributes.All },
                SecurityGroups = true,
                AdministrativeUnits = true
            };
        }

        public bool IsAdminRole()
        {
            return Id == GetAdminRole().Id;
        }

        public bool IsDefaultRole()
        {
            return Id == GetDefaultRole().Id;
        }
    }
}
