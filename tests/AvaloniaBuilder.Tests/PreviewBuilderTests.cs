using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaBuilder.Metadata;
using AvaloniaBuilder.Model;
using AvaloniaBuilder.Preview;
using AvaloniaBuilder.Xaml;

namespace AvaloniaBuilder.Tests;

public class PreviewBuilderTests
{
	private const string Header = "xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";

	private const string Catalog = $$"""
        <UserControl {{Header}} Width="640" Height="480" Padding="4" FontSize="13" Foreground="#202020">
            <Grid RowDefinitions="Auto,*,2*,40" ColumnDefinitions="200,*" RowSpacing="4" ColumnSpacing="6" Margin="8,4">
                <StackPanel Grid.Row="0" Grid.ColumnSpan="2" Orientation="Horizontal" Spacing="8" HorizontalAlignment="Right">
                    <Button x:Name="Ok" Classes="accent big" Content="OK" IsDefault="True" Padding="12,4" MinWidth="80" ToolTip.Tip="Accept" />
                    <ToggleButton Content="Toggle" IsChecked="True" CornerRadius="4,0,0,4" />
                    <RepeatButton Content="Repeat" Delay="200" Interval="50" />
                    <CheckBox Content="Check" IsChecked="True" IsThreeState="True" />
                    <RadioButton Content="Radio" GroupName="g" IsChecked="False" />
                    <ToggleSwitch OnContent="On" OffContent="Off" IsChecked="True" />
                    <HyperlinkButton Content="Link" NavigateUri="https://avaloniaui.net" />
                </StackPanel>
                <DockPanel Grid.Row="1" LastChildFill="False" Background="#10808080">
                    <TextBlock DockPanel.Dock="Top" Text="Title" FontWeight="Bold" FontStyle="Italic" FontSize="18" TextAlignment="Center"
                               TextWrapping="Wrap" TextTrimming="CharacterEllipsis" Foreground="DarkRed" Opacity="0.8" MaxLines="2" />
                    <TextBox DockPanel.Dock="Top" Text="abc" PlaceholderText="Type here" AcceptsReturn="True" MaxLength="20" IsReadOnly="True"
                             PasswordChar="*" TextAlignment="Right" />
                    <Slider DockPanel.Dock="Bottom" Minimum="-10" Maximum="10" Value="2.5" TickFrequency="5" IsSnapToTickEnabled="False"
                            Orientation="Horizontal" />
                    <ProgressBar DockPanel.Dock="Bottom" Minimum="0" Maximum="200" Value="50" IsIndeterminate="False" ShowProgressText="True" />
                    <NumericUpDown DockPanel.Dock="Left" Minimum="0" Maximum="100" Value="12.5" Increment="0.5" FormatString="0.0" />
                </DockPanel>
                <Border Grid.Row="1" Grid.Column="1" Background="#223344" BorderBrush="Red" BorderThickness="1,2,3,4" CornerRadius="6"
                        Padding="10" BoxShadow="0 2 4 0 #40000000" ClipToBounds="True" IsVisible="True" ZIndex="2" Cursor="Hand">
                    <Canvas Width="200" Height="100">
                        <Rectangle Canvas.Left="10" Canvas.Top="20" Width="50" Height="30" Fill="SteelBlue" Stroke="Black" StrokeThickness="1.5"
                                   RadiusX="4" RadiusY="4" StrokeDashArray="2,1" />
                        <Ellipse Canvas.Left="80" Canvas.Top="20" Width="40" Height="40" Fill="#80FF0000" />
                        <Line StartPoint="0,0" EndPoint="100,50" Stroke="Green" StrokeThickness="2" />
                        <Polygon Points="0,0 20,0 10,20" Fill="Orange" />
                        <Path Data="M 0,0 L 10,10 L 0,20 Z" Fill="Purple" Stretch="Uniform" Width="20" Height="20" />
                    </Canvas>
                </Border>
                <TabControl Grid.Row="2" TabStripPlacement="Top" SelectedIndex="1">
                    <TabItem Header="One">
                        <ListBox SelectionMode="Multiple" SelectedIndex="1">
                            <ListBoxItem Content="A" />
                            <ListBoxItem Content="B" IsEnabled="False" />
                        </ListBox>
                    </TabItem>
                    <TabItem Header="Two">
                        <ComboBox SelectedIndex="0" MaxDropDownHeight="120" PlaceholderText="Pick">
                            <ComboBoxItem Content="First" />
                            <ComboBoxItem Content="Second" />
                        </ComboBox>
                    </TabItem>
                </TabControl>
                <ScrollViewer Grid.Row="2" Grid.Column="1" HorizontalScrollBarVisibility="Auto" VerticalScrollBarVisibility="Visible">
                    <WrapPanel Orientation="Vertical" ItemWidth="60" ItemHeight="24">
                        <Label Content="Label" />
                        <Expander Header="More" IsExpanded="True" ExpandDirection="Down">
                            <UniformGrid Rows="1" Columns="2">
                                <Calendar SelectionMode="SingleDate" />
                                <Image Stretch="UniformToFill" Width="16" Height="16" />
                            </UniformGrid>
                        </Expander>
                        <Viewbox Stretch="Uniform" Width="40">
                            <TextBlock Text="Zoom" />
                        </Viewbox>
                        <Separator Margin="0,4" />
                        <Menu>
                            <MenuItem Header="_File">
                                <MenuItem Header="Open" InputGesture="Ctrl+O" />
                                <Separator />
                                <MenuItem Header="Exit" IsEnabled="False" />
                            </MenuItem>
                        </Menu>
                    </WrapPanel>
                </ScrollViewer>
                <GridSplitter Grid.Row="3" ResizeDirection="Columns" Width="4" />
                <SplitView Grid.Row="3" Grid.Column="1" DisplayMode="Inline" IsPaneOpen="True" OpenPaneLength="120" PanePlacement="Right" />
            </Grid>
        </UserControl>
        """;

	[AvaloniaFact]
	public void The_preview_sets_the_same_values_as_the_real_xaml_loader()
	{
		var document = DesignXamlReader.Parse(Catalog);

		using var result = PreviewBuilder.Build(document, new PreviewOptions { DesignMode = false });
		object expected = AvaloniaRuntimeXamlLoader.Load(Catalog, typeof(App).Assembly);

		Assert.Empty(result.Diagnostics.Select(d => d.Message));
		Assert.NotNull(result.Root);
		var differences = new List<string>();
		int compared = Compare(document, document.Root, result.Root, expected, differences);
		Assert.Empty(differences);
		Assert.True(compared > 150, $"only {compared} values were compared");
	}

	[AvaloniaFact]
	public void A_bad_value_gives_a_message_and_the_rest_still_works()
	{
		var document = Parse("""
            <StackPanel>
                <Button Content="One" Margin="abc" />
                <Button Content="Two" Nonsense="1" />
                <Buton Content="Three" />
                <Button Content="Four" Width="120" />
            </StackPanel>
            """);

		using var result = PreviewBuilder.Build(document);

		var panel = Assert.IsType<StackPanel>(((UserControl)result.Root!).Content);
		Assert.Equal(4, panel.Children.Count);
		Assert.Equal(120, ((Button)panel.Children[3]).Width);
		Assert.Equal(3, result.Diagnostics.Count(d => d.Level == DiagnosticLevel.Error));
		Assert.Contains(result.Diagnostics, d => d.Attribute == "Margin" && d.Message.Contains("abc"));
		Assert.Contains(result.Diagnostics, d => d.Attribute == "Nonsense" && d.Message.Contains("no property"));
		Assert.Contains(result.Diagnostics, d => d.Message.Contains("<Buton>"));
	}

	[AvaloniaFact]
	public void A_control_of_the_users_app_becomes_a_placeholder_at_the_right_place()
	{
		var document = DesignXamlReader.Parse($"""
            <UserControl {Header} xmlns:ctl="using:MyApp.Controls">
                <Grid ColumnDefinitions="*,*">
                    <ctl:ArmView Grid.Column="1" Width="150" ArmBrush="Red" Speed="{"{Binding Speed}"}" />
                </Grid>
            </UserControl>
            """);

		using var result = PreviewBuilder.Build(document);

		var placeholder = result.GetControl(document.Root.Children[0].Children[0]);
		Assert.NotNull(placeholder);
		Assert.Equal(1, Grid.GetColumn(placeholder));
		Assert.Equal(150, placeholder.Width);
		Assert.DoesNotContain(result.Diagnostics, d => d.Level == DiagnosticLevel.Error);
	}

	[AvaloniaFact]
	public void A_window_root_is_shown_as_a_user_control()
	{
		var document = DesignXamlReader.Parse($"""
            <Window {Header} Title="Hello" Width="400" Height="300" WindowStartupLocation="CenterScreen" Background="Beige" Closing="OnClosing">
                <Window.KeyBindings>
                    <KeyBinding Gesture="Ctrl+S" Command="{"{Binding SaveCommand}"}" />
                </Window.KeyBindings>
                <TextBlock Text="Hi" />
            </Window>
            """);

		using var result = PreviewBuilder.Build(document);

		var root = Assert.IsType<UserControl>(result.Root);
		Assert.Equal(400, root.Width);
		Assert.Equal(Brushes.Beige.Color, ((ISolidColorBrush)root.Background!).Color);
		Assert.Single(root.KeyBindings);
		Assert.Empty(result.Diagnostics.Select(d => d.Message));
	}

	[AvaloniaFact]
	public void A_binding_shows_its_path_and_a_bound_list_shows_sample_items()
	{
		var document = Parse("""
            <StackPanel>
                <TextBlock Text="{Binding Speed, StringFormat={}{0:F1}}" />
                <Button Content="{Binding Caption}" Command="{Binding SaveCommand}" IsEnabled="{Binding CanSave}" />
                <Slider Value="{Binding Speed}" />
                <ListBox ItemsSource="{Binding People}" />
            </StackPanel>
            """);

		using var result = PreviewBuilder.Build(document);

		var panel = (StackPanel)((UserControl)result.Root!).Content!;
		Assert.Equal("{Speed}", ((TextBlock)panel.Children[0]).Text);
		Assert.Equal("{Caption}", ((Button)panel.Children[1]).Content);
		Assert.True(panel.Children[1].IsEnabled);
		Assert.Equal(3, ((ListBox)panel.Children[3]).ItemCount);
		Assert.Empty(result.Diagnostics.Select(d => d.Message));
	}

	[AvaloniaFact]
	public void Property_elements_work()
	{
		var document = Parse("""
            <Grid>
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto" />
                    <RowDefinition Height="2*" MinHeight="20" />
                </Grid.RowDefinitions>
                <Border Grid.Row="1">
                    <Border.Background>
                        <LinearGradientBrush StartPoint="0%,0%" EndPoint="100%,0%">
                            <GradientStop Offset="0" Color="Red" />
                            <GradientStop Offset="1" Color="Blue" />
                        </LinearGradientBrush>
                    </Border.Background>
                    <ToolTip.Tip>
                        <TextBlock Text="A tip" />
                    </ToolTip.Tip>
                </Border>
                <TextBlock>
                    <TextBlock.Inlines>
                        <Run Text="a" />
                        <Run Text="b" FontWeight="Bold" />
                    </TextBlock.Inlines>
                </TextBlock>
                <SplitView IsPaneOpen="True">
                    <SplitView.Pane>
                        <StackPanel x:Name="PanePanel" />
                    </SplitView.Pane>
                    <Grid x:Name="ContentGrid" />
                </SplitView>
            </Grid>
            """);

		using var result = PreviewBuilder.Build(document);

		Assert.Empty(result.Diagnostics.Select(d => d.Message));
		var grid = (Grid)((UserControl)result.Root!).Content!;
		Assert.Equal(2, grid.RowDefinitions.Count);
		Assert.Equal(new GridLength(2, GridUnitType.Star), grid.RowDefinitions[1].Height);
		var border = (Border)grid.Children[0];
		Assert.Equal(2, Assert.IsType<LinearGradientBrush>(border.Background).GradientStops.Count);
		Assert.IsType<TextBlock>(ToolTip.GetTip(border));
		Assert.Equal(2, ((TextBlock)grid.Children[1]).Inlines!.Count);
		var split = (SplitView)grid.Children[2];
		Assert.Equal("PanePanel", Assert.IsType<StackPanel>(split.Pane).Name);
		Assert.Equal("ContentGrid", Assert.IsType<Grid>(split.Content).Name);
	}

	[AvaloniaFact]
	public void Styles_and_templates_in_the_document_are_used()
	{
		var document = Parse("""
            <StackPanel>
                <StackPanel.Styles>
                    <Style Selector="Button.warm">
                        <Setter Property="Background" Value="Orange" />
                    </Style>
                </StackPanel.Styles>
                <Button Classes="warm" Content="Styled" />
                <ItemsControl>
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <TextBlock Text="templated" />
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                    <x:String>one</x:String>
                    <x:String>two</x:String>
                </ItemsControl>
            </StackPanel>
            """);

		PreviewBuilder.Build(document).Dispose();
		using var result = PreviewBuilder.Build(document);
		var window = new Window { Content = result.Root };
		window.Show();
		Dispatcher.UIThread.RunJobs();

		Assert.Empty(result.Diagnostics.Select(d => d.Message));
		var panel = (StackPanel)((UserControl)result.Root!).Content!;
		Assert.Equal(Brushes.Orange.Color, ((ISolidColorBrush)((Button)panel.Children[0]).Background!).Color);
		var items = (ItemsControl)panel.Children[1];
		Assert.Equal(2, items.ItemCount);
		Assert.NotNull(items.ItemTemplate);
		window.Close();
	}

	[AvaloniaFact]
	public void Theme_resources_are_found()
	{
		var document = Parse("""<Border Background="{DynamicResource SystemAccentColor}" BorderBrush="{StaticResource NoSuchKey}" />""");

		using var result = PreviewBuilder.Build(document);
		var window = new Window { Content = result.Root };
		window.Show();
		Dispatcher.UIThread.RunJobs();

		var border = (Border)((UserControl)result.Root!).Content!;
		Assert.IsAssignableFrom<ISolidColorBrush>(border.Background);
		Assert.Null(border.BorderBrush);
		window.Close();
	}

	[AvaloniaFact]
	public void An_empty_panel_gets_a_size_in_design_mode_only()
	{
		var document = Parse("<StackPanel><Grid /><Border /></StackPanel>");

		using var design = PreviewBuilder.Build(document, new PreviewOptions { DesignMode = true });
		using var run = PreviewBuilder.Build(document, new PreviewOptions { DesignMode = false });

		var grid = document.Root.Children[0].Children[0];
		Assert.True(design.GetControl(grid)!.MinHeight > 0);
		Assert.Contains(grid, design.EmptyContainers);
		Assert.Equal(0, run.GetControl(grid)!.MinHeight);
		Assert.Empty(run.EmptyContainers);
	}

	[AvaloniaFact]
	public void The_tab_with_the_selected_element_is_opened()
	{
		var document = Parse("""
            <TabControl>
                <TabItem Header="One"><TextBlock Text="1" /></TabItem>
                <TabItem Header="Two"><TextBlock Text="2" /></TabItem>
            </TabControl>
            """);
		var tabControlNode = document.Root.Children[0];
		var textInSecondTab = tabControlNode.Children[1].Children[0];
		var activeTabs = new Dictionary<DesignNode, int>();

		using var first = PreviewBuilder.Build(document, new PreviewOptions { Selected = textInSecondTab, ActiveTabs = activeTabs });
		using var second = PreviewBuilder.Build(document, new PreviewOptions { Selected = document.Root, ActiveTabs = activeTabs });

		Assert.Equal(1, ((TabControl)first.GetControl(tabControlNode)!).SelectedIndex);
		Assert.Equal(1, ((TabControl)second.GetControl(tabControlNode)!).SelectedIndex);
	}

	[AvaloniaFact]
	public void A_real_file_with_custom_controls_builds_without_errors()
	{
		string xaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "RobotMainWindow.axaml.txt"));
		var document = DesignXamlReader.Parse(xaml);

		using var result = PreviewBuilder.Build(document);
		var window = new Window { Content = result.Root, Width = 1200, Height = 760 };
		window.Show();
		Dispatcher.UIThread.RunJobs();

		Assert.NotNull(result.Root);
		Assert.Empty(result.Diagnostics.Where(d => d.Level != DiagnosticLevel.Info).Select(d => d.Message));
		Assert.True(result.Objects.Count > 20);
		window.Close();
	}

	[AvaloniaFact]
	public void Every_control_type_of_avalonia_has_metadata()
	{
		foreach (var type in XamlTypes.AllAvaloniaTypes.Where(t => typeof(Control).IsAssignableFrom(t)))
		{
			var meta = XamlTypes.GetMeta(type);
			Assert.NotEmpty(meta.Properties);
			foreach (var property in meta.Properties)
				PropertyClassifier.GetKind(property.ValueType);
		}
		Assert.Equal(typeof(Button), XamlTypes.ResolveAvalonia("Button"));
		Assert.Equal("Content", XamlTypes.GetMeta(typeof(Button)).ContentProperty?.Name);
		Assert.Equal("Children", XamlTypes.GetMeta(typeof(Grid)).ContentProperty?.Name);
		Assert.NotNull(XamlTypes.GetMeta(typeof(TextBlock)).FindProperty("FontSize"));
		Assert.NotNull(XamlTypes.GetMeta(typeof(Button)).FindEvent("Click"));
		Assert.NotNull(XamlTypes.FindAttached(typeof(Grid), "Row", typeof(Button)));
	}

	private static DesignDocument Parse(string content) =>
		DesignXamlReader.Parse($"<UserControl {Header}>{content}</UserControl>");

	private static int Compare(DesignDocument document, DesignNode node, object mine, object theirs, List<string> differences)
	{
		if (mine.GetType() != theirs.GetType())
		{
			differences.Add($"<{node.ElementName}>: we made {mine.GetType().Name}, the loader made {theirs.GetType().Name}");
			return 0;
		}

		int compared = 0;
		var meta = XamlTypes.GetMeta(mine.GetType());
		foreach (var attribute in node.Attributes)
		{
			if (attribute.Name.Contains(':') || attribute.Name == "Classes" || XamlMarkup.IsMarkup(attribute.Value))
				continue;
			PropertyMeta? property;
			int dot = attribute.Name.IndexOf('.');
			if (dot > 0)
			{
				var owner = XamlTypes.ResolveAvalonia(attribute.Name[..dot])!;
				var attached = XamlTypes.FindAttached(owner, attribute.Name[(dot + 1)..], mine.GetType());
				property = attached is null ? null : new PropertyMeta(attached.Name, attached.PropertyType, owner, attached, null, true, true);
			}
			else
			{
				property = meta.FindProperty(attribute.Name);
			}
			if (property is null)
			{
				differences.Add($"<{node.ElementName}> {attribute.Name}: property not found");
				continue;
			}
			object? a = property.GetValue(mine);
			object? b = property.GetValue(theirs);
			compared++;
			if (!Same(a, b))
				differences.Add($"<{node.ElementName}> {attribute.Name}=\"{attribute.Value}\": we have [{Text(a)}], the loader has [{Text(b)}]");
		}

		if (mine is StyledElement mineStyled && theirs is StyledElement theirStyled)
		{
			compared++;
			if (!mineStyled.Classes.Where(c => !c.StartsWith(':')).SequenceEqual(theirStyled.Classes.Where(c => !c.StartsWith(':'))))
				differences.Add($"<{node.ElementName}> Classes differ");
			if (mineStyled.Name != theirStyled.Name)
				differences.Add($"<{node.ElementName}> Name differs");
		}

		var childNodes = node.ContentChildren.ToList();
		if (childNodes.Count > 0 && meta.ContentProperty is { } content)
		{
			var mineChildren = Items(content.GetValue(mine));
			var theirChildren = Items(content.GetValue(theirs));
			if (mineChildren.Count != childNodes.Count || theirChildren.Count != childNodes.Count)
			{
				differences.Add($"<{node.ElementName}>: {childNodes.Count} child elements, we have {mineChildren.Count}, the loader has {theirChildren.Count}");
				return compared;
			}
			for (int i = 0; i < childNodes.Count; i++)
				compared += Compare(document, childNodes[i], mineChildren[i], theirChildren[i], differences);
		}
		return compared;
	}

	private static List<object> Items(object? value) => value switch
	{
		null => new List<object>(),
		string text => new List<object> { text },
		IEnumerable list => list.Cast<object>().ToList(),
		_ => new List<object> { value },
	};

	private static bool Same(object? a, object? b)
	{
		if (Equals(a, b))
			return true;
		if (a is null || b is null)
			return false;
		if (a is Geometry ga && b is Geometry gb)
			return ga.Bounds == gb.Bounds;
		if (a is not string && a is IEnumerable ea && b is IEnumerable eb)
			return ea.Cast<object>().Select(Text).SequenceEqual(eb.Cast<object>().Select(Text));
		return Text(a) == Text(b) && a.GetType().Name.Replace("Immutable", "") == b.GetType().Name.Replace("Immutable", "");
	}

	private static string Text(object? value) => value switch
	{
		null => "null",
		ISolidColorBrush brush => "brush " + brush.Color + " " + brush.Opacity,
		Cursor => "cursor",
		_ => ValueFormatter.Format(value) is { Length: > 0 } text ? text : value.ToString() ?? "",
	};
}
