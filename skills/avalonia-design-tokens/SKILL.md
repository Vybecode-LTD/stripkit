---
name: avalonia-design-tokens
description: >-
  Package a design system (colours, accent, typography, spacing, control styles)
  as a shared reusable Avalonia 11 resource set so every app starts wearing the
  house style. Use when standardizing the look across several Avalonia tools, when
  defining brand tokens like an accent colour or app font in one place, when
  building a dark theme, when control styles should be centralized rather than
  copied per window, or when extracting scattered inline colours and fonts into
  named resources. Covers organizing Colors, SolidColorBrushes and a font family
  as keyed resources, merging a ResourceDictionary into App.Resources, control
  Styles and selectors, theme variants and the Fluent SystemAccentColor override,
  StaticResource versus DynamicResource, and sharing the token file across
  projects. Triggers on Avalonia theme, design tokens, ResourceDictionary, accent
  colour, app font, dark theme, control styles, SystemAccentColor, DynamicResource,
  shared styles.
---

# Avalonia Design Tokens

When you ship several apps that should look like they came from the same studio,
the worst thing you can do is paste the same hex codes and font name into every
window. Define the look **once** as named resources and shared styles, and have
every app reference those names. Re-skinning then means editing one file, and a
new app starts already on-brand.

## Core principle

A "design token" is a named, reusable value — a colour, a brush, a font, a
spacing, a radius — referenced by key rather than by literal. Put the tokens and
the control styles in shared resource files, merge them into the application, and
forbid inline literals in views. The view says `{StaticResource AccentBrush}`,
never `#e8440a`.

## Organise the tokens

Keep raw `Color`s separate from the semantic `SolidColorBrush`es that views
actually bind to. That indirection lets you retune the palette without touching
every usage.

```xml
<!-- Theme/Tokens.axaml : a merged ResourceDictionary -->
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

  <!-- raw palette -->
  <Color x:Key="AccentColor">#FFE8440A</Color>
  <Color x:Key="Surface0">#FF121212</Color>
  <Color x:Key="Surface1">#FF1A1A1A</Color>
  <Color x:Key="TextPrimary">#FFFFFFFF</Color>
  <Color x:Key="TextMuted">#FFB0B0B0</Color>

  <!-- semantic brushes (what views reference) -->
  <SolidColorBrush x:Key="AccentBrush" Color="{StaticResource AccentColor}" />
  <SolidColorBrush x:Key="WindowBackgroundBrush" Color="{StaticResource Surface0}" />
  <SolidColorBrush x:Key="PanelBackgroundBrush" Color="{StaticResource Surface1}" />
  <SolidColorBrush x:Key="TextPrimaryBrush" Color="{StaticResource TextPrimary}" />
  <SolidColorBrush x:Key="TextMutedBrush" Color="{StaticResource TextMuted}" />

  <!-- typography & metrics -->
  <FontFamily x:Key="AppFont">JetBrains Mono, Cascadia Code, Consolas, monospace</FontFamily>
  <x:Double x:Key="SpacingS">6</x:Double>
  <x:Double x:Key="SpacingM">12</x:Double>
  <CornerRadius x:Key="RadiusM">6</CornerRadius>
</ResourceDictionary>
```

## Merge tokens and styles into the app

Tokens go into `Application.Resources`; control styles go into
`Application.Styles`. Also override the Fluent **`SystemAccentColor`** so built-in
controls (sliders, checkboxes, focus rings) adopt the brand accent instead of the
default blue.

```xml
<!-- App.axaml -->
<Application ... RequestedThemeVariant="Dark">
  <Application.Resources>
    <ResourceDictionary>
      <ResourceDictionary.MergedDictionaries>
        <ResourceInclude Source="avares://MyApp/Theme/Tokens.axaml" />
      </ResourceDictionary.MergedDictionaries>

      <!-- make Fluent controls use the brand accent -->
      <Color x:Key="SystemAccentColor">#FFE8440A</Color>
      <Color x:Key="SystemAccentColorDark1">#FFD13C08</Color>
      <Color x:Key="SystemAccentColorDark2">#FFB83507</Color>
      <Color x:Key="SystemAccentColorDark3">#FF9E2E06</Color>
    </ResourceDictionary>
  </Application.Resources>

  <Application.Styles>
    <FluentTheme />
    <StyleInclude Source="avares://MyApp/Theme/Styles.axaml" />
  </Application.Styles>
</Application>
```

## Centralize control styles

Put recurring control styling in `Theme/Styles.axaml`, keyed off element type or a
class selector. Reference tokens inside the styles, so the styles are themeable too.

```xml
<Styles xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

  <Style Selector="Window">
    <Setter Property="Background" Value="{DynamicResource WindowBackgroundBrush}" />
    <Setter Property="FontFamily" Value="{StaticResource AppFont}" />
  </Style>

  <!-- a reusable role, used as <TextBlock Classes="section"> -->
  <Style Selector="TextBlock.section">
    <Setter Property="Foreground" Value="{DynamicResource AccentBrush}" />
    <Setter Property="FontWeight" Value="Bold" />
  </Style>
</Styles>
```

## StaticResource vs DynamicResource

- **`StaticResource`** resolves once when the element loads. Cheaper; correct for
  values that never change at runtime (a fixed font, a constant spacing).
- **`DynamicResource`** re-resolves whenever the keyed resource changes. Use it
  for any token you intend to swap at runtime — i.e. anything that differs between
  theme variants — so a theme switch actually repaints.

Rule of thumb: reference *colour/brush* tokens with `DynamicResource` (so theme
switching works) and truly-fixed scalars with `StaticResource`.

## Theme variants

For light/dark, define each variant's values in `ThemeDictionaries` keyed by
`ThemeVariant`, and Avalonia picks the right set from the active
`RequestedThemeVariant`. Reference those tokens via `DynamicResource` so flipping
the variant restyles the running app without a restart.

## Share across projects

Put `Tokens.axaml` and `Styles.axaml` in a small shared class-library project that
every app references, and include them with that library's `avares://` URI
(`avares://MyCompany.Theme/Tokens.axaml`). One edit there reskins every app. If a
shared project is overkill, copy the two files and keep them in sync deliberately —
but a shared library is the cleaner answer once you have more than two apps.

## Anti-patterns

- Hard-coding hex colours and font names inline on controls instead of referencing
  named tokens — a re-skin then means a find-and-replace across the codebase.
- Copy-pasting the same `Style` blocks into each window rather than centralizing.
- Using `StaticResource` for tokens you intend to theme-swap — the switch won't
  repaint them.
- Not overriding `SystemAccentColor`, so Fluent controls show default blue next to
  your brand accent.
- One giant unstructured style sheet instead of a tokens/styles split, so the
  palette and the component styling can't evolve independently.
- Duplicating the token file across many apps with no shared library and letting
  the copies drift.
