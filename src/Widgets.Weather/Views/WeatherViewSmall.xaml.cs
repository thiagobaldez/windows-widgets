using System.Windows.Controls;

namespace Widgets.Weather.Views;

public partial class WeatherViewSmall : UserControl
{
    public WeatherViewSmall(WeatherViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
