using DesktopBuddy.Core;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace DesktopBuddy.App.Views.Settings;

/// Mail-style token field: existing entries show as chips inside the field, followed by the place to type.
/// Return or a comma adds; the x on a chip (or Backspace on an empty field) removes.
/// A problem with what was typed shows as a status line with an icon, and the text stays so it can be fixed.
internal sealed class TokenField : StackPanel
{
    private readonly List<string> _tokens;
    private readonly Func<string, string?> _validate;
    private readonly Action<List<string>> _changed;
    private readonly IDictionary<string, string?> _problems;
    private readonly string _key;
    private readonly TextBox _input;
    private readonly Border _box;
    private readonly TextBlock _note;
    private readonly FrameworkElement _problemRow;
    private TextBlock? _problemText;

    public TokenField(string title, string prompt, string hint, IEnumerable<string> tokens, string glyph,
                      Func<string, string?> validate, Action<List<string>> changed, IDictionary<string, string?> problems, string key)
    {
        _tokens = tokens.ToList();
        _validate = validate;
        _changed = changed;
        _problems = problems;
        _key = key;
        Spacing = 8;

        var header = new Grid { ColumnSpacing = 10 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = Sk.Icon(glyph, 16);
        icon.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(icon);
        var name = Sk.Text(title);
        name.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(name, 1);
        header.Children.Add(name);
        var count = Sk.Caption(_tokens.Count == 0 ? "None yet" : $"{_tokens.Count} added");
        count.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(count, 2);
        header.Children.Add(count);
        Children.Add(header);

        var wrap = new WrapPanel();
        foreach (var token in _tokens)
        {
            var t = token;
            wrap.Children.Add(Sk.Chip(t, null, () => Remove(t), $"Remove {t}", $"{key}_remove_{t}"));
        }

        _input = new TextBox
        {
            PlaceholderText = _tokens.Count == 0 ? prompt : "Add another…",
            MinWidth = 200,
            MinHeight = 26,
            Padding = new Thickness(4, 3, 4, 3),
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            Tag = key + "_input",
            VerticalAlignment = VerticalAlignment.Center,
        };
        _input.Id(key + "_input", title);
        // Draw the field's own border once, around the chips and the text box together.
        foreach (var k in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused", "TextControlBackgroundDisabled",
                                  "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused", "TextControlBorderBrushDisabled" })
        {
            _input.Resources[k] = new SolidColorBrush(Colors.Transparent);
        }
        _input.Resources["TextControlBorderThemeThickness"] = new Thickness(0);
        _input.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(0);
        wrap.Children.Add(_input);

        _box = new Border { Style = Sk.Sty("SkTokenBox"), Child = wrap };
        _box.Tapped += (_, _) => _input.Focus(FocusState.Programmatic);
        _input.GotFocus += (_, _) => _box.Style = Sk.Sty("SkTokenBoxFocused");
        _input.LostFocus += (_, _) =>
        {
            _box.Style = Sk.Sty("SkTokenBox");
            if (_input.Text.Trim().Length > 0) Add();   // typed but not added with Return: add it so nothing is lost
        };
        _input.KeyDown += OnKeyDown;
        _input.TextChanged += (_, _) => { if (_input.Text.Contains(',')) Add(); };
        Children.Add(_box);

        _note = Sk.Caption(hint);
        _problemRow = BuildProblem();
        Children.Add(_note);
        Children.Add(_problemRow);
        ShowProblem(_problems.TryGetValue(_key, out var p) ? p : null);
    }

    private FrameworkElement BuildProblem()
    {
        var row = Sk.StatusText(StatusLevelCaution, "");
        _problemText = (TextBlock)row.Children[1];
        row.Visibility = Visibility.Collapsed;
        return row;
    }

    private const DesktopBuddy.Presentation.Settings.StatusLevel StatusLevelCaution = DesktopBuddy.Presentation.Settings.StatusLevel.Caution;

    private void ShowProblem(string? message)
    {
        _problems[_key] = message;
        if (_problemText is not null) _problemText.Text = message ?? "";
        _problemRow.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
        _note.Visibility = message is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            Add();
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Back && _input.Text.Length == 0 && _tokens.Count > 0)
        {
            _tokens.RemoveAt(_tokens.Count - 1);
            _changed(_tokens);
            e.Handled = true;
        }
    }

    private void Remove(string token)
    {
        _tokens.RemoveAll(x => x == token);
        _changed(_tokens);
    }

    private void Add()
    {
        var parts = _input.Text.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        if (parts.Count == 0) { _input.Text = ""; return; }
        var bad = parts.Select(p => (Part: p, Message: _validate(p))).Where(x => x.Message is not null).ToList();
        var good = parts.Where(p => !bad.Any(b => b.Part == p)).ToList();
        var merged = TokenRules.Merge(string.Join(",", good), _tokens);
        if (bad.Count > 0)
        {
            ShowProblem(bad[0].Message);
            _input.Text = bad[0].Part;               // keep what they typed so it can be corrected
            _input.SelectionStart = _input.Text.Length;
        }
        else
        {
            ShowProblem(null);
            _input.Text = "";
        }
        if (!merged.SequenceEqual(_tokens))
        {
            _tokens.Clear();
            _tokens.AddRange(merged);
            _changed(_tokens);
        }
    }
}
