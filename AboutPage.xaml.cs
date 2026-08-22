namespace DailyFantasyMAUI;

public partial class AboutPage : ContentPage
{
    public AboutPage()
    {
        InitializeComponent();
        var version = AppInfo.Current.VersionString;
        var build   = AppInfo.Current.BuildString;
        lblVersion.Text  = $"Version {version} (Build {build})";
        lblCopyright.Text = $"© {DateTime.Today.Year} [Your Name]. All rights reserved.";
    }

    async void BtnBack_Clicked(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("..", false);
}
