using CommunityToolkit.Mvvm.ComponentModel;

namespace FengBroPlayer.Models;

public partial class NavItem : ObservableObject
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required string Icon { get; init; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
