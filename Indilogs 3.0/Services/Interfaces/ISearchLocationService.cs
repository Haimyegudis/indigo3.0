using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using IndiLogs_3._0.Models.Grep;

namespace IndiLogs_3._0.Services.Interfaces
{
    public interface ISearchLocationService
    {
        IReadOnlyList<SearchLocation> Locations { get; }
        void Add(SearchLocation location);
        void Remove(Guid id);
        void Update(SearchLocation location);
        Task<ConnectionStatus> TestConnectivityAsync(SearchLocation location);
        void Save();
    }
}
