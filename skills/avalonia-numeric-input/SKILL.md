---
name: avalonia-numeric-input
description: >-
  Bind numeric entry controls in Avalonia 11 to a view model correctly, including
  the nullable trap that bites NumericUpDown. Use when adding a NumericUpDown or
  numeric TextBox to a settings panel, when a numeric value will not two-way bind
  or resets to zero, when a NumericUpDown bound to an int or double misbehaves,
  when you need min/max clamping, step increment, decimal-place formatting, or
  per-field validation with error display, or when deciding between NumericUpDown
  and a TextBox plus converter. Covers NumericUpDown.Value being a nullable decimal
  and how it binds to int and double, FormatString and Increment, Minimum and
  Maximum, validation via ObservableValidator, and a TextBox-plus-converter
  fallback. Triggers on Avalonia NumericUpDown, numeric binding, decimal versus int
  binding, value not updating, NumericUpDown resets, FormatString, Minimum Maximum,
  spinner increment, numeric validation, ObservableValidator, numeric TextBox.
---

# Avalonia Numeric Input

Numeric fields look trivial until a `NumericUpDown` bound to an `int` quietly
stops updating, or snaps to zero when the user clears it. The cause is almost
always one fact people miss: `NumericUpDown.Value` is a **nullable decimal**, and
everything else follows from reconciling that with the `int` or `double` your
view model actually wants to hold.

## Core principle

`NumericUpDown.Value` is `decimal?`. Avalonia's binding will convert between
`decimal?` and your `int`/`double` property automatically, and for a field that
always has a value this is fine. The trouble is the *nullable* edge: when the box
is emptied, `Value` becomes `null`, and `null` cannot be assigned to a
non-nullable `int` — so the binding either rejects the change or resets. Design
for that edge and the control behaves.

## The reliable pattern (most fields)

Bind to the natural type and set `Minimum`, `Maximum`, `Increment`, and a
`FormatString`. The min/max keep the value in range, and a format string stops
integers rendering as `64.00`.

```xml
<!-- An integer frame count -->
<NumericUpDown Value="{Binding FrameCount}"
               Minimum="2" Maximum="512" Increment="1"
               FormatString="0" />

<!-- A one-decimal angle -->
<NumericUpDown Value="{Binding SweepDegrees}"
               Minimum="1" Maximum="360" Increment="5"
               FormatString="0.0" />
```

```csharp
[ObservableProperty] private int _frameCount = 64;       // converts cleanly via min/max
[ObservableProperty] private double _sweepDegrees = 270; // double works the same way
```

`FormatString` uses standard .NET numeric formats: `"0"` for integers, `"0.0"`
for a fixed decimal, `"0.##"` to trim trailing zeros. `Increment` sets the spinner
step; `AllowSpin`, `ShowButtonSpinner`, and `ButtonSpinnerLocation` control the
spinner buttons.

## When you want no nullable risk at all

Two options remove the `decimal?` mismatch entirely:

1. **Hold the value as `decimal`** in the view model and convert where the engine
   needs `int`/`double`. The binding is then `decimal?` to `decimal` with no lossy
   conversion. Slightly more conversion code, zero surprise.
2. **Clamp on the way in.** Keep `int`, but in the property's change hook coerce
   out-of-range or transient values, so a momentary `null`/blank cannot corrupt
   state.

For most tools the reliable pattern above is enough; reach for these only if a
field genuinely misbehaves.

## Per-field validation with error display

Use CommunityToolkit's `ObservableValidator` plus data-annotation attributes;
Avalonia surfaces the error automatically (red border + message) because the
binding reports it through `INotifyDataErrorInfo`.

```csharp
public partial class SettingsViewModel : ObservableValidator
{
    [ObservableProperty]
    [NotifyDataErrorInfo]                       // re-validate on every set
    [Range(2, 512, ErrorMessage = "Frames must be 2–512.")]
    private int _frameCount = 64;
}
```

```xml
<!-- Fluent shows the validation message under the control by default; this is
     only needed if you want to customise where errors render. -->
<NumericUpDown Value="{Binding FrameCount}" Minimum="2" Maximum="512" />
```

Do the validation here, declaratively, rather than hand-rolling checks in the
setter — the attribute path is what lights up the UI error affordance.

## The TextBox-plus-converter fallback

When you need exact control — custom parsing, units like `"440 Hz"`, or culture
handling NumericUpDown does not give you — bind a `TextBox` to a numeric property
through an `IValueConverter`:

```csharp
public sealed class DoubleTextConverter : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c)
        => ((double)(v ?? 0d)).ToString(c);

    public object ConvertBack(object? v, Type t, object? p, CultureInfo c)
        => double.TryParse(v as string, NumberStyles.Any, c, out var d) ? d : 0d;
}
```

```xml
<TextBox Text="{Binding Cutoff, Converter={StaticResource DoubleTextConverter}}" />
```

## Culture gotcha

`NumericUpDown` parses with the current culture, so the decimal separator differs
by locale (`,` vs `.`). If you read or persist numbers as strings yourself, parse
and format with an explicit `CultureInfo` (often `InvariantCulture` for storage)
so a German user's `1,5` and your saved `1.5` do not diverge. Set
`ParsingNumberStyle` on `NumericUpDown` if you need to accept thousands separators.

## Anti-patterns

- Binding `decimal?` to a non-nullable `int`/`double` with no `Minimum` and being
  surprised when clearing the box resets the field to zero.
- Assuming `NumericUpDown.Value` is `double` — it is `decimal?`.
- No `FormatString`, so an integer field shows `64.00` or a double shows a long
  tail of digits.
- Validating in the property setter instead of `ObservableValidator` +
  `[NotifyDataErrorInfo]`, so the UI never shows the error.
- Ignoring culture, so the decimal separator breaks for international users or
  for round-tripping saved values.
- Reaching for the TextBox+converter fallback for every field when plain
  `NumericUpDown` with min/max/format would do.
