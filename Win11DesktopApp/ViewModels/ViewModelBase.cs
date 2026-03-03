using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace Win11DesktopApp.ViewModels
{
    public interface ICleanable
    {
        void Cleanup();
    }

    public class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected static string Res(string key) =>
            Application.Current?.TryFindResource(key) as string ?? key;
    }
}
