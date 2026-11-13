using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ModHearth.Utils
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

        /// <summary>
        /// Assigns <paramref name="field"/> and raises <see cref="PropertyChanged"/> when the value
        /// actually changes. Returns whether the value changed, so callers can run side effects only
        /// on a real transition.
        /// </summary>
        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
            => SetField(ref field, value, EqualityComparer<T>.Default, propertyName);

        /// <summary>
        /// Variant for values that need custom equality (e.g. floating-point tolerance or a custom
        /// comparer).
        /// </summary>
        protected bool SetField<T>(
            ref T field,
            T value,
            IEqualityComparer<T> comparer,
            [CallerMemberName] string? propertyName = null)
        {
            if (comparer.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
