using DelegationStationShared.Enums;
using DelegationStationShared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.QuickGrid;
using Microsoft.Azure.Cosmos.Linq;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace DelegationStation.Pages
{
    static class CsvColumns
    {
        // These are the index values of each column in the CSV
        public const int Make = 0;
        public const int Model = 1;
        public const int SerialNumber = 2;
        public const int OS = 3;
        public const int HostName = 4;
        public const int Action = 5;

        // This is the total number of columns
        public const int Count = 6;
    }
    public partial class DevicesBulkAction
    {
        [CascadingParameter]
        public Task<AuthenticationState>? AuthState { get; set; }
        private System.Security.Claims.ClaimsPrincipal user = new System.Security.Claims.ClaimsPrincipal();
        private string userId = string.Empty;
        private string userName = string.Empty;

        private List<DeviceTag> deviceTags = new List<DeviceTag>();
        private List<string> groups = new List<string>();
        private string tagSearch = "";
        private List<string> appliedTags = new();

        private List<IBrowserFile> loadedFiles = new();

        private long maxFileSize = 1024 * 30000;
        private int maxAllowedFiles = 1;
        private bool isLoading;
        private List<DeviceBulk> devices = new();
        private IQueryable<DeviceBulk> loadedDevices = new List<DeviceBulk>().AsQueryable();
        PaginationState pagination = new PaginationState { ItemsPerPage = 10 };
        private List<string> fileError = new();
        private int completedDevices = 0;
        private int totalDevices = 0;
        private bool isUpdating;
        private List<string> updateErrors = new();
        private string userMessage = string.Empty;


        protected override async Task OnInitializedAsync()
        {
            if (AuthState is not null)
            {
                var authState = await AuthState;
                user = authState?.User ?? new System.Security.Claims.ClaimsPrincipal();
                userName = user.Claims.Where(c => c.Type == "name").Select(c => c.Value.ToString()).FirstOrDefault() ?? "";
                userId = user.Claims.Where(c => c.Type == "http://schemas.microsoft.com/identity/claims/objectidentifier").Select(c => c.Value.ToString()).FirstOrDefault() ?? "";
            }

            UpdateClaims();
            await GetTags();
            pagination.TotalItemCountChanged += (sender, eventArgs) => StateHasChanged();
        }

        private void UpdateClaims()
        {
            groups = new List<string>();
            var groupClaims = user.Claims.Where(c => c.Type == "http://schemas.microsoft.com/ws/2008/06/identity/claims/role" || c.Type == "roles");

            if (groupClaims == null)
            {
                return;
            }

            foreach (var c in groupClaims)
            {
                groups.Add(c.Value);
            }
        }

        private async Task GetTags()
        {
            Guid c = Guid.NewGuid();
            userMessage = string.Empty;
            try
            {
                deviceTags = await deviceTagDBService.GetDeviceTagsAsync(groups);
            }
            catch (Exception ex)
            {
                logger.LogError($"Error retrieving tags.\n{ex.Message}\nUser: {userName} {userId}.\nCorrelation Id:{c.ToString()}");
            }
        }

        private void AddRemoveTag(DeviceTag tag)
        {
            if (appliedTags.Contains(tag.Id.ToString()))
            {
                appliedTags.Remove(tag.Id.ToString());
            }
            else
            {
                appliedTags.Clear();
                appliedTags.Add(tag.Id.ToString());
            }
        }

        private async Task LoadFiles(InputFileChangeEventArgs e)
        {
            Guid c = Guid.NewGuid();
            userMessage = string.Empty;

            isLoading = true;
            loadedFiles.Clear();
            fileError = new();
            string path = "";
            devices = new();

            foreach (var file in e.GetMultipleFiles(maxAllowedFiles))
            {
                try
                {
                    loadedFiles.Add(file);
                    if (file.Size > maxFileSize)
                    {
                        isLoading = false;
                        fileError.Add("File too large");
                        return;
                    }


                    var trustedFileNameForFileStorage = Path.GetRandomFileName();
                    var rootFolder = Path.Combine(Environment.ContentRootPath,
                        Environment.EnvironmentName, "unsafe_uploads");
                    if (!Directory.Exists(rootFolder))
                    {
                        Directory.CreateDirectory(rootFolder);
                    }
                    path = Path.Combine(rootFolder,
                            trustedFileNameForFileStorage);

                    await using FileStream fs = new(path, FileMode.Create);
                    await file.OpenReadStream(maxFileSize).CopyToAsync(fs);
                    fs.Dispose();

                    // Read the file and parse it line by line.
                    using (StreamReader newFile = new(path))
                    {
                        string ln;
                        int line = 0;

                        while ((ln = newFile.ReadLine()!) != null)
                        {
                            line++;

                            // parse CSV and add to devices list
                            // Make,Model,SerialNumber,Action
                            if (ln.StartsWith("Make,"))
                            {
                                //Ignore the header if present
                                continue;
                            }

                            // Using Regex to split on commas, but ignore commas within quotes
                            string splitOn = ",(?=(?:[^\"]*\"[^\"]*\")*[^\"]*$)";
                            var input = Regex.Split(ln, splitOn);

                            if (input.Length != CsvColumns.Count)
                            {
                                var message = $"File upload error.\nFile Name: {file.Name}\nLine {line}. Invalid number of columns. Input contains {input.Length} columns and should have {CsvColumns.Count}.\nCorrelation Id: {c.ToString()}";
                                fileError.Add(message);
                                logger.LogError($"{message}\nUser: {userName} {userId}");
                                isLoading = false;
                                return;
                            }

                            // clean up any extra "" from the input
                            for (int i = 0; i < input.Length; i++)
                            {
                                input[i] = input[i].Trim('"');
                            }

                            try
                            {
                                // Validate Make, Model, SerialNumber, Action
                                if (input[CsvColumns.Action].ToLower() != "add" && input[CsvColumns.Action].ToLower() != "remove")
                                {
                                    var message = $"File upload error.\nFile Name: {file.Name}\nInvalid action. Action should be either add or remove.\nCorrelation Id: {c.ToString()}";
                                    fileError.Add(message);
                                    logger.LogWarning($"{message}\nUser: {userName} {userId}");
                                    continue;
                                }

                                // Validate OS if adding device (ignore field on remove)
                                // Parsing ignore case = true
                                DeviceOS? os = null;
                                if (input[CsvColumns.Action].ToLower() == "add")
                                {
                                    if (!Enum.TryParse(input[CsvColumns.OS].Trim(), true, out DeviceOS os_out))
                                    {
                                        var message = $"File upload error.\nFile Name: {file.Name}\nInvalid OS.  Valid values include:  Windows, MacOS, iOS and Android.\nCorrelation Id: {c.ToString()}";
                                        fileError.Add(message);
                                        logger.LogWarning($"{message}\nUser: {userName} {userId}");
                                        continue;
                                    }
                                    else
                                    {
                                        if (os_out == DeviceOS.Unknown)
                                        {
                                            var message = $"File upload error.\nFile Name: {file.Name}\nInvalid OS.  Valid values include:  Windows, MacOS, iOS and Android.\nCorrelation Id: {c.ToString()}";
                                            fileError.Add(message);
                                            logger.LogWarning($"{message}\nUser: {userName} {userId}");
                                            continue;
                                        }
                                    }
                                    os = os_out;
                                }

                                var newDevice = new DeviceBulk()
                                {
                                    Make = input[CsvColumns.Make],
                                    Model = input[CsvColumns.Model],
                                    SerialNumber = input[CsvColumns.SerialNumber],
                                    OS = os,
                                    PreferredHostName = input[CsvColumns.HostName],
                                    Action = (DeviceBulkAction)Enum.Parse(typeof(DeviceBulkAction), (input[CsvColumns.Action].ToLower()))
                                };
                                var context = new ValidationContext(newDevice, null, null);
                                var results = new List<ValidationResult>();

                                if (!Validator.TryValidateObject(newDevice, context, results, true))
                                {
                                    System.Text.StringBuilder sb = new System.Text.StringBuilder();
                                    sb.Append($"Line {line}. Invalid input\n");
                                    foreach (var result in results)
                                    {
                                        sb.Append("\t" + result.ErrorMessage + "\n" ?? "");
                                    };
                                    sb.AppendLine($"Correlation Id: {c.ToString()}");
                                    fileError.Add(sb.ToString());
                                    logger.LogWarning($"{sb.ToString()}\nUser: {userName} {userId}");
                                    continue;
                                }

                                devices.Add(newDevice);
                            }
                            catch (Exception ex)
                            {
                                var message = $"File upload error.\nFile Name: {file.Name}\nLine {line}.\n{ex.Message}\nCorrelation Id: {c.ToString()}";
                                fileError.Add(message);
                                logger.LogError($"{message}\nUser: {userName} {userId}");
                                isLoading = false;
                                return;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    var message = $"File upload error.\nFile Name: {file.Name}\n{ex.Message}\nCorrelation Id: {c.ToString()}";
                    fileError.Add(message);
                    logger.LogError($"{message}\nUser: {userName} {userId}");
                }
                finally
                {
                    // Delete file from temporary location
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
            }
            loadedDevices = devices.AsQueryable();
            isLoading = false;
        }

        private string GetBytes(long bytes)
        {
            string[] suffixes = { "Bytes", "KB", "MB", "GB", "TB", "PB" };
            int counter = 0;
            decimal number = (decimal)bytes;
            while (Math.Round(number / 1024) >= 1)
            {
                number = number / 1024;
                counter++;
            }
            return string.Format("{0:n0} {1}", number, suffixes[counter]);
        }

        private async Task UpdateDevices()
        {
            Guid c = Guid.NewGuid();
            userMessage = string.Empty;

            isUpdating = true;
            completedDevices = 0;
            totalDevices = devices.Count();
            updateErrors = new();

            string tagToApply = appliedTags.FirstOrDefault() ?? "";
            if (string.IsNullOrEmpty(tagToApply))
            {
                updateErrors.Add("Must select at least one tag.");
                isUpdating = false;
                return;
            }

            foreach (DeviceBulk device in devices)
            {
                if (string.IsNullOrEmpty(userId))
                {
                    var message = $"Error: User ID not found. {userId}\nCorrelation Id: {c.ToString()}";
                    updateErrors.Add(message);
                    logger.LogError($"{message}\nUser: {userName} {userId}");
                    isUpdating = false;
                    return;
                }

                if (deviceTags.Any(t => t.Id.ToString() == tagToApply) == false)
                {
                    var message = $"Error: Tag {tagToApply} not found.\nCorrelation Id: {c.ToString()}";
                    updateErrors.Add(message);
                    logger.LogError($"{message}\nUser: {userName} {userId}");
                    isUpdating = false;
                    return;
                }

                DeviceTag tag = deviceTags.First(t => t.Id.ToString() == tagToApply);

                if (authorizationService.AuthorizeAsync(user, tag, Authorization.DeviceTagOperations.BulkUpload).Result.Succeeded == false)
                {
                    var message = $"Error: Unauthorized to apply tag. TagId: {tag.Id}\nCorrelation Id: {c.ToString()}";
                    updateErrors.Add(message);
                    logger.LogError($"{message}\nUser: {userName} {userId}");
                    isUpdating = false;
                    return;
                }

                if (device.Action == DeviceBulkAction.add)
                {
                    Device d = new Device();
                    d.Make = device.Make;
                    d.Model = device.Model;
                    d.SerialNumber = device.SerialNumber;
                    d.OS = device.OS;
                    d.PreferredHostName = device.PreferredHostName;
                    d.ModifiedUTC = DateTime.UtcNow;
                    d.AddedBy = userId;
                    d.Tags.Add(tagToApply);
                    Device? deviceResult = null;

                    try
                    {
                        deviceResult = await deviceDBService.AddOrUpdateDeviceAsync(d);
                    }
                    catch (Exception ex)
                    {
                        var message = $"{ex.Message}\nMake: {device.Make}\nModel: {device.Model}\nSerialNumber: {device.SerialNumber}\nOperating System: {device.OS}\nPreferred Host Name: {device.PreferredHostName}\nCorrelation Id: {c.ToString()}";
                        updateErrors.Add(message);
                        logger.LogError($"{message}\nUser: {userName} {userId}");
                    }
                }
                else if (device.Action == DeviceBulkAction.remove)
                {
                    Device? d = null;
                    try
                    {
                        d = await deviceDBService.GetDeviceAsync(device.Make, device.Model, device.SerialNumber);
                    }
                    catch (Exception ex)
                    {
                        var message = $"{ex.Message}\nMake: {device.Make}\nModel: {device.Model}\nSerialNumber: {device.SerialNumber}\nPreferred Host Name: {device.PreferredHostName}\nCorrelation Id: {c.ToString()}";
                        logger.LogError($"{message}\nUser: {userName} {userId}");
                    }
                    if (d != null)
                    {
                        // Validate the applied tag is on the device
                        if (!d.Tags.Contains(tagToApply))
                        {
                            var message = $"Bulk Updating Devices Error on Delete:\nMake: {device.Make}\nModel: {device.Model}\nSerialNumber: {device.SerialNumber}\nPreferred Host Name: {device.PreferredHostName}\nTag: {tagToApply} not found on device.\nCorrelation Id: {c.ToString()}";
                            updateErrors.Add(message);
                            logger.LogError($"{message}\nUser: {userName} {userId}");
                        }
                        else
                        {
                            await deviceDBService.MarkDeviceToDeleteAsync(d);
                            logger.LogInformation($"Device Marked for Deletion:\nMake: {device.Make}\nModel: {device.Model}\nSerialNumber: {device.SerialNumber}\nPreferred Host Name: {device.PreferredHostName}\nUser: {userName} {userId}");
                        }
                    }
                    else
                    {
                        var message = $"Device to remove not found: \nMake: {device.Make}\nModel: {device.Model}\nSerialNumber: {device.SerialNumber}\nPreferred Host Name: {device.PreferredHostName}\nCorrelation Id: {c.ToString()}";
                        updateErrors.Add(message);
                        logger.LogError($"{message}\nUser: {userName} {userId}");
                    }
                }
                else
                {
                    var message = $"No recognized action provided: \nMake: {device.Make}\nModel: {device.Model}\nSerialNumber: {device.SerialNumber}\nPreferred Host Name: {device.PreferredHostName}\nNo update action.\nCorrelation Id: {c.ToString()}";
                    updateErrors.Add(message);
                    logger.LogError($"{message}\nUser: {userName} {userId}");
                }

                completedDevices++;
                StateHasChanged();
            }
            isUpdating = false;
        }

        private void Clear()
        {
            updateErrors.Clear();
            fileError.Clear();
            loadedFiles.Clear();
            devices.Clear();
            completedDevices = 0;
            totalDevices = 0;
        }

    }
}
