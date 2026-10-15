using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Collaterale.App.ViewModels;

/// <summary>An observable list that can be replaced with a single change notification (adding 100k rows one by one would freeze the UI).</summary>
public sealed class BulkCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        Items.Clear();
        foreach (var i in items)
        {
            Items.Add(i);
        }

        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
