using System;
using System.Windows;
using IndiLogs_3._0.Services.Interfaces;

namespace IndiLogs_3._0.Services
{
    /// <summary>
    /// Default window factory that creates windows via reflection.
    /// </summary>
    public class ViewFactory : IViewFactory
    {
        public T Create<T>(params object[] args) where T : Window
            => (T)Activator.CreateInstance(typeof(T), args)!;
    }
}
