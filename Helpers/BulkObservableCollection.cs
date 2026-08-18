using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace FengBroPlayer.Helpers;

/// <summary>
/// An observable collection that can publish one reset after a batch mutation,
/// avoiding one expensive UI refresh per removed item.
/// </summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public int RemoveRange(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        CheckReentrancy();

        var removed = 0;
        foreach (var item in items)
        {
            if (Items.Remove(item))
                removed++;
        }

        if (removed == 0)
            return 0;

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        return removed;
    }
}
