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

**Don't use C# tuples.** Valheim runs on Unity's Mono runtime, which doesn't include `System.ValueTuple`. Tuple
types such as `(Vector3 position, Vector2 uv)`, methods that return tuples, and `var (a, b) = ...` all compile
without complaint, then throw `Could not load file or assembly 'System.ValueTuple'` the first time the code runs
in game. Use a small `struct` or `out` parameters instead. To check a build, search the decompiled DLL for
`ValueTuple`: `ilspycmd bin\Release\<ModName>.dll | findstr ValueTuple` should print nothing.

**Cloning another mod's pieces: strip Jotunn's placeholder names.** A piece added by another Jotunn mod doesn't
hold its real recipe items and crafting station straight away. Until Jotunn resolves them, each is a placeholder
named `JVLmock_<real name>` (for example `JVLmock_Bronze`). If your mod copies that piece's recipe or station
during `OnVanillaPrefabsAvailable`, remove the `JVLmock_` prefix from each name before building your own
`RequirementConfig` and `CraftingStation`, or the copy points at names that never resolve. Jotunn's own constant
for the prefix (`MockManager.JVLMockPrefix`) is internal, so declare your own. Vanilla pieces aren't affected.

**Config files: numbered sections in a deliberate order.** Gale's config editor shows each `[Section]` of a
mod's `.cfg` as its own group, so a well-sectioned file is far easier to manage than one long list. Two facts about
how BepInEx writes the file (`ConfigFile.Save`) decide the layout:
- **Sections are sorted alphabetically.** That's why a section called "Main" can end up below "Fuelling". So number
  every section, Azumatt-style, **starting at `0 - General`**: mod-wide settings, such as a master **Enabled**
  switch where the mod has one. Then `1 - Limits`, `2 - ...` and so on, with admin settings early and logging last.
  The sort is by text, not number, so `10 - X` would land before `2 - Y`: keep to ten sections (0–9), or number them
  all with two digits (`00 - General`).
- **Settings inside a section keep the order the code binds them in.** Bind them in the order a reader needs them,
  most important first.

Also:
- One topic per section; no catch-all "Main".
- Every setting has a description, and says when it's synced from the server or admin-only.
- Use an enum, an `AcceptableValueList` or an `AcceptableValueRange` wherever a setting has a fixed set of values or
  a range. Gale and Configuration Manager then show a dropdown or a slider instead of a free text box, and the value
  can't be mistyped.
- Never rename a section or setting after release: players' saved values sit under the old name and would be lost.
  If a rename is unavoidable, migrate the old value in code and say so in the changelog.

**Server-enforced or personal: decide for every setting.** Our mods run on the server and every client, and the
server's settings are authoritative wherever they affect other players.
- **Use Jotunn's config sync,** not ServerSync. Mark a setting `IsAdminOnly = true` (in `ConfigurationManagerAttributes`,
  or `BindConfig(..., synced: true)`). Every client then gets the server's value on connect and any later change, and
  only admins (the server's `adminlist.txt`) can change it in game. ServerSync, which many mods embed, exists mainly so
  mods can sync without depending on Jotunn; ours already depend on Jotunn, so it adds nothing. There's no master
  "Lock Configuration" switch: each setting is enforced or not by design.
- **Challenge each setting:** does it affect other players, fairness or the server's experience? Then it's
  enforced. Is it about the player's own view or comfort? Then it's personal. For example, a map mod enforces the
  exploration radius and forced position sharing, while which pin types show is personal.
- **Keep enforced and personal settings in separate sections** where possible, and mark personal ones in the section
  name, e.g. `8 - Map (personal)`.
- **Say it in every description:** "Synced from the server; admins only." or "Personal." Gale shows descriptions but
  not Configuration Manager's lock icon.
- Each design doc's config table has an Enforced/Personal column.
- **Data that isn't a setting** (a YAML list, for instance) doesn't sync by itself. Register an extra config file
  with Jotunn's `SynchronizationManager.RegisterCustomConfig`, or send it with a Jotunn custom RPC.
  Nothing is lost against ServerSync's synced values: Jotunn's custom RPCs compress and split large packages and send
  them in a coroutine. What ServerSync adds is ready-made plumbing (send on connect, resend when the file changes); we
  write that once as a small shared helper when a mod first needs it, and add it to the template.
- **Timing:** data that changes how prefabs are set up (which creatures are tameable, say) must be applied before the
  world's objects load, or objects that load first behave as vanilla. Design for that whichever way the data travels.

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
   - In `thunderstore.toml` and `hexium.json`, set `name` to the mod's `AssemblyName`. Leave the categories (and
     Hexium's `installLocation`) as `undefined` until you know them: every build fails while they say `undefined`,
     which is the point, but commits don't, so design docs can go in first. When you're ready to build, set them
     (see "Store categories" in section 6).
   - Replace the template's root `README.md` with one about the mod. That's the GitHub page.
   - Keep `LICENSE` (MIT) as it is.
   - Keep `docs/valheim-dependencies.md`, and add a row to it whenever the mod starts relying on game code or a
     vanilla prefab (see section 10).
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

**Debug builds or a local zip import, not both.** Gale's **Import local mod** installs a zip to
`BepInEx\plugins\<ModName>\`, without the team prefix, while a Debug build deploys to
`BepInEx\plugins\<Team>-<ModName>\`. With both in one profile, BepInEx finds two copies of the plugin and loads
only one, so you may be testing an older build without noticing. To test a Release zip, import it into the
profile and stop running Debug builds there. To go back to Debug builds, remove the imported copy in Gale first.

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
   **This build is the gate for the tag.** It checks `thunderstore.toml` and `hexium.json` (see "Store categories"
   below), with each category checked against that store's live list. If it failed, or printed the warning that the list
   couldn't be downloaded, fix that and build again before step 5. Never tag or publish a release from a commit
   whose Release build didn't pass.
5. Commit, tag and push. The tag must be **annotated** (`-a`): `--follow-tags` silently skips lightweight tags.
   ```powershell
   git commit -am "Release 1.0.1"
   git tag -a v1.0.1 -m "<ModName> 1.0.1"
   git push --follow-tags
   ```
6. Create the GitHub release for the tag, with the zip attached. Use this version's `CHANGELOG.md` section as the
   notes. The release must be published (not a draft) and the zip must keep its built name, because that's what the
   Publish workflow looks for.
   ```powershell
   gh release create v1.0.1 "dist\<Team>-<ModName>-1.0.1.zip" --title "<ModName> 1.0.1" --notes "<changelog section>"
   ```
7. **Publish** to Thunderstore and Hexium with the Publish workflow in the private `.publishing` repo (see
   `.publishing/docs/design.md`). Nobody uploads to either store by hand any more. Run it yourself from that repo's
   Actions tab, or ask Claude, who will first run a check, report exactly what would be uploaded to which store, and
   publish only after you confirm (the rule is in `.claude/CLAUDE.md`, "Terms, and the publishing gate"). A mod's
   first publish creates its listing on both stores; see section 7 for the one-off steps.

The words, as used here and in the workspace rules: **commit** is a local git commit, **push** sends it to GitHub,
**tag / release** is steps 5-6, and **publish** is step 7 only.

How the generated `manifest.json` gets its fields:

| Field | Source |
|---|---|
| `name` | `<AssemblyName>`. Letters, digits and underscores only. |
| `version_number` | `<Version>` |
| `description` | `<ThunderstoreDescription>`: 250 characters max, and no double quotes, because it isn't JSON-escaped. Thunderstore's search matches descriptions as well as names, so include the words people will search for (e.g. "tame", "breed") and the names of mods this one replaces. |
| `website_url` | `<RepoUrl>` if set; otherwise the repo's `origin` remote, converted to https, with any embedded credentials and `.git` removed. |
| `dependencies` | BepInExPack's version, read from the profile's `changelog.txt`; Jotunn's version, read from its `manifest.json` in the profile; and any `<ThunderstoreDependency>` items in the `.csproj`. Write those as `Team-Name` to use the version installed in the mod's Gale profile (read from `BepInEx\plugins\Team-Name\manifest.json`, so it's the version you tested against), or `Team-Name-x.y.z` to pin one. |

### Store categories (`thunderstore.toml` and `hexium.json`)

Each mod keeps its store listing settings in two files at the repo root, one per store, so the Publish workflow in
`.publishing` can publish the same Release zip to both. It reads them as they were at the release's tag. They hold only what the manifest doesn't: the team
(`namespace`), the package `name`, the community and the **categories** (plus Hexium's install location). Version,
description and dependencies stay in the `.csproj`. Neither file is packed into the zip.

When choosing categories, choose both stores' together, so the two listings describe the mod the same way.

#### Thunderstore: `thunderstore.toml`

In the format of Thunderstore's own CLI (`tcli`). The Publish workflow takes the namespace, name and categories from
it.

```toml
[publish.categories]
valheim = ["building", "mods", "client-side", "server-side"]
```

Categories are Thunderstore's lowercase **slugs** (`client-side`, not `Client-side`), listed at
<https://thunderstore.io/api/experimental/community/valheim/category/>, and in `.publishing/categories/`, which every
Publish run refreshes. Each publish sets the mod's Thunderstore categories from this file, so change them here, not
on the site.

Every build checks the file, Debug included, and fails if it's missing, if `namespace` isn't `ThunderstoreTeam`, if
`name` isn't `AssemblyName`, if the categories are empty or still `undefined`, or if one isn't written as a slug.
A Release build also checks each one against Thunderstore's live list (offline it warns instead). The categories are
`undefined` in the template and in any new mod until someone chooses them, so nothing can be built, released or
tagged without them; commits still work.

#### Hexium: `hexium.json`

```json
"installLocation": "both",
"categories": ["Pieces", "Building", "Open Source", "Valheim 1.0"]
```

Hexium's upload ignores categories and install location, so the Publish workflow sets them from this file straight
after each upload (Hexium's `POST /api/mod/{namespace}/{name}/set-metadata`). Change them here, not on the site.

- **Categories** are Hexium's slugs, which are the display names, capitals and spaces included (`Quality of Life`),
  listed at <https://hexium.gg/api/experimental/community/valheim/category/>. Hexium's list differs from
  Thunderstore's, so pick from it rather than translating.
- **`installLocation`** is where the mod must be installed: `client` (players only), `server` (server only), `both`
  (server and every player; any mod with `EveryoneMustHaveMod`), or `client_opt_server` (players, and optionally the
  server). Hexium shows it as the Client-only, Server-only, Client & Server or Client (& Server) label, so those four
  never go in `categories`.

Every build checks the file as it does `thunderstore.toml`: `namespace` and `name` must match, `installLocation` must
be one of the four values, and the categories must be set and not `undefined` (in any capitalisation). A Release
build also checks each category against Hexium's live list (case-sensitive; offline it warns instead).

About the BepInExPack heuristic: the pack's `changelog.txt` is a git log listed newest first. Pack versions look
like `5.4.2351` (four-digit patch), while BepInEx's own versions look like `5.4.23.5`. The build takes the first
`x.y.NNNN` in the file. If that ever picks the wrong one, set `BepInExPackDependency` in `Local.props` and the
build uses that instead.

## 7. The stores (first publish only)

**Once per team, already done for `Quandru`:** a team on Thunderstore and one on Hexium, both named exactly as
`ThunderstoreTeam` in `Directory.Build.props`. The name is the author prefix (`Quandru-QuanDupeLightsources`) and the
namespace in `thunderstore.toml` and `hexium.json`, and dev deploys use it so they land in the same folder as
installed releases. Each team has an API token stored as a secret in `.publishing` (`.publishing/docs/tokens.md`).

**Each new mod:** nothing to upload by hand. Its first publish (section 6, step 7) creates the listing on both
stores, with the categories from its store files. Afterwards:
1. Check both listings look right (icon, README, categories).
2. Add the mod to the synced server profile in Gale like any other mod.

Both stores are public; there's no private option.

**Licence:** every mod is MIT-licensed (`LICENSE` at the repo root, from this template). Neither store has a licence
setting that we've found; the repo's `LICENSE` is what counts. Keep it: it's what lets us
tag listings `Open Source`.

## 8. Troubleshooting

| Symptom | Likely cause |
|---|---|
| `GaleProfileDir isn't set` | There's no `Local.props`. Copy it from `Local.props.example`. |
| `... not found in ...` | A path in `Local.props` is wrong, or the profile hasn't been launched once. |
| `CS0012: The type 'X' is defined in an assembly that is not referenced` | The code uses a game type from another DLL. Add that DLL from `valheim_Data\Managed` in `Directory.Build.props`, following the existing pattern. |
| `CS0246: 'Piece' could not be found` | `assembly_valheim` isn't resolving. Check `ValheimDir`. |
| `PluginInfo` doesn't exist | It's generated during the build. Build once. |
| Release: `Couldn't find a BepInExPack version` | The pack's changelog format changed. Set `BepInExPackDependency` in `Local.props`. |
| BepInEx warns about a duplicate plugin GUID | There's an old dev folder alongside the current one, e.g. one from before `ThunderstoreTeam` was set, or a Gale local import (`<ModName>`) next to a Debug deploy (`<Team>-<ModName>`). Delete the stale folder. |
| Plugin missing from the log | The DLL isn't in that profile's `BepInEx\plugins`, or you launched a different profile. |
| Kicked on connect with a version mismatch | Server and client have different plugin versions. `VersionStrictness.Minor` means `x.y` must match. |
| `Could not load file or assembly 'System.ValueTuple'` in the log, and the mod's content is missing | The code uses C# tuples, which Valheim's runtime can't load. Replace them with a `struct` or `out` parameters (see section 0). |
| A cloned piece's build card shows red-and-white shields and "None" for the station | It was copied from another mod's piece before Jotunn replaced its `JVLmock_` placeholders. Strip that prefix from the item and station names (see section 0). |
| Release: `Dependency '...' isn't installed in the Gale profile` | A `<ThunderstoreDependency>` without a version takes it from the profile. Install that mod in the profile, or pin a version as `Team-Name-x.y.z`. |
| `thunderstore.toml: ...` or `hexium.json: ...` | The file is missing, doesn't match the `.csproj`, or its categories (or Hexium's install location) aren't set. The message says which. See "Store categories" in section 6. |
| Need a private game field | Add `BepInEx.AssemblyPublicizer.MSBuild` and set `Publicize="true"` on the `assembly_valheim` reference. |
| Build errors about types from `research/` or another project in a subfolder | The SDK compiles every `.cs` file under the repo. `research/` is excluded in `Directory.Build.props`; exclude any other folder in the mod's `.csproj` with `<DefaultItemExcludes>$(DefaultItemExcludes);Folder/**</DefaultItemExcludes>`. |

## 9. Keeping mods in step with the template

A mod repo starts as a copy of the template and never picks up later template changes by itself. So **any
difference found between a mod and the template is a learning to share, not something to leave**:

1. **Decide which side is right.** A fix or improvement first found in a mod (a build setting, a reference, a
   `.gitignore` entry, a doc note) goes into the template first.
2. **Merge it into every other mod, one repo at a time.** Commit only the template-derived files, and leave any
   unrelated work in progress in that repo untouched.
3. **Check every mod, not just the ones cloned here.** Start with the local folders, then list the Quanheim GitHub
   organisation (`gh repo list Quanheim`) for mod repos that aren't cloned yet.

Template-derived files to compare: `LICENSE`, `.gitignore`, `Directory.Build.props`, `Directory.Build.targets`,
`Local.props.example`, `docs/DEVELOPMENT.md`, the layout of `thunderstore.toml` and `hexium.json` (comments, keys), the
section layout of `docs/valheim-dependencies.md`, and the shape of
`.vscode/` (which the template no longer tracks).

Expected differences, which are **not** drift: the mod's name, GUID, version and description in its `.csproj` and
plugin class, its `Package/` files, its root `README.md`, the entries in `docs/valheim-dependencies.md`, the `name`,
categories and install location in `thunderstore.toml` and `hexium.json`, and machine- or mod-specific paths in `Local.props.example`.

## 10. Valheim updates

Mod updates that say only "updated for Valheim x.y.z" are the result of this check. Every mod keeps
`docs/valheim-dependencies.md`, listing the game code it patches, reaches by reflection, calls or relies on, plus
the vanilla prefabs and assets it uses. The private `.decompiles` repo (cloned beside the mods) holds an unaltered
decompile of every game version, so each update can be read as a plain diff.

**Keeping the doc current.** Add a row in the same commit as any code that patches, reflects into, calls or relies
on something new in the game. A mod that only adds Jotunn pieces still lists the vanilla prefabs it clones.

**When Valheim updates** (`.decompiles\check-version.ps1` exits 1, or Steam has patched the game):

1. **Record the new version.** In `.decompiles`, run `.\decompile.ps1 -Version <x.y.z>`, then commit, tag and push
   as its README says.
2. **First pass across all mods.** Run `.\check-mods.ps1`. Each mod's entries come back as `MISSING` (will break),
   `CHANGED`, `CLASS CHANGED` or nothing.
3. **Read what it flags.** `git diff v<old> v<new> -- <file>` in `.decompiles` for each flagged entry. Decide per
   mod: no change, a fix, or a rethink. Name matching is coarse, so also skim the whole diff's file list for
   anything a mod relies on indirectly.
4. **Look for opportunities.** Read the whole diff once more against what each mod does. Iron Gate sometimes fixes
   a bug a mod patches around (1.0.21 partly fixed the sail crash QuanChunkLoader works around), or adds something
   a mod could use instead of its own code.
5. **Test in game** for every mod, flagged or not: load it in its Gale profile on the new version, check the log
   for errors and exercise each item in the **Prefabs and assets** table. Asset changes never show in a code diff.
6. **Record the result.** Update "Checked against Valheim" in each mod's dependency doc. A mod that needed no code
   change still gets a release if Thunderstore users need a sign it works, usually as a patch version with a
   changelog line "Checked against Valheim x.y.z".

## 11. Working together

More than one person may develop these mods. The workspace rules in the private `.claude` repo (`CLAUDE.md`) set
out the details; the essentials:

- **Clone every repo.** Every repo in the Quanheim organisation lives beside `.claude` and in
  `Quanheim.code-workspace`, so that a change to the template or a convention can reach every mod. The `.claude`
  repo's `scripts\whats-new.ps1` reports any that are missing.
- **Check what's new before you start.** The same script pulls the shared repos and lists what anyone else changed.
- **Log every convention change** in `.claude/CONVENTIONS-LOG.md`, in the same commit: what, why, and what the other
  person needs to do. That covers this guide, the rest of the template, the `.decompiles` scripts and the workspace
  rules.
- **Pull before working on a mod, push when done.** Two people on the same mod at the same time: use a branch and a
  pull request.
- **Disagree in conversation, not by reverting.** Raise it with the other person and log what you agree.
