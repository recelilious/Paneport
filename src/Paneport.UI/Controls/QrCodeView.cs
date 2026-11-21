// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Net.Codecrete.QrCodeGenerator;

namespace Paneport.UI.Controls;

/// <summary>
/// Draws a text as a QR code (S2.3.10, plan decision D13): the Steam sign-in's challenge, which the
/// Steam mobile app on another device scans (C-0007 section 3). It encodes the text again whenever
/// the text changes, and never shows the text itself.
/// </summary>
/// <remarks>
/// Net.Codecrete.QrCodeGenerator encodes the text at error correction level M. The control draws the
/// dark modules black on white, whatever the theme, inside the quiet zone of four modules that the
/// QR code standard asks for, with whole pixels per module so that the code stays sharp.
/// </remarks>
public sealed class QrCodeView : Control
{
    /// <summary>The width of the quiet zone around the code, in modules.</summary>
    public const int QuietZone = 4;

    /// <summary>Defines the <see cref="Text"/> property.</summary>
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<QrCodeView, string?>(nameof(Text));

    private const double PreferredSize = 264;

    private QrCode? _code;

    static QrCodeView()
    {
        AffectsRender<QrCodeView>(TextProperty);
    }

    /// <summary>Gets or sets the text that the code encodes; null or empty draws nothing.</summary>
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Gets the number of modules along one side of the code, or 0 when there is none.</summary>
    public int ModuleCount => _code?.Size ?? 0;

    /// <summary>Gets how many times the control encoded a text, which the tests read to see a redraw.</summary>
    public int Encodings { get; private set; }

    /// <summary>Gets whether the module at a column and a row is dark.</summary>
    /// <param name="x">The column, from 0.</param>
    /// <param name="y">The row, from 0.</param>
    /// <returns>Whether the module is dark; false outside the code or when there is none.</returns>
    public bool IsDark(int x, int y) => _code is { } code && code.GetModule(x, y);

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_code is not { } code)
        {
            return;
        }

        var side = Math.Min(Bounds.Width, Bounds.Height);
        var modules = code.Size + (2 * QuietZone);
        var moduleSize = Math.Max(1, Math.Floor(side / modules));
        var origin = Math.Floor((side - (moduleSize * modules)) / 2);
        context.FillRectangle(Brushes.White, new Rect(origin, origin, moduleSize * modules, moduleSize * modules));
        for (var y = 0; y < code.Size; y++)
        {
            for (var x = 0; x < code.Size; x++)
            {
                if (code.GetModule(x, y))
                {
                    var left = origin + ((x + QuietZone) * moduleSize);
                    var top = origin + ((y + QuietZone) * moduleSize);
                    context.FillRectangle(Brushes.Black, new Rect(left, top, moduleSize, moduleSize));
                }
            }
        }
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
        {
            var text = change.GetNewValue<string?>();
            _code = string.IsNullOrEmpty(text) ? null : QrCode.EncodeText(text, QrCode.Ecc.Medium);
            Encodings++;
        }
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var side = Math.Min(PreferredSize, Math.Min(availableSize.Width, availableSize.Height));
        return double.IsInfinity(side) ? new Size(PreferredSize, PreferredSize) : new Size(side, side);
    }
}
