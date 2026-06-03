---
name: dotnet-cli-from-engine
description: >-
  Wrap an existing C#/.NET engine or library as a headless command-line tool with
  System.CommandLine, so the logic that powers a GUI can run in batch or CI
  without a UI. Use when adding a CLI front end to a library, when building a
  console app that parses options and arguments, when you need subcommands, exit
  codes, piping, or progress output, when running a renderer or processor over
  many files unattended, or when packaging a tool as a dotnet global tool. Covers
  keeping the engine pure and the CLI a thin shell, the System.CommandLine model
  of RootCommand, Option and Argument and a handler, parsing and validation,
  non-zero exit codes on failure, progress to stderr while results go to stdout,
  and packaging with PackAsTool. Triggers on System.CommandLine, build a CLI,
  console app, dotnet tool, command line parser, RootCommand, headless, batch
  processing tool, subcommands, exit codes, wrap a library as a CLI.
---

# .NET CLI From an Engine

A pure engine — a renderer, a processor, anything with no UI — is worth far more
once it can also run from the command line, because then it works in scripts, in
CI, and over a folder of files unattended. The discipline that makes this clean
is to treat the CLI as a *thin shell*: it parses input, calls the engine, and
turns the result into output and an exit code. Nothing more.

## Core principle

Keep all logic in the engine; let the CLI only translate between the shell and
the engine. The same engine then backs the GUI and the CLI with no duplicated
behaviour to drift apart. A CLI command body should read like glue: parse → call
engine → write output → return an exit code.

## The System.CommandLine model

`System.CommandLine` gives you a `RootCommand`, typed `Option<T>` and
`Argument<T>` values, optional sub-`Command`s, and a handler that runs when the
line parses. The shape:

```csharp
using System.CommandLine;

var input  = new Option<FileInfo>("--input")  { Description = "Source PNG", Required = true };
var type   = new Option<string>("--type")     { Description = "knob | vfader | hslider" };
var frames = new Option<int>("--frames")      { Description = "Frame count" };
var output = new Option<FileInfo>("--out")    { Description = "Output PNG", Required = true };
type.SetDefaultValue("knob");
frames.SetDefaultValue(64);

var root = new RootCommand("FilmstripForge CLI — render a control filmstrip.");
root.Add(input); root.Add(type); root.Add(frames); root.Add(output);

root.SetHandler((inp, ty, fr, outp) =>
{
    return Render(inp!, ty!, fr, outp!);   // delegate straight to engine glue
}, input, type, frames, output);

return await root.InvokeAsync(args);
```

> **Pin the version and verify the handler API.** `System.CommandLine` has
> changed its handler surface across releases (`SetHandler(...)` in the
> long-lived betas; a `SetAction(ParseResult => ...)` redesign in newer drops).
> Pin an exact version in the csproj and match the snippet above to *that*
> version's API rather than assuming. The conceptual structure — root command,
> typed options/arguments, one handler — is stable; the exact handler signature
> is not.

## Exit codes — the contract with scripts

A CLI's real interface to automation is its exit code. Return `0` on success and
a non-zero code on failure, and never swallow an error into a `0`.

```csharp
static int Render(FileInfo input, string type, int frames, FileInfo output)
{
    try
    {
        if (!input.Exists)
        {
            Console.Error.WriteLine($"Input not found: {input.FullName}");
            return 2;                       // distinct codes aid scripting
        }
        // ... call the pure engine here ...
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
}
```

## stdout vs stderr — keep pipes clean

Results that a downstream tool might consume go to **stdout**; progress, logs, and
errors go to **stderr**. That way `mytool ... | next-step` receives only the data,
even while the user watches progress on screen.

```csharp
Console.Error.WriteLine($"Rendering {frames} frames…");   // progress -> stderr
Console.WriteLine(output.FullName);                        // machine-readable result -> stdout
```

For batch runs, write one result line per item to stdout so the output is
greppable and pipeable. Accept input from stdin when no input argument is given,
so the tool composes in a pipeline.

## Packaging as a dotnet global tool

Make it installable with `dotnet tool install`:

```xml
<PropertyGroup>
  <PackAsTool>true</PackAsTool>
  <ToolCommandName>filmstripforge</ToolCommandName>
  <PackageId>FilmstripForge.Cli</PackageId>
  <Version>1.0.0</Version>
</PropertyGroup>
```

```
dotnet pack -c Release
dotnet tool install --global --add-source ./nupkg FilmstripForge.Cli
filmstripforge --input knob.png --type knob --frames 64 --out knob_64.png
```

## Sharing the engine

Reference the engine project (or its single-file form, e.g. `FilmstripEngine.cs`)
from both the GUI and the CLI projects. The CLI project takes no UI dependency —
no Avalonia, no WPF — so it stays small and runs anywhere `dotnet` does, including
headless CI containers.

## Anti-patterns

- Putting real logic in the command handler instead of the engine — it then
  diverges from the GUI's behaviour.
- Writing progress or logs to stdout, polluting anything that pipes the output.
- Catching an exception and still returning exit code `0` — scripts can't tell it
  failed.
- Not pinning `System.CommandLine`, then pasting a handler snippet whose API does
  not match the installed version.
- Taking a UI-framework dependency in the CLI so it won't run headless.
- Hand-rolling argument parsing (`args[0]`, `args[1]`) instead of typed options —
  no help text, no validation, brittle ordering.
