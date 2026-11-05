using DelegationStationShared.Models;

namespace DelegationStation.Interfaces
{
    public interface ICorpIdDBService
    {
        Task<CorpIDCounter?> GetCorpIDCounterAsync();
    }
}
