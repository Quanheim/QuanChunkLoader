# Developing a Valheim plugin

This guide takes you from a bare Windows machine to a built, tested and packaged plugin. It assumes no
previous experience of building a DLL.

The setup is **one Git repo per mod**, each created from the `valheim-plugin-template` repo, and **one Gale
profile per mod**, holding only BepInExPack, Jotunn, that mod and its real dependencies.

## 0. What the pieces are

| Piece | What it does | Where the build gets it |
|---|---|---|
| **BepInEx** | The mod loader. It injects into Valheim at launch and loads every DLL in `BepInEx\plugins`. Your plugin is a class inheriting `BaseUnityPlugin`. | `BepInEx\core\BepInEx.dll` in the mod's Gale profile. |
| **Harmony** (HarmonyX) | Patches game methods at runtime. It's already referenced, ready for mods that need it. | `BepInEx\core\0Harmony.dll`, which ships inside BepInEx. |
| **Jotunn** | A library for adding content (pieces, items, prefabs) safely. It also handles the network version check. | `BepInEx\plugins\ValheimModding-Jotunn\Jotunn.dll` in the mod's Gale profile. |
| **Game assemblies** | `assembly_valheim.dll` (game code such as `Piece`) and Unity's `UnityEngine*.dll`. | Your Valheim install. |

You **compile against** all of these but **ship none of them**. The references are marked `Private=false`, so
they aren't copied next to your DLL. Never commit them to a repo either: `assembly_valheim.dll` is Iron Gate's
code.

The plugin targets **.NET Framework 4.6.2** (`net462`), because that's what Valheim's runtime and Jotunn expect.
You don't install .NET Framework yourself. The `Microsoft.NETFramework.ReferenceAssemblies` package lets the
modern .NET SDK compile for it.

## 1. Install the tools (once per machine)

1. **Git for Windows**: <https://git-scm.com/download/win>. The default install options are fine.
2. **.NET SDK**, current LTS: <https://dotnet.microsoft.com/download>. Get the **SDK**, not just the runtime.
3. **VS Code**: <https://code.visualstudio.com/>.
4. The VS Code extension **C# Dev Kit** (`ms-dotnettools.csdevkit`). Each repo's `.vscode/extensions.json`
   prompts for it.

Check everything in a new terminal:

```powershell
git --version
dotnet --version
```

## 2. Create the mod's Gale profile (once per mod)

Each mod gets its own profile, so a test contains only that mod and what it really depends on. **Never
develop against `Default`**: your server's `BepInEx` folder is a junction into it.

1. In Gale, create a profile named after the mod, e.g. `QuanDupeLightsources`.
2. Install **Jotunn**, which pulls in BepInExPack, plus any mods yours depends on or is tested alongside.
3. Launch Valheim once from that profile and quit, so BepInEx creates its folders and config.
4. The profile folder is `%APPDATA%\com.kesomannen.gale\valheim\profiles\<ProfileName>`. Check that it contains:
   - `changelog.txt`, which is BepInExPack's and is where the build reads the pack version
   - `BepInEx\core\BepInEx.dll` and `BepInEx\core\0Harmony.dll`
   - `BepInEx\plugins\ValheimModding-Jotunn\Jotunn.dll` and `manifest.json`
5. Optional, but useful: in `BepInEx\config\BepInEx.cfg`, under `[Logging.Console]`, set `Enabled = true` to get
   a live log window.

## 3. Create the repo (once per mod)

**The template repo, set up once:** push `valheim-plugin-template` to GitHub, then go to
`Settings > General` and tick **Template repository**.

**Each new mod:**

1. On the template's GitHub page, click **Use this template > Create a new repository**. Name it after the
   mod, e.g. `QuanDupeLightsources`.
2. Clone it:
   ```powershell
   git clone <repo-url> C:\Dev\Valheim\QuanDupeLightsources
   ```
3. Open the repo folder on its own (`File > Open Folder`), not inside a multi-folder workspace. Then rename the stub to the mod's name:
   - `MyValheimPlugin.csproj` becomes `<ModName>.csproj`. Inside it, set `AssemblyName`, `RootNamespace` and
     `ThunderstoreDescription`.
   - Leave `RepoUrl` empty. A Release build fills it from the repo's git remote, so the Thunderstore page
     links to this repo and its Issues. Set it only if the link should point somewhere else.
   - In `Plugin.cs`, set the `namespace`, `PluginGuid` (`quandru.<ModName>`) and `PluginName`. Leave
     `PluginVersion = PluginInfo.Version` alone. Change `NetworkCompatibility` if the mod is client-only.
   - In `Package\`, write `README.md` (the Thunderstore page), start `CHANGELOG.md`, and replace `icon.png`
     with a PNG of exactly 256×256.
   - Replace the template's root `README.md` with one about the mod. That's the GitHub page.
4. Copy `Local.props.example` to `Local.props`, which is gitignored, and set:
   - `GaleProfileDir`: this mod's Gale profile from step 2.
   - `ValheimDir`, only if Valheim isn't in the default Steam location.
5. First commit:
   ```powershell
   git add .
   git commit -m "Scaffold <ModName> 1.0.0"
   git push
   ```

There's no solution file to create. `dotnet build` and C# Dev Kit both pick up the single `.csproj` in the repo
root.

## 4. Build

1. Open the repo folder in VS Code and wait for C# Dev Kit to load the project.
2. Press **Ctrl+Shift+B**. This runs the default task, **Build (Debug, deploy to Gale dev profile)**.
   From a terminal, the equivalent is:
   ```powershell
   dotnet build -c Debug
   ```
3. What happens:
   - The first build restores one NuGet package, so it's slow once.
   - `PluginInfo.Version` is generated from `<Version>`. A red squiggle on it before the first build is normal.
   - The full package (the DLL, a generated `manifest.json`, and `README.md`, `CHANGELOG.md` and `icon.png` from
     `Package\`) is staged in `obj\Debug\package\`, then copied into
     `<GaleProfileDir>\BepInEx\plugins\<Team>-<ModName>\`. That's exactly how Gale lays out an installed
     Thunderstore release, so the dev build is a true rehearsal, and a later install of the release overwrites
     it instead of loading a second copy. `<Team>` is `ThunderstoreTeam` in `Directory.Build.props`.

If a path is wrong, the build stops and names the file it couldn't find.

## 5. Test

1. Launch Valheim from the mod's Gale profile.
2. In the log (the console window, or `<GaleProfileDir>\BepInEx\LogOutput.log`), check for
   `Loading [<ModName> x.y.z]` and the mod's own log lines.
3. Test in a local world. Single player runs a local server, so `EveryoneMustHaveMod` is satisfied.
4. Log out and back in to check that anything you placed or changed persists.

## 6. Release

1. Bump `<Version>`. That's the only place it lives: the plugin attribute and `manifest.json`
   are both generated from it.
2. Add a section to the top of `Package\CHANGELOG.md`.
3. Update the mod's Gale profile and run a final test. The package declares the BepInExPack and Jotunn versions
   installed in that profile, meaning the ones you actually tested against.
4. Run the **Package (Release zip to /dist)** task, or:
   ```powershell
   dotnet build -c Release
   ```
   Check the `depends on:` line in the output. It's the dependency list going into the manifest.
   The zip has the same contents as the Debug deploy you tested, just zipped instead of copied.
5. Commit, tag and push. The tag must be **annotated** (`-a`): `--follow-tags` silently skips lightweight tags.
   ```powershell
   git commit -am "Release 1.0.1"
   git tag -a v1.0.1 -m "<ModName> 1.0.1"
   git push --follow-tags
   ```
6. Upload `dist\<Team>-<ModName>-<version>.zip` to Thunderstore (see section 7). Optionally, attach the same zip to a
   GitHub release for the tag.

How the generated `manifest.json` gets its fields:

| Field | Source |
|---|---|
| `name` | `<AssemblyName>`. Letters, digits and underscores only. |
| `version_number` | `<Version>` |
| `description` | `<ThunderstoreDescription>`: 250 characters max, and no double quotes, because it isn't JSON-escaped. |
| `website_url` | `<RepoUrl>` if set; otherwise the repo's `origin` remote, converted to https, with any embedded credentials and `.git` removed. |
| `dependencies` | BepInExPack's version, read from the profile's `changelog.txt`; Jotunn's version, read from its `manifest.json` in the profile; and any `<ThunderstoreDependency>` items in the `.csproj`. |

About the BepInExPack heuristic: the pack's `changelog.txt` is a git log listed newest first. Pack versions look
like `5.4.2351` (four-digit patch), while BepInEx's own versions look like `5.4.23.5`. The build takes the first
`x.y.NNNN` in the file. If that ever picks the wrong one, set `BepInExPackDependency` in `Local.props` and the
build uses that instead.

## 7. Thunderstore (first release only)

1. Sign in at <https://thunderstore.io> with Discord or GitHub.
2. Create a **team**. Its name becomes the author prefix, e.g. `Quandru-QuanDupeLightsources`. It must match
   `ThunderstoreTeam` in `Directory.Build.props`, so dev deploys land in the same folder as installed releases.
3. Upload at <https://thunderstore.io/c/valheim/create/>, choosing the team and the Valheim community.

Thunderstore packages are public; there's no private option. Once a mod is published, add it to the synced
server profile in Gale like any other mod.

## 8. Troubleshooting

| Symptom | Likely cause |
|---|---|
| `GaleProfileDir isn't set` | There's no `Local.props`. Copy it from `Local.props.example`. |
| `... not found in ...` | A path in `Local.props` is wrong, or the profile hasn't been launched once. |
| `CS0012: The type 'X' is defined in an assembly that is not referenced` | The code uses a game type from another DLL. Add that DLL from `valheim_Data\Managed` in `Directory.Build.props`, following the existing pattern. |
| `CS0246: 'Piece' could not be found` | `assembly_valheim` isn't resolving. Check `ValheimDir`. |
| `PluginInfo` doesn't exist | It's generated during the build. Build once. |
| Release: `Couldn't find a BepInExPack version` | The pack's changelog format changed. Set `BepInExPackDependency` in `Local.props`. |
| BepInEx warns about a duplicate plugin GUID | There's an old dev folder alongside the current one, e.g. one from before `ThunderstoreTeam` was set. Delete the stale folder. |
| Plugin missing from the log | The DLL isn't in that profile's `BepInEx\plugins`, or you launched a different profile. |
| Kicked on connect with a version mismatch | Server and client have different plugin versions. `VersionStrictness.Minor` means `x.y` must match. |
| Need a private game field | Add `BepInEx.AssemblyPublicizer.MSBuild` and set `Publicize="true"` on the `assembly_valheim` reference. |
