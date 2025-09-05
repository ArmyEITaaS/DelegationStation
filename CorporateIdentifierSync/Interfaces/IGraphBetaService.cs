using Microsoft.Graph.Beta.Models;

namespace CorporateIdentifierSync.Interfaces
{
    public interface IGraphBetaService
    {
        Task<ImportedDeviceIdentity> AddCorporateIdentifier(ImportedDeviceIdentityType type, string identifier);

        Task<bool> DeleteCorporateIdentifier(string identifierID);

        Task<bool> CorporateIdentifierExists(string identiferID);
    }
}
