using System.Windows.Controls;

namespace Widgets.Weather.Views;

public partial class WeatherViewLarge : UserControl
{
    public WeatherViewLarge(WeatherViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
