using AvaloniaBuilder.CodeGen;
using AvaloniaBuilder.Model;
using AvaloniaBuilder.Xaml;

namespace AvaloniaBuilder.Metadata;

public sealed record DocumentTemplate(string Name, string Description, string Xaml)
{
	public bool IsWindow => Xaml.TrimStart().StartsWith("<Window", StringComparison.Ordinal);
}

public static class DocumentTemplates
{
	private const string NamespaceToken = "__NS__";
	private const string ViewToken = "__VIEW__";
	private const string ViewModelToken = "__VM__";

	private const string WindowStart = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:vm="using:__NS__.ViewModels"
                x:Class="__NS__.Views.__VIEW__"
                x:DataType="vm:__VM__"
        """;

	private const string DesignData = """

            <Design.DataContext>
                <vm:__VM__ />
            </Design.DataContext>

        """;

	public static IReadOnlyList<DocumentTemplate> All { get; } = new[]
	{
		new DocumentTemplate("Empty window", "A window with one empty Grid.", WindowStart + """
                    Title="__VIEW__"
                    Width="800" Height="450">
            """ + DesignData + """
                <Grid Margin="16" />
            </Window>
            """),

		new DocumentTemplate("Control panel", "Buttons, a slider and read-outs on the left. A big area on the right.", WindowStart + """
                    Title="Control panel"
                    Width="1000" Height="640" MinWidth="700" MinHeight="420">
            """ + DesignData + """
                <Grid ColumnDefinitions="320,*">
                    <DockPanel Margin="16">
                        <StackPanel DockPanel.Dock="Top" Spacing="12">
                            <StackPanel Orientation="Horizontal" Spacing="8">
                                <Button Classes="accent" Content="Start" Command="{Binding StartCommand}" />
                                <Button Content="Home" Command="{Binding HomeCommand}" />
                                <Button Content="STOP" Command="{Binding StopCommand}" />
                            </StackPanel>
                            <TextBlock Text="Speed" FontWeight="SemiBold" />
                            <Slider Minimum="0" Maximum="100" Value="{Binding Speed}" />
                            <Grid ColumnDefinitions="90,Auto" RowDefinitions="Auto,Auto">
                                <TextBlock Text="target" />
                                <TextBlock Grid.Column="1" Text="{Binding Speed, StringFormat={}{0:F1}}" />
                                <TextBlock Grid.Row="1" Text="measured" />
                                <TextBlock Grid.Row="1" Grid.Column="1" Text="{Binding Measured, StringFormat={}{0:F1}}" />
                            </Grid>
                            <CheckBox Content="Drives enabled" IsChecked="{Binding DrivesEnabled}" />
                            <TextBlock Text="Event log" FontWeight="SemiBold" />
                        </StackPanel>
                        <TextBlock DockPanel.Dock="Bottom" Text="{Binding StatusText}" FontSize="12" Opacity="0.7" Margin="0,8,0,0" />
                        <ListBox ItemsSource="{Binding Log}" />
                    </DockPanel>

                    <Border Grid.Column="1" Background="#18808080" CornerRadius="8" Margin="0,16,16,16">
                        <TextBlock Text="Main view" HorizontalAlignment="Center" VerticalAlignment="Center" Opacity="0.5" />
                    </Border>
                </Grid>
            </Window>
            """),

		new DocumentTemplate("Menu, content and status bar", "A classic main window.", WindowStart + """
                    Title="__VIEW__"
                    Width="900" Height="600">
            """ + DesignData + """
                <DockPanel>
                    <Menu DockPanel.Dock="Top">
                        <MenuItem Header="_File">
                            <MenuItem Header="_New" Command="{Binding NewCommand}" />
                            <MenuItem Header="_Open..." Command="{Binding OpenCommand}" />
                            <MenuItem Header="_Save" Command="{Binding SaveCommand}" />
                            <Separator />
                            <MenuItem Header="E_xit" Command="{Binding ExitCommand}" />
                        </MenuItem>
                        <MenuItem Header="_Help">
                            <MenuItem Header="_About" Command="{Binding AboutCommand}" />
                        </MenuItem>
                    </Menu>
                    <Border DockPanel.Dock="Bottom" BorderBrush="#40808080" BorderThickness="0,1,0,0" Padding="8,4">
                        <TextBlock Text="{Binding StatusText}" FontSize="12" />
                    </Border>
                    <Grid Margin="16" />
                </DockPanel>
            </Window>
            """),

		new DocumentTemplate("Form dialog", "Captions and inputs, with OK and Cancel at the bottom.", WindowStart + """
                    Title="Edit"
                    Width="420" Height="260"
                    CanResize="False"
                    WindowStartupLocation="CenterOwner">
            """ + DesignData + """
                <DockPanel Margin="16">
                    <StackPanel DockPanel.Dock="Bottom" Orientation="Horizontal" Spacing="8" HorizontalAlignment="Right">
                        <Button Classes="accent" Content="OK" IsDefault="True" Command="{Binding OkCommand}" />
                        <Button Content="Cancel" IsCancel="True" Command="{Binding CancelCommand}" />
                    </StackPanel>
                    <Grid ColumnDefinitions="Auto,*" RowDefinitions="Auto,Auto,Auto" ColumnSpacing="12" RowSpacing="8" VerticalAlignment="Top">
                        <TextBlock Text="Name" VerticalAlignment="Center" />
                        <TextBox Grid.Column="1" Text="{Binding Name}" />
                        <TextBlock Grid.Row="1" Text="Email" VerticalAlignment="Center" />
                        <TextBox Grid.Row="1" Grid.Column="1" Text="{Binding Email}" />
                        <TextBlock Grid.Row="2" Text="Active" VerticalAlignment="Center" />
                        <CheckBox Grid.Row="2" Grid.Column="1" IsChecked="{Binding IsActive}" />
                    </Grid>
                </DockPanel>
            </Window>
            """),

		new DocumentTemplate("List and details", "A list on the left. The selected item on the right.", WindowStart + """
                    Title="__VIEW__"
                    Width="900" Height="560">
            """ + DesignData + """
                <Grid ColumnDefinitions="260,4,*">
                    <ListBox ItemsSource="{Binding Items}" SelectedItem="{Binding SelectedItem}" />
                    <GridSplitter Grid.Column="1" ResizeDirection="Columns" />
                    <StackPanel Grid.Column="2" Margin="16" Spacing="8">
                        <TextBlock Text="{Binding SelectedItem}" FontSize="20" FontWeight="SemiBold" />
                        <TextBlock Text="Details" Opacity="0.7" />
                    </StackPanel>
                </Grid>
            </Window>
            """),

		new DocumentTemplate("Empty user control", "A reusable piece of UI. It has no window of its own.", """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
                         xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
                         xmlns:vm="using:__NS__.ViewModels"
                         mc:Ignorable="d" d:DesignWidth="800" d:DesignHeight="450"
                         x:Class="__NS__.Views.__VIEW__"
                         x:DataType="vm:__VM__">
                <Grid Margin="16" />
            </UserControl>
            """),
	};

	public static DesignDocument Create(DocumentTemplate template, string rootNamespace = "MyApp", string? viewName = null)
	{
		viewName ??= template.IsWindow ? "MainWindow" : "MainView";
		string viewModelName = CodeNames.Split(ViewModelGenerator.SuggestViewModelClass(rootNamespace + ".Views." + viewName)).Name;
		string xaml = template.Xaml
			.Replace(NamespaceToken, rootNamespace)
			.Replace(ViewModelToken, viewModelName)
			.Replace(ViewToken, viewName);
		var document = DesignXamlReader.Parse(xaml);
		document.MarkSaved();
		return document;
	}
}
