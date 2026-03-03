namespace IndiLogs_3._0.Models
{
    public class GlobalEntry
    {
        public string Name { get; set; } = "";
        public string Value { get; set; } = "";
        public string? Default { get; set; }
        public bool IsRelevant { get; set; }

        // Pre-cached lowercase for fast search (avoids ToLower() on every keystroke)
        public string NameLower { get; set; } = "";
        public string ValueLower { get; set; } = "";
        public string? DefaultLower { get; set; }
    }
}
