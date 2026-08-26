# The `contract.ctproj` File Format

The `contract.ctproj` file is the project settings file for the Contract programming language. It lives at the root of a Contract project directory and describes how to build the sources in that folder. A project can be an **executable** (requires a `Main` entry point) or a **library** (no entry point; the compiled module is included by other projects). A project with a `Projects` array acts as a **solution**, coordinating the build of multiple sub-projects.

## Format

- **Filename**: Always `contract.ctproj`
- **Format**: JSON (loosened)
- **Comments**: `//` comments are allowed and ignored
- **Trailing commas**: Allowed
- **Property names**: Case-insensitive

## Fields

All fields are optional. Fields without a default are `null` when omitted.

### `Name`

| | |
|---|---|
| **Type** | `string` |
| **Default** | `"app"` |
| **Description** | Project name. Set to the folder name by default during `ccl new`. |

### `Type`

| | |
|---|---|
| **Type** | `string` |
| **Default** | `"exe"` |
| **Allowed values** | `"exe"` or `"lib"` (case-insensitive) |
| **Description** | Determines build behavior. `"exe"` produces an executable binary (`.orbt`); `"lib"` produces a library module (`.oil`). |

### `Main`

| | |
|---|---|
| **Type** | `string` |
| **Default** | `"src/main.ct"` |
| **Description** | Main source file, relative to the project root. This is the entry point compiled by the compiler. Required for `"exe"` projects. Optional for `"lib"` projects that have a `Sources` array. |

### `Namespace`

| | |
|---|---|
| **Type** | `string \| null` |
| **Default** | `null` |
| **Description** | Namespace applied to new files created by `ccl new`. When `null`, `ccl new` falls back to the lowercased project name. Not used during compilation. |

### `Output`

| | |
|---|---|
| **Type** | `string` |
| **Default** | `"bin"` |
| **Description** | Output directory for compiled modules, relative to the project root. |

### `Version`

| | |
|---|---|
| **Type** | `string \| null` |
| **Default** | `null` (`ccl new` sets it to `"0.1.0"`) |
| **Description** | Semver version string (e.g. `"1.0.0"`). Package metadata for the Purr registry. |

### `Author`

| | |
|---|---|
| **Type** | `string \| null` |
| **Default** | `null` |
| **Description** | Package author name. Metadata for the Purr registry. |

### `Description`

| | |
|---|---|
| **Type** | `string \| null` |
| **Default** | `null` (`ccl new` sets it to `"A Contract application"` or `"A Contract library"`) |
| **Description** | Short description of the project. Metadata for the Purr registry. |

### `License`

| | |
|---|---|
| **Type** | `string \| null` |
| **Default** | `null` |
| **Description** | License identifier (e.g. `"MIT"`, `"GPL-3.0"`). Should be a SPDX identifier. Metadata for the Purr registry. |

### `Tags`

| | |
|---|---|
| **Type** | `string[] \| null` |
| **Default** | `null` (`ccl new` sets it to `["application"]` or `["library"]`) |
| **Description** | Tags for Purr registry search (e.g. `["library", "gui"]`). |

### `Dependencies`

| | |
|---|---|
| **Type** | `PackageDependency[] \| null` |
| **Default** | `null` |
| **Description** | Package dependencies from the Purr registry. Add with `ccl install <pkg>`, remove with `ccl remove <pkg>`. |

Each entry is a `PackageDependency` object:

#### `PackageDependency`

| Field | Type | Default | Description |
|---|---|---|---|
| `Name` | `string` | `""` | Package name on the Purr registry (e.g. `"ObjektRT"`). |
| `Version` | `string` | `"*"` | Semver version range. `"*"` or empty string means latest. |

### `Projects`

| | |
|---|---|
| **Type** | `string[] \| null` |
| **Default** | `null` |
| **Description** | Sub-project paths, relative to this project's root. When present, this project acts as a **solution**: each sub-project is built in dependency order (topologically sorted by analyzing import statements). Mutually exclusive with `Sources`. |

Each entry can be either a directory path (e.g. `"LibA"`, in which case `contract.ctproj` is looked for inside) or a direct path to a `.ctproj` file (e.g. `"LibB/contract.ctproj"`).

Circular dependencies throw an error during build.

### `Sources`

| | |
|---|---|
| **Type** | `string[] \| null` |
| **Default** | `null` |
| **Description** | Source file globs for single-project multi-file builds (e.g. `["*.ct"]` or `["src/**/*.ct"]`). All matching `.ct` files are compiled together into one output module. Mutually exclusive with `Projects`. For `"lib"` projects, `Sources` can be used without a `Main` file; the output filename is derived from `Name`. For `"exe"` projects, `Main` is still required. |

## Examples

### Minimal library

```json
{
  "Name": "LibA",
  "Type": "lib",
  "Main": "src/main.ct",
  "Output": "bin"
}
```

### Library with only `Sources` (no main file)

```json
{
  "Name": "MyLib",
  "Type": "lib",
  "Sources": ["*.ct"],
  "Output": "bin"
}
```

The compiler compiles every `.ct` file in the project root and emits `bin/MyLib.oil`. `Main` is not required.

### Full solution with sub-projects

```json
{
  "Name": "MultiProjectSolution",
  "Type": "lib",
  "Main": "src/main.ct",
  "Namespace": null,
  "Output": "bin",
  "Version": null,
  "Author": null,
  "Description": null,
  "License": null,
  "Tags": null,
  "Dependencies": null,
  "Projects": [
    "LibB/contract.ctproj",
    "App/contract.ctproj",
    "LibA"
  ],
  "Sources": null
}
```

### Executable with dependencies

```json
{
  "Name": "MyApp",
  "Type": "exe",
  "Main": "src/main.ct",
  "Output": "bin",
  "Version": "1.2.0",
  "Author": "Charlie",
  "Description": "My Contract application",
  "License": "MIT",
  "Tags": ["application", "cli"],
  "Dependencies": [
    { "Name": "ObjektRT", "Version": "1.0.0" },
    { "Name": "SomeLib", "Version": "*" }
  ]
}
```
