# Mjolnir Coding Standards
Adapted from OverTheCounter's coding standards for the Mjolnir mod codebase.
These keep the project consistent and predictable without being overly strict.

## General Best Practice
* Review the codebase thoroughly before making changes.

## File and Namespace Structure
* All classes must exist in a logical namespace matching the folder structure.
```C#
namespace ThorHammer { ... }
```

## Naming Conventions
* In general, naming follows the default Jetbrains Rider suggestions.
* **PascalCase** for class names, methods, properties, and non-private fields.
* **camelCase** for local variables and private fields.
* Prefix private fields and internal fields with `_`.
```C#
private int _myInteger;
internal string _name;
private static bool _initialized;
```
* Static readonly fields use PascalCase (e.g., `HardwareShopNames`).
```C#
private static readonly string[] HardwareShopNames = { "Handy Hank's Hardware", "Dan's Hardware" };
```
* Enums do not need to be prefixed with `E`.
* Utilize Enums over strings where possible.
* Utilize existing common naming conventions from the codebase.

## Access Modifiers
* Explicit usage of access modifiers at all times.
* Use `static` on utility classes that hold no instance state.
* Arrow functions (`=>`) are used for simple single-expression methods and properties.
```C#
// property example
public string Name =>
    _npc.FullName;

// method example
public float AddNumbers(float a, float b) =>
    a + b;
```
* Use `readonly` or `const` for immutable values.
```C#
private const float MeleeDamage = 25f;
private static readonly Vector3 DefaultPosition = new(0.35f, -0.3f, 0.5f);
```
* Nullable variables should be declared using `?`.

## Documentation
* All `public` methods and properties should have XML summaries.
```C#
/// <summary>
/// The configured key for summoning lightning from the hammer.
/// </summary>
public static KeyCode LightningKey { get; private set; } = KeyCode.X;
```
* Internal/private members should have summaries when the logic isn't self-evident.

## Conditional Build Compilation
* ThorHammer targets MelonLoader (IL2CPP + Mono). Use `#if IL2CPP` / `#else` for platform-specific logic.
* Wrap and alias `using` statements to provide platform-agnostic support. Consolidate into a single `#if IL2CPP` / `#else` / `#endif` block per file — don't stack adjacent blocks with the same condition.
```C#
#if IL2CPP
using Il2CppScheduleOne.Combat;
using Il2CppScheduleOne.NPCs;
#else
using ScheduleOne.Combat;
using ScheduleOne.NPCs;
#endif
```

## Logging
* Use `Melon<Core>.Logger` for all logging.
* Use appropriate log levels: `Msg` for info, `Warning` for non-fatal issues, `Error` for failures.
```C#
Melon<Core>.Logger.Msg("Mjolnir registered.");
Melon<Core>.Logger.Warning("No thunder AudioClip found in game assets");
Melon<Core>.Logger.Error($"RenderHammerIcon failed: {ex.Message}");
```

## Code Organization
* Group related members together using comments or region separators.
* Organize classes with: constants/static fields first, then instance fields, then public API, then private implementation.
* Keep Il2Cpp game types behind `#if IL2CPP` guards — never expose them unconditionally.

## What **NOT** to Do
* Do not leave unused fields or dead code after refactoring.
* Do not use reflection on IL2CPP types — use direct property/field access instead.
* Do not commit machine-specific paths (LocalPaths.targets is gitignored for this reason).
