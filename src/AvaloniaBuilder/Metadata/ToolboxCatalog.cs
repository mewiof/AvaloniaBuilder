using AvaloniaBuilder.Model;
using AvaloniaBuilder.Xaml;

namespace AvaloniaBuilder.Metadata;

public sealed record ToolboxItem(string Name, string Category, string Xaml, string Description)
{
	public List<DesignNode> Create(DesignDocument document, out List<XamlNamespace> namespaces) =>
		DesignXamlReader.ParseFragment(Xaml, document, out namespaces);
}

public static class ToolboxCatalog
{
	public const string Layout = "Layout";
	public const string Containers = "Containers";
	public const string Text = "Text";
	public const string Buttons = "Buttons";
	public const string Values = "Values";
	public const string Lists = "Lists and menus";
	public const string Dates = "Date and time";
	public const string Shapes = "Shapes and images";
	public const string ReadyMade = "Ready-made";

	public static IReadOnlyList<string> Categories { get; } = new[]
	{
		ReadyMade, Layout, Containers, Text, Buttons, Values, Lists, Dates, Shapes,
	};

	public static IReadOnlyList<ToolboxItem> Items { get; } = Build();

	private static List<ToolboxItem> Build()
	{
		var items = new List<ToolboxItem>();
		void Add(string category, string name, string description, string xaml) =>
			items.Add(new ToolboxItem(name, category, xaml, description));

		Add(Layout, "Grid", "Rows and columns. The most flexible panel.",
			"<Grid />");
		Add(Layout, "Grid 2 × 2", "A grid with two rows and two columns.",
			"<Grid RowDefinitions=\"*,*\" ColumnDefinitions=\"*,*\" />");
		Add(Layout, "StackPanel", "Children one below the other.",
			"<StackPanel Spacing=\"8\" />");
		Add(Layout, "StackPanel (horizontal)", "Children side by side.",
			"<StackPanel Orientation=\"Horizontal\" Spacing=\"8\" />");
		Add(Layout, "DockPanel", "Children stick to an edge. The last child fills the rest.",
			"<DockPanel />");
		Add(Layout, "WrapPanel", "Children side by side. A full row continues on the next row.",
			"<WrapPanel />");
		Add(Layout, "UniformGrid", "A grid where every cell has the same size.",
			"<UniformGrid Columns=\"2\" />");
		Add(Layout, "Canvas", "Children at fixed x and y positions.",
			"<Canvas />");
		Add(Layout, "Border", "A frame around one child, with a background and round corners.",
			"<Border BorderBrush=\"Gray\" BorderThickness=\"1\" CornerRadius=\"4\" Padding=\"8\" />");
		Add(Layout, "ScrollViewer", "Scroll bars around one child.",
			"<ScrollViewer />");
		Add(Layout, "Viewbox", "Scales one child to the free space.",
			"<Viewbox />");
		Add(Layout, "GridSplitter", "A bar that the user drags to resize grid rows or columns.",
			"<GridSplitter Width=\"4\" ResizeDirection=\"Columns\" />");
		Add(Layout, "Separator", "A thin line between things.",
			"<Separator />");

		Add(Containers, "TabControl", "Pages with tabs. It comes with two tabs.",
			"<TabControl><TabItem Header=\"Tab 1\"><Grid /></TabItem><TabItem Header=\"Tab 2\"><Grid /></TabItem></TabControl>");
		Add(Containers, "TabItem", "One more tab. Drop it on a TabControl.",
			"<TabItem Header=\"Tab\"><Grid /></TabItem>");
		Add(Containers, "Expander", "A header that opens and closes its content.",
			"<Expander Header=\"Expander\" IsExpanded=\"True\"><StackPanel Spacing=\"8\" /></Expander>");
		Add(Containers, "GroupBox", "A frame with a title.",
			"<GroupBox Header=\"Group\"><StackPanel Spacing=\"8\" /></GroupBox>");
		Add(Containers, "SplitView", "A side pane that can open and close, next to the main content.",
			"<SplitView IsPaneOpen=\"True\" DisplayMode=\"Inline\" OpenPaneLength=\"200\"><SplitView.Pane><StackPanel Spacing=\"8\" Margin=\"8\" /></SplitView.Pane><Grid /></SplitView>");
		Add(Containers, "ContentControl", "Shows one object. With a view model as content, Avalonia finds the view for it.",
			"<ContentControl />");

		Add(Text, "TextBlock", "Text that the user cannot change.",
			"<TextBlock Text=\"TextBlock\" />");
		Add(Text, "Heading", "A TextBlock with big, bold text.",
			"<TextBlock Text=\"Heading\" FontSize=\"20\" FontWeight=\"SemiBold\" />");
		Add(Text, "Label", "Text that belongs to another control.",
			"<Label Content=\"Label\" />");
		Add(Text, "TextBox", "One line of text that the user can change.",
			"<TextBox PlaceholderText=\"Type here\" />");
		Add(Text, "TextBox (many lines)", "A text box that takes Enter and wraps long lines.",
			"<TextBox AcceptsReturn=\"True\" TextWrapping=\"Wrap\" Height=\"80\" />");
		Add(Text, "Password box", "A text box that hides what the user types.",
			"<TextBox PasswordChar=\"●\" PlaceholderText=\"Password\" />");
		Add(Text, "NumericUpDown", "A number with up and down buttons.",
			"<NumericUpDown Value=\"0\" Minimum=\"0\" Maximum=\"100\" />");
		Add(Text, "AutoCompleteBox", "A text box that suggests items while the user types.",
			"<AutoCompleteBox />");
		Add(Text, "SelectableTextBlock", "Text that the user can select and copy.",
			"<SelectableTextBlock Text=\"Text that you can select\" />");

		Add(Buttons, "Button", "Runs a command when the user clicks it.",
			"<Button Content=\"Button\" />");
		Add(Buttons, "Button (accent)", "A button in the accent color, for the main action.",
			"<Button Classes=\"accent\" Content=\"Button\" />");
		Add(Buttons, "CheckBox", "On or off.",
			"<CheckBox Content=\"CheckBox\" />");
		Add(Buttons, "RadioButton", "One choice from a group.",
			"<RadioButton Content=\"RadioButton\" GroupName=\"Group1\" />");
		Add(Buttons, "ToggleSwitch", "On or off, as a switch.",
			"<ToggleSwitch OnContent=\"On\" OffContent=\"Off\" />");
		Add(Buttons, "ToggleButton", "A button that stays pressed.",
			"<ToggleButton Content=\"Toggle\" />");
		Add(Buttons, "RepeatButton", "Fires again and again while the user holds it.",
			"<RepeatButton Content=\"Repeat\" />");
		Add(Buttons, "HyperlinkButton", "Opens a web address.",
			"<HyperlinkButton Content=\"Link\" NavigateUri=\"https://avaloniaui.net\" />");
		Add(Buttons, "DropDownButton", "A button that opens a menu.",
			"<DropDownButton Content=\"Menu\" />");
		Add(Buttons, "SplitButton", "A button with a second part that opens a menu.",
			"<SplitButton Content=\"Split\" />");

		Add(Values, "Slider", "The user drags a thumb to pick a number.",
			"<Slider Minimum=\"0\" Maximum=\"100\" Value=\"50\" />");
		Add(Values, "ProgressBar", "Shows how far a task is.",
			"<ProgressBar Minimum=\"0\" Maximum=\"100\" Value=\"50\" />");
		Add(Values, "ProgressBar (busy)", "A bar that moves all the time: the end is not known.",
			"<ProgressBar IsIndeterminate=\"True\" />");

		Add(Lists, "ListBox", "A list where the user selects an item. It comes with three items.",
			"<ListBox><ListBoxItem Content=\"Item 1\" /><ListBoxItem Content=\"Item 2\" /><ListBoxItem Content=\"Item 3\" /></ListBox>");
		Add(Lists, "ListBox (bound)", "A list that takes its items from the view model.",
			"<ListBox ItemsSource=\"{Binding Items}\" SelectedItem=\"{Binding SelectedItem}\" />");
		Add(Lists, "ComboBox", "A drop-down list. It comes with three items.",
			"<ComboBox SelectedIndex=\"0\"><ComboBoxItem Content=\"Item 1\" /><ComboBoxItem Content=\"Item 2\" /><ComboBoxItem Content=\"Item 3\" /></ComboBox>");
		Add(Lists, "ComboBox (bound)", "A drop-down list that takes its items from the view model.",
			"<ComboBox ItemsSource=\"{Binding Options}\" SelectedItem=\"{Binding SelectedOption}\" />");
		Add(Lists, "ItemsControl (bound)", "Shows one piece of UI for each item of a list. No selection.",
			"<ItemsControl ItemsSource=\"{Binding Items}\" />");
		Add(Lists, "TreeView", "Items with child items.",
			"<TreeView><TreeViewItem Header=\"Root\" IsExpanded=\"True\"><TreeViewItem Header=\"Child 1\" /><TreeViewItem Header=\"Child 2\" /></TreeViewItem></TreeView>");
		Add(Lists, "Menu", "A menu bar. It comes with File and Edit.",
			"<Menu><MenuItem Header=\"_File\"><MenuItem Header=\"_Open\" /><MenuItem Header=\"_Save\" /><Separator /><MenuItem Header=\"E_xit\" /></MenuItem><MenuItem Header=\"_Edit\"><MenuItem Header=\"_Undo\" /><MenuItem Header=\"_Redo\" /></MenuItem></Menu>");
		Add(Lists, "MenuItem", "One more menu entry. Drop it on a Menu or on a MenuItem.",
			"<MenuItem Header=\"Item\" />");
		Add(Lists, "ListBoxItem", "One more item. Drop it on a ListBox.",
			"<ListBoxItem Content=\"Item\" />");
		Add(Lists, "ComboBoxItem", "One more item. Drop it on a ComboBox.",
			"<ComboBoxItem Content=\"Item\" />");
		Add(Lists, "TreeViewItem", "One more tree item. Drop it on a TreeView or on a TreeViewItem.",
			"<TreeViewItem Header=\"Item\" />");

		Add(Dates, "DatePicker", "Day, month and year in three wheels.",
			"<DatePicker />");
		Add(Dates, "TimePicker", "Hours and minutes in wheels.",
			"<TimePicker />");
		Add(Dates, "CalendarDatePicker", "A text box that opens a calendar.",
			"<CalendarDatePicker />");
		Add(Dates, "Calendar", "A month calendar.",
			"<Calendar />");

		Add(Shapes, "Rectangle", "A rectangle.",
			"<Rectangle Width=\"100\" Height=\"60\" Fill=\"SteelBlue\" />");
		Add(Shapes, "Ellipse", "A circle or an ellipse.",
			"<Ellipse Width=\"60\" Height=\"60\" Fill=\"DarkOrange\" />");
		Add(Shapes, "Line", "A straight line.",
			"<Line StartPoint=\"0,0\" EndPoint=\"100,0\" Stroke=\"Gray\" StrokeThickness=\"2\" />");
		Add(Shapes, "Path", "A free shape, made from path commands.",
			"<Path Data=\"M 0,20 L 20,0 L 40,20 L 30,20 L 30,40 L 10,40 L 10,20 Z\" Fill=\"SeaGreen\" />");
		Add(Shapes, "Image", "A picture from a file.",
			"<Image Width=\"100\" Height=\"100\" Stretch=\"Uniform\" />");

		Add(ReadyMade, "Label + text box", "A caption with a text box below it. The text is bound.",
			"<StackPanel Spacing=\"4\"><TextBlock Text=\"Name\" /><TextBox Text=\"{Binding Name}\" /></StackPanel>");
		Add(ReadyMade, "Label + slider + value", "A caption, a slider and the number. All three are bound.",
			"<Grid ColumnDefinitions=\"Auto,*,Auto\" ColumnSpacing=\"8\"><TextBlock Text=\"Speed\" VerticalAlignment=\"Center\" /><Slider Grid.Column=\"1\" Minimum=\"0\" Maximum=\"100\" Value=\"{Binding Speed}\" /><TextBlock Grid.Column=\"2\" Text=\"{Binding Speed, StringFormat={}{0:F1}}\" VerticalAlignment=\"Center\" MinWidth=\"40\" TextAlignment=\"Right\" /></Grid>");
		Add(ReadyMade, "OK and Cancel", "Two buttons on the right, with commands.",
			"<StackPanel Orientation=\"Horizontal\" Spacing=\"8\" HorizontalAlignment=\"Right\"><Button Classes=\"accent\" Content=\"OK\" IsDefault=\"True\" Command=\"{Binding OkCommand}\" /><Button Content=\"Cancel\" IsCancel=\"True\" Command=\"{Binding CancelCommand}\" /></StackPanel>");
		Add(ReadyMade, "Button row", "Three buttons side by side, with commands.",
			"<StackPanel Orientation=\"Horizontal\" Spacing=\"8\"><Button Content=\"Start\" Command=\"{Binding StartCommand}\" /><Button Content=\"Home\" Command=\"{Binding HomeCommand}\" /><Button Content=\"Stop\" Command=\"{Binding StopCommand}\" /></StackPanel>");
		Add(ReadyMade, "Form with 3 rows", "A grid with captions on the left and inputs on the right.",
			"<Grid ColumnDefinitions=\"Auto,*\" RowDefinitions=\"Auto,Auto,Auto\" ColumnSpacing=\"12\" RowSpacing=\"8\"><TextBlock Text=\"First name\" VerticalAlignment=\"Center\" /><TextBox Grid.Column=\"1\" Text=\"{Binding FirstName}\" /><TextBlock Grid.Row=\"1\" Text=\"Last name\" VerticalAlignment=\"Center\" /><TextBox Grid.Row=\"1\" Grid.Column=\"1\" Text=\"{Binding LastName}\" /><TextBlock Grid.Row=\"2\" Text=\"Active\" VerticalAlignment=\"Center\" /><CheckBox Grid.Row=\"2\" Grid.Column=\"1\" IsChecked=\"{Binding IsActive}\" /></Grid>");
		Add(ReadyMade, "Value read-out", "Caption and value in two columns, for numbers that the app measures.",
			"<Grid ColumnDefinitions=\"90,Auto\" RowDefinitions=\"Auto,Auto\"><TextBlock Text=\"target\" /><TextBlock Grid.Column=\"1\" Text=\"{Binding Target, StringFormat={}{0:F1}}\" /><TextBlock Grid.Row=\"1\" Text=\"measured\" /><TextBlock Grid.Row=\"1\" Grid.Column=\"1\" Text=\"{Binding Measured, StringFormat={}{0:F1}}\" /></Grid>");
		Add(ReadyMade, "Card", "A frame with a title and a text.",
			"<Border BorderBrush=\"Gray\" BorderThickness=\"1\" CornerRadius=\"8\" Padding=\"12\"><StackPanel Spacing=\"6\"><TextBlock Text=\"Title\" FontSize=\"16\" FontWeight=\"SemiBold\" /><TextBlock Text=\"Some text about this card.\" TextWrapping=\"Wrap\" /></StackPanel></Border>");
		Add(ReadyMade, "Status pill", "A small colored label. Good for a state like \"Enabled\".",
			"<Border Background=\"SeaGreen\" CornerRadius=\"10\" Padding=\"10,3\" HorizontalAlignment=\"Left\"><TextBlock Text=\"{Binding Status}\" Foreground=\"White\" FontSize=\"12\" /></Border>");
		Add(ReadyMade, "Status bar", "A thin bar with a text. Dock it to the bottom of a DockPanel.",
			"<Border DockPanel.Dock=\"Bottom\" BorderBrush=\"Gray\" BorderThickness=\"0,1,0,0\" Padding=\"8,4\"><TextBlock Text=\"{Binding StatusText}\" FontSize=\"12\" /></Border>");
		Add(ReadyMade, "Bound list with template", "A list that shows two values of each item. Change the template to your needs.",
			"<ItemsControl ItemsSource=\"{Binding Entries}\"><ItemsControl.ItemTemplate><DataTemplate><StackPanel Orientation=\"Horizontal\" Spacing=\"8\"><TextBlock Text=\"{Binding Name}\" FontWeight=\"SemiBold\" /><TextBlock Text=\"{Binding Value}\" /></StackPanel></DataTemplate></ItemsControl.ItemTemplate></ItemsControl>");
		Add(ReadyMade, "Your own control", "A place for a control from your own code. The preview shows a gray box.",
			"<local:MyControl />");

		return items;
	}
}
