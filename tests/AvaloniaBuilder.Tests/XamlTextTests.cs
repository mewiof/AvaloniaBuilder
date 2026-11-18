using AvaloniaBuilder.Model;
using AvaloniaBuilder.Xaml;

namespace AvaloniaBuilder.Tests;

public class XamlTextTests
{
	private const string Simple = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                x:Class="MyApp.Views.MainWindow"
                Title="Hello">
            <!-- The main layout. -->
            <Grid RowDefinitions="Auto,*">
                <TextBlock Text="Title" />

                <Button x:Name="OkButton" Grid.Row="1" Content="OK" />
            </Grid>
        </Window>

        """;

	[Fact]
	public void Reading_gives_the_element_tree()
	{
		var document = DesignXamlReader.Parse(Simple);

		Assert.Equal("Window", document.Root.TypeName);
		Assert.Equal("Hello", document.Root.Get("Title"));
		Assert.Equal("MyApp.Views.MainWindow", document.ViewClass);
		var grid = Assert.Single(document.Root.Children);
		Assert.Equal("Grid", grid.TypeName);
		Assert.Equal(new[] { " The main layout. " }, grid.Comments);
		Assert.Equal(2, grid.Children.Count);
		Assert.Equal("OkButton", grid.Children[1].Name);
		Assert.Equal("1", grid.Children[1].Get("Grid.Row"));
		Assert.True(grid.Children[1].BlankLineBefore);
		Assert.Equal(XamlNs.Xaml, document.GetNamespaceUri("x"));
	}

	[Fact]
	public void Writing_what_was_read_gives_the_same_text()
	{
		var document = DesignXamlReader.Parse(Simple);

		string written = DesignXamlWriter.Write(document);

		Assert.Equal(Simple.ReplaceLineEndings("\n"), written);
	}

	[Fact]
	public void A_real_file_survives_read_and_write_without_losing_anything()
	{
		string original = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "RobotMainWindow.axaml.txt"));

		var first = DesignXamlReader.Parse(original);
		string written = DesignXamlWriter.Write(first);
		var second = DesignXamlReader.Parse(written);
		string writtenAgain = DesignXamlWriter.Write(second);

		Assert.Equal(written, writtenAgain);
		Assert.Equal(Describe(first.Root), Describe(second.Root));
		Assert.Equal(first.Namespaces, second.Namespaces);
		Assert.Equal(first.Root.SelfAndDescendants().Sum(n => n.Comments.Count), second.Root.SelfAndDescendants().Sum(n => n.Comments.Count));
		Assert.Contains("ctl:ArmView", written);
	}

	[Fact]
	public void Long_start_tags_get_one_attribute_per_line()
	{
		var button = new DesignNode("Button");
		button.Set("Content", "A very long caption for a button, so the line gets long");
		button.Set("Command", "{Binding DoSomethingVeryImportantCommand}");
		button.Set("HorizontalAlignment", "Stretch");
		var root = new DesignNode("UserControl");
		root.Add(button);

		string text = DesignXamlWriter.Write(new DesignDocument(root));

		string[] lines = text.Split('\n');
		Assert.Contains(lines, line => line == "    <Button Content=\"A very long caption for a button, so the line gets long\"");
		Assert.Contains(lines, line => line == "            HorizontalAlignment=\"Stretch\"");
		Assert.Contains(lines, line => line == "            Command=\"{Binding DoSomethingVeryImportantCommand}\" />");
	}

	[Fact]
	public void Special_characters_are_escaped_and_come_back()
	{
		var text = new DesignNode("TextBlock");
		text.Set("Text", "a < b & \"c\"\nnext line");
		var root = new DesignNode("UserControl");
		root.Add(text);

		string written = DesignXamlWriter.Write(new DesignDocument(root));
		var back = DesignXamlReader.Parse(written);

		Assert.Contains("a &lt; b &amp; &quot;c&quot;&#10;next line", written);
		Assert.Equal("a < b & \"c\"\nnext line", back.Root.Children[0].Get("Text"));
	}

	[Fact]
	public void New_attributes_go_to_a_tidy_place()
	{
		var button = new DesignNode("Button");
		button.Set("Margin", "8");
		button.Set("Command", "{Binding SaveCommand}");
		button.Set("Content", "Save");
		button.Set("Grid.Row", "1");
		button.Set("x:Name", "SaveButton");

		Assert.Equal(new[] { "x:Name", "Grid.Row", "Content", "Margin", "Command" }, button.Attributes.Select(a => a.Name));
	}

	[Fact]
	public void Invalid_xml_reports_the_line()
	{
		var error = Assert.Throws<XamlReadException>(() => DesignXamlReader.Parse("<Window>\n  <Grid>\n</Window>"));

		Assert.Equal(3, error.Line);
	}

	[Fact]
	public void A_fragment_can_use_the_prefixes_of_the_document()
	{
		var document = DesignXamlReader.Parse(Simple);

		var nodes = DesignXamlReader.ParseFragment("<Button x:Name=\"A\" /><vm:Thing />", document, out var declared);

		Assert.Equal(2, nodes.Count);
		Assert.Equal("A", nodes[0].Name);
		Assert.Equal("vm", nodes[1].Prefix);
		Assert.Contains(declared, ns => ns.Prefix == "vm");
	}

	[Fact]
	public void Text_inside_an_element_is_kept()
	{
		var document = DesignXamlReader.Parse("<UserControl xmlns=\"https://github.com/avaloniaui\"><TextBlock>  Hello\n   world </TextBlock></UserControl>");

		Assert.Equal("Hello world", document.Root.Children[0].Text);
		Assert.Contains("<TextBlock>Hello world</TextBlock>", DesignXamlWriter.Write(document));
	}

	[Fact]
	public void Text_mixed_with_elements_becomes_runs()
	{
		var document = DesignXamlReader.Parse("<UserControl xmlns=\"https://github.com/avaloniaui\"><TextBlock>Hello <Run FontWeight=\"Bold\">big</Run> world</TextBlock></UserControl>");

		var block = document.Root.Children[0];
		Assert.Equal(new[] { "Run", "Run", "Run" }, block.Children.Select(c => c.TypeName));
		Assert.Equal("Hello ", block.Children[0].Get("Text"));
		Assert.Equal(" world", block.Children[2].Get("Text"));
	}

	[Theory]
	[InlineData("{Binding Speed}", "Binding", "Speed", null)]
	[InlineData("{Binding Path=Speed, Mode=TwoWay}", "Binding", "Speed", "TwoWay")]
	[InlineData("{Binding Speed, StringFormat={}{0:F1} m/s, Mode=OneWay}", "Binding", "Speed", "OneWay")]
	[InlineData("{Binding}", "Binding", "", null)]
	[InlineData("{Binding !IsBusy}", "Binding", "!IsBusy", null)]
	[InlineData("{Binding Items[0].Name}", "Binding", "Items[0].Name", null)]
	public void Bindings_are_parsed(string value, string name, string path, string? mode)
	{
		var markup = XamlMarkup.Parse(value);

		Assert.NotNull(markup);
		Assert.Equal(name, markup.Name);
		Assert.Equal(path, markup.BindingPath);
		Assert.Equal(mode, markup.Get("Mode"));
	}

	[Theory]
	[InlineData("Hello")]
	[InlineData("{}{0:F1}")]
	[InlineData("{Binding Speed")]
	[InlineData("{A}{B}")]
	public void Plain_text_and_broken_braces_are_not_markup(string value)
	{
		Assert.Null(XamlMarkup.Parse(value));
	}

	[Fact]
	public void Nested_markup_is_one_value()
	{
		var markup = XamlMarkup.Parse("{Binding Drives, Converter={StaticResource DriveStateBrush}, ConverterParameter='a, b'}");

		Assert.NotNull(markup);
		Assert.Equal("{StaticResource DriveStateBrush}", markup.Get("Converter"));
		Assert.Equal("'a, b'", markup.Get("ConverterParameter"));
	}

	private static string Describe(DesignNode node)
	{
		var lines = new List<string>();
		void Walk(DesignNode n, int depth)
		{
			lines.Add(new string(' ', depth) + n.ElementName + " " + string.Join(" ", n.Attributes.Select(a => a.ToString())) + " |" + n.Text);
			foreach (var child in n.Children)
				Walk(child, depth + 1);
		}
		Walk(node, 0);
		return string.Join("\n", lines);
	}
}
