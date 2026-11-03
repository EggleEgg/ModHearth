using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ModHearth.Utilities
{
    /// <summary>
    /// Minimal <see cref="INotifyPropertyChanged"/> implementation shared by view models and other
    /// observable models, so change notifications are raised the same way everywhere.
    /// </summary>
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
