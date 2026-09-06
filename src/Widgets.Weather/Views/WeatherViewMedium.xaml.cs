using System.Windows.Controls;

namespace Widgets.Weather.Views;

public partial class WeatherViewMedium : UserControl
{
    public WeatherViewMedium(WeatherViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
