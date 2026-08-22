using System.Windows;
using NiiRMotion.Core;

namespace NiiRMotion.App;

public partial class HardwareSetupWindow : Window
{
    public UserHardwareInventory Inventory { get; private set; } = UserHardwareInventory.Empty;

    public HardwareSetupWindow(UserHardwareInventory? current = null)
    {
        InitializeComponent();
        if (current is null) return;
        JoyConChoice.IsChecked = current.HasJoyCons;
        PsMoveChoice.IsChecked = current.HasPsMoves;
        PhoneChoice.IsChecked = current.HasPhone;
        BoardChoice.IsChecked = current.HasBalanceBoard;
        HandsChoice.IsChecked = current.UsesHandTracking;
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        Inventory = new(1, JoyConChoice.IsChecked == true, PsMoveChoice.IsChecked == true, PhoneChoice.IsChecked == true,
            BoardChoice.IsChecked == true, HandsChoice.IsChecked == true, DateTimeOffset.UtcNow);
        DialogResult = true;
    }
}
