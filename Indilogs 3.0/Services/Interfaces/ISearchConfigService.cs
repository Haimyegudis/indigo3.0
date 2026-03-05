using System.Collections.Generic;
using IndiLogs_3._0.Models.Grep;

namespace IndiLogs_3._0.Services.Interfaces
{
    public interface ISearchConfigService
    {
        void SaveProfile(SearchProfile profile);
        SearchProfile? LoadProfile(string name);
        SearchProfile? LoadProfileFromFile(string filePath);
        List<string> ListProfiles();
        bool DeleteProfile(string name);
        bool RenameProfile(string oldName, string newName);
        void ExportProfile(SearchProfile profile, string filePath);
        SearchProfile? ImportProfile(string filePath);
    }
}
