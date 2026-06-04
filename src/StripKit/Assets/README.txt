Assets folder — bundled app resources.

Contents
--------
  stripkit.ico   Multi-resolution Windows icon (16/32/48/64/256 px, 32-bit DIB).
                 Wired to <ApplicationIcon> in the .csproj; appears on the exe,
                 the taskbar, and the installer.

  stripkit.png   High-resolution PNG icon (used for the window title bar).

  software_logo.png
                 The StripKit wordmark / brand lockup. Used in the About section
                 and the installer.

Font stack
----------
StripKit uses the system sans-serif fallback chain:
    Verdana, Segoe UI, Arial, sans-serif
No fonts are bundled here. All fonts are resolved from the host OS.
