#nullable disable
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IndiLogs_3._0.Models.Grep
{
    /// <summary>
    /// Represents a searchable log location (local path, UNC share, or mapped drive).
    /// </summary>
    public class SearchLocation : INotifyPropertyChanged
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        private string _name;
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        private string _address;
        /// <summary>
        /// IP address or hostname of the machine.
        /// </summary>
        public string Address
        {
            get => _address;
            set { _address = value; OnPropertyChanged(); }
        }

        private string _basePath;
        /// <summary>
        /// UNC path or mapped drive path to the log directory.
        /// When Address is set, this can be auto-built as \\Address\share.
        /// </summary>
        public string BasePath
        {
            get => _basePath;
            set { _basePath = value; OnPropertyChanged(); }
        }

        private bool _isActive = true;
        public bool IsActive
        {
            get => _isActive;
            set { _isActive = value; OnPropertyChanged(); }
        }

        private ConnectionStatus _connectionStatus = ConnectionStatus.Unknown;
        public ConnectionStatus ConnectionStatus
        {
            get => _connectionStatus;
            set { _connectionStatus = value; OnPropertyChanged(); }
        }

        private DateTime? _lastAccessed;
        public DateTime? LastAccessed
        {
            get => _lastAccessed;
            set { _lastAccessed = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public enum ConnectionStatus
    {
        Unknown,
        Connected,
        Disconnected,
        Testing
    }
}
