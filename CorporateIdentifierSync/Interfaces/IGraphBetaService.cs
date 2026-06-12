using CorporateIdentifierSync.Enums;
using Microsoft.Graph.Beta.Models;

namespace CorporateIdentifierSync.Interfaces
{
    public interface IGraphBetaService
    {
        Task<ImportedDeviceIdentity> AddCorporateIdentifier(ImportedDeviceIdentityType type, string identifier);

        Task<DeleteCorpIdResult> DeleteCorporateIdentifier(string identifierID);

        Task<bool> CorporateIdentifierExists(string identiferID);

        Task<int> GetCorporateDeviceIdentifierCountAsync();
    }
}
