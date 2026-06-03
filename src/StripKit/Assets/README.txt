Drop bundled assets here (icons, fonts).

To embed JetBrains Mono instead of relying on it being installed system-wide:
  1. Copy JetBrainsMono-Regular.ttf (and other weights) into this folder.
  2. They are picked up automatically by the <AvaloniaResource Include="Assets/**" /> entry in the .csproj.
  3. Reference it in App.axaml, e.g.:
       <FontFamily x:Key="AppFont">avares://StripKit/Assets/JetBrainsMono-Regular.ttf#JetBrains Mono</FontFamily>
     then set the window FontFamily to {StaticResource AppFont}.

The app currently uses the font fallback chain "JetBrains Mono, Cascadia Code,
Consolas, monospace", which resolves to JetBrains Mono when it is installed.
