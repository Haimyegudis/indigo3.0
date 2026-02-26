using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IndiLogs_3._0.ViewModels
{
    /// <summary>
    /// Base class for all ViewModels. Provides INotifyPropertyChanged and IDisposable patterns.
    /// Derived classes should override Dispose(bool) to clean up:
    ///   - Event subscriptions (PropertyChanged, Tick, custom events)
    ///   - Timers (DispatcherTimer, System.Threading.Timer)
    ///   - Large collection references
    /// </summary>
    public abstract class ViewModelBase : INotifyPropertyChanged, IDisposable
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private bool _disposed;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        /// <summary>
        /// Also allows external callers (like parent VMs) to notify a property change by name.
        /// </summary>
        public void NotifyPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing)
            {
                // Derived classes override this to clean up managed resources
            }
            _disposed = true;
        }

        protected bool IsDisposed => _disposed;
    }
}
